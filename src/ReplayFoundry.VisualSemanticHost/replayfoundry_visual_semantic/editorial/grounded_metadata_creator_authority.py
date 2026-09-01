"""Typed creator-embodiment authority for grounded audience copy."""
from __future__ import annotations

import re
from typing import Any, Callable

from ..errors import InferenceError, _fail
from .grounded_metadata_lexical import (
    _CENTER_TOKEN_STOP_WORDS,
    _case_fact_terms,
    normalize_lexical,
    shared_token_windows,
)


_GENERIC_PERSON_SUBJECT_OPENING = re.compile(
    r"^\s*(?:(?:a|an|the|this|that)\s+"
    r"(?:[\w'’-]+\s+){0,4})?"
    r"(?:man|woman|person|guy|player|character)\b",
    re.IGNORECASE,
)
_INVENTORY_CENTER_SUBJECT = re.compile(
    r"\b(?:camera|display|interface|monitor|screen|text|title card)\b",
    re.IGNORECASE,
)
_LEADING_SUBJECT_ARTICLE = re.compile(
    r"^\s*(?:a|an|the|this|that)\s+",
    re.IGNORECASE,
)
_FIRST_PERSON_GENERIC_OBSERVER_OPENING = re.compile(
    r"^\s*(?:i|we)\s+"
    r"(?:heard|noticed|observed|saw|spotted|watched)\s+"
    r"(?:(?:a|an|the|this|that)\s+)?"
    r"(?:[\w'’-]+\s+){0,4}"
    r"(?:man|woman|person|guy|player|character)\b",
    re.IGNORECASE,
)
_FIRST_PERSON_REFERENCE = re.compile(
    r"\b(?:i|me|my|mine|we|us|our|ours)\b",
    re.IGNORECASE,
)
_FIRST_PERSON_POSSESSIVE = re.compile(
    r"\b(?:my|mine|our|ours)\b",
    re.IGNORECASE,
)
_FIRST_PERSON_SUBJECT_ACTION = re.compile(
    r"\b(?:i|we)\s+(?:had\s+)?([a-z]+)\b",
    re.IGNORECASE,
)
_CREATOR_ENCOUNTER_ACTIONS = {
    "approached", "arrived", "confronted", "discovered", "encountered",
    "entered", "escaped", "faced", "followed", "found", "met", "noticed",
    "observed", "reached", "saw", "spotted", "watched", "witnessed",
}
_CREATOR_AFFECTED_ACTIONS = {
    "became", "died", "dropped", "escaped", "fell", "got", "lost",
    "received", "stumbled", "suffered", "survived", "took", "was", "were",
}


def _editorial_excerpt(value: str, maximum: int = 600) -> str:
    normalized = " ".join(value.split())
    if len(normalized) <= maximum:
        return normalized
    return normalized[:maximum].rsplit(" ", 1)[0].rstrip(" ,;:-")


def _action_linked_candidate_subjects(primary: dict[str, Any]) -> list[str]:
    action_tokens = {
        token
        for action in primary.get("actions", [])
        if isinstance(action, str)
        for token in normalize_lexical(action).split()
        if len(token) >= 3 and token not in _CENTER_TOKEN_STOP_WORDS
    }
    candidates: list[str] = []
    seen: set[str] = set()
    for value in primary.get("subjectsAndObjects", []):
        if not isinstance(value, str) or (
            _GENERIC_PERSON_SUBJECT_OPENING.search(value)
            or _INVENTORY_CENTER_SUBJECT.search(value)
        ):
            continue
        normalized = " ".join(value.split()).strip(" ,;:-")
        articleless = _LEADING_SUBJECT_ARTICLE.sub("", normalized).strip()
        subject_tokens = {
            token for token in normalize_lexical(articleless).split()
            if len(token) >= 3 and token not in _CENTER_TOKEN_STOP_WORDS
        }
        key = articleless.casefold()
        if (
            articleless
            and key not in seen
            and subject_tokens.intersection(action_tokens)
        ):
            candidates.append(articleless)
            seen.add(key)
    return candidates[:4]


def _audience_frame(
    editorial_frame: dict[str, Any],
    primary: dict[str, Any],
    actor_authority: str,
    creator_relation: str,
) -> dict[str, Any]:
    presentation = editorial_frame.get("primaryPresentationKind", "Unclear")
    creator_controlled = (
        actor_authority == "CreatorControlled"
        and creator_relation == "CreatorActed"
    )
    center_kind = (
        "NarrativePresentation"
        if presentation in {"CinematicSequence", "InWorldRecording"}
        else "DocumentPresentation"
        if presentation == "DocumentOrLore"
        else "InterfaceProgression"
        if presentation in {"MenuOrLoadout", "ObjectiveOrInterface"}
        else "CreatorAction"
        if presentation == "InteractiveGameplay" and creator_controlled
        else "NeutralGameplayEvent"
    )
    return {
        "authorityKind": "GrammaticalShapeOnly",
        "centerKind": center_kind,
        "presentationKind": presentation,
        "momentKind": editorial_frame.get("momentKind", "Unclear"),
        "candidateSubjects": _action_linked_candidate_subjects(primary),
        "primaryActionRole": (
            "SupportingDetailOnly"
            if center_kind == "NarrativePresentation"
            else "FactualSupportOnly"
        ),
    }


def _narrative_case_fact_terms(authority: dict[str, Any]) -> set[str]:
    if authority.get("audienceFrame", {}).get("centerKind") != \
            "NarrativePresentation":
        return set()
    primary = authority.get("primaryVisual", {})
    values = [
        *primary.get("actions", []),
        *authority.get("audienceFrame", {}).get("candidateSubjects", []),
        *authority.get("stableReadableText", []),
    ]
    facts = _case_fact_terms([value for value in values if isinstance(value, str)])
    identity = authority.get("confirmedGameIdentity", {})
    return facts.difference(_case_fact_terms([identity.get("name", "")]))


def narrative_presentation_has_distinct_fact(authority: dict[str, Any]) -> bool:
    return authority.get("audienceFrame", {}).get("centerKind") != \
        "NarrativePresentation" or bool(_narrative_case_fact_terms(authority))


def validate_narrative_case_fact_retention(
    title: str,
    description: str,
    authority: dict[str, Any],
) -> None:
    facts = _narrative_case_fact_terms(authority)
    if authority.get("audienceFrame", {}).get("centerKind") != \
            "NarrativePresentation":
        return
    if not facts or any(
        not facts.intersection(_case_fact_terms([field]))
        for field in (title, description)
    ):
        _fail(
            InferenceError,
            "Grounded editorial rephrase did not retain a case-local primary "
            "fact in both audience fields.",
        )


def build_typed_editorial_authority(
    request: dict[str, Any],
    drafts: list[dict[str, Any]],
    primary_ordinal: int,
    actor_authority: str,
    creator_relation: str,
    stable_readable_text: list[str],
    binding_id: Callable[[str, str], str],
) -> dict[str, Any]:
    primary = drafts[primary_ordinal - 1]
    editorial_frame = request.get("_editorialFraming")
    anchor: dict[str, Any] = {
        "authorityKind": "BoundedTypedRetryAuthority",
        "primaryVisual": {
            "ordinal": primary_ordinal,
            "environment": primary.get("environment"),
            "environmentUncertain": bool(primary.get("environmentUncertain")),
            "subjectsAndObjects": list(primary.get("subjectsAndObjects", [])),
            "actions": list(primary.get("actions", [])),
            "actorAuthority": actor_authority,
            "creatorExperienceRelation": creator_relation,
        },
        "chronologicalProgression": [
            {
                "phase": (
                    "LeadIn"
                    if ordinal < primary_ordinal
                    else "VisibleFollowThrough"
                ),
                "actions": list(draft.get("actions", [])),
            }
            for ordinal, draft in enumerate(drafts, start=1)
            if ordinal != primary_ordinal and draft.get("actions")
        ],
    }
    if isinstance(editorial_frame, dict):
        anchor["editorialFraming"] = {
            "policyVersion": editorial_frame.get("policyVersion"),
            "authorityKind": "StoryShapeOnly",
            "momentKind": editorial_frame.get("momentKind", "Unclear"),
            "primaryPresentationKind": editorial_frame.get(
                "primaryPresentationKind", "Unclear"
            ),
            "supportingPresentationKinds": list(
                editorial_frame.get("supportingPresentationKinds", [])
            ),
            "premise": editorial_frame.get("premise"),
            "supportingDraftOrdinals": list(
                editorial_frame.get("supportingDraftOrdinals", [])
            ),
            "creatorAuthorityAdjusted": bool(
                editorial_frame.get("creatorAuthorityAdjusted")
            ),
        }
        anchor["audienceFrame"] = _audience_frame(
            anchor["editorialFraming"],
            primary,
            actor_authority,
            creator_relation,
        )
    if stable_readable_text:
        anchor["stableReadableText"] = [
            _editorial_excerpt(value, 160)
            for value in stable_readable_text[:12]
        ]
    game = request["game"]
    game_identity_confirmed = game.get("source") in {
        "UserConfirmed",
        "ReusedUserMemory",
    }
    if game_identity_confirmed:
        anchor["confirmedGameIdentity"] = {
            "name": game["name"],
            "hashtag": game["hashtag"],
            "source": game["source"],
        }
    reviewed_speech = [
        {"authority": item["authority"], "excerpt": _editorial_excerpt(item["text"])}
        for item in request.get("transcripts", [])
        if item.get("role") == "CreatorSpeech"
        and item.get("authority") in {"UserCorrected", "HumanReviewed"}
    ][:2]
    if reviewed_speech:
        anchor["reviewedCreatorSpeech"] = reviewed_speech
    notes = game.get("notes")
    if notes and game.get("source") in {"UserConfirmed", "ReusedUserMemory"}:
        anchor["userGameContext"] = {
            "authority": game["source"],
            "notes": notes,
        }
    matches = (
        (request.get("gameKnowledge") or {}).get("matches", [])
        if game_identity_confirmed
        else []
    )
    selected = [
        item for item in matches
        if item.get("temporalRelation") == "CurrentEventCandidate"
    ]
    if selected:
        anchor["selectedGameKnowledge"] = [
            {
                "id": item["id"], "section": item["section"],
                "text": item["text"], "strength": item["strength"],
                "temporalRelation": item["temporalRelation"],
                "clipEvidenceIds": item["clipEvidenceIds"],
                "authorizedBindingIds": [
                    binding_id(item["id"], evidence_id)
                    for evidence_id in item["clipEvidenceIds"]
                ],
            }
            for item in selected
        ]
    return anchor


def _action_stem(value: str) -> str:
    word = value.casefold()
    if len(word) > 5 and word.endswith("ing"):
        word = word[:-3]
    elif len(word) > 4 and word.endswith("ed"):
        word = word[:-2]
    elif len(word) > 4 and word.endswith("es"):
        word = word[:-2]
    elif len(word) > 3 and word.endswith("s"):
        word = word[:-1]
    if len(word) > 3 and word[-1:] == word[-2:-1]:
        word = word[:-1]
    return word


def _reviewed_commentary_authorizes_creator_voice(
    request: dict[str, Any],
    audience_copy: str,
) -> bool:
    if request.get("profile", {}).get("variantIntent") != "CommentaryLed" or (
        _FIRST_PERSON_POSSESSIVE.search(audience_copy)
        or re.search(r"\b(?:me|mine|us|ours)\b", audience_copy, re.IGNORECASE)
    ):
        return False
    direct_actions = [
        match.group(1).casefold()
        for match in _FIRST_PERSON_SUBJECT_ACTION.finditer(audience_copy)
    ]
    if not direct_actions:
        return False
    reviewed_creator_speech = [
        " " + normalize_lexical(transcript.get("text", "")) + " "
        for transcript in request.get("transcripts", [])
        if transcript.get("role") == "CreatorSpeech"
        and transcript.get("authority") in {"UserCorrected", "HumanReviewed"}
    ]
    return bool(reviewed_creator_speech) and all(
        any(
            f" i {action} " in transcript
            or f" we {action} " in transcript
            or f" i had {action} " in transcript
            or f" we had {action} " in transcript
            for transcript in reviewed_creator_speech
        )
        for action in direct_actions
    )


def validate_creator_actor_authority(
    title_body: str,
    description: str,
    tags: list[str],
    request: dict[str, Any],
    primary_visual_draft: dict[str, Any],
    actor_authority: str,
    creator_experience_relation: str,
) -> None:
    """Reject creator embodiment that the typed primary evidence did not authorize."""
    # Tags are descriptive metadata, not grammatical audience narration. A game
    # fictional tag such as "Legends Between Us" must not turn neutral prose into a
    # first-person creator claim merely because it contains the standalone word
    # "Us".
    audience_copy = "\n".join([title_body, description])
    if not _FIRST_PERSON_REFERENCE.search(audience_copy):
        return
    if _reviewed_commentary_authorizes_creator_voice(request, audience_copy):
        return
    if creator_experience_relation == "Unestablished":
        _fail(
            InferenceError,
            "Grounded metadata used unsupported creator embodiment without an "
            "established creator-experience relation.",
        )
    if actor_authority == "CreatorControlled":
        return

    direct_actions = [
        match.group(1).casefold()
        for match in _FIRST_PERSON_SUBJECT_ACTION.finditer(audience_copy)
    ]
    if creator_experience_relation == "CreatorEncountered":
        if (
            _FIRST_PERSON_POSSESSIVE.search(audience_copy)
            or any(action not in _CREATOR_ENCOUNTER_ACTIONS for action in direct_actions)
        ):
            _fail(
                InferenceError,
                "Grounded metadata used unsupported creator embodiment for another "
                "person's primary action; use neutral past-action or grounded "
                "creator-encounter wording.",
            )
        return

    if creator_experience_relation == "CreatorAffected":
        primary_action_stems = {
            _action_stem(token)
            for action in primary_visual_draft.get("actions", [])
            for token in normalize_lexical(action).split()
            if len(token) >= 3
        }
        if (
            actor_authority == "OtherPerson"
            and _FIRST_PERSON_POSSESSIVE.search(audience_copy)
        ) or any(
            action not in _CREATOR_AFFECTED_ACTIONS
            and action not in _CREATOR_ENCOUNTER_ACTIONS
            or (
                _action_stem(action) in primary_action_stems
                and action not in {"got", "was", "were"}
            )
            for action in direct_actions
        ):
            _fail(
                InferenceError,
                "Grounded metadata used unsupported creator embodiment for another "
                "person's body or primary action; describe only the grounded effect "
                "on the creator experience.",
            )
        return

    _fail(
        InferenceError,
        "Grounded metadata used unsupported creator embodiment without "
        "creator-controlled primary-action authority.",
    )
