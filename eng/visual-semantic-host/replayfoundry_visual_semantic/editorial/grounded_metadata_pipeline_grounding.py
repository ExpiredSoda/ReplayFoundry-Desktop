"""Visual grounding stages for a grounded metadata candidate."""
from __future__ import annotations

import time
from typing import Any

from ..commands import InferenceError, _add_failure_diagnostic, _fail
from ..constants import GROUNDED_EDITORIAL_MAX_NEW_TOKENS
from .grounded_knowledge_selection import (
    KNOWLEDGE_SELECTION_PROMPT_SHA256,
    KNOWLEDGE_SELECTION_PROMPT_VERSION,
    KNOWLEDGE_SELECTION_SCHEMA_VERSION,
    _current_knowledge_candidates,
    _knowledge_selection_messages,
    _knowledge_selection_prompt_text,
    _knowledge_selection_schema,
    _strict_knowledge_selection,
)
from .grounded_metadata_pipeline_contract import (
    GroundingPacket,
    VISUAL_DRAFT_MAXIMUM_NEW_TOKENS,
    _new_grounding_packet,
)
from .grounded_metadata_sampling import SAMPLING_POLICY_VERSION, adaptive_sampling_plan
from .grounded_metadata_json_whitespace import ANY_WHITESPACE
from .grounded_metadata_synthesis import (
    STABLE_READABLE_TEXT_POLICY_VERSION,
    _stable_readable_text,
)
from .grounded_visual_drafts import (
    VISUAL_DRAFT_PROMPT_SHA256,
    VISUAL_DRAFT_PROMPT_VERSION,
    VISUAL_DRAFT_SCHEMA_VERSION,
    _strict_visual_draft,
    _visual_draft_messages,
    _visual_draft_prompt_text,
    _visual_draft_schema,
)
from .grounded_visual_event_selection import (
    VISUAL_EVENT_SELECTION_PROMPT_SHA256,
    VISUAL_EVENT_SELECTION_PROMPT_VERSION,
    VISUAL_EVENT_SELECTION_SCHEMA_VERSION,
    _strict_visual_event_selection,
    _visual_event_selection_messages,
    _visual_event_selection_prompt_text,
    _visual_event_selection_schema,
)
from .structured_decoding import StructuredDecodingSession


def _build_grounding_packet_impl(
    request: dict[str, Any],
    case_ordinal: int,
    model: Any,
    processor: Any,
    torch: Any,
    torchcodec: Any,
    process_vision_info: Any,
    session: StructuredDecodingSession,
    generate_json_once: Any,
) -> GroundingPacket:
    grounding_started = time.perf_counter()
    visual_schema, visual_schema_sha256 = _visual_draft_schema()
    visual_grammar, visual_base_audit = session.compile_json_schema(
        visual_schema,
        VISUAL_DRAFT_SCHEMA_VERSION,
        visual_schema_sha256,
        any_whitespace=ANY_WHITESPACE,
    )
    visual_prompt_text = _visual_draft_prompt_text()
    duration = float(request["_validated"]["videoDuration"])
    sampling_plan = adaptive_sampling_plan(duration)
    visual_drafts: list[dict[str, Any]] = []
    visual_draft_records: list[dict[str, Any]] = []
    generation_pass_count = 0
    for window_index, sampling_window in enumerate(sampling_plan, start=1):
        window = (
            sampling_window.start_seconds,
            sampling_window.end_seconds,
        )
        try:
            (
                metadata,
                visual_trace,
                _,
                visual_decoded_sha256,
                visual_sampling,
                _,
                _,
            ) = generate_json_once(
                request,
                case_ordinal,
                _visual_draft_messages(
                    request,
                    visual_prompt_text,
                    window,
                    window_index,
                    len(sampling_plan),
                    sampling_window,
                ),
                model,
                processor,
                torch,
                torchcodec,
                process_vision_info,
                session,
                visual_grammar,
                visual_base_audit,
                VISUAL_DRAFT_MAXIMUM_NEW_TOKENS,
                _strict_visual_draft,
            )
        except InferenceError as error:
            _fail(
                InferenceError,
                f"Visual draft {window_index} of {len(sampling_plan)} failed: {error}",
            )
        if visual_sampling is None:
            _fail(InferenceError, "Visual draft did not retain sampling provenance.")
        tensor_shape = visual_sampling["tensorShape"]
        actual_frame_count = int(visual_sampling["frameCount"])
        actual_height = int(tensor_shape[-2])
        actual_width = int(tensor_shape[-1])
        visual_drafts.append(metadata)
        visual_draft_records.append(
            {
                "ordinal": window_index,
                "startSeconds": window[0],
                "endSeconds": window[1],
                "sampling": {
                    "policyVersion": SAMPLING_POLICY_VERSION,
                    "tier": sampling_window.tier,
                    "framesPerSecond": sampling_window.frames_per_second,
                    "minimumFrames": sampling_window.minimum_frames,
                    "maximumFrames": sampling_window.maximum_frames,
                    "maximumPixelsPerFrame":
                        sampling_window.maximum_pixels_per_frame,
                    "maximumTotalVideoPixels":
                        sampling_window.maximum_total_video_pixels,
                    "actualFrameCount": actual_frame_count,
                    "actualFrameWidth": actual_width,
                    "actualFrameHeight": actual_height,
                    "actualPixelsPerFrame": actual_width * actual_height,
                    "actualTotalVideoPixels":
                        actual_frame_count * actual_width * actual_height,
                },
                **metadata,
                "generatedTokenCount": visual_trace.generated_token_count,
                "decodedTextSha256": visual_decoded_sha256,
            }
        )
        generation_pass_count += 1

    visual_event_selection_applied = len(visual_drafts) > 1
    actor_authority_assessment_applied = True
    event_schema, event_schema_sha256 = _visual_event_selection_schema(
        len(visual_drafts)
    )
    event_grammar, event_base_audit = session.compile_json_schema(
        event_schema,
        VISUAL_EVENT_SELECTION_SCHEMA_VERSION,
        event_schema_sha256,
        any_whitespace=ANY_WHITESPACE,
    )
    event_selection, _, _, _, _, _, _ = generate_json_once(
        request,
        case_ordinal,
        _visual_event_selection_messages(
            _visual_event_selection_prompt_text(), visual_drafts
        ),
        model,
        processor,
        torch,
        torchcodec,
        process_vision_info,
        session,
        event_grammar,
        event_base_audit,
        GROUNDED_EDITORIAL_MAX_NEW_TOKENS,
        lambda value: _strict_visual_event_selection(
            value,
            visual_drafts,
            visual_event_selection_applied,
        ),
    )
    generation_pass_count += 1
    primary_visual_draft_ordinal = event_selection["primaryVisualDraftOrdinal"]
    visual_event_selection_assessments = event_selection["assessments"]
    primary_actor_assessment = visual_event_selection_assessments[
        primary_visual_draft_ordinal - 1
    ]
    primary_actor_authority = primary_actor_assessment["actorAuthority"]
    primary_creator_experience_relation = primary_actor_assessment[
        "creatorExperienceRelation"
    ]
    _add_failure_diagnostic(
        "Visual-event primaryVisualDraftOrdinal="
        + str(primary_visual_draft_ordinal)
        + "; actorAuthority="
        + primary_actor_authority
        + "; creatorExperienceRelation="
        + primary_creator_experience_relation
    )

    candidates = _current_knowledge_candidates(request)
    knowledge_selection_applied = bool(candidates)
    selected_current_passage_id = "None"
    knowledge_selection_assessments: list[dict[str, Any]] = []
    if candidates:
        selection_schema, selection_schema_sha256 = _knowledge_selection_schema(candidates)
        selection_grammar, selection_base_audit = session.compile_json_schema(
            selection_schema,
            KNOWLEDGE_SELECTION_SCHEMA_VERSION,
            selection_schema_sha256,
            any_whitespace=ANY_WHITESPACE,
        )
        selection, _, _, _, _, _, _ = generate_json_once(
            request,
            case_ordinal,
            _knowledge_selection_messages(
                request,
                _knowledge_selection_prompt_text(),
                visual_drafts,
                candidates,
            ),
            model,
            processor,
            torch,
            torchcodec,
            process_vision_info,
            session,
            selection_grammar,
            selection_base_audit,
            GROUNDED_EDITORIAL_MAX_NEW_TOKENS,
            lambda value: _strict_knowledge_selection(value, candidates),
        )
        generation_pass_count += 1
        selected_current_passage_id = selection["currentPassageId"]
        knowledge_selection_assessments = selection["assessments"]
        _add_failure_diagnostic(
            "Knowledge selectedCurrentPassageId=" + selected_current_passage_id
        )
    stable_readable_text = _stable_readable_text(visual_drafts)
    return _new_grounding_packet(
        request,
        generation_pass_count,
        round(time.perf_counter() - grounding_started, 6),
        {
            "visualDrafts": visual_drafts,
            "visualDraftRecords": visual_draft_records,
            "stableReadableText": stable_readable_text,
            "visualEventSelectionApplied": visual_event_selection_applied,
            "actorAuthorityAssessmentApplied":
                actor_authority_assessment_applied,
            "primaryVisualDraftOrdinal": primary_visual_draft_ordinal,
            "primaryActorAuthority": primary_actor_authority,
            "primaryCreatorExperienceRelation":
                primary_creator_experience_relation,
            "visualEventSelectionAssessments": visual_event_selection_assessments,
            "knowledgeSelectionApplied": knowledge_selection_applied,
            "selectedCurrentPassageId": selected_current_passage_id,
            "knowledgeSelectionAssessments": knowledge_selection_assessments,
            "samplingPolicyVersion": SAMPLING_POLICY_VERSION,
            "stableReadableTextPolicyVersion":
                STABLE_READABLE_TEXT_POLICY_VERSION,
            "visualDraftPromptVersion": VISUAL_DRAFT_PROMPT_VERSION,
            "visualDraftPromptSha256": VISUAL_DRAFT_PROMPT_SHA256,
            "visualDraftSchemaVersion": VISUAL_DRAFT_SCHEMA_VERSION,
            "visualEventSelectionPromptVersion":
                VISUAL_EVENT_SELECTION_PROMPT_VERSION,
            "visualEventSelectionPromptSha256":
                VISUAL_EVENT_SELECTION_PROMPT_SHA256,
            "visualEventSelectionSchemaVersion":
                VISUAL_EVENT_SELECTION_SCHEMA_VERSION,
            "knowledgeSelectionPromptVersion": KNOWLEDGE_SELECTION_PROMPT_VERSION,
            "knowledgeSelectionPromptSha256": KNOWLEDGE_SELECTION_PROMPT_SHA256,
            "knowledgeSelectionSchemaVersion": KNOWLEDGE_SELECTION_SCHEMA_VERSION,
        },
    )
