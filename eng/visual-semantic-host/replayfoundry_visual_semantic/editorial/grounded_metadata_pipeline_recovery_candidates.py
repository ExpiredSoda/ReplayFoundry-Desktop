"""Bounded seeded candidates for grounded metadata recovery."""
from __future__ import annotations

import hashlib
import json
from typing import Any

from ..commands import (
    InferenceError,
    _add_failure_diagnostic,
    _append_failure_recovery_pool_ledger,
)
from ..errors import RerollTitleTooSimilarError
from .grounded_metadata_pipeline_attestation import (
    _finish_synthesis_attestation,
    _require_complete_pool_candidate_attestation,
    _require_synthesis_attestation,
    _synthesis_attestation_context,
)
from .grounded_metadata_pipeline_contract import (
    MAXIMUM_NEW_TOKENS,
    _reroll_title_reference,
)
from .grounded_metadata_pipeline_state import (
    SynthesisContext,
    SynthesisFunctions,
    SynthesisProgress,
)
from .grounded_metadata_reroll_similarity import (
    evaluate_reroll_title,
)
from .grounded_metadata_synthesis_decoding import (
    LOGICAL_PASS_ORDINAL as SYNTHESIS_RECOVERY_POOL_LOGICAL_PASS_ORDINAL,
    RETRYABLE_COMPLETED_SEMANTIC_REJECTIONS,
    SYNTHESIS_RECOVERY_POOL_DECODINGS,
)
from .grounded_metadata_validation import (
    reviewable_metadata as _reviewable_metadata,
)


def run_recovery_candidates(
    context: SynthesisContext,
    functions: SynthesisFunctions,
    progress: SynthesisProgress,
    recovery_messages: list[dict[str, Any]] | None,
) -> None:
    if recovery_messages is None:
        return
    request = context.request
    synthesis_request = context.synthesis_request
    case_ordinal = context.case_ordinal
    visual_drafts = context.visual_drafts
    primary_visual_draft_ordinal = context.primary_visual_draft_ordinal
    primary_actor_authority = context.primary_actor_authority
    primary_creator_experience_relation = (
        context.primary_creator_experience_relation
    )
    all_prior_accepted_titles = context.all_prior_accepted_titles
    model = context.model
    processor = context.processor
    torch = context.torch
    torchcodec = context.torchcodec
    process_vision_info = context.process_vision_info
    session = context.session
    grammar = context.grammar
    base_audit = context.base_audit

    rejected_rules = progress.rejected_rules
    synthesis_recovery_pool_source_pass_ordinal = (
        progress.synthesis_recovery_pool_source_pass_ordinal
    )
    synthesis_recovery_pool_source_rejected_json_sha256 = (
        progress.synthesis_recovery_pool_source_rejected_json_sha256
    )
    synthesis_recovery_pool_source_selection_reason = (
        progress.synthesis_recovery_pool_source_selection_reason
    )
    synthesis_recovery_pool_selected_candidate_ordinal = (
        progress.synthesis_recovery_pool_selected_candidate_ordinal
    )
    synthesis_recovery_pool_attempted_candidate_count = (
        progress.synthesis_recovery_pool_attempted_candidate_count
    )
    sticky_retry_envelope = progress.sticky_retry_envelope
    sticky_retry_anchor_applied = progress.sticky_retry_anchor_applied
    sticky_retry_envelope_sha256 = progress.sticky_retry_envelope_sha256
    sticky_retry_authority_sha256 = progress.sticky_retry_authority_sha256
    synthesis_pass_attestations = progress.synthesis_pass_attestations
    synthesis_pass_count = progress.synthesis_pass_count
    diversity_result = progress.diversity_result
    metadata = progress.metadata
    trace = progress.trace
    audit = progress.audit
    decoded_sha256 = progress.decoded_sha256
    completed_json = progress.completed_json
    metadata_review_issues = progress.metadata_review_issues

    last_pool_error: InferenceError | None = None
    pool_messages_sha256 = hashlib.sha256(
        json.dumps(
            recovery_messages,
            ensure_ascii=False,
            sort_keys=True,
            separators=(",", ":"),
            allow_nan=False,
        ).encode("utf-8")
    ).hexdigest()
    pool_prompt_sha256: str | None = None
    pool_prompt_bytes: int | None = None
    for synthesis_decoding in SYNTHESIS_RECOVERY_POOL_DECODINGS:
        synthesis_recovery_pool_attempted_candidate_count += 1
        attestation_context = _synthesis_attestation_context(
            SYNTHESIS_RECOVERY_POOL_LOGICAL_PASS_ORDINAL,
            synthesis_decoding.candidate_ordinal,
            "RecoveryPool",
            synthesis_decoding.seed,
            synthesis_recovery_pool_source_pass_ordinal,
            synthesis_recovery_pool_source_rejected_json_sha256,
            synthesis_recovery_pool_source_selection_reason,
            sticky_retry_envelope is not None,
            None,
            sticky_retry_envelope_sha256,
            sticky_retry_authority_sha256,
        )
        try:
            (
                metadata,
                trace,
                audit,
                decoded_sha256,
                _,
                completed_json,
                synthesis_attestation,
            ) = functions.generate_json_once(
                synthesis_request,
                case_ordinal,
                recovery_messages,
                model,
                processor,
                torch,
                torchcodec,
                process_vision_info,
                session,
                grammar,
                base_audit,
                MAXIMUM_NEW_TOKENS,
                lambda value: _reviewable_metadata(
                    value,
                    synthesis_request,
                    visual_drafts,
                    primary_visual_draft_ordinal,
                    primary_actor_authority,
                    primary_creator_experience_relation,
                ),
                synthesis_decoding=synthesis_decoding,
                synthesis_attestation_context=attestation_context,
            )
            metadata_review_issues = metadata.pop("_reviewIssues", [])
            synthesis_attestation = _require_synthesis_attestation(
                synthesis_attestation,
                attestation_context,
            )
            if (
                synthesis_attestation["canonicalMessagesSha256"]
                != pool_messages_sha256
            ):
                raise AssertionError(
                    "Recovery-pool attestation did not match its immutable messages."
                )
            if pool_prompt_sha256 is None:
                pool_prompt_sha256 = synthesis_attestation[
                    "renderedPromptSha256"
                ]
                pool_prompt_bytes = synthesis_attestation[
                    "renderedPromptUtf8ByteCount"
                ]
            elif (
                synthesis_attestation["renderedPromptSha256"]
                != pool_prompt_sha256
                or synthesis_attestation["renderedPromptUtf8ByteCount"]
                != pool_prompt_bytes
            ):
                raise AssertionError(
                    "Recovery-pool candidates did not receive identical messages."
                )
            candidate_title = _reroll_title_reference(
                request,
                metadata["title"],
            )
            diversity_result = evaluate_reroll_title(
                candidate_title,
                all_prior_accepted_titles,
            )
            if not diversity_result.is_materially_distinct:
                error = RerollTitleTooSimilarError(
                    "Grounded metadata reroll title was materially indistinct "
                    "from accepted audience copy for this candidate and exact cut."
                )
                error.schema_valid_rejected_json = completed_json
                error.synthesis_attestation = synthesis_attestation
                raise error
        except InferenceError as error:
            raw_error_attestation = getattr(
                error,
                "synthesis_attestation",
                None,
            )
            if raw_error_attestation is None:
                _add_failure_diagnostic(
                    "Recovery-pool synthesis terminated before a completed "
                    "output attestation "
                    + json.dumps(
                        attestation_context,
                        sort_keys=True,
                        separators=(",", ":"),
                    )
                )
                raise
            error_attestation = _require_synthesis_attestation(
                raw_error_attestation,
                attestation_context,
            )
            if (
                error_attestation["canonicalMessagesSha256"]
                != pool_messages_sha256
            ):
                raise AssertionError(
                    "Recovery-pool attestation did not match its immutable messages."
                )
            if pool_prompt_sha256 is None:
                pool_prompt_sha256 = error_attestation[
                    "renderedPromptSha256"
                ]
                pool_prompt_bytes = error_attestation[
                    "renderedPromptUtf8ByteCount"
                ]
            elif (
                error_attestation["renderedPromptSha256"]
                != pool_prompt_sha256
                or error_attestation["renderedPromptUtf8ByteCount"]
                != pool_prompt_bytes
            ):
                raise AssertionError(
                    "Recovery-pool candidates did not receive identical messages."
                )
            code = functions.validation_failure_code(error)
            rejected_completed_json = getattr(
                error,
                "schema_valid_rejected_json",
                None,
            )
            _require_complete_pool_candidate_attestation(
                error_attestation,
                rejected_completed_json,
            )
            synthesis_pass_count += 1
            finished_attestation = _finish_synthesis_attestation(
                error_attestation,
                rejection_code=code,
                accepted=False,
            )
            synthesis_pass_attestations.append(finished_attestation)
            _append_failure_recovery_pool_ledger(finished_attestation)
            if code not in RETRYABLE_COMPLETED_SEMANTIC_REJECTIONS:
                raise
            rejected_rules.append(code)
            last_pool_error = error
            continue
        _require_complete_pool_candidate_attestation(
            synthesis_attestation,
            completed_json,
        )
        synthesis_pass_count += 1
        finished_attestation = _finish_synthesis_attestation(
            synthesis_attestation,
            rejection_code=None,
            accepted=True,
        )
        synthesis_pass_attestations.append(finished_attestation)
        _append_failure_recovery_pool_ledger(finished_attestation)
        synthesis_recovery_pool_selected_candidate_ordinal = (
            synthesis_decoding.candidate_ordinal
        )
        sticky_retry_anchor_applied = (
            sticky_retry_anchor_applied
            or sticky_retry_envelope is not None
        )
        break
    else:
        if last_pool_error is None:
            raise AssertionError("Recovery pool ended without a typed rejection.")
        # A completed schema-valid reroll is still useful audience copy.  Keep
        # the last bounded interpretation and surface the diversity finding to
        # Studio/Publish instead of discarding the entire generated clip.
        if (
            metadata is None
            or trace is None
            or audit is None
            or decoded_sha256 is None
            or completed_json is None
        ):
            raise last_pool_error
        review_code = functions.validation_failure_code(last_pool_error)
        if review_code not in metadata_review_issues:
            metadata_review_issues.append(review_code)
        if rejected_rules and rejected_rules[-1] == review_code:
            rejected_rules.pop()
        synthesis_pass_attestations[-1] = _finish_synthesis_attestation(
            error_attestation,
            rejection_code=None,
            accepted=True,
        )
        synthesis_recovery_pool_selected_candidate_ordinal = (
            synthesis_decoding.candidate_ordinal
        )

    progress.synthesis_recovery_pool_selected_candidate_ordinal = (
        synthesis_recovery_pool_selected_candidate_ordinal
    )
    progress.synthesis_recovery_pool_attempted_candidate_count = (
        synthesis_recovery_pool_attempted_candidate_count
    )
    progress.sticky_retry_anchor_applied = sticky_retry_anchor_applied
    progress.synthesis_pass_count = synthesis_pass_count
    progress.diversity_result = diversity_result
    progress.metadata = metadata
    progress.trace = trace
    progress.audit = audit
    progress.decoded_sha256 = decoded_sha256
    progress.completed_json = completed_json
    progress.metadata_review_issues = metadata_review_issues
