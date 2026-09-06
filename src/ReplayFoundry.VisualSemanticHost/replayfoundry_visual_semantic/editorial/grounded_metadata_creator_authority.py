"""Typed creator-embodiment authority for grounded audience copy."""
from __future__ import annotations

import re
from typing import Any, Callable

from ..errors import InferenceError, _fail
from .grounded_metadata_lexical import (
    _CENTER_TOKEN_STOP_WORDS,
    _case_fact_terms,
    normalize_lexical,
)
from .grounded_metadata_output_schema import title_body_maximum


from .grounded_metadata_automatic_commentary import (
    _FIRST_PERSON_REFERENCE,
    _automatic_commentary_words,
    _automatic_commentary_normalize,
    _automatic_commentary_stem,
    _automatic_commentary_topic_terms,
    _safe_automatic_commentary_angle,
    _requires_balanced_copy,
    balanced_copy_field_plan,
    _balanced_copy_satisfied,
    _without_automatic_commentary,
    _scoped_retry_authority,
    _automatic_commentary_authorizes_creator_voice,
)
from .grounded_metadata_editorial_framing import (
    _GENERIC_PERSON_SUBJECT_OPENING,
    _INVENTORY_CENTER_SUBJECT,
    _LEADING_SUBJECT_ARTICLE,
    _FIRST_PERSON_GENERIC_OBSERVER_OPENING,
    _timed_synthesis_drafts,
    _draft_temporal_role,
    _editorial_excerpt,
    _action_linked_candidate_subjects,
    _audience_frame,
    _narrative_case_fact_terms,
    narrative_presentation_has_distinct_fact,
    validate_narrative_case_fact_retention,
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
            **_draft_temporal_role(primary, primary, primary_ordinal, primary_ordinal),
            "environment": primary.get("environment"),
            "environmentUncertain": bool(primary.get("environmentUncertain")),
            "subjectsAndObjects": list(primary.get("subjectsAndObjects", [])),
            "actions": list(primary.get("actions", [])),
            "actorAuthority": actor_authority,
            "creatorExperienceRelation": creator_relation,
        },
        "chronologicalProgression": [
            {
                "ordinal": ordinal,
                **_draft_temporal_role(draft, primary, ordinal, primary_ordinal),
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
    automatic_angle = _safe_automatic_commentary_angle(request)
    if automatic_angle is not None:
        anchor["automaticCreatorCommentary"] = {
            "authority": "AutomaticUnreviewed",
            "authorityKind": "AttributedQuestionOrComparisonOnly",
            "safeCommentaryAngle": automatic_angle,
            "fieldAuthorizations": [],
            "exactQuotationPermitted": False,
            "creatorEmbodimentPermitted": False,
        }
        if _requires_balanced_copy(request):
            anchor["copyObjective"] = "BalancedActionAndCommentary"
            profile = request["profile"]
            anchor["copyProfile"] = {
                "audienceAddress": profile.get("audienceAddress"),
                "namingGuidance": profile.get("namingGuidance"),
                "defaultTags": list(profile.get("defaultTags", [])),
                "titleBodyMaximumCharacters": title_body_maximum(request["game"]["hashtag"]),
                "priorTitleExclusions": list(request.get("priorAcceptedTitles", [])),
                "gameplayVoice": "CreatorFirstPerson" if (
                    profile.get("voicePerspective") == "CreatorFirstPerson"
                    and actor_authority == "CreatorControlled"
                    and creator_relation == "CreatorActed"
                ) else "NeutralNoSubject",
            }
            claims = [dict(claim) for claim in (request.get("editorialBrief") or {}).get("claims", [])
                      if claim.get("state") in {"Confirmed", "Supported"}
                      and claim.get("fieldAuthorizations")
                      and claim.get("authority") != "AutomaticTranscriptCue"]
            if claims:
                anchor["groundedClaims"] = claims
                claim_ids = {claim.get("id") for claim in claims}
                anchor["groundedClaimBindings"] = [dict(binding)
                    for binding in (request.get("editorialBrief") or {}).get("sourceBindings", [])
                    if binding.get("claimId") in claim_ids]
            anchor["primaryVisual"]["hasUncertainties"] = bool(
                primary.get("hasUncertainties") or primary.get("uncertainties"))
            for item in anchor["chronologicalProgression"]:
                source = drafts[item["ordinal"] - 1]
                item["hasUncertainties"] = bool(
                    source.get("hasUncertainties") or source.get("uncertainties"))
                item["environmentUncertain"] = bool(source.get("environmentUncertain"))
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
    if _reviewed_commentary_authorizes_creator_voice(request, audience_copy) or (
        _automatic_commentary_authorizes_creator_voice(request, audience_copy)
    ):
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
