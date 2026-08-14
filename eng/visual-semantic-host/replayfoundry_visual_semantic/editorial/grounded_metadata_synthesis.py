"""Prompt identity and bounded context for grounded metadata synthesis."""
from __future__ import annotations

import hashlib
import re
import unicodedata
from typing import Any

from ..commands import HOST_DIRECTORY, UsageOrInputError, _fail
from .grounded_metadata_audience_validation import contains_unsupported_mental_state
from .grounded_metadata_validation import grounding_binding_id
from .grounded_metadata_lexical import normalize_lexical, readable_text_fragments

PROMPT_NAME = "ReplayFoundry Grounded Editorial Metadata"
PROMPT_VERSION = "1.37"
PROMPT_SHA256 = "f7952e452ef7d8ac2b586cd96fcd21b779bb06c891656fa47687644be6310dbf"
STABLE_READABLE_TEXT_POLICY_VERSION = "1.0"
SYNTHESIS_EVIDENCE_POLICY_VERSION = (
    "grounded-editorial-synthesis-evidence-1.0"
)


def _readable_text_key(value: str) -> tuple[str, str] | None:
    normalized = " ".join(
        unicodedata.normalize("NFKC", value).split()
    ).strip()
    if len(normalized) < 4 or not any(character.isalpha() for character in normalized):
        return None
    return normalized.lower(), normalized


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


def _contains_readable_fragment(value: str, readable_values: list[str]) -> bool:
    normalized_value = " " + normalize_lexical(value) + " "
    for readable in readable_values:
        for fragment in readable_text_fragments(readable):
            if " " + fragment + " " in normalized_value:
                return True
    return False


def _redact_embedded_readable_text(value: str) -> str:
    redacted = re.sub(
        r"(?i)\b(?:reading|reads|labelled|labeled|displaying|displays)\b.*$",
        "",
        value,
    )
    redacted = re.sub(
        r"(['\"])(?:[^'\"]*\s){3,}[^'\"]*\1",
        "",
        redacted,
    )
    return redacted.rstrip(" ,;:-").strip()


_UNSUPPORTED_ACTOR_ROLE = re.compile(
    r"\b(?:player|character|streamer|creator|camera\s+wearer)\b",
    re.IGNORECASE,
)
_UNSUPPORTED_ACTOR_ROLE_PREFIX = re.compile(
    r"^\s*(?:(?:a|an|the)\s+)?"
    r"(?:player[-\s]+controlled\s+)?"
    r"(?:player|character|streamer|creator|camera\s+wearer)\b"
    r"[\s,:;-]*",
    re.IGNORECASE,
)


def _strip_unsupported_actor_role(value: str) -> str:
    projected = _UNSUPPORTED_ACTOR_ROLE_PREFIX.sub("", value).strip()
    return "" if _UNSUPPORTED_ACTOR_ROLE.search(projected) else projected


def _safe_synthesis_text(value: str) -> str:
    sanitized = _strip_unsupported_actor_role(
        _redact_embedded_readable_text(value)
    )
    return (
        ""
        if contains_unsupported_mental_state(sanitized)
        else sanitized
    )


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
    unstable = [
        value
        for value in draft.get("readableText", [])
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
            sanitized = _safe_synthesis_text(value)
            if sanitized and not _contains_readable_fragment(sanitized, unstable):
                result[key] = sanitized
            continue
        if isinstance(value, list):
            result[key] = [
                sanitized
                for item in value
                for sanitized in [
                    _safe_synthesis_text(item)
                    if isinstance(item, str)
                    else item
                ]
                if not isinstance(sanitized, str)
                or (
                    sanitized
                    and not _contains_readable_fragment(sanitized, unstable)
                )
            ]
            continue
        result[key] = value
    return result


def _prompt_text() -> str:
    path = HOST_DIRECTORY / "replayfoundry-editorial-metadata-prompt-1.37.txt"
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
    return {
        "game": (
            {
                "name": request["game"]["name"],
                "hashtag": request["game"]["hashtag"],
                "notes": (
                    request["game"]["notes"]
                    if include_game_notes
                    else None
                ),
                "notesAuthority": (
                    request["game"]["source"]
                    if include_game_notes and request["game"]["notes"] is not None
                    else "None"
                ),
            }
            if include_game_identity
            else {"identityWithheldForSafety": True}
        ),
        "transcripts": [
            {
                "role": item["role"],
                "authority": item["authority"],
                "text": item["text"],
            }
            for item in request["transcripts"]
            if include_clip_context
            and (
                include_unreviewed_transcripts
                or item["authority"] != "AutomaticUnreviewed"
            )
        ],
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
            if request["gameKnowledge"] is None or not include_game_knowledge
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
    """Return bounded affirmative evidence for a grammar-only retry.

    This deliberately excludes source paths, transcript text, OCR/readable text,
    and unselected knowledge.  It is repeated at the end of a retry solely to
    keep the model's attention on unchanged typed authority; it does not widen
    that authority.
    """
    primary = grounded_drafts[primary_visual_draft_ordinal - 1]
    sanitized_primary = _synthesis_draft(primary, [])
    anchor: dict[str, Any] = {
        "authorityKind": "BoundedTypedRetryAuthority",
        "primaryVisual": {
            "ordinal": primary_visual_draft_ordinal,
            "environment": sanitized_primary.get("environment"),
            "environmentUncertain": bool(primary.get("environmentUncertain")),
            "subjectsAndObjects": list(
                sanitized_primary.get("subjectsAndObjects", [])
            ),
            "actions": list(sanitized_primary.get("actions", [])),
            "actorAuthority": primary_actor_authority,
            "creatorExperienceRelation":
                primary_creator_experience_relation,
        },
    }
    game = request["game"]
    notes = game.get("notes")
    notes_authority = game.get("source")
    if (
        isinstance(notes, str)
        and notes
        and notes_authority in {"UserConfirmed", "ReusedUserMemory"}
    ):
        anchor["userGameContext"] = {
            "authority": notes_authority,
            "notes": notes,
        }

    selected_matches = [
        item
        for item in (request.get("gameKnowledge") or {}).get("matches", [])
        if item.get("temporalRelation") == "CurrentEventCandidate"
    ]
    if selected_matches:
        anchor["selectedGameKnowledge"] = [
            {
                "id": item["id"],
                "section": item["section"],
                "text": item["text"],
                "strength": item["strength"],
                "temporalRelation": item["temporalRelation"],
                "clipEvidenceIds": item["clipEvidenceIds"],
                "authorizedBindingIds": [
                    grounding_binding_id(item["id"], evidence_id)
                    for evidence_id in item["clipEvidenceIds"]
                ],
            }
            for item in selected_matches
        ]
    general_context = [
        item
        for item in (request.get("gameKnowledge") or {}).get("matches", [])
        if item.get("strength") == "GeneralContext"
        and item.get("temporalRelation") == "Unspecified"
    ]
    if general_context:
        anchor["generalGameContext"] = [
            {
                "id": item["id"],
                "section": item["section"],
                "text": item["text"],
                "strength": item["strength"],
                "temporalRelation": item["temporalRelation"],
            }
            for item in general_context
        ]
    return anchor


def _variant_intent_guidance(variant_intent: str) -> str:
    policies = {
        "DirectAction": (
            "Foreground one completed physical action visibly supported by the "
            "selected primary event."
        ),
        "SpecificCuriosity": (
            "Foreground a concrete unusual visible situation, entity, object, or "
            "unresolved visual detail from the selected primary event without "
            "inventing an answer, motive, cause, or off-screen fact."
        ),
        "OutcomeFocused": (
            "Foreground a completed visible outcome only when the selected primary "
            "event supplies it; otherwise use another supported completed action "
            "and do not manufacture an outcome."
        ),
        "ConcreteDetail": (
            "Foreground a supported canonical entity, concrete object, or visible "
            "setting detail from the selected primary event and connect it to a "
            "completed supported action."
        ),
        "CommentaryLed": (
            "Foreground only creator commentary authorized by a HumanReviewed or "
            "UserCorrected transcript and keep every visual claim independently "
            "supported."
        ),
    }
    try:
        return policies[variant_intent]
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
