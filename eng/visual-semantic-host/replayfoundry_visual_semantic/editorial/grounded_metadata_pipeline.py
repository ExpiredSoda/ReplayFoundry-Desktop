"""One-candidate grounded metadata pipeline built from focused stages."""
from __future__ import annotations

import time
from typing import Any

from .grounded_knowledge_selection import _request_with_selected_knowledge
from .grounded_metadata_generation import _generate_json_once
from .grounded_metadata_generation import (
    _generate_json_once as _generate_rephrase_json_once,
)
from .grounded_metadata_pipeline_attestation import (
    _finish_synthesis_attestation,
    _grounded_metadata_module_identities,
    _require_complete_pool_candidate_attestation,
    _require_synthesis_attestation,
    _requires_primary_only_synthesis_evidence,
    _synthesis_attestation_context,
)
from .grounded_metadata_pipeline_contract import (
    GROUNDED_METADATA_MODULE_FILES,
    GROUNDING_PACKET_SCHEMA_VERSION,
    GROUNDING_REUSE_IDENTITY_VERSION,
    MAXIMUM_NEW_TOKENS,
    MAXIMUM_ORDINARY_REFINEMENT_PASSES,
    MAXIMUM_SYNTHESIS_GENERATIONS,
    METADATA_SCHEMA_VERSION,
    METADATA_VIDEO_FPS,
    METADATA_VIDEO_MAX_FRAMES,
    METADATA_VIDEO_MAX_PIXELS_PER_FRAME,
    METADATA_VIDEO_MIN_FRAMES,
    METADATA_VIDEO_TOTAL_PIXEL_BUDGET,
    STICKY_RETRY_INVALIDATING_RULES,
    VISUAL_DRAFT_MAXIMUM_NEW_TOKENS,
    GroundingPacket,
    _anchor_sha256,
    _canonical_json,
    _combined_prior_title_references,
    _duplicates_prior_synthesis,
    _grounding_reuse_identity,
    _new_grounding_packet,
    _reroll_title_reference,
    _reroll_title_scope,
    _retry_correction_envelope,
    _retry_guidance,
    _sticky_non_retrospective_envelope,
    _visual_windows,
)
from .grounded_metadata_json_whitespace import ANY_WHITESPACE
from .grounded_metadata_pipeline_grounding import (
    _build_grounding_packet_impl,
)
from .grounded_metadata_pipeline_recovery import prepare_recovery_pool
from .grounded_metadata_pipeline_recovery_candidates import (
    run_recovery_candidates,
)
from .grounded_metadata_pipeline_refinement import run_ordinary_refinement
from .grounded_metadata_pipeline_result import build_synthesis_result
from .grounded_metadata_rephrase import run_editorial_rephrase
from .grounded_metadata_pipeline_state import (
    SynthesisContext,
    SynthesisFunctions,
    SynthesisProgress,
)
from .grounded_metadata_reroll_similarity import (
    RerollTitleReference,
    RerollTitleScope,
)
from .grounded_metadata_validation import (
    metadata_schema as _metadata_schema,
    validation_failure_code as _validation_failure_code,
)
from .structured_decoding import StructuredDecodingSession


def _retry_feedback(error: Exception) -> tuple[str, str]:
    """Preserve the historical patchable validation-code boundary."""
    code = _validation_failure_code(error)
    return code, _retry_guidance((code,))


def _build_grounding_packet(
    request: dict[str, Any],
    case_ordinal: int,
    model: Any,
    processor: Any,
    torch: Any,
    torchcodec: Any,
    process_vision_info: Any,
    session: StructuredDecodingSession,
) -> GroundingPacket:
    return _build_grounding_packet_impl(
        request,
        case_ordinal,
        model,
        processor,
        torch,
        torchcodec,
        process_vision_info,
        session,
        _generate_json_once,
    )


def _prepare_synthesis_context(
    request: dict[str, Any],
    case_ordinal: int,
    prompt_text: str,
    packet: GroundingPacket,
    grounding_packet_reused: bool,
    model: Any,
    processor: Any,
    torch: Any,
    torchcodec: Any,
    process_vision_info: Any,
    session: StructuredDecodingSession,
    prior_accepted_titles: tuple[RerollTitleReference, ...],
) -> SynthesisContext:
    synthesis_started = time.perf_counter()
    request_identity_sha256, _ = _grounding_reuse_identity(request)
    if request_identity_sha256 != packet.request_identity_sha256:
        raise ValueError(
            "Grounding packet cannot be reused with different factual inputs."
        )
    facts = packet.materialize_facts()
    synthesis_request = _request_with_selected_knowledge(
        request,
        facts["selectedCurrentPassageId"],
    )
    canonical_schema, schema_sha256 = _metadata_schema(synthesis_request)
    grammar, base_audit = session.compile_json_schema(
        canonical_schema,
        METADATA_SCHEMA_VERSION,
        schema_sha256,
        any_whitespace=ANY_WHITESPACE,
    )
    all_prior_accepted_titles = _combined_prior_title_references(
        request,
        prior_accepted_titles,
    )
    prior_title_bodies = tuple(
        prior.title[: -(len(prior.game_hashtag) + 1)]
        for prior in all_prior_accepted_titles
    )
    return SynthesisContext(
        request=request,
        case_ordinal=case_ordinal,
        prompt_text=prompt_text,
        packet=packet,
        grounding_packet_reused=grounding_packet_reused,
        model=model,
        processor=processor,
        torch=torch,
        torchcodec=torchcodec,
        process_vision_info=process_vision_info,
        session=session,
        synthesis_started=synthesis_started,
        visual_drafts=facts["visualDrafts"],
        visual_draft_records=facts["visualDraftRecords"],
        stable_readable_text=facts["stableReadableText"],
        visual_event_selection_applied=facts["visualEventSelectionApplied"],
        actor_authority_assessment_applied=(
            facts["actorAuthorityAssessmentApplied"]
        ),
        primary_visual_draft_ordinal=facts["primaryVisualDraftOrdinal"],
        primary_actor_authority=facts["primaryActorAuthority"],
        primary_creator_experience_relation=(
            facts["primaryCreatorExperienceRelation"]
        ),
        visual_event_selection_assessments=(
            facts["visualEventSelectionAssessments"]
        ),
        knowledge_selection_applied=facts["knowledgeSelectionApplied"],
        selected_current_passage_id=facts["selectedCurrentPassageId"],
        knowledge_selection_assessments=(
            facts["knowledgeSelectionAssessments"]
        ),
        synthesis_request=synthesis_request,
        grammar=grammar,
        base_audit=base_audit,
        grounded_metadata_module_identities=(
            _grounded_metadata_module_identities()
        ),
        all_prior_accepted_titles=all_prior_accepted_titles,
        prior_title_bodies=prior_title_bodies,
    )


def _synthesize_case(
    request: dict[str, Any],
    case_ordinal: int,
    prompt_text: str,
    packet: GroundingPacket,
    grounding_packet_reused: bool,
    model: Any,
    processor: Any,
    torch: Any,
    torchcodec: Any,
    process_vision_info: Any,
    session: StructuredDecodingSession,
    prior_accepted_titles: tuple[RerollTitleReference, ...] = (),
) -> dict[str, Any]:
    functions = SynthesisFunctions(
        _generate_json_once,
        _generate_rephrase_json_once,
        _validation_failure_code,
        _grounded_metadata_module_identities,
    )
    context = _prepare_synthesis_context(
        request,
        case_ordinal,
        prompt_text,
        packet,
        grounding_packet_reused,
        model,
        processor,
        torch,
        torchcodec,
        process_vision_info,
        session,
        prior_accepted_titles,
    )
    progress = SynthesisProgress()
    run_ordinary_refinement(context, functions, progress)
    recovery_messages = prepare_recovery_pool(context, progress)
    run_recovery_candidates(
        context,
        functions,
        progress,
        recovery_messages,
    )
    # Model-free pipeline tests deliberately omit the qualified model and
    # processor. Production always supplies both and therefore always executes
    # the attested text-only editorial pass.
    if (
        model is not None
        and processor is not None
        and not progress.editorial_rephrase_attempted
    ):
        run_editorial_rephrase(context, functions, progress)
    return build_synthesis_result(context, progress)


def _infer_case(
    request: dict[str, Any],
    case_ordinal: int,
    prompt_text: str,
    model: Any,
    processor: Any,
    torch: Any,
    torchcodec: Any,
    process_vision_info: Any,
    session: StructuredDecodingSession,
) -> dict[str, Any]:
    """Compatibility path for a single attempt; batching owns packet reuse."""
    packet = _build_grounding_packet(
        request,
        case_ordinal,
        model,
        processor,
        torch,
        torchcodec,
        process_vision_info,
        session,
    )
    return _synthesize_case(
        request,
        case_ordinal,
        prompt_text,
        packet,
        False,
        model,
        processor,
        torch,
        torchcodec,
        process_vision_info,
        session,
    )
