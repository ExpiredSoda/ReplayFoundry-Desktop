"""Stable contracts and identities for the grounded metadata pipeline."""
from __future__ import annotations

from dataclasses import dataclass
from decimal import Decimal
import hashlib
import json
import math
from typing import Any

from ..constants import GROUNDED_EDITORIAL_MAX_NEW_TOKENS
from .grounded_metadata_reroll_similarity import (
    RerollTitleReference,
    RerollTitleScope,
)
from .grounded_metadata_sampling import (
    CORE_FRAMES_PER_SECOND,
    CORE_MAXIMUM_FRAMES,
    CORE_MAXIMUM_PIXELS_PER_FRAME,
    CORE_MAXIMUM_TOTAL_VIDEO_PIXELS,
    CORE_MINIMUM_FRAMES,
    adaptive_sampling_plan,
)
from .grounded_metadata_synthesis_decoding import (
    POOL_SIZE as SYNTHESIS_RECOVERY_POOL_SIZE,
)
from .grounded_metadata_validation import (
    validation_failure_code as _validation_failure_code,
    validation_feedback as _validation_feedback,
)


MAXIMUM_NEW_TOKENS = GROUNDED_EDITORIAL_MAX_NEW_TOKENS
VISUAL_DRAFT_MAXIMUM_NEW_TOKENS = GROUNDED_EDITORIAL_MAX_NEW_TOKENS
MAXIMUM_ORDINARY_REFINEMENT_PASSES = 3
MAXIMUM_SYNTHESIS_GENERATIONS = (
    MAXIMUM_ORDINARY_REFINEMENT_PASSES + SYNTHESIS_RECOVERY_POOL_SIZE
)
METADATA_SCHEMA_VERSION = "grounded-editorial-metadata-json-schema-1.8"
METADATA_VIDEO_FPS = CORE_FRAMES_PER_SECOND
METADATA_VIDEO_MIN_FRAMES = CORE_MINIMUM_FRAMES
METADATA_VIDEO_MAX_FRAMES = CORE_MAXIMUM_FRAMES
METADATA_VIDEO_MAX_PIXELS_PER_FRAME = CORE_MAXIMUM_PIXELS_PER_FRAME
METADATA_VIDEO_TOTAL_PIXEL_BUDGET = CORE_MAXIMUM_TOTAL_VIDEO_PIXELS
GROUNDING_PACKET_SCHEMA_VERSION = "grounded-editorial-grounding-packet-1.0"
GROUNDING_REUSE_IDENTITY_VERSION = "grounded-editorial-grounding-reuse-identity-1.0"
STICKY_RETRY_INVALIDATING_RULES = frozenset({
    "UnsupportedCreatorEmbodiment",
    "UnsupportedInterfaceAttribution",
    "UnsupportedMentalState",
    "UnreviewedTranscriptReuse",
    "CrossDraftTitleContamination",
    "UnstableReadableTextReuse",
    "UncoupledKnowledgeReference",
    "UnsupportedKnowledgeGrounding",
    "UnresolvedVisualGrounding",
    "StrictOutputValidation",
})
GROUNDED_METADATA_MODULE_FILES = (
    ("pipeline", "grounded_metadata_pipeline.py"),
    ("pipelineContract", "grounded_metadata_pipeline_contract.py"),
    ("pipelineAttestation", "grounded_metadata_pipeline_attestation.py"),
    ("pipelineGrounding", "grounded_metadata_pipeline_grounding.py"),
    ("pipelineState", "grounded_metadata_pipeline_state.py"),
    ("pipelineRefinement", "grounded_metadata_pipeline_refinement.py"),
    ("pipelineRecovery", "grounded_metadata_pipeline_recovery.py"),
    (
        "pipelineRecoveryCandidates",
        "grounded_metadata_pipeline_recovery_candidates.py",
    ),
    ("pipelineResult", "grounded_metadata_pipeline_result.py"),
    ("editorialRephrase", "grounded_metadata_rephrase.py"),
    ("editorialRephraseMessages", "grounded_metadata_rephrase_messages.py"),
    ("synthesis", "grounded_metadata_synthesis.py"),
    ("synthesisMessages", "grounded_metadata_synthesis_messages.py"),
    ("generation", "grounded_metadata_generation.py"),
    ("jsonWhitespace", "grounded_metadata_json_whitespace.py"),
    ("validation", "grounded_metadata_validation.py"),
    ("audienceValidation", "grounded_metadata_audience_validation.py"),
    ("creatorAuthority", "grounded_metadata_creator_authority.py"),
    ("groundingValidation", "grounded_metadata_grounding_validation.py"),
    ("structuredDecoding", "structured_decoding.py"),
    ("recoveryPoolPolicy", "grounded_metadata_synthesis_decoding.py"),
)
@dataclass(frozen=True)
class GroundingPacket:
    """Immutable, in-memory visual facts shared by compatible synthesis attempts."""

    schema_version: str
    request_identity_sha256: str
    fact_sha256: str
    source_attempt: int
    grounding_pass_count: int
    grounding_elapsed_seconds: float
    canonical_facts: str

    def materialize_facts(self) -> dict[str, Any]:
        return json.loads(self.canonical_facts)


def _canonical_json(value: Any) -> str:
    """Serialize canonical JSON while preserving Decimal values as numbers."""
    if value is None:
        return "null"
    if value is True:
        return "true"
    if value is False:
        return "false"
    if isinstance(value, Decimal):
        if not value.is_finite():
            raise ValueError("Grounding identity requires finite decimals.")
        normalized = format(value, "f")
        if "." in normalized:
            normalized = normalized.rstrip("0").rstrip(".")
        return "0" if normalized in {"", "-0"} else normalized
    if isinstance(value, int) and not isinstance(value, bool):
        return str(value)
    if isinstance(value, float):
        if not math.isfinite(value):
            raise ValueError("Grounding identity requires finite numbers.")
        return json.dumps(value, ensure_ascii=False, allow_nan=False)
    if isinstance(value, str):
        return json.dumps(value, ensure_ascii=False)
    if isinstance(value, list):
        return "[" + ",".join(_canonical_json(item) for item in value) + "]"
    if isinstance(value, dict):
        if any(not isinstance(key, str) for key in value):
            raise TypeError("Grounding identity requires string object keys.")
        return "{" + ",".join(
            json.dumps(key, ensure_ascii=False)
            + ":"
            + _canonical_json(value[key])
            for key in sorted(value)
        ) + "}"
    raise TypeError(
        f"Unsupported grounding identity value: {type(value).__name__}."
    )


def _grounding_reuse_identity(
    request: dict[str, Any],
) -> tuple[str, str]:
    """Bind every shared fact input while leaving attempt intent per synthesis."""
    validated = request["_validated"]
    profile = {
        key: value
        for key, value in request["profile"].items()
        if key != "variantIntent"
    }
    identity = {
        "schemaVersion": GROUNDING_REUSE_IDENTITY_VERSION,
        "candidateId": request["candidateId"],
        "reviewVideo": {
            "path": str(validated["videoPath"]),
            "sha256": validated["expectedVideoHash"],
            "byteLength": validated["expectedVideoLength"],
            "lastWriteTimeUtc": validated["expectedLastWriteUtc"].isoformat(),
            "durationSeconds": validated["videoDuration"],
            "sourceAbsoluteOffsetSeconds": validated["sourceAbsoluteOffset"],
            "candidateStartSeconds": validated["candidateStart"],
            "candidateEndSeconds": validated["candidateEnd"],
        },
        "game": request["game"],
        "gameKnowledge": request["gameKnowledge"],
        "visualText": request["visualText"],
        "clip": request["clip"],
        "transcripts": request["transcripts"],
        "evidence": request["evidence"],
        "profileExceptVariantIntent": profile,
    }
    canonical = _canonical_json(identity)
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest(), canonical


def _new_grounding_packet(
    request: dict[str, Any],
    grounding_pass_count: int,
    grounding_elapsed_seconds: float,
    facts: dict[str, Any],
) -> GroundingPacket:
    request_identity_sha256, _ = _grounding_reuse_identity(request)
    canonical_facts = _canonical_json(facts)
    fact_identity = _canonical_json(
        {
            "schemaVersion": GROUNDING_PACKET_SCHEMA_VERSION,
            "requestIdentitySha256": request_identity_sha256,
            "facts": facts,
        }
    )
    return GroundingPacket(
        GROUNDING_PACKET_SCHEMA_VERSION,
        request_identity_sha256,
        hashlib.sha256(fact_identity.encode("utf-8")).hexdigest(),
        request["attempt"],
        grounding_pass_count,
        grounding_elapsed_seconds,
        canonical_facts,
    )


def _visual_windows(duration: float) -> list[tuple[float, float]]:
    return [
        (window.start_seconds, window.end_seconds)
        for window in adaptive_sampling_plan(duration)
    ]


def _duplicates_prior_synthesis(
    metadata: dict[str, Any],
    prior_successes: list[dict[str, Any]],
) -> bool:
    return bool(prior_successes) and metadata == prior_successes[-1]


def _reroll_title_scope(request: dict[str, Any]) -> RerollTitleScope:
    clip = request["clip"]
    return RerollTitleScope(
        request["candidateId"],
        clip["startSeconds"],
        clip["endSeconds"],
    )


def _reroll_title_reference(
    request: dict[str, Any],
    title: str,
) -> RerollTitleReference:
    return RerollTitleReference(
        _reroll_title_scope(request),
        title,
        request["game"]["hashtag"],
    )


def _combined_prior_title_references(
    request: dict[str, Any],
    host_prior_titles: tuple[RerollTitleReference, ...],
) -> tuple[RerollTitleReference, ...]:
    external = tuple(
        _reroll_title_reference(request, title)
        for title in request.get("priorAcceptedTitles", ())
    )
    combined: list[RerollTitleReference] = []
    identities: set[tuple[RerollTitleScope, str, str]] = set()
    for prior in (*external, *host_prior_titles):
        identity = (prior.scope, prior.title.casefold(), prior.game_hashtag)
        if identity in identities:
            continue
        identities.add(identity)
        combined.append(prior)
    return tuple(combined)


def _retry_guidance(rule_codes: tuple[str, ...]) -> str:
    unique_codes = tuple(dict.fromkeys(rule_codes))
    if not unique_codes:
        raise ValueError("Metadata retry guidance requires a typed rule.")
    guidance = list(dict.fromkeys(
        _validation_feedback(code) for code in unique_codes
    ))
    guidance.append(
        "preserve only observable completed action and remove unsupported intent, "
        "purpose, emotion, and unseen causality"
    )
    if "UnreviewedTranscriptReuse" in unique_codes:
        guidance.append(
            "automatic unreviewed transcript text is withheld from this retry, "
            "so use only the remaining grounded context"
        )
    return "; also, ".join(guidance)


def _retry_feedback(error: InferenceError) -> tuple[str, str]:
    code = _validation_failure_code(error)
    return code, _retry_guidance((code,))


def _retry_correction_envelope(error: InferenceError) -> dict[str, Any] | None:
    """Expose bounded parser diagnostics to the next pass as non-evidence."""
    string_fields = (
        ("rejectedTitleBody", "rejected_title_body"),
        ("rejectedDescription", "rejected_description"),
        ("offendingActionField", "offending_action_field"),
        ("offendingActionForm", "offending_action_form"),
    )
    envelope = {
        output_name: value
        for output_name, attribute_name in string_fields
        for value in [getattr(error, attribute_name, None)]
        if isinstance(value, str) and value
    }
    list_fields = (
        ("forbiddenReadableTextPhrases", "offending_readable_text_phrases"),
        ("affectedAudienceFields", "offending_readable_text_fields"),
    )
    for output_name, attribute_name in list_fields:
        raw_values = getattr(error, attribute_name, None)
        if not isinstance(raw_values, (list, tuple)):
            continue
        values = [
            value[:160]
            for value in raw_values[:8]
            if isinstance(value, str) and value
        ]
        if values:
            envelope[output_name] = values
    return {"nonEvidence": True, **envelope} if envelope else None


def _sticky_non_retrospective_envelope(
    error: InferenceError,
) -> dict[str, Any] | None:
    """Snapshot the first complete tense diagnostic as non-authoritative data."""
    current = _retry_correction_envelope(error)
    required = {
        "rejectedTitleBody",
        "offendingActionField",
        "offendingActionForm",
    }
    if current is None or not required.issubset(current):
        return None
    return {
        "nonEvidence": True,
        "nonAuthority": True,
        **{name: current[name] for name in sorted(required)},
    }


def _anchor_sha256(value: dict[str, Any]) -> str:
    canonical = json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
        allow_nan=False,
    )
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()
