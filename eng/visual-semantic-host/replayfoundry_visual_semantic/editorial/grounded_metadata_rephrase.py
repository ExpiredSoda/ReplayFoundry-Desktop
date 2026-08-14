"""One bounded text-only polish pass over already accepted grounded metadata."""
from __future__ import annotations

import hashlib
import json
from typing import Any

from ..commands import HOST_DIRECTORY
from ..errors import InferenceError, RerollTitleTooSimilarError
from .grounded_metadata_pipeline_attestation import _require_synthesis_attestation
from .grounded_metadata_pipeline_contract import MAXIMUM_NEW_TOKENS, _reroll_title_reference
from .grounded_metadata_pipeline_state import SynthesisContext, SynthesisFunctions, SynthesisProgress
from .grounded_metadata_reroll_similarity import evaluate_reroll_title
from .grounded_metadata_rephrase_messages import _rephrase_messages
from .grounded_metadata_lexical import contains_unapproved_non_latin
from .grounded_metadata_synthesis import _typed_retry_authority_anchor
from .grounded_metadata_validation import strict_metadata, validation_failure_code


POLICY_VERSION = "grounded-editorial-rephrase-2.0"
POLICY_FILE_NAME = "replayfoundry-grounded-editorial-rephrase-policy-2.0.txt"
POLICY_SHA256 = "556b11ad5535f4d16883a2a43bbd72ad83996520f4d6d8fc87d06615dccbba04"
OUTCOME_APPLIED = "Applied"
OUTCOME_NO_CHANGE = "RetainedOriginalNoMaterialChange"
OUTCOME_SEMANTIC_REJECTION = "RetainedOriginalSemanticRejection"

def require_policy() -> None:
    text = (HOST_DIRECTORY / POLICY_FILE_NAME).read_text(encoding="utf-8")
    normalized = text.replace("\r\n", "\n").replace("\r", "\n").strip()
    if hashlib.sha256(normalized.encode("utf-8")).hexdigest() != POLICY_SHA256:
        raise AssertionError("Grounded editorial rephrase policy source changed.")


def _attestation_context(
    source_sha256: str,
    source_kind: str,
    source_rejection_code: str | None,
) -> dict[str, Any]:
    return {
        "stage": "EditorialRephrase",
        "policyVersion": POLICY_VERSION,
        "policySha256": POLICY_SHA256,
        "sourceJsonSha256": source_sha256,
        "sourceKind": source_kind,
        "sourceRejectionCode": source_rejection_code,
        "seed": 0,
    }


def _generate_candidate(
    context: SynthesisContext,
    functions: SynthesisFunctions,
    source_json: str,
    source_kind: str,
    source_rejection_code: str | None,
) -> tuple[dict[str, Any], Any, Any, str, str, dict[str, Any], Any]:
    authority = _typed_retry_authority_anchor(
        context.synthesis_request,
        context.visual_drafts,
        context.primary_visual_draft_ordinal,
        context.primary_actor_authority,
        context.primary_creator_experience_relation,
    )
    messages = _rephrase_messages(
        source_json,
        authority,
        context.synthesis_request["profile"]["variantIntent"],
        source_kind,
        source_rejection_code,
    )
    source_sha256 = hashlib.sha256(source_json.encode("utf-8")).hexdigest()
    attestation_context = _attestation_context(
        source_sha256,
        source_kind,
        source_rejection_code,
    )
    (
        candidate_metadata,
        trace,
        audit,
        decoded_sha256,
        _,
        candidate_json,
        attestation,
    ) = functions.generate_rephrase_json_once(
        context.synthesis_request,
        context.case_ordinal,
        messages,
        context.model,
        context.processor,
        context.torch,
        context.torchcodec,
        context.process_vision_info,
        context.session,
        context.grammar,
        context.base_audit,
        MAXIMUM_NEW_TOKENS,
        lambda value: strict_metadata(
            value,
            context.synthesis_request,
            context.visual_drafts,
            context.primary_visual_draft_ordinal,
            context.primary_actor_authority,
            context.primary_creator_experience_relation,
        ),
        synthesis_attestation_context=attestation_context,
    )
    attestation = _finish_attestation(
        _require_synthesis_attestation(attestation, attestation_context)
    )
    try:
        _preserve_non_audience_fields(
            source_json,
            candidate_json,
            context.synthesis_request,
            source_rejection_code,
        )
    except InferenceError as error:
        error.schema_valid_rejected_json = candidate_json
        error.synthesis_attestation = attestation
        raise
    candidate_title = _reroll_title_reference(
        context.request,
        candidate_metadata["title"],
    )
    candidate_diversity = evaluate_reroll_title(
        candidate_title,
        context.all_prior_accepted_titles,
    )
    if not candidate_diversity.is_materially_distinct:
        error = RerollTitleTooSimilarError(
            "Grounded editorial rephrase became too similar to prior copy."
        )
        error.schema_valid_rejected_json = candidate_json
        error.synthesis_attestation = attestation
        raise error
    if candidate_metadata in context.visual_drafts:
        error = InferenceError(
            "Grounded editorial rephrase repeated analysis draft content."
        )
        error.schema_valid_rejected_json = candidate_json
        error.synthesis_attestation = attestation
        raise error
    return (
        candidate_metadata,
        trace,
        audit,
        decoded_sha256,
        candidate_json,
        attestation,
        candidate_diversity,
    )


def _finish_attestation(value: dict[str, Any]) -> dict[str, Any]:
    required_hashes = (
        "canonicalMessagesSha256",
        "renderedPromptSha256",
        "inputTokenIdsSha256",
        "outputSha256",
        "completedJsonSha256",
    )
    if any(
        not isinstance(value.get(name), str) or len(value[name]) != 64
        for name in required_hashes
    ) or not isinstance(value.get("renderedPromptUtf8ByteCount"), int) \
            or value["renderedPromptUtf8ByteCount"] <= 0 \
            or not isinstance(value.get("inputTokenCount"), int) \
            or value["inputTokenCount"] <= 0:
        raise AssertionError("Grounded editorial rephrase omitted exact attestation.")
    return dict(value)


def _preserve_non_audience_fields(
    source_json: str,
    candidate_json: str,
    request: dict[str, Any],
    source_rejection_code: str | None,
) -> None:
    source = json.loads(source_json)
    candidate = json.loads(candidate_json)
    for name in ("grounding", "temporalVoice"):
        if candidate.get(name) != source.get(name):
            raise InferenceError(
                f"Grounded editorial rephrase changed immutable field {name}."
            )
    if source_rejection_code != "OutputLanguage":
        if candidate.get("tags") != source.get("tags"):
            raise InferenceError(
                "Grounded editorial rephrase changed immutable field tags."
            )
        return
    source_tags = source.get("tags")
    candidate_tags = candidate.get("tags")
    if not isinstance(source_tags, list) or not isinstance(candidate_tags, list):
        raise InferenceError(
            "Grounded editorial rephrase changed immutable field tags."
        )
    retained_tags = [
        tag for tag in source_tags
        if not contains_unapproved_non_latin(tag, request)
    ]
    if not candidate_tags or candidate_tags != retained_tags:
        raise InferenceError(
            "Grounded editorial rephrase changed tags beyond the bounded "
            "non-English tag omission rule."
        )


def run_editorial_rephrase(
    context: SynthesisContext,
    functions: SynthesisFunctions,
    progress: SynthesisProgress,
) -> None:
    if progress.metadata is None or not progress.completed_json:
        raise AssertionError("Editorial rephrase requires accepted grounded metadata.")
    require_policy()
    original_metadata = progress.metadata
    original_diversity = progress.diversity_result
    source_json = progress.completed_json
    source_rejection_code = (
        progress.metadata_review_issues[0]
        if progress.metadata_review_issues
        else None
    )
    source_kind = (
        "ReviewRequiredMetadata"
        if source_rejection_code is not None
        else "AcceptedMetadata"
    )
    source_sha256 = hashlib.sha256(source_json.encode("utf-8")).hexdigest()
    attestation_context = _attestation_context(
        source_sha256,
        source_kind,
        source_rejection_code,
    )
    progress.editorial_rephrase_attempted = True
    try:
        (
            candidate_metadata,
            _,
            _,
            _,
            candidate_json,
            attestation,
            candidate_diversity,
        ) = _generate_candidate(
            context,
            functions,
            source_json,
            source_kind,
            source_rejection_code,
        )
    except InferenceError as error:
        rejected_json = getattr(error, "schema_valid_rejected_json", None)
        raw_attestation = getattr(error, "synthesis_attestation", None)
        if not isinstance(rejected_json, str) or raw_attestation is None:
            raise
        progress.editorial_rephrase_applied = False
        progress.editorial_rephrase_outcome = OUTCOME_SEMANTIC_REJECTION
        message = str(error)
        progress.editorial_rephrase_rejection_code = (
            "ImmutableFieldsChanged"
            if "changed immutable field" in message
            else "RepeatedAnalysisDraft"
            if "repeated analysis draft" in message
            else validation_failure_code(error)
        )
        progress.editorial_rephrase_output_json_sha256 = hashlib.sha256(
            rejected_json.encode("utf-8")
        ).hexdigest()
        progress.editorial_rephrase_attestation = _finish_attestation(
            _require_synthesis_attestation(raw_attestation, attestation_context)
        )
        return

    progress.editorial_rephrase_attestation = attestation
    progress.editorial_rephrase_output_json_sha256 = hashlib.sha256(
        candidate_json.encode("utf-8")
    ).hexdigest()
    if candidate_json == source_json:
        progress.editorial_rephrase_applied = False
        progress.editorial_rephrase_outcome = OUTCOME_NO_CHANGE
        return
    progress.editorial_rephrase_applied = True
    progress.editorial_rephrase_outcome = OUTCOME_APPLIED
    progress.metadata = candidate_metadata
    progress.completed_json = candidate_json
    progress.diversity_result = candidate_diversity
    progress.metadata_review_issues = []
__all__ = [name for name in globals() if not name.startswith("__")]
