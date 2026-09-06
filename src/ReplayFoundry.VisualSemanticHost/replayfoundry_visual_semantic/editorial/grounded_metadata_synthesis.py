"""Prompt identity and bounded context for grounded metadata synthesis."""
from __future__ import annotations

import hashlib
from typing import Any

from ..commands import HOST_DIRECTORY, UsageOrInputError, _fail
from .grounded_metadata_creator_authority import (
    build_typed_editorial_authority,
    _requires_balanced_copy,
    _without_automatic_commentary,
)
from .grounded_metadata_rephrase_messages import (
    EDITORIAL_FRAME_VARIANT_PRIORITY,
)
from .grounded_metadata_synthesis_sanitization import (
    _GENERIC_HUMAN_OBJECT,
    _contains_exact_readable_text,
    _quoted_readable_values,
    _safe_synthesis_text,
)
from .grounded_metadata_validation import grounding_binding_id
from .grounded_metadata_lexical import (
    contains_readable_fragment as _contains_readable_fragment,
    readable_text_key as _readable_text_key,
)

PROMPT_NAME = "ReplayFoundry Grounded Editorial Metadata"
PROMPT_VERSION = "1.46"
PROMPT_SHA256 = "61ad677ba7cb97a250df90bf77aa0fcaeb27dcd226af871b7226d89b1cf6b2d0"
STABLE_READABLE_TEXT_POLICY_VERSION = "1.0"
SYNTHESIS_EVIDENCE_POLICY_VERSION = (
    "grounded-editorial-synthesis-evidence-1.0"
)


def _stable_readable_text(
    grounded_drafts: list[dict[str, Any]],
) -> list[str]:
    first_values: dict[str, str] = {}
    draft_ordinals: dict[str, set[int]] = {}
    for ordinal, draft in enumerate(grounded_drafts, start=1):
        for value in draft["readableText"]:
            keyed = _readable_text_key(value)
            if keyed is None:
                continue
            key, normalized = keyed
            first_values.setdefault(key, normalized)
            draft_ordinals.setdefault(key, set()).add(ordinal)
    return [
        first_values[key]
        for key in first_values
        if len(draft_ordinals[key]) >= 2
    ][:4]


def _synthesis_draft(
    draft: dict[str, Any],
    stable_readable_text: list[str],
) -> dict[str, Any]:
    stable_keys = {
        keyed[0]
        for value in stable_readable_text
        for keyed in [_readable_text_key(value)]
        if keyed is not None
    }
    quoted_readable = [
        quoted
        for value in draft.values()
        for item in (value if isinstance(value, list) else [value])
        if isinstance(item, str)
        for quoted in _quoted_readable_values(item)
    ]
    unstable_quoted = [
        value
        for value in quoted_readable
        if (
            (keyed := _readable_text_key(value)) is not None
            and keyed[0] not in stable_keys
        )
    ]
    readable_candidates = [*draft.get("readableText", []), *quoted_readable]
    unstable = [
        value
        for value in readable_candidates
        if isinstance(value, str)
        and (
            (keyed := _readable_text_key(value)) is not None
            and keyed[0] not in stable_keys
        )
    ]
    result: dict[str, Any] = {}
    for key, value in draft.items():
        if key in {"readableText", "uncertainties"} or (
            key == "environment" and draft.get("environmentUncertain")
        ):
            continue
        if isinstance(value, str):
            sanitized = _safe_synthesis_text(value, stable_readable_text)
            if sanitized and not _contains_readable_fragment(sanitized, unstable):
                result[key] = sanitized
            continue
        if isinstance(value, list):
            result[key] = [
                sanitized
                for item in value
                for sanitized in [
                    _safe_synthesis_text(item, stable_readable_text)
                    if isinstance(item, str)
                    else item
                ]
                if not isinstance(sanitized, str)
                or (
                    sanitized
                    and not (
                        key == "subjectsAndObjects"
                        and _GENERIC_HUMAN_OBJECT.search(sanitized)
                    )
                    and not _contains_exact_readable_text(
                        sanitized,
                        unstable_quoted,
                    )
                    and not _contains_readable_fragment(sanitized, unstable)
                )
            ]
            continue
        result[key] = value
    return result


def _prompt_text() -> str:
    path = HOST_DIRECTORY / "replayfoundry-editorial-metadata-prompt-1.46.txt"
    text = path.read_text(encoding="utf-8").replace("\r\n", "\n").replace("\r", "\n").strip()
    if hashlib.sha256(text.encode("utf-8")).hexdigest() != PROMPT_SHA256:
        _fail(UsageOrInputError, "Grounded metadata prompt source changed.")
    return text


def _model_context(
    request: dict[str, Any],
    include_game_knowledge: bool = True,
    include_clip_context: bool = True,
    include_unreviewed_transcripts: bool = True,
    include_game_identity: bool = True,
    include_game_notes: bool = True,
    primary_actor_authority: str = "Unknown",
    primary_creator_experience_relation: str = "Unestablished",
) -> dict[str, Any]:
    """Return only audience-facing grounding, never analysis bookkeeping."""
    if not include_unreviewed_transcripts or not include_clip_context:
        request = _without_automatic_commentary(request)
    game = request["game"]
    game_source = game.get("source", "UserConfirmed")
    has_confirmed_identity = game_source in {
        "UserConfirmed",
        "ReusedUserMemory",
    }
    return {
        "game": (
            {
                "name": game["name"],
                "hashtag": game["hashtag"],
                "notes": (
                    game["notes"]
                    if include_game_notes
                    else None
                ),
                "notesAuthority": (
                    game_source
                    if include_game_notes and game["notes"] is not None
                    else "None"
                ),
            }
            if include_game_identity and has_confirmed_identity
            else {
                "identityWithheldForSafety": True,
                "identityAuthority": game_source,
            }
        ),
        "transcripts": [
            {
                "role": item["role"],
                "authority": item["authority"],
                "text": item["text"],
                "timedSpans": item.get("spans", []),
            }
            for item in request["transcripts"]
            if include_clip_context
            and (
                include_unreviewed_transcripts
                or item["authority"] != "AutomaticUnreviewed"
            )
        ],
        "editorialBrief": request.get("editorialBrief"),
        "editorialFraming": request.get("_editorialFraming"),
        "visualObservations": [
            {"description": item["description"]}
            for item in request["evidence"]
            if include_clip_context and item["kind"] == "VisualObservation"
        ],
        "visualTextAnchors": (
            []
            if not include_clip_context or request.get("visualText") is None
            else [
                {
                    "text": item["text"],
                    "occurrenceCount": item["occurrenceCount"],
                }
                for item in request["visualText"]["groundingAnchors"]
            ]
        ),
        "gameKnowledge": (
            None
            if request["gameKnowledge"] is None
            or not include_game_knowledge
            or not has_confirmed_identity
            else {
                "sources": [
                    {
                        "id": item["id"],
                        "role": item["role"],
                        "title": item["title"],
                        "revisionId": item["revisionId"],
                        "licenseIdentifier": item["licenseIdentifier"],
                        "attribution": item["attribution"],
                    }
                    for item in request["gameKnowledge"]["sources"]
                ],
                "matches": [
                    {
                        "id": item["id"],
                        "sourceId": item["sourceId"],
                        "section": item["section"],
                        "text": item["text"],
                        "strength": item["strength"],
                        "temporalRelation": item["temporalRelation"],
                        "matchedTerms": item["matchedTerms"],
                        "clipEvidenceIds": item["clipEvidenceIds"],
                        "authorizedBindingIds": [
                            grounding_binding_id(item["id"], evidence_id)
                            for evidence_id in item["clipEvidenceIds"]
                        ],
                    }
                    for item in request["gameKnowledge"]["matches"]
                ],
            }
        ),
        "profile": {
            "copyObjective": "BalancedActionAndCommentary" if _requires_balanced_copy(request) else "FollowVariant",
            "audienceAddress": request["profile"]["audienceAddress"],
            "namingGuidance": request["profile"]["namingGuidance"],
            "defaultTags": request["profile"]["defaultTags"],
            "voicePerspective": _effective_voice_perspective(
                request["profile"]["voicePerspective"],
                primary_actor_authority,
                primary_creator_experience_relation,
            ),
            "variantIntent": request["profile"]["variantIntent"],
        },
    }


def _effective_voice_perspective(
    requested_voice_perspective: str,
    actor_authority: str,
    creator_experience_relation: str,
) -> str:
    """Prevent a style preference from claiming unsupported creator agency."""
    if requested_voice_perspective != "CreatorFirstPerson":
        return "NeutralNoSubject"
    if (
        actor_authority == "CreatorControlled"
        and creator_experience_relation == "CreatorActed"
    ) or creator_experience_relation in {"CreatorAffected", "CreatorEncountered"}:
        return "CreatorFirstPerson"
    return "NeutralNoSubject"


def _typed_retry_authority_anchor(
    request: dict[str, Any],
    grounded_drafts: list[dict[str, Any]],
    primary_visual_draft_ordinal: int,
    primary_actor_authority: str,
    primary_creator_experience_relation: str,
) -> dict[str, Any]:
    """Return bounded affirmative evidence for retry and editorial shaping.

    This deliberately excludes source paths, automatic transcript text,
    single-window OCR, and unselected knowledge. Repeated readable text remains
    available as explicitly typed stable evidence. The final rephrase therefore
    stays on the same facts while retaining useful document and objective names.
    """
    stable_readable_text = _stable_readable_text(grounded_drafts)
    synthesis_drafts = [
        _synthesis_draft(draft, stable_readable_text)
        for draft in grounded_drafts
    ]
    if _requires_balanced_copy(request):
        # Retain the existence of caution without turning speculative free-text
        # alternatives (names, intentions, unseen events) into authoring facts.
        for source, projected in zip(grounded_drafts, synthesis_drafts):
            projected["hasUncertainties"] = bool(source.get("uncertainties"))
    return build_typed_editorial_authority(
        request,
        synthesis_drafts,
        primary_visual_draft_ordinal,
        primary_actor_authority,
        primary_creator_experience_relation,
        stable_readable_text,
        grounding_binding_id,
    )


def _variant_intent_guidance(variant_intent: str) -> str:
    policies = {
        "DirectAction": (
            "Open with the clearest completed physical action inside that already-"
            "selected beat."
        ),
        "SpecificCuriosity": (
            "Create interest around one concrete supported aspect of that already-"
            "selected beat without inventing an answer, motive, cause, or off-screen "
            "fact."
        ),
        "OutcomeFocused": (
            "Lead with a completed visible outcome only when that already-selected "
            "beat supplies it; otherwise keep the beat and do not manufacture an "
            "outcome."
        ),
        "ConcreteDetail": (
            "Use one grounded detail as the hook only when it clarifies that already-"
            "selected beat, then express the beat itself. For a document, menu, "
            "objective, cinematic, or recording, keep the detail subordinate to the "
            "supported review, choice, progress, reveal, or exposition role; never "
            "reduce the copy to an object, person, or display inventory."
        ),
        "CommentaryLed": (
            "Foreground only creator commentary authorized by a HumanReviewed or "
            "UserCorrected transcript and keep every visual claim independently "
            "supported."
        ),
    }
    try:
        return EDITORIAL_FRAME_VARIANT_PRIORITY + " " + policies[variant_intent]
    except KeyError:
        raise ValueError(
            f"Unsupported grounded metadata variant intent: {variant_intent}"
        ) from None


def _metadata_messages(
    request: dict[str, Any],
    prompt_text: str,
    validation_feedback: str | None = None,
    grounded_drafts: list[dict[str, Any]] | None = None,
    primary_visual_draft_ordinal: int = 1,
    withhold_unreviewed_transcripts: bool = False,
    primary_only_evidence: bool = False,
    primary_actor_authority: str = "Unknown",
    primary_creator_experience_relation: str = "Unestablished",
    prior_accepted_title_bodies: tuple[str, ...] = (),
    schema_valid_rejected_json: str | None = None,
    rejected_rule_codes: tuple[str, ...] = (),
    duplicate_synthesis_recovery_applied: bool = False,
    retry_correction_envelope: dict[str, Any] | None = None,
    sticky_non_retrospective_envelope: dict[str, Any] | None = None,
    typed_retry_authority_anchor: dict[str, Any] | None = None,
    withhold_rejected_audience_copy: bool = False,
) -> list[dict[str, Any]]:
    """Build the frozen synthesis messages through the focused implementation."""
    from .grounded_metadata_synthesis_messages import (
        _metadata_messages as _metadata_messages_impl,
    )

    return _metadata_messages_impl(
        request, prompt_text, validation_feedback, grounded_drafts,
        primary_visual_draft_ordinal, withhold_unreviewed_transcripts,
        primary_only_evidence, primary_actor_authority,
        primary_creator_experience_relation, prior_accepted_title_bodies,
        schema_valid_rejected_json, rejected_rule_codes,
        duplicate_synthesis_recovery_applied, retry_correction_envelope,
        sticky_non_retrospective_envelope, typed_retry_authority_anchor,
        withhold_rejected_audience_copy)
