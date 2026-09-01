"""One frozen Prompt 2.3 editorial inference case."""
from __future__ import annotations

from decimal import Decimal
from typing import Any

from ..generation import *
from .contract import (
    EditorialContractError,
    parse_and_canonicalize_editorial_output,
)
from .structured_decoding import StructuredDecodingSession
from .structured_decoding_policy import (
    StructuredDecodingSchemaCompilationError,
)


def _json_numbers(value: Any) -> Any:
    if isinstance(value, Decimal):
        return int(value) if value == value.to_integral() else float(value)
    if isinstance(value, list):
        return [_json_numbers(item) for item in value]
    if isinstance(value, dict):
        return {key: _json_numbers(item) for key, item in value.items()}
    return value


def _editorial_context(request: dict[str, Any]) -> dict[str, Any]:
    validated = request["_validated"]
    transcript = request["transcript"]
    return {
        "candidateMode": request["candidate"]["mode"],
        "reviewVideoDurationSeconds": float(validated["videoDuration"]),
        "candidateIntervalSeconds": {
            "start": float(validated["candidateStart"]),
            "end": float(validated["candidateEnd"]),
        },
        "composition": request["composition"],
        "transcript": {
            "policy": transcript["policy"],
            "evidenceStatus": transcript["evidenceStatus"],
            "spans": transcript["spans"],
            "accuracyWarning": transcript["accuracyWarning"],
        },
        "deterministicSummary": request["deterministicSummary"],
        "videoPolicy": validated["videoPolicy"],
    }


def _editorial_messages(
    request: dict[str, Any],
    prompt_text: str,
) -> list[dict[str, Any]]:
    video_path: Path = request["_validated"]["videoPath"]
    start = float(request["_validated"]["sourceAbsoluteOffset"])
    end = start + float(request["_validated"]["videoDuration"])
    context_json = json.dumps(
        _editorial_context(request),
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
        allow_nan=False,
    )
    return [
        {
            "role": "system",
            "content": [{"type": "text", "text": prompt_text}],
        },
        {
            "role": "user",
            "content": [
                {
                    "type": "video",
                    "video": str(video_path),
                    "max_pixels": VIDEO_MAX_PIXELS_PER_FRAME,
                    "total_pixels": VIDEO_TOTAL_PIXEL_BUDGET,
                    "fps": VIDEO_FPS,
                    "min_frames": VIDEO_MIN_FRAMES,
                    "max_frames": VIDEO_MAX_FRAMES,
                    "video_start": start,
                    "video_end": end,
                },
                {
                    "type": "text",
                    "text": (
                        "Apply the frozen editorial contract to this silent "
                        "bounded review. Context JSON follows:\n"
                        + context_json
                    ),
                },
            ],
        },
    ]


def infer_editorial_case(
    request: dict[str, Any],
    case_ordinal: int,
    run_kind: str,
    prompt_text: str,
    model: Any,
    processor: Any,
    torch: Any,
    torchcodec: Any,
    process_vision_info: Any,
    structured_decoding_session: StructuredDecodingSession | None = None,
    cache_implementation: str | None = None,
) -> dict[str, Any]:
    started = time.perf_counter()
    messages = _secure_model_messages(
        _editorial_messages(request, prompt_text)
    )
    inputs = None
    generated_ids = None
    trace = None
    structured_audit = None
    structured_processor = None

    try:
        logits_processor = None
        if structured_decoding_session is not None:
            try:
                grammar, structured_audit, _ = (
                    structured_decoding_session.compile_case(
                        request["_validated"]["videoDuration"],
                        request["_validated"]["candidateStart"],
                        request["_validated"]["candidateEnd"],
                    )
                )
            except StructuredDecodingSchemaCompilationError as error:
                failed_audit = getattr(error, "audit", None)
                if failed_audit is not None:
                    _set_failure_structured_decoding(
                        failed_audit.to_json()
                    )
                raise
            _set_failure_structured_decoding(
                structured_audit.to_json()
            )
            structured_processor = (
                structured_decoding_session.new_logits_processor(
                    grammar,
                    _normalized_eos_token_ids(model),
                )
            )
            logits_processor = [structured_processor]
        rendered = processor.apply_chat_template(
            messages,
            tokenize=False,
            add_generation_prompt=True,
        )
        _set_failure_stage("VideoSampling")
        (
            videos,
            video_metadatas,
            video_kwargs,
            sampling,
            execution_timing,
        ) = _process_video_inputs(
            request,
            case_ordinal,
            messages,
            process_vision_info,
            torchcodec,
        )
        _set_failure_execution_timing(execution_timing)
        _set_failure_stage("Inference")
        inputs = processor(
            text=rendered,
            images=None,
            videos=videos,
            video_metadata=video_metadatas,
            return_tensors="pt",
            do_resize=False,
            **video_kwargs,
        )
        inputs = _move_inputs_to_cuda(inputs)
        torch.manual_seed(0)
        torch.cuda.manual_seed_all(0)
        _set_failure_stage("Generation")
        with torch.inference_mode():
            trace = _generate_with_trace(
                model,
                inputs,
                ACTIVE_POLICY_MAX_NEW_TOKENS,
                logits_processor=logits_processor,
                cache_implementation=cache_implementation,
            )
            generated_ids = trace.sequences
        if structured_audit is not None:
            structured_audit = structured_audit.with_generation(
                trace.generated_token_count,
                trace.termination_reason,
            )
            _set_failure_structured_decoding(
                structured_audit.to_json()
            )
        if structured_processor is not None:
            structured_processor.require_completed()
        trimmed = [
            output_ids[len(input_ids):]
            for input_ids, output_ids in zip(
                inputs.input_ids,
                generated_ids,
            )
        ]
        decoded = processor.batch_decode(
            trimmed,
            skip_special_tokens=True,
            clean_up_tokenization_spaces=False,
        )
        if len(decoded) != 1:
            _fail(
                InferenceError,
                "Editorial generation returned an unexpected output count.",
            )
        generation = _generation_case_payload(
            request,
            case_ordinal,
            trace,
            decoded[0],
        )
        _set_failure_generation(_failure_generation_payload(generation))
        _set_case_generation(generation)
        _set_failure_provider_output(
            rawGeneratedTextSha256=generation["decodedTextSha256"]
        )
        _require_completed_generation(trace)
        _set_failure_stage("OutputValidation")
        canonical, audit = parse_and_canonicalize_editorial_output(
            decoded[0],
            review_duration_seconds=request["_validated"]["videoDuration"],
            candidate_start_seconds=request["_validated"]["candidateStart"],
            candidate_end_seconds=request["_validated"]["candidateEnd"],
        )
        if structured_audit is not None:
            structured_audit = structured_audit.with_parser_outcome(True)
            _set_failure_structured_decoding(
                structured_audit.to_json()
            )
        canonical = _json_numbers(canonical)
        semantic_hash = _canonical_json_sha256(canonical)
        trusted = {
            "caseId": request["caseId"],
            "candidateId": request["candidate"]["id"],
            "caseOrdinal": case_ordinal,
            "runKind": run_kind,
            "observation": canonical,
        }
        binding = {
            "caseId": request["caseId"],
            "candidateId": request["candidate"]["id"],
            "caseOrdinal": case_ordinal,
            "runKind": run_kind,
            "semanticPayloadSha256": semantic_hash,
            "trustedEnvelopeSha256": _canonical_json_sha256(trusted),
            "boundAtUtc": (
                datetime.now(timezone.utc)
                .isoformat(timespec="microseconds")
                .replace("+00:00", "Z")
            ),
        }
        result = {
            "observation": canonical,
            "canonicalizationAudit": audit,
            "requestBinding": binding,
            "generation": generation,
            "executionTiming": execution_timing,
            "sampling": sampling,
            "elapsedSeconds": round(
                time.perf_counter() - started,
                6,
            ),
        }
        if structured_audit is not None:
            result["structuredDecodingAudit"] = structured_audit.to_json()
        return result
    except EditorialContractError as error:
        if structured_audit is not None:
            structured_audit = structured_audit.with_parser_outcome(False)
            _set_failure_structured_decoding(
                structured_audit.to_json()
            )
        raise InferenceError(
            f"Prompt 2.3 output failed strict validation: {error}"
        ) from error
    finally:
        del generated_ids
        del trace
        del inputs
        try:
            torch.cuda.empty_cache()
        except Exception:
            pass
