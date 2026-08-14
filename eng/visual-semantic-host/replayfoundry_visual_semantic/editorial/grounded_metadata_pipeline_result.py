"""Accepted grounded-metadata result and provenance assembly."""
from __future__ import annotations

import time
from typing import Any

from .grounded_knowledge_selection import (
    KNOWLEDGE_SELECTION_PROMPT_SHA256,
    KNOWLEDGE_SELECTION_PROMPT_VERSION,
    KNOWLEDGE_SELECTION_SCHEMA_VERSION,
)
from .grounded_metadata_pipeline_contract import (
    MAXIMUM_SYNTHESIS_GENERATIONS,
    METADATA_VIDEO_FPS,
    METADATA_VIDEO_MAX_FRAMES,
    METADATA_VIDEO_MAX_PIXELS_PER_FRAME,
    METADATA_VIDEO_MIN_FRAMES,
    METADATA_VIDEO_TOTAL_PIXEL_BUDGET,
)
from .grounded_metadata_pipeline_state import SynthesisContext, SynthesisProgress
from .grounded_metadata_json_whitespace import (
    ANY_WHITESPACE as GROUNDED_JSON_ANY_WHITESPACE,
    POLICY_SHA256 as GROUNDED_JSON_WHITESPACE_POLICY_SHA256,
    POLICY_VERSION as GROUNDED_JSON_WHITESPACE_POLICY_VERSION,
)
from .grounded_metadata_reroll_similarity import REROLL_DIVERSITY_POLICY_VERSION
from .grounded_metadata_rephrase import POLICY_SHA256 as REPHRASE_POLICY_SHA256
from .grounded_metadata_rephrase import POLICY_VERSION as REPHRASE_POLICY_VERSION
from .grounded_metadata_sampling import SAMPLING_POLICY_VERSION
from .grounded_metadata_synthesis import (
    STABLE_READABLE_TEXT_POLICY_VERSION,
    SYNTHESIS_EVIDENCE_POLICY_VERSION,
)
from .grounded_metadata_synthesis_decoding import (
    LOGICAL_PASS_ORDINAL as SYNTHESIS_RECOVERY_POOL_LOGICAL_PASS_ORDINAL,
    POLICY_SHA256 as SYNTHESIS_DECODING_POLICY_SHA256,
    POLICY_VERSION as SYNTHESIS_DECODING_POLICY_VERSION,
    POOL_SIZE as SYNTHESIS_RECOVERY_POOL_SIZE,
    RETRYABLE_COMPLETED_SEMANTIC_REJECTIONS,
    RETRYABLE_COMPLETED_SEMANTIC_REJECTIONS_SHA256,
    SEEDS as SYNTHESIS_RECOVERY_POOL_SEEDS,
    SYNTHESIS_RECOVERY_POOL_DECODINGS,
    TRIGGER as SYNTHESIS_RECOVERY_POOL_TRIGGER,
)
from .grounded_visual_drafts import (
    VISUAL_DRAFT_PROMPT_SHA256,
    VISUAL_DRAFT_PROMPT_VERSION,
    VISUAL_DRAFT_SCHEMA_VERSION,
)
from .grounded_visual_event_selection import (
    VISUAL_EVENT_SELECTION_PROMPT_SHA256,
    VISUAL_EVENT_SELECTION_PROMPT_VERSION,
    VISUAL_EVENT_SELECTION_SCHEMA_VERSION,
)


def build_synthesis_result(
    context: SynthesisContext,
    progress: SynthesisProgress,
) -> dict[str, Any]:
    request = context.request
    packet = context.packet
    grounding_packet_reused = context.grounding_packet_reused
    synthesis_started = context.synthesis_started
    visual_drafts = context.visual_drafts
    visual_draft_records = context.visual_draft_records
    stable_readable_text = context.stable_readable_text
    visual_event_selection_applied = context.visual_event_selection_applied
    actor_authority_assessment_applied = (
        context.actor_authority_assessment_applied
    )
    primary_visual_draft_ordinal = context.primary_visual_draft_ordinal
    primary_actor_authority = context.primary_actor_authority
    primary_creator_experience_relation = (
        context.primary_creator_experience_relation
    )
    visual_event_selection_assessments = (
        context.visual_event_selection_assessments
    )
    knowledge_selection_applied = context.knowledge_selection_applied
    selected_current_passage_id = context.selected_current_passage_id
    knowledge_selection_assessments = context.knowledge_selection_assessments
    grounded_metadata_module_identities = (
        context.grounded_metadata_module_identities
    )

    rejected_rules = progress.rejected_rules
    primary_only_synthesis_evidence = (
        progress.primary_only_synthesis_evidence
    )
    duplicate_synthesis_recovery_applied = (
        progress.duplicate_synthesis_recovery_applied
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
    synthesis_recovery_pool_applied = progress.synthesis_recovery_pool_applied
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
    sticky_retry_anchor_applied = progress.sticky_retry_anchor_applied
    sticky_retry_source_pass_ordinal = progress.sticky_retry_source_pass_ordinal
    sticky_retry_source_rule = progress.sticky_retry_source_rule
    sticky_retry_envelope_sha256 = progress.sticky_retry_envelope_sha256
    sticky_retry_authority_sha256 = progress.sticky_retry_authority_sha256
    synthesis_pass_attestations = progress.synthesis_pass_attestations
    synthesis_pass_count = progress.synthesis_pass_count
    diversity_result = progress.diversity_result
    metadata = progress.metadata
    trace = progress.trace
    audit = progress.audit
    decoded_sha256 = progress.decoded_sha256
    editorial_rephrase_attestation = progress.editorial_rephrase_attestation

    if synthesis_pass_count > MAXIMUM_SYNTHESIS_GENERATIONS:
        raise AssertionError("Grounded synthesis exceeded its bounded generation count.")
    if diversity_result is None:
        raise AssertionError("Grounded metadata title diversity was not evaluated.")
    if metadata is None or trace is None or audit is None or decoded_sha256 is None:
        raise AssertionError("Grounded synthesis accepted no complete metadata result.")
    generation_pass_count = synthesis_pass_count + (
        0 if grounding_packet_reused else packet.grounding_pass_count
    ) + (1 if progress.editorial_rephrase_attempted else 0)
    return {
        "candidateId": request["candidateId"],
        "attempt": request["attempt"],
        "metadata": metadata,
        "generation": {
            "generatedTokenCount": trace.generated_token_count,
            "maximumNewTokens": trace.maximum_new_tokens,
            "terminationReason": trace.termination_reason,
            "firstEndOfSequenceGeneratedIndex": trace.first_eos_generated_index,
            "decodedTextSha256": decoded_sha256,
            "metadataReviewRequired": bool(
                progress.metadata_review_issues
            ),
            "metadataReviewIssues": list(
                progress.metadata_review_issues
            ),
            "editorialRephrasePolicyVersion": REPHRASE_POLICY_VERSION,
            "editorialRephrasePolicySha256": REPHRASE_POLICY_SHA256,
            "editorialRephraseAttempted": progress.editorial_rephrase_attempted,
            "editorialRephraseApplied": progress.editorial_rephrase_applied,
            "editorialRephraseOutcome": progress.editorial_rephrase_outcome,
            "editorialRephraseSourceJsonSha256": (
                editorial_rephrase_attestation["sourceJsonSha256"]
                if editorial_rephrase_attestation is not None else None
            ),
            "editorialRephraseOutputJsonSha256": (
                progress.editorial_rephrase_output_json_sha256
            ),
            "editorialRephraseRejectionCode": (
                progress.editorial_rephrase_rejection_code
            ),
            "editorialRephraseCanonicalMessagesSha256": (
                editorial_rephrase_attestation["canonicalMessagesSha256"]
                if editorial_rephrase_attestation is not None else None
            ),
            "editorialRephraseRenderedPromptSha256": (
                editorial_rephrase_attestation["renderedPromptSha256"]
                if editorial_rephrase_attestation is not None else None
            ),
            "editorialRephraseRenderedPromptUtf8ByteCount": (
                editorial_rephrase_attestation["renderedPromptUtf8ByteCount"]
                if editorial_rephrase_attestation is not None else None
            ),
            "editorialRephraseInputTokenIdsSha256": (
                editorial_rephrase_attestation["inputTokenIdsSha256"]
                if editorial_rephrase_attestation is not None else None
            ),
            "editorialRephraseInputTokenCount": (
                editorial_rephrase_attestation["inputTokenCount"]
                if editorial_rephrase_attestation is not None else None
            ),
            "editorialRephraseRawOutputSha256": (
                editorial_rephrase_attestation["outputSha256"]
                if editorial_rephrase_attestation is not None else None
            ),
            "generationPassCount": generation_pass_count,
            "groundingPassCount": packet.grounding_pass_count,
            "synthesisPassCount": synthesis_pass_count,
            "nonRetrospectiveRetryAnchorApplied":
                sticky_retry_anchor_applied,
            "nonRetrospectiveRetryAnchorSourcePassOrdinal":
                sticky_retry_source_pass_ordinal
                if sticky_retry_anchor_applied
                else None,
            "nonRetrospectiveRetryAnchorSourceRule":
                sticky_retry_source_rule
                if sticky_retry_anchor_applied
                else None,
            "nonRetrospectiveRetryAnchorEnvelopeSha256":
                sticky_retry_envelope_sha256
                if sticky_retry_anchor_applied
                else None,
            "nonRetrospectiveRetryAnchorAuthoritySha256":
                sticky_retry_authority_sha256
                if sticky_retry_anchor_applied
                else None,
            "duplicateSynthesisRecoveryApplied":
                duplicate_synthesis_recovery_applied,
            "duplicateSynthesisRecoverySourcePassOrdinal":
                duplicate_synthesis_source_pass_ordinal,
            "duplicateSynthesisRecoveryRepeatedPassOrdinal":
                duplicate_synthesis_repeated_pass_ordinal,
            "duplicateSynthesisRecoverySourceRejectedJsonSha256":
                duplicate_synthesis_source_rejected_json_sha256,
            "duplicateSynthesisRecoveryRepeatedRejectedJsonSha256":
                duplicate_synthesis_repeated_rejected_json_sha256,
            "synthesisDecodingPolicyVersion":
                SYNTHESIS_DECODING_POLICY_VERSION,
            "synthesisDecodingPolicySha256":
                SYNTHESIS_DECODING_POLICY_SHA256,
            "synthesisRecoveryPoolApplied": synthesis_recovery_pool_applied,
            "synthesisRecoveryPoolSourcePassOrdinal":
                synthesis_recovery_pool_source_pass_ordinal
                if synthesis_recovery_pool_applied
                else None,
            "synthesisRecoveryPoolSourceRejectedJsonSha256":
                synthesis_recovery_pool_source_rejected_json_sha256
                if synthesis_recovery_pool_applied
                else None,
            "synthesisRecoveryPoolSourceSelectionReason":
                synthesis_recovery_pool_source_selection_reason
                if synthesis_recovery_pool_applied
                else None,
            "synthesisRecoveryPoolSelectedCandidateOrdinal":
                synthesis_recovery_pool_selected_candidate_ordinal,
            "synthesisRecoveryPoolAttemptedCandidateCount":
                synthesis_recovery_pool_attempted_candidate_count,
            "synthesisRecoveryPoolPolicyVersion":
                SYNTHESIS_DECODING_POLICY_VERSION,
            "synthesisRecoveryPoolPolicySha256":
                SYNTHESIS_DECODING_POLICY_SHA256,
            "synthesisRecoveryPoolRetryableSemanticRejections":
                list(RETRYABLE_COMPLETED_SEMANTIC_REJECTIONS),
            "synthesisRecoveryPoolRetryableSemanticRejectionsSha256":
                RETRYABLE_COMPLETED_SEMANTIC_REJECTIONS_SHA256,
            "synthesisRecoveryPoolTrigger":
                SYNTHESIS_RECOVERY_POOL_TRIGGER
                if synthesis_recovery_pool_applied
                else "None",
            "synthesisRecoveryPoolLogicalPassOrdinal":
                SYNTHESIS_RECOVERY_POOL_LOGICAL_PASS_ORDINAL,
            "synthesisRecoveryPoolSize": SYNTHESIS_RECOVERY_POOL_SIZE,
            "synthesisRecoveryPoolSeeds": list(SYNTHESIS_RECOVERY_POOL_SEEDS),
            "synthesisRecoveryPoolBatchSize":
                SYNTHESIS_RECOVERY_POOL_DECODINGS[0].batch_size,
            "synthesisRecoveryPoolDoSample":
                SYNTHESIS_RECOVERY_POOL_DECODINGS[0].do_sample,
            "synthesisRecoveryPoolNumberOfBeams":
                SYNTHESIS_RECOVERY_POOL_DECODINGS[0].number_of_beams,
            "synthesisRecoveryPoolUseCache":
                SYNTHESIS_RECOVERY_POOL_DECODINGS[0].use_cache,
            "synthesisRecoveryPoolTemperature":
                SYNTHESIS_RECOVERY_POOL_DECODINGS[0].temperature,
            "synthesisRecoveryPoolTopP":
                SYNTHESIS_RECOVERY_POOL_DECODINGS[0].top_p,
            "synthesisRecoveryPoolTopK":
                SYNTHESIS_RECOVERY_POOL_DECODINGS[0].top_k,
            "synthesisRecoveryPoolFreshMatcher": True,
            "synthesisRecoveryPoolUnconstrainedFallbackUsed": False,
            "synthesisRecoveryPoolSemanticRepairApplied": False,
            "groundedMetadataModuleIdentities":
                grounded_metadata_module_identities,
            "groundedJsonWhitespacePolicyVersion":
                GROUNDED_JSON_WHITESPACE_POLICY_VERSION,
            "groundedJsonWhitespacePolicySha256":
                GROUNDED_JSON_WHITESPACE_POLICY_SHA256,
            "groundedJsonAnyWhitespace": GROUNDED_JSON_ANY_WHITESPACE,
            "synthesisPassAttestations": synthesis_pass_attestations,
            "groundingPacketSchemaVersion": packet.schema_version,
            "groundingPacketRequestSha256": packet.request_identity_sha256,
            "groundingPacketFactSha256": packet.fact_sha256,
            "groundingPacketSourceAttempt": packet.source_attempt,
            "groundingPacketReused": grounding_packet_reused,
            "synthesisEvidencePolicyVersion":
                SYNTHESIS_EVIDENCE_POLICY_VERSION,
            "primaryOnlySynthesisEvidenceApplied":
                primary_only_synthesis_evidence,
            "visualDraftCount": len(visual_drafts),
            "visualDrafts": visual_draft_records,
            "stableReadableText": stable_readable_text,
            "stableReadableTextPolicyVersion":
                STABLE_READABLE_TEXT_POLICY_VERSION,
            "visualDraftPromptVersion": VISUAL_DRAFT_PROMPT_VERSION,
            "visualDraftPromptSha256": VISUAL_DRAFT_PROMPT_SHA256,
            "visualDraftSchemaVersion": VISUAL_DRAFT_SCHEMA_VERSION,
            "visualEventSelectionApplied": visual_event_selection_applied,
            "actorAuthorityAssessmentApplied":
                actor_authority_assessment_applied,
            "primaryVisualDraftOrdinal": primary_visual_draft_ordinal,
            "primaryActorAuthority": primary_actor_authority,
            "primaryCreatorExperienceRelation":
                primary_creator_experience_relation,
            "visualEventSelectionAssessmentCount": len(visual_event_selection_assessments),
            "visualEventSelectionAssessments": visual_event_selection_assessments,
            "visualEventSelectionPromptVersion": VISUAL_EVENT_SELECTION_PROMPT_VERSION,
            "visualEventSelectionPromptSha256": VISUAL_EVENT_SELECTION_PROMPT_SHA256,
            "visualEventSelectionSchemaVersion": VISUAL_EVENT_SELECTION_SCHEMA_VERSION,
            "knowledgeSelectionApplied": knowledge_selection_applied,
            "selectedCurrentPassageId": selected_current_passage_id,
            "knowledgeSelectionAssessmentCount": len(knowledge_selection_assessments),
            "knowledgeSelectionAssessments": knowledge_selection_assessments,
            "knowledgeSelectionPromptVersion": KNOWLEDGE_SELECTION_PROMPT_VERSION,
            "knowledgeSelectionPromptSha256": KNOWLEDGE_SELECTION_PROMPT_SHA256,
            "knowledgeSelectionSchemaVersion": KNOWLEDGE_SELECTION_SCHEMA_VERSION,
            "groundingReviewApplied": True,
            "rejectedValidationRules": rejected_rules,
            "rerollDiversityPolicyVersion":
                REROLL_DIVERSITY_POLICY_VERSION,
            "priorAcceptedTitleCount":
                diversity_result.comparable_prior_count,
            "rerollTitleDiversityCode": diversity_result.code.value,
            "rerollTitleTokenJaccardNumerator":
                diversity_result.token_jaccard.numerator,
            "rerollTitleTokenJaccardDenominator":
                diversity_result.token_jaccard.denominator,
            "samplingPolicyVersion": SAMPLING_POLICY_VERSION,
            "videoFramesPerSecond": METADATA_VIDEO_FPS,
            "minimumVideoFrames": METADATA_VIDEO_MIN_FRAMES,
            "maximumVideoFrames": METADATA_VIDEO_MAX_FRAMES,
            "maximumPixelsPerFrame": METADATA_VIDEO_MAX_PIXELS_PER_FRAME,
            "maximumTotalVideoPixels": METADATA_VIDEO_TOTAL_PIXEL_BUDGET,
        },
        "structuredDecodingAudit": audit.to_json(),
        "elapsedSeconds": round(
            time.perf_counter() - synthesis_started + (
                0.0
                if grounding_packet_reused
                else packet.grounding_elapsed_seconds
            ),
            6,
        ),
    }
