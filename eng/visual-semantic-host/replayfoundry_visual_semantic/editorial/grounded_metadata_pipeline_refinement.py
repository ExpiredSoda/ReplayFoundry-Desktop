"""Ordinary greedy refinement passes for grounded metadata synthesis."""
from __future__ import annotations

import hashlib
import json

from ..commands import InferenceError, _add_failure_diagnostic
from ..errors import RerollTitleTooSimilarError
from .grounded_metadata_pipeline_attestation import (
    _anchor_sha256,
    _finish_synthesis_attestation,
    _requires_primary_only_synthesis_evidence,
    _require_synthesis_attestation,
    _synthesis_attestation_context,
)
from .grounded_metadata_pipeline_contract import (
    MAXIMUM_NEW_TOKENS,
    MAXIMUM_ORDINARY_REFINEMENT_PASSES,
    STICKY_RETRY_INVALIDATING_RULES,
    _combined_prior_title_references,
    _retry_correction_envelope,
    _retry_guidance,
    _reroll_title_reference,
    _sticky_non_retrospective_envelope,
)
from .grounded_metadata_pipeline_state import (
    SynthesisContext,
    SynthesisFunctions,
    SynthesisProgress,
)
from .grounded_metadata_reroll_similarity import (
    evaluate_reroll_title,
)
from .grounded_metadata_synthesis import (
    _metadata_messages,
    _typed_retry_authority_anchor,
)
from .grounded_metadata_synthesis_decoding import (
    RETRYABLE_COMPLETED_SEMANTIC_REJECTIONS,
    STICKY_GRAMMAR_SOURCE_RULE,
    rejected_audience_copy_withholding,
)
from .grounded_metadata_validation import (
    reviewable_metadata as _reviewable_metadata,
)


def run_ordinary_refinement(
    context: SynthesisContext,
    functions: SynthesisFunctions,
    progress: SynthesisProgress,
) -> None:
    request = context.request
    synthesis_request = context.synthesis_request
    case_ordinal = context.case_ordinal
    prompt_text = context.prompt_text
    visual_drafts = context.visual_drafts
    primary_visual_draft_ordinal = context.primary_visual_draft_ordinal
    primary_actor_authority = context.primary_actor_authority
    primary_creator_experience_relation = (
        context.primary_creator_experience_relation
    )
    prior_title_bodies = context.prior_title_bodies
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
    correction_rule_codes = progress.correction_rule_codes
    validation_feedback = progress.validation_feedback
    retry_correction_envelope = progress.retry_correction_envelope
    schema_valid_rejected_json = progress.schema_valid_rejected_json
    schema_valid_rejected_pass_ordinal = progress.schema_valid_rejected_pass_ordinal
    first_schema_valid_rejected_json = progress.first_schema_valid_rejected_json
    first_schema_valid_rejected_json_sha256 = (
        progress.first_schema_valid_rejected_json_sha256
    )
    first_schema_valid_rejection_code = (
        progress.first_schema_valid_rejection_code
    )
    withhold_unreviewed_transcripts = progress.withhold_unreviewed_transcripts
    primary_only_synthesis_evidence = (
        progress.primary_only_synthesis_evidence
    )
    duplicate_synthesis_recovery_applied = (
        progress.duplicate_synthesis_recovery_applied
    )
    semantic_exhaustion_recovery_applied = (
        progress.semantic_exhaustion_recovery_applied
    )
    duplicate_synthesis_source_pass_ordinal = (
        progress.duplicate_synthesis_source_pass_ordinal
    )
    duplicate_synthesis_repeated_pass_ordinal = (
        progress.duplicate_synthesis_repeated_pass_ordinal
    )
    duplicate_synthesis_source_rejected_json_sha256 = (
        progress.duplicate_synthesis_source_rejected_json_sha256
    )
    duplicate_synthesis_repeated_rejected_json_sha256 = (
        progress.duplicate_synthesis_repeated_rejected_json_sha256
    )
    sticky_retry_envelope = progress.sticky_retry_envelope
    sticky_retry_authority = progress.sticky_retry_authority
    sticky_retry_anchor_applied = progress.sticky_retry_anchor_applied
    sticky_retry_source_pass_ordinal = progress.sticky_retry_source_pass_ordinal
    sticky_retry_source_rule = progress.sticky_retry_source_rule
    sticky_retry_envelope_sha256 = progress.sticky_retry_envelope_sha256
    sticky_retry_authority_sha256 = progress.sticky_retry_authority_sha256
    sticky_retry_disabled_reason = progress.sticky_retry_disabled_reason
    synthesis_pass_attestations = progress.synthesis_pass_attestations
    synthesis_pass_count = progress.synthesis_pass_count
    diversity_result = progress.diversity_result
    metadata = progress.metadata
    trace = progress.trace
    audit = progress.audit
    decoded_sha256 = progress.decoded_sha256
    completed_json = progress.completed_json
    metadata_review_issues = progress.metadata_review_issues
    for refinement_pass in range(1, MAXIMUM_ORDINARY_REFINEMENT_PASSES + 1):
        previous_rejection_code = rejected_rules[-1] if rejected_rules else None
        withhold_rejected_audience_copy, rejected_copy_source_reason = (
            rejected_audience_copy_withholding(
                schema_valid_rejected_json,
                primary_only_synthesis_evidence,
                previous_rejection_code,
            )
        )
        source_rejected_json_sha256 = (
            hashlib.sha256(schema_valid_rejected_json.encode("utf-8")).hexdigest()
            if schema_valid_rejected_json is not None
            else None
        )
        anchor_applied_this_pass = sticky_retry_envelope is not None
        attestation_context = _synthesis_attestation_context(
            refinement_pass,
            None,
            "Greedy",
            0,
            schema_valid_rejected_pass_ordinal,
            source_rejected_json_sha256,
            rejected_copy_source_reason,
            anchor_applied_this_pass,
            sticky_retry_disabled_reason,
            sticky_retry_envelope_sha256 if anchor_applied_this_pass else None,
            sticky_retry_authority_sha256 if anchor_applied_this_pass else None,
        )
        try:
            if anchor_applied_this_pass:
                sticky_retry_anchor_applied = True
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
                _metadata_messages(
                    synthesis_request,
                    prompt_text,
                    validation_feedback,
                    visual_drafts,
                    primary_visual_draft_ordinal,
                    withhold_unreviewed_transcripts,
                    primary_only_synthesis_evidence,
                    primary_actor_authority,
                    primary_creator_experience_relation,
                    prior_title_bodies,
                    schema_valid_rejected_json,
                    tuple(correction_rule_codes),
                    duplicate_synthesis_recovery_applied,
                    retry_correction_envelope,
                    sticky_retry_envelope,
                    sticky_retry_authority,
                    withhold_rejected_audience_copy,
                ),
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
                synthesis_attestation_context=attestation_context,
            )
            metadata_review_issues = metadata.pop("_reviewIssues", [])
            synthesis_attestation = _require_synthesis_attestation(
                synthesis_attestation,
                attestation_context,
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
            previous_json = getattr(error, "schema_valid_rejected_json", None)
            raw_error_attestation = getattr(
                error,
                "synthesis_attestation",
                None,
            )
            if raw_error_attestation is None:
                _add_failure_diagnostic(
                    "Grounded synthesis terminated before a completed output "
                    "attestation "
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
            if not isinstance(previous_json, str) or not previous_json:
                code = functions.validation_failure_code(error)
                synthesis_pass_attestations.append(
                    _finish_synthesis_attestation(
                        error_attestation,
                        rejection_code=code,
                        accepted=False,
                    )
                )
                raise
            previous_bytes = previous_json.encode("utf-8")
            immediately_prior_bytes = (
                schema_valid_rejected_json.encode("utf-8")
                if schema_valid_rejected_json is not None
                else None
            )
            previous_sha256 = hashlib.sha256(previous_bytes).hexdigest()
            immediately_prior_sha256 = (
                hashlib.sha256(immediately_prior_bytes).hexdigest()
                if immediately_prior_bytes is not None
                else None
            )
            final_ordinary_pass_repeated_previous = (
                refinement_pass == MAXIMUM_ORDINARY_REFINEMENT_PASSES
                and schema_valid_rejected_pass_ordinal == 2
                and immediately_prior_bytes is not None
                and previous_sha256 == immediately_prior_sha256
                and previous_bytes == immediately_prior_bytes
            )
            code = functions.validation_failure_code(error)
            if refinement_pass == MAXIMUM_ORDINARY_REFINEMENT_PASSES:
                if not final_ordinary_pass_repeated_previous:
                    if code not in RETRYABLE_COMPLETED_SEMANTIC_REJECTIONS:
                        synthesis_pass_attestations.append(
                            _finish_synthesis_attestation(
                                error_attestation,
                                rejection_code=code,
                                accepted=False,
                            )
                        )
                        raise
                    semantic_exhaustion_recovery_applied = True
                if final_ordinary_pass_repeated_previous:
                    duplicate_synthesis_recovery_applied = True
                    duplicate_synthesis_source_pass_ordinal = 2
                    duplicate_synthesis_repeated_pass_ordinal = 3
                    duplicate_synthesis_source_rejected_json_sha256 = (
                        immediately_prior_sha256
                    )
                    duplicate_synthesis_repeated_rejected_json_sha256 = (
                        previous_sha256
                    )
            if code in STICKY_RETRY_INVALIDATING_RULES:
                sticky_retry_envelope = None
                sticky_retry_authority = None
                sticky_retry_disabled_reason = code
            if code not in correction_rule_codes:
                correction_rule_codes.append(code)
            if duplicate_synthesis_recovery_applied:
                duplicate_code = "GroundedRefinementUnchanged"
                if duplicate_code not in correction_rule_codes:
                    correction_rule_codes.append(duplicate_code)
            rejected_rules.append(code)
            if first_schema_valid_rejected_json is None:
                first_schema_valid_rejected_json = previous_json
                first_schema_valid_rejected_json_sha256 = previous_sha256
                first_schema_valid_rejection_code = code
            schema_valid_rejected_json = previous_json
            schema_valid_rejected_pass_ordinal = refinement_pass
            validation_feedback = _retry_guidance(tuple(correction_rule_codes))
            retry_correction_envelope = (None
                if code == "CrossDraftTitleContamination"
                else _retry_correction_envelope(error))
            captured_this_pass = False
            if (
                sticky_retry_source_pass_ordinal is None
                and code == STICKY_GRAMMAR_SOURCE_RULE
                and code not in STICKY_RETRY_INVALIDATING_RULES
            ):
                candidate_sticky_envelope = (
                    _sticky_non_retrospective_envelope(error)
                )
                if candidate_sticky_envelope is not None:
                    candidate_authority = _typed_retry_authority_anchor(
                        synthesis_request,
                        visual_drafts,
                        primary_visual_draft_ordinal,
                        primary_actor_authority,
                        primary_creator_experience_relation,
                    )
                    sticky_retry_envelope = candidate_sticky_envelope
                    sticky_retry_authority = candidate_authority
                    sticky_retry_source_pass_ordinal = refinement_pass
                    sticky_retry_source_rule = code
                    sticky_retry_disabled_reason = None
                    sticky_retry_envelope_sha256 = _anchor_sha256(
                        candidate_sticky_envelope
                    )
                    sticky_retry_authority_sha256 = _anchor_sha256(
                        candidate_authority
                    )
                    captured_this_pass = True
            if code == "UnreviewedTranscriptReuse":
                withhold_unreviewed_transcripts = True
            if _requires_primary_only_synthesis_evidence(code):
                primary_only_synthesis_evidence = True
            synthesis_pass_count += 1
            synthesis_pass_attestations.append(
                _finish_synthesis_attestation(
                    error_attestation,
                    rejection_code=code,
                    accepted=False,
                    retry_anchor_captured=captured_this_pass,
                    retry_anchor_disabled_reason=sticky_retry_disabled_reason,
                    retry_anchor_envelope_sha256=(
                        sticky_retry_envelope_sha256
                        if captured_this_pass
                        else None
                    ),
                    retry_anchor_authority_sha256=(
                        sticky_retry_authority_sha256
                        if captured_this_pass
                        else None
                    ),
                )
            )
            continue
        synthesis_pass_count += 1
        synthesis_pass_attestations.append(
            _finish_synthesis_attestation(
                synthesis_attestation,
                rejection_code=None,
                accepted=True,
            )
        )
        break
    else:
        if not (
            duplicate_synthesis_recovery_applied
            or semantic_exhaustion_recovery_applied
        ):
            raise AssertionError(
                "Three greedy synthesis passes ended without acceptance or recovery."
            )

    progress.validation_feedback = validation_feedback
    progress.retry_correction_envelope = retry_correction_envelope
    progress.schema_valid_rejected_json = schema_valid_rejected_json
    progress.schema_valid_rejected_pass_ordinal = schema_valid_rejected_pass_ordinal
    progress.first_schema_valid_rejected_json = first_schema_valid_rejected_json
    progress.first_schema_valid_rejected_json_sha256 = (
        first_schema_valid_rejected_json_sha256
    )
    progress.first_schema_valid_rejection_code = first_schema_valid_rejection_code
    progress.withhold_unreviewed_transcripts = withhold_unreviewed_transcripts
    progress.primary_only_synthesis_evidence = primary_only_synthesis_evidence
    progress.duplicate_synthesis_recovery_applied = duplicate_synthesis_recovery_applied
    progress.semantic_exhaustion_recovery_applied = semantic_exhaustion_recovery_applied
    progress.duplicate_synthesis_source_pass_ordinal = (
        duplicate_synthesis_source_pass_ordinal
    )
    progress.duplicate_synthesis_repeated_pass_ordinal = (
        duplicate_synthesis_repeated_pass_ordinal
    )
    progress.duplicate_synthesis_source_rejected_json_sha256 = (
        duplicate_synthesis_source_rejected_json_sha256
    )
    progress.duplicate_synthesis_repeated_rejected_json_sha256 = (
        duplicate_synthesis_repeated_rejected_json_sha256
    )
    progress.sticky_retry_envelope = sticky_retry_envelope
    progress.sticky_retry_authority = sticky_retry_authority
    progress.sticky_retry_anchor_applied = sticky_retry_anchor_applied
    progress.sticky_retry_source_pass_ordinal = sticky_retry_source_pass_ordinal
    progress.sticky_retry_source_rule = sticky_retry_source_rule
    progress.sticky_retry_envelope_sha256 = sticky_retry_envelope_sha256
    progress.sticky_retry_authority_sha256 = sticky_retry_authority_sha256
    progress.sticky_retry_disabled_reason = sticky_retry_disabled_reason
    progress.synthesis_pass_count = synthesis_pass_count
    progress.diversity_result = diversity_result
    progress.metadata = metadata
    progress.trace = trace
    progress.audit = audit
    progress.decoded_sha256 = decoded_sha256
    progress.completed_json = completed_json
    progress.metadata_review_issues = metadata_review_issues
