"""Recovery source selection and immutable message preparation."""
from __future__ import annotations

import hashlib
from typing import Any

from .grounded_metadata_pipeline_attestation import _anchor_sha256
from .grounded_metadata_pipeline_contract import _retry_guidance
from .grounded_metadata_pipeline_state import (
    SynthesisContext,
    SynthesisProgress,
)
from .grounded_metadata_synthesis import (
    _metadata_messages,
    _typed_retry_authority_anchor,
)
from .grounded_metadata_synthesis_decoding import (
    SOURCE_REASON_CREATOR_AUTHORITY_REJECTED_COPY_WITHHELD,
    SOURCE_REASON_ORIGINAL_FIRST_REJECTED,
    SOURCE_REASON_PRIMARY_ONLY_CROSS_DRAFT_COPY_WITHHELD,
)


def prepare_recovery_pool(
    context: SynthesisContext,
    progress: SynthesisProgress,
) -> list[dict[str, Any]] | None:
    synthesis_request = context.synthesis_request
    prompt_text = context.prompt_text
    visual_drafts = context.visual_drafts
    primary_visual_draft_ordinal = context.primary_visual_draft_ordinal
    primary_actor_authority = context.primary_actor_authority
    primary_creator_experience_relation = (
        context.primary_creator_experience_relation
    )
    prior_title_bodies = context.prior_title_bodies

    correction_rule_codes = progress.correction_rule_codes
    validation_feedback = progress.validation_feedback
    schema_valid_rejected_json = progress.schema_valid_rejected_json
    schema_valid_rejected_pass_ordinal = (
        progress.schema_valid_rejected_pass_ordinal
    )
    first_schema_valid_rejected_json = (
        progress.first_schema_valid_rejected_json
    )
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
    duplicate_synthesis_source_rejected_json_sha256 = (
        progress.duplicate_synthesis_source_rejected_json_sha256
    )
    duplicate_synthesis_repeated_rejected_json_sha256 = (
        progress.duplicate_synthesis_repeated_rejected_json_sha256
    )
    sticky_retry_envelope = progress.sticky_retry_envelope
    retry_correction_envelope = progress.retry_correction_envelope
    sticky_retry_authority = progress.sticky_retry_authority
    sticky_retry_envelope_sha256 = progress.sticky_retry_envelope_sha256
    sticky_retry_authority_sha256 = progress.sticky_retry_authority_sha256
    synthesis_recovery_pool_applied = progress.synthesis_recovery_pool_applied
    synthesis_recovery_pool_source_json = (
        progress.synthesis_recovery_pool_source_json
    )
    synthesis_recovery_pool_source_pass_ordinal = (
        progress.synthesis_recovery_pool_source_pass_ordinal
    )
    synthesis_recovery_pool_source_rejected_json_sha256 = (
        progress.synthesis_recovery_pool_source_rejected_json_sha256
    )
    synthesis_recovery_pool_source_selection_reason = (
        progress.synthesis_recovery_pool_source_selection_reason
    )
    recovery_messages: list[dict[str, Any]] | None = None

    if (
        duplicate_synthesis_recovery_applied
        or semantic_exhaustion_recovery_applied
    ):
        if (
            first_schema_valid_rejected_json is None
            or first_schema_valid_rejected_json_sha256 is None
            or first_schema_valid_rejection_code is None
            or duplicate_synthesis_recovery_applied
                and duplicate_synthesis_repeated_rejected_json_sha256 is None
        ):
            raise AssertionError(
                "Synthesis recovery omitted its immutable pass-one source."
            )
        use_primary_only_cross_draft_source = (
            "CrossDraftTitleContamination" in progress.rejected_rules
            and primary_only_synthesis_evidence
        )
        use_creator_authority_withheld_source = (
            first_schema_valid_rejection_code ==
                "UnsupportedCreatorEmbodiment"
        )
        if use_primary_only_cross_draft_source:
            if (
                schema_valid_rejected_json is None
                or schema_valid_rejected_pass_ordinal != 3
            ):
                raise AssertionError(
                    "Primary-only recovery omitted its final greedy source."
                )
            repeated_sha256 = hashlib.sha256(
                schema_valid_rejected_json.encode("utf-8")
            ).hexdigest()
            if duplicate_synthesis_recovery_applied and (
                duplicate_synthesis_source_rejected_json_sha256 is None
                or duplicate_synthesis_repeated_rejected_json_sha256 is None
                or repeated_sha256
                    != duplicate_synthesis_source_rejected_json_sha256
                or repeated_sha256
                    != duplicate_synthesis_repeated_rejected_json_sha256
            ):
                raise AssertionError(
                    "Primary-only recovery source did not match passes two and three."
                )
            synthesis_recovery_pool_source_json = schema_valid_rejected_json
            synthesis_recovery_pool_source_pass_ordinal = 3
            synthesis_recovery_pool_source_rejected_json_sha256 = repeated_sha256
            synthesis_recovery_pool_source_selection_reason = (
                SOURCE_REASON_PRIMARY_ONLY_CROSS_DRAFT_COPY_WITHHELD
            )
        elif use_creator_authority_withheld_source:
            synthesis_recovery_pool_source_json = (
                first_schema_valid_rejected_json
            )
            synthesis_recovery_pool_source_pass_ordinal = 1
            synthesis_recovery_pool_source_rejected_json_sha256 = (
                first_schema_valid_rejected_json_sha256
            )
            synthesis_recovery_pool_source_selection_reason = (
                SOURCE_REASON_CREATOR_AUTHORITY_REJECTED_COPY_WITHHELD
            )
        else:
            synthesis_recovery_pool_source_json = (
                first_schema_valid_rejected_json
            )
            synthesis_recovery_pool_source_pass_ordinal = 1
            synthesis_recovery_pool_source_rejected_json_sha256 = (
                first_schema_valid_rejected_json_sha256
            )
            synthesis_recovery_pool_source_selection_reason = (
                SOURCE_REASON_ORIGINAL_FIRST_REJECTED
            )
        if sticky_retry_authority is None:
            sticky_retry_authority = _typed_retry_authority_anchor(
                synthesis_request,
                visual_drafts,
                primary_visual_draft_ordinal,
                primary_actor_authority,
                primary_creator_experience_relation,
            )
            sticky_retry_authority_sha256 = _anchor_sha256(
                sticky_retry_authority
            )
        if (
            use_primary_only_cross_draft_source
            or use_creator_authority_withheld_source
        ):
            sticky_retry_envelope = None
            sticky_retry_envelope_sha256 = None
        if duplicate_synthesis_recovery_applied:
            duplicate_code = "GroundedRefinementUnchanged"
            if duplicate_code not in correction_rule_codes:
                correction_rule_codes.append(duplicate_code)
        validation_feedback = _retry_guidance(tuple(correction_rule_codes))
        earliest_grammar_envelope = (
            {
                name: sticky_retry_envelope[name]
                for name in (
                    "nonEvidence",
                    "rejectedTitleBody",
                    "offendingActionField",
                    "offendingActionForm",
                )
            }
            if sticky_retry_envelope is not None
            else None
        )
        readable_text_envelope = (
            {
                name: retry_correction_envelope[name]
                for name in (
                    "nonEvidence",
                    "forbiddenReadableTextPhrases",
                    "affectedAudienceFields",
                )
                if name in retry_correction_envelope
            }
            if (
                "UnstableReadableTextReuse" in correction_rule_codes
                and retry_correction_envelope is not None
                and "forbiddenReadableTextPhrases"
                    in retry_correction_envelope
            )
            else None
        )
        recovery_correction_envelope = (
            None
            if (
                use_primary_only_cross_draft_source
                or use_creator_authority_withheld_source
            )
            else readable_text_envelope or earliest_grammar_envelope
        )
        recovery_messages = _metadata_messages(
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
            synthesis_recovery_pool_source_json,
            tuple(correction_rule_codes),
            True,
            recovery_correction_envelope,
            sticky_retry_envelope,
            sticky_retry_authority,
            (
                use_primary_only_cross_draft_source
                or use_creator_authority_withheld_source
            ),
        )
        synthesis_recovery_pool_applied = True

    progress.validation_feedback = validation_feedback
    progress.sticky_retry_envelope = sticky_retry_envelope
    progress.sticky_retry_envelope_sha256 = sticky_retry_envelope_sha256
    progress.sticky_retry_authority = sticky_retry_authority
    progress.sticky_retry_authority_sha256 = sticky_retry_authority_sha256
    progress.synthesis_recovery_pool_applied = synthesis_recovery_pool_applied
    progress.synthesis_recovery_pool_source_json = (
        synthesis_recovery_pool_source_json
    )
    progress.synthesis_recovery_pool_source_pass_ordinal = (
        synthesis_recovery_pool_source_pass_ordinal
    )
    progress.synthesis_recovery_pool_source_rejected_json_sha256 = (
        synthesis_recovery_pool_source_rejected_json_sha256
    )
    progress.synthesis_recovery_pool_source_selection_reason = (
        synthesis_recovery_pool_source_selection_reason
    )
    return recovery_messages
