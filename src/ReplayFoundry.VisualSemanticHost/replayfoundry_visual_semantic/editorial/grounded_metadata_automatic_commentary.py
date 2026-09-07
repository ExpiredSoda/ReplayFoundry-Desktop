"""Bounded automatic-commentary attribution and retry source scope."""
from __future__ import annotations

import re
from typing import Any

from .grounded_metadata_lexical import _case_fact_terms


_FIRST_PERSON_REFERENCE = re.compile(
    r"\b(?:i|me|my|mine|we|us|our|ours)\b",
    re.IGNORECASE,
)
def _automatic_commentary_words(value: str) -> list[str]:
    # Deliberately identical to the C# guard: ASCII word extraction followed by
    # ASCII lowercase, not Python casefold versus .NET invariant Unicode casing.
    return [word.lower() for word in re.findall(r"[A-Za-z0-9]+", value)]


def _automatic_commentary_normalize(value: str) -> str:
    return " ".join(_automatic_commentary_words(value))


def _automatic_commentary_stem(word: str) -> str:
    if len(word) > 5 and word.endswith("ing"):
        word = word[:-3]
        if len(word) > 3 and word[-1] == word[-2]:
            word = word[:-1]
    elif len(word) > 3 and word.endswith("s"):
        word = word[:-1]
    return word


def _automatic_commentary_topic_terms(value: str) -> set[str]:
    stop = set((
        "about after again also and any are back because been being but can could "
        "did does doing for from guess had has have how into its just kind know like "
        "look looks lot make more most much not now only our out really right said "
        "same say see should some take than that the their them then there these "
        "they thing think this those too very was way wearing well were what "
        "when where which who why will with would you your"
    ).split())
    return {_automatic_commentary_stem(word) for word in _automatic_commentary_words(value)
            if len(word) >= 3 and word not in stop}


def _safe_automatic_commentary_angle(request: dict[str, Any]) -> str | None:
    if request.get("_automaticCommentaryWithheld") is True:
        return None
    brief = request.get("editorialBrief") or {}
    angle = brief.get("safeCommentaryAngle")
    if not isinstance(angle, str) or not angle.strip() or (
        "AutomaticCreatorReactionAngleAvailable" not in brief.get("qualityFlags", [])
    ):
        return None
    normalized = _automatic_commentary_normalize(angle)
    if not normalized or not any(
        item.get("role") == "CreatorSpeech"
        and item.get("authority") == "AutomaticUnreviewed"
        and any(normalized in _automatic_commentary_normalize(value) for value in (
            item.get("text", ""),
            " ".join(span.get("text", "") for span in item.get("spans", [])),
        ))
        for item in request.get("transcripts", [])
    ):
        return None
    return angle


def _requires_balanced_copy(request: dict[str, Any]) -> bool:
    """A packaging preference, never a change to transcript or factual authority."""
    return request.get("profile", {}).get("copyObjective") == "BalancedActionAndCommentary" \
        and request.get("profile", {}).get("variantIntent") != "CommentaryLed" \
        and _safe_automatic_commentary_angle(request) is not None


def balanced_copy_field_plan(
    variant_intent: str, title_maximum: int, gameplay_voice: str = "NeutralNoSubject",
) -> dict[str, Any]:
    """One field allocation shared by compact authoring and constrained decoding."""
    openings = ("I wondered about", "I wondered whether", "I wondered if", "I compared")
    # Very long exact game hashtags can leave too little room for an attributed
    # title. Other eligible variants already permit attribution in either field.
    attributed_field = "titleBody" if (
        variant_intent == "SpecificCuriosity"
        and title_maximum >= max(len(value) + 2 for value in openings)
    ) else "description"
    return {
        "visualField": "description" if attributed_field == "titleBody" else "titleBody",
        "attributedThoughtField": attributed_field,
        "gameplayVoice": gameplay_voice,
        "allowedAttributionOpenings": list(openings),
        "titleBodyMaximumCharacters": title_maximum,
        "variantIntent": variant_intent,
    }


def _balanced_copy_satisfied(
    request: dict[str, Any], title: str, description: str,
    visual_drafts: list[dict[str, Any]] | None,
) -> bool:
    if not _requires_balanced_copy(request):
        return True
    # This checks field coverage, not semantic entailment. The existing visual,
    # chronology, quotation and actor-authority checks still validate the copy.
    fields = (title, description)
    attributed = [_automatic_commentary_authorizes_creator_voice(request, value)
                  for value in fields]
    visual_terms = _case_fact_terms([
        action for draft in (visual_drafts or []) for action in draft.get("actions", [])
        if isinstance(action, str)
    ])
    attributed_fields = (1,) if request.get("profile", {}).get("variantIntent") == "DirectAction" else (0, 1)
    return any(
        attributed[index] and not attributed[1 - index]
        and bool(visual_terms.intersection(_case_fact_terms([fields[1 - index]])))
        for index in attributed_fields
    )


def _without_automatic_commentary(request: dict[str, Any]) -> dict[str, Any]:
    """Withhold model-facing automatic nominations; retain ASR for quote checks."""
    brief = request.get("editorialBrief")
    if isinstance(brief, dict):
        brief = {
            **brief,
            "safeCommentaryAngle": None,
            "qualityFlags": [flag for flag in brief.get("qualityFlags", [])
                             if flag != "AutomaticCreatorReactionAngleAvailable"],
            "claims": [claim for claim in brief.get("claims", [])
                       if claim.get("kind") != "AutomaticTranscriptCue"
                       and claim.get("authority") != "AutomaticTranscriptCue"],
        }
    return {**request, "_automaticCommentaryWithheld": True, "editorialBrief": brief}


def _scoped_retry_authority(
    authority: dict[str, Any] | None, withhold_automatic: bool, primary_only: bool,
) -> dict[str, Any] | None:
    if authority is None or not (withhold_automatic or primary_only):
        return authority
    scoped = dict(authority)
    scoped.pop("automaticCreatorCommentary", None)
    scoped.pop("copyObjective", None)
    scoped.pop("copyProfile", None)
    scoped.pop("groundedClaims", None)
    scoped.pop("groundedClaimBindings", None)
    if withhold_automatic:
        scoped.pop("confirmedGameIdentity", None)
        scoped.pop("userGameContext", None)
        scoped.pop("selectedGameKnowledge", None)
        scoped.pop("reviewedCreatorSpeech", None)
        scoped.pop("stableReadableText", None)
        scoped["identityWithheldForSafety"] = True
    if primary_only:
        scoped["evidenceScopeSelectedPrimaryOnly"] = True
        scoped["chronologicalProgression"] = []
        for key in ("reviewedCreatorSpeech", "userGameContext", "selectedGameKnowledge", "stableReadableText"):
            scoped.pop(key, None)
        if isinstance(scoped.get("editorialFraming"), dict):
            ordinal = scoped.get("primaryVisual", {}).get("ordinal")
            scoped["editorialFraming"] = {
                **scoped["editorialFraming"],
                "supportingDraftOrdinals": [ordinal] if ordinal is not None else [],
                "supportingPresentationKinds": [],
            }
    return scoped


def _automatic_commentary_authorizes_creator_voice(
    request: dict[str, Any], audience_copy: str,
) -> bool:
    """Allow attribution grammar only, never objective fact or avatar authority.

    This is not semantic entailment. All ordinary visual, title-scope, action-strength,
    quote-reuse, language and knowledge validators must still run on unchanged copy.
    """
    angle = _safe_automatic_commentary_angle(request)
    if angle is None:
        return False
    # Do not paraphrase a negated comparison into a positive creator act. A
    # rhetorical question may still retain a neutral topic without inverting it.
    negated = bool(re.search(
        r"\b(?:no|not|never|neither|nor|without)\b|n['’]t\b", angle, re.IGNORECASE,
    ))
    found = False
    topic = _automatic_commentary_topic_terms(angle)
    for sentence in re.split(r"(?<=[.!?])\s+|\n+", audience_copy):
        # The common noun in 'a mine' is not a first-person possessive.
        references = re.sub(r"\b(a|the|this|that)\s+mine\b", r"\1 object", sentence,
                            flags=re.IGNORECASE)
        if not _FIRST_PERSON_REFERENCE.search(references):
            continue
        opening = re.match(
            r"^\s*I\s+(?:wondered\s+(?:whether|if|about)\s+|compared\s+)",
            sentence, re.IGNORECASE,
        )
        if opening is None or _FIRST_PERSON_REFERENCE.search(references[opening.end():]):
            return False
        if negated and not re.match(r"\s*I\s+wondered\s+about\b", sentence, re.IGNORECASE):
            return False
        remainder = sentence[opening.end():]
        # A small permission for one attribution, not a blanket permission for a
        # coordinated creator action, causal assertion, possessive or quoted claim.
        if not remainder.strip(" .?!") or re.search(
            r"[\"'“”‘’«»‹›「」『』`;:,]|\b(?:and|or|but|then|so|because|before|after|while)\b",
            remainder, re.IGNORECASE,
        ):
            return False
        remainder_topic = _automatic_commentary_topic_terms(remainder)
        if not topic.intersection(remainder_topic) or not remainder_topic.issubset(topic):
            return False
        normalized_copy = " " + _automatic_commentary_normalize(sentence) + " "
        angle_words = _automatic_commentary_words(angle)
        if any(" " + " ".join(angle_words[index:index + 4]) + " " in normalized_copy
               for index in range(max(0, len(angle_words) - 3))):
            return False
        found = True
    return found
