"""One strictly constrained JSON generation pass for grounded metadata."""
from __future__ import annotations

import hashlib
import json
from typing import Any, Callable

from ..commands import (
    InferenceError,
    UsageOrInputError,
    _add_failure_diagnostic,
    _fail,
    _set_failure_generation,
    _set_failure_provider_output,
    _set_failure_stage,
    _set_failure_structured_decoding,
)
from ..canonical_json import _secure_model_messages
from ..generation import (
    _failure_generation_payload,
    _generation_case_payload,
    _generate_with_trace,
    _move_inputs_to_cuda,
    _normalized_eos_token_ids,
    _require_completed_generation,
    _tensor_token_row,
    _token_ids_sha256,
)
from ..grounded_cuda_memory import (
    CACHE_IMPLEMENTATION,
    admit_grounded_generation,
    grounded_sdpa_context,
    is_cuda_out_of_memory,
    record_grounded_cuda_out_of_memory,
)
from .grounded_metadata_sampling import SAMPLING_POLICY_VERSION
from .grounded_metadata_synthesis_decoding import (
    GroundedMetadataSynthesisDecoding,
)
from .structured_decoding import StructuredDecodingSession


MAXIMUM_REJECTED_JSON_UTF8_BYTES = 8192


def _bounded_completed_json(text: str) -> str | None:
    """Canonicalize one completed constrained object without truncating it."""

    def reject_constant(value: str) -> Any:
        raise ValueError(f"non-finite JSON token {value}")

    try:
        value = json.loads(text, parse_constant=reject_constant)
    except (json.JSONDecodeError, ValueError):
        return None
    if not isinstance(value, dict):
        return None
    canonical = json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
        allow_nan=False,
    )
    if len(canonical.encode("utf-8")) > MAXIMUM_REJECTED_JSON_UTF8_BYTES:
        return None
    return canonical


def _generate_json_once(
    request: dict[str, Any],
    case_ordinal: int,
    messages: list[dict[str, Any]],
    model: Any,
    processor: Any,
    torch: Any,
    torchcodec: Any,
    process_vision_info: Any,
    session: StructuredDecodingSession,
    grammar: Any,
    audit: Any,
    maximum_new_tokens: int,
    parse_output: Callable[[str], dict[str, Any]],
    synthesis_decoding: GroundedMetadataSynthesisDecoding | None = None,
    synthesis_attestation_context: dict[str, Any] | None = None,
) -> tuple[
    dict[str, Any],
    Any,
    Any,
    str,
    dict[str, Any] | None,
    str,
    dict[str, Any] | None,
]:
    from ..commands import _process_video_inputs

    messages = _secure_model_messages(messages)
    rendered = processor.apply_chat_template(
        messages, tokenize=False, add_generation_prompt=True
    )
    synthesis_attestation = None
    if synthesis_attestation_context is not None:
        canonical_messages = json.dumps(
            messages,
            ensure_ascii=False,
            sort_keys=True,
            separators=(",", ":"),
            allow_nan=False,
        ).encode("utf-8")
        rendered_bytes = rendered.encode("utf-8")
        synthesis_attestation = {
            **synthesis_attestation_context,
            "canonicalMessagesSha256":
                hashlib.sha256(canonical_messages).hexdigest(),
            "renderedPromptSha256":
                hashlib.sha256(rendered_bytes).hexdigest(),
            "renderedPromptUtf8ByteCount": len(rendered_bytes),
            "inputTokenIdsSha256": None,
            "inputTokenCount": None,
            "outputSha256": None,
            "completedJsonSha256": None,
            "rejectionCode": None,
            "accepted": False,
        }
    video_items = [
        item
        for message in messages
        for item in message["content"]
        if item.get("type") == "video"
    ]
    sampling_identity: dict[str, Any] | None = None
    if video_items:
        video_element = video_items[0]
        window_start = float(video_element["video_start"])
        window_end = float(video_element["video_end"])
        sampling_request = dict(request)
        sampling_request["_validated"] = dict(request["_validated"])
        sampling_request["_validated"].update(
            {
                "sourceAbsoluteOffset": window_start,
                "videoDuration": window_end - window_start,
                "candidateStart": 0.0,
                "candidateEnd": window_end - window_start,
            }
        )
        sampling_request["_visualSamplingLimits"] = {
            "policyVersion": SAMPLING_POLICY_VERSION,
            "minimumFrames": int(video_element["min_frames"]),
            "maximumFrames": int(video_element["max_frames"]),
            "maximumPixelsPerFrame": int(video_element["max_pixels"]),
            "maximumTotalVideoPixels": int(video_element["total_pixels"]),
        }
        videos, video_metadatas, video_kwargs, sampling_identity, _ = _process_video_inputs(
            sampling_request,
            case_ordinal,
            messages,
            process_vision_info,
            torchcodec,
        )
        _set_failure_stage("Inference")
        inputs = processor(
            text=[rendered],
            images=None,
            videos=videos,
            video_metadata=video_metadatas,
            return_tensors="pt",
            do_resize=False,
            **video_kwargs,
        )
    else:
        inputs = processor(
            text=[rendered], images=None, videos=None, return_tensors="pt"
        )
    if synthesis_attestation is not None:
        input_token_ids = _tensor_token_row(
            getattr(inputs, "input_ids", None),
            "Grounded synthesis attestation input IDs",
        )
        synthesis_attestation["inputTokenIdsSha256"] = (
            _token_ids_sha256(input_token_ids)
        )
        synthesis_attestation["inputTokenCount"] = len(input_token_ids)
        _add_failure_diagnostic(
            "Synthesis prompt attestation "
            + json.dumps(
                synthesis_attestation,
                sort_keys=True,
                separators=(",", ":"),
            )
        )
    try:
        inputs = _move_inputs_to_cuda(inputs)
    except Exception as error:
        if is_cuda_out_of_memory(error, torch):
            record_grounded_cuda_out_of_memory(torch)
            raise InferenceError(
                "Grounded Qwen input transfer reached its fixed CUDA "
                "allocator limit despite the frozen vision-encoder CPU "
                "placement; the memory policy did not move additional model "
                "modules or relax the limit."
            ) from error
        raise
    generation_seed = 0 if synthesis_decoding is None else synthesis_decoding.seed
    torch.manual_seed(generation_seed)
    torch.cuda.manual_seed_all(generation_seed)
    logits_processor = session.new_logits_processor(
        grammar, _normalized_eos_token_ids(model)
    )
    approved_generation_arguments = (
        None
        if synthesis_decoding is None
        else synthesis_decoding.generation_arguments()
    )
    if synthesis_decoding is not None:
        _add_failure_diagnostic(
            "Recovery-pool synthesis decoding "
            + json.dumps(
                {
                    "policyVersion": synthesis_decoding.policy_version,
                    "policySha256": synthesis_decoding.policy_sha256,
                    "trigger": synthesis_decoding.trigger,
                    "logicalPassOrdinal":
                        synthesis_decoding.logical_pass_ordinal,
                    "candidateOrdinal": synthesis_decoding.candidate_ordinal,
                    "batchSize": synthesis_decoding.batch_size,
                    "doSample": synthesis_decoding.do_sample,
                    "numberOfBeams": synthesis_decoding.number_of_beams,
                    "useCache": synthesis_decoding.use_cache,
                    "seed": synthesis_decoding.seed,
                    "temperature": synthesis_decoding.temperature,
                    "topP": synthesis_decoding.top_p,
                    "topK": synthesis_decoding.top_k,
                },
                sort_keys=True,
                separators=(",", ":"),
            )
        )
    try:
        admit_grounded_generation(torch)
        with torch.inference_mode(), grounded_sdpa_context(torch):
            trace = _generate_with_trace(
                model,
                inputs,
                maximum_new_tokens,
                logits_processor=[logits_processor],
                approved_generation_arguments=
                    approved_generation_arguments,
                cache_implementation=CACHE_IMPLEMENTATION,
            )
    except Exception as error:
        if is_cuda_out_of_memory(error, torch):
            record_grounded_cuda_out_of_memory(torch)
            raise InferenceError(
                "Grounded Qwen generation reached its fixed CUDA allocator "
                "limit despite the frozen vision-encoder CPU placement; the "
                "memory policy did not move additional model modules or "
                "relax the limit."
            ) from error
        raise
    finally:
        torch.cuda.empty_cache()
    attempt_audit = audit.with_generation(
        trace.generated_token_count, trace.termination_reason
    )
    trimmed = [
        output_ids[len(input_ids):]
        for input_ids, output_ids in zip(inputs.input_ids, trace.sequences)
    ]
    decoded = processor.batch_decode(
        trimmed,
        skip_special_tokens=True,
        clean_up_tokenization_spaces=False,
    )
    if len(decoded) != 1:
        _fail(
            InferenceError,
            "Grounded metadata generation returned an unexpected output count.",
        )
    generation_case = _generation_case_payload(
        request,
        case_ordinal,
        trace,
        decoded[0],
    )
    failure_generation_arguments = (
        {}
        if synthesis_decoding is None
        else {
            "policy_version": synthesis_decoding.policy_version,
            "policy_sha256": synthesis_decoding.policy_sha256,
            "do_sample": synthesis_decoding.do_sample,
            "number_of_beams": synthesis_decoding.number_of_beams,
            "use_cache": synthesis_decoding.use_cache,
        }
    )
    _set_failure_generation(
        _failure_generation_payload(
            generation_case,
            **failure_generation_arguments,
        )
    )
    _set_failure_provider_output(
        rawGeneratedTextSha256=generation_case["decodedTextSha256"],
    )
    _set_failure_structured_decoding(
        attempt_audit.with_parser_outcome(False).to_json()
    )
    if trace.termination_reason != "EndOfSequence":
        _add_failure_diagnostic(
            "Incomplete constrained output "
            + json.dumps(
                {
                    "generatedTokenCount": trace.generated_token_count,
                    "head": decoded[0][:800],
                    "tail": decoded[0][-400:],
                },
                ensure_ascii=False,
                sort_keys=True,
                separators=(",", ":"),
            )
        )
    logits_processor.require_completed()
    _require_completed_generation(trace)
    decoded_sha256 = generation_case["decodedTextSha256"]
    if synthesis_attestation is not None:
        synthesis_attestation["outputSha256"] = decoded_sha256
    completed_json = _bounded_completed_json(decoded[0])
    if synthesis_attestation is not None and completed_json is not None:
        synthesis_attestation["completedJsonSha256"] = hashlib.sha256(
            completed_json.encode("utf-8")
        ).hexdigest()
    try:
        metadata = parse_output(decoded[0])
    except (InferenceError, UsageOrInputError) as error:
        _add_failure_diagnostic(
            "Rejected constrained output "
            + json.dumps(
                {"sha256": decoded_sha256, "text": decoded[0][:1200]},
                ensure_ascii=False,
                sort_keys=True,
                separators=(",", ":"),
            )
        )
        if completed_json is not None:
            error.schema_valid_rejected_json = completed_json
        if synthesis_attestation is not None:
            error.synthesis_attestation = synthesis_attestation
        raise
    if completed_json is None:
        _fail(
            InferenceError,
            "Completed constrained output could not be retained as bounded strict JSON.",
        )
    return (
        metadata,
        trace,
        attempt_audit.with_parser_outcome(True),
        decoded_sha256,
        sampling_identity,
        completed_json,
        synthesis_attestation,
    )
