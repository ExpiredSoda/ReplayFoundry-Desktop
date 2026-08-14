"""ReplayFoundry local visual-semantic host implementation module."""
from __future__ import annotations

from .sampling_audit import *  # noqa: F401,F403
from .generation_watchdog import (
    complete_generation_watchdog,
    prepare_generation_watchdog,
    record_generation_watchdog_exception,
)
from .generation_payloads import (
    _failure_generation_payload,
    _generation_case_payload,
    _generation_manifest_payload,
)

def _token_ids_sha256(values: list[int]) -> str:
    return _canonical_json_sha256(values)


def _normalized_eos_token_ids(model: Any) -> list[int]:
    generation_config = getattr(model, "generation_config", None)
    raw_value = getattr(generation_config, "eos_token_id", None)
    if isinstance(raw_value, int) and not isinstance(raw_value, bool):
        values = [raw_value]
    elif isinstance(raw_value, (list, tuple)):
        values = list(raw_value)
    else:
        _fail(
            InitializationError,
            "The pinned model has no usable EOS token configuration.",
        )

    result: list[int] = []
    for index, value in enumerate(values):
        if (
            isinstance(value, bool)
            or not isinstance(value, int)
            or value < 0
        ):
            _fail(
                InitializationError,
                f"EOS token ID {index} is invalid.",
            )
        if value not in result:
            result.append(value)

    if not result:
        _fail(
            InitializationError,
            "The pinned model EOS token set is empty.",
        )

    forced_eos = getattr(
        generation_config,
        "forced_eos_token_id",
        None,
    )
    if forced_eos is not None:
        _fail(
            InitializationError,
            "The pinned model configures a prohibited forced EOS token.",
        )
    stop_strings = getattr(generation_config, "stop_strings", None)
    if (
        stop_strings is not None
        and stop_strings != ()
        and stop_strings != []
    ):
        _fail(
            InitializationError,
            "The pinned model configures prohibited generation stop strings.",
        )
    return sorted(result)


def _tensor_token_row(value: Any, location: str) -> list[int]:
    try:
        rows = len(value)
    except (TypeError, AttributeError) as error:
        _fail(
            InferenceError,
            f"{location} is not a batch-one sequence tensor: {error}",
        )
    if rows != 1:
        _fail(
            InferenceError,
            f"{location} must contain exactly one sequence.",
        )
    try:
        raw_tokens = value[0].detach().cpu().tolist()
    except (AttributeError, IndexError, TypeError, RuntimeError) as error:
        _fail(
            InferenceError,
            f"{location} could not be read as a plain sequence tensor: "
            f"{type(error).__name__}: {error}",
        )
    if not isinstance(raw_tokens, list) or any(
        isinstance(token, bool)
        or not isinstance(token, int)
        or token < 0
        for token in raw_tokens
    ):
        _fail(
            InferenceError,
            f"{location} contains invalid token IDs.",
        )
    return raw_tokens


def _generate_with_trace(
    model: Any,
    inputs: Any,
    maximum_new_tokens: int,
    logits_processor: list[Any] | None = None,
    approved_generation_arguments: dict[str, Any] | None = None,
    cache_implementation: str | None = None,
) -> _GenerationTrace:
    if maximum_new_tokens not in {
        LEGACY_DIAGNOSTIC_MAX_NEW_TOKENS,
        ACTIVE_POLICY_MAX_NEW_TOKENS,
    }:
        _fail(
            UsageOrInputError,
            "Generation token ceiling is not an approved policy value.",
        )

    eos_token_ids = _normalized_eos_token_ids(model)
    input_token_ids = _tensor_token_row(
        getattr(inputs, "input_ids", None),
        "Provider input IDs",
    )
    if not input_token_ids:
        _fail(
            InferenceError,
            "Provider input IDs must not be empty.",
        )

    generation_arguments = {
        "max_new_tokens": maximum_new_tokens,
        "do_sample": False,
        "num_beams": NUMBER_OF_BEAMS,
        "use_cache": True,
    }
    if approved_generation_arguments is not None:
        expected_keys = {
            "do_sample",
            "num_beams",
            "use_cache",
            "temperature",
            "top_p",
            "top_k",
        }
        if set(approved_generation_arguments) != expected_keys:
            _fail(
                UsageOrInputError,
                "Approved generation arguments have an unsupported shape.",
            )
        generation_arguments.update(approved_generation_arguments)
    if cache_implementation is not None:
        if cache_implementation != "offloaded":
            _fail(
                UsageOrInputError,
                "Generation cache implementation is not approved.",
            )
        if generation_arguments["use_cache"] is not True:
            _fail(
                UsageOrInputError,
                "Offloaded generation cache requires enabled KV caching.",
            )
        generation_arguments["cache_implementation"] = (
            cache_implementation
        )
    if logits_processor is not None:
        generation_arguments["logits_processor"] = logits_processor
    watchdog = prepare_generation_watchdog()
    if watchdog is not None:
        generation_arguments["max_time"] = (
            watchdog.effective_maximum_seconds
        )
    try:
        sequences = model.generate(**inputs, **generation_arguments)
    except Exception:
        if watchdog is not None:
            record_generation_watchdog_exception(watchdog)
        raise
    complete_ids = _tensor_token_row(
        sequences,
        "Provider generation output",
    )
    input_token_count = len(input_token_ids)
    if complete_ids[:input_token_count] != input_token_ids:
        _fail(
            InferenceError,
            "Provider generation did not preserve the exact input-token "
            "prefix.",
        )
    generated_token_ids = complete_ids[input_token_count:]
    generated_count = len(generated_token_ids)
    if not generated_token_ids:
        _fail(
            InferenceError,
            "Provider generation returned zero new tokens.",
        )
    if generated_count > maximum_new_tokens:
        _fail(
            InferenceError,
            "Provider generation exceeded the configured token ceiling.",
        )

    first_eos_index = next(
        (
            index
            for index, token_id in enumerate(generated_token_ids)
            if token_id in eos_token_ids
        ),
        None,
    )
    terminal_token_id = generated_token_ids[-1]
    if first_eos_index is not None:
        if first_eos_index != generated_count - 1:
            _fail(
                InferenceError,
                "Provider generation contains tokens after its first EOS "
                "token.",
            )
        termination_reason = "EndOfSequence"
    elif generated_count == maximum_new_tokens:
        termination_reason = "MaximumNewTokensReached"
    else:
        termination_reason = "UnexpectedStop"

    if watchdog is None:
        generation_wall_clock_seconds = None
        generation_watchdog_triggered = False
        generation_watchdog_timeout_reason = None
        maximum_generation_wall_clock_seconds = None
    else:
        (
            generation_wall_clock_seconds,
            generation_watchdog_triggered,
            generation_watchdog_timeout_reason,
        ) = complete_generation_watchdog(
            watchdog,
        )
        maximum_generation_wall_clock_seconds = (
            watchdog.effective_maximum_seconds
        )

    prefix_count = min(
        LEGACY_DIAGNOSTIC_MAX_NEW_TOKENS,
        generated_count,
    )
    return _GenerationTrace(
        sequences=sequences,
        generated_token_ids=generated_token_ids,
        input_token_count=input_token_count,
        generated_token_count=generated_count,
        maximum_new_tokens=maximum_new_tokens,
        eos_token_ids=eos_token_ids,
        first_eos_generated_index=first_eos_index,
        terminal_token_id=terminal_token_id,
        termination_reason=termination_reason,
        generated_token_ids_sha256=
            _token_ids_sha256(generated_token_ids),
        legacy_prefix_token_count=prefix_count,
        legacy_prefix_token_ids_sha256=
            _token_ids_sha256(generated_token_ids[:prefix_count]),
        generation_wall_clock_seconds=generation_wall_clock_seconds,
        maximum_generation_wall_clock_seconds=
            maximum_generation_wall_clock_seconds,
        generation_watchdog_triggered=generation_watchdog_triggered,
        generation_watchdog_timeout_reason=
            generation_watchdog_timeout_reason,
    )


def _require_completed_generation(trace: _GenerationTrace) -> None:
    if trace.generation_watchdog_triggered:
        if trace.maximum_generation_wall_clock_seconds is None:
            raise AssertionError(
                "Triggered generation watchdog omitted its effective limit."
            )
        _fail(
            GenerationWallClockBudgetExceededError,
            "Provider generation exceeded its effective wall-clock budget "
            f"({trace.maximum_generation_wall_clock_seconds:.6f} seconds; "
            f"reason={trace.generation_watchdog_timeout_reason}).",
        )
    if trace.generated_token_count >= trace.maximum_new_tokens:
        _fail(
            GenerationTokenBudgetExceededError,
            "Provider generation exhausted the configured maximum-new-token "
            "budget before completing strictly below the active ceiling.",
        )
    if trace.termination_reason != "EndOfSequence":
        _fail(
            UnexpectedGenerationTerminationError,
            "Provider generation ended without EOS before the configured "
            "maximum-new-token budget.",
        )


def _move_inputs_to_cuda(inputs: Any) -> Any:
    try:
        return inputs.to(DEVICE)
    except Exception as error:
        raise InferenceError(
            f"Could not move provider inputs to CUDA device 0: "
            f"{type(error).__name__}: {error}"
        ) from error


def _infer_one(
    request: dict[str, Any],
    case_ordinal: int,
    prompt_text: str,
    model: Any,
    processor: Any,
    torch: Any,
    torchcodec: Any,
    process_vision_info: Any,
    raw_audit_output_path: Path | None = None,
    raw_audit_identity: dict[str, Any] | None = None,
) -> tuple[
    dict[str, Any],
    float,
    dict[str, Any],
    dict[str, Any],
]:
    if (raw_audit_output_path is None) != (raw_audit_identity is None):
        _fail(
            UsageOrInputError,
            "Raw-audit output and identity must be supplied together.",
        )

    started = time.perf_counter()
    messages = _secure_model_messages(
        _messages_for_request(request, prompt_text)
    )

    inputs = None
    generated_ids = None
    generation_trace = None
    try:
        rendered_text = processor.apply_chat_template(
            messages,
            tokenize=False,
            add_generation_prompt=True,
        )
        _set_failure_stage("VideoSampling")
        (
            videos,
            video_metadatas,
            video_kwargs,
            _,
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
            text=rendered_text,
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
            generation_trace = _generate_with_trace(
                model,
                inputs,
                MAX_NEW_TOKENS,
            )
            generated_ids = generation_trace.sequences

        trimmed_ids = [
            output_ids[len(input_ids) :]
            for input_ids, output_ids in zip(inputs.input_ids, generated_ids)
        ]
        decoded = processor.batch_decode(
            trimmed_ids,
            skip_special_tokens=True,
            clean_up_tokenization_spaces=False,
        )
        if len(decoded) != 1:
            _fail(InferenceError, "Provider returned an unexpected output count.")
        generation_case = _generation_case_payload(
            request,
            case_ordinal,
            generation_trace,
            decoded[0],
        )
        _set_failure_generation(
            _failure_generation_payload(generation_case)
        )
        _set_case_generation(generation_case)
        _set_failure_provider_output(
            rawGeneratedTextSha256=
                generation_case["decodedTextSha256"],
        )
        try:
            if raw_audit_output_path is not None:
                _capture_provider_output_audit(
                    decoded[0],
                    request,
                    raw_audit_output_path,
                    raw_audit_identity,
                    time.perf_counter() - started,
                    generation_case,
                    case_ordinal,
                )
            _require_completed_generation(generation_trace)
            observation = _parse_provider_observation(
                decoded[0],
                request,
                case_ordinal,
            )
        except UsageOrInputError as error:
            raise InferenceError(
                f"Provider output failed strict validation: {error}"
            ) from error
        elapsed = time.perf_counter() - started
        return (
            observation,
            elapsed,
            execution_timing,
            generation_case,
        )
    except HostError:
        raise
    except Exception as error:
        raise RuntimeError(
            f"Case '{request['caseId']}' encountered an unexpected runtime "
            f"failure: {type(error).__name__}: {error}"
        ) from error
    finally:
        del generated_ids
        del generation_trace
        del inputs
        try:
            torch.cuda.empty_cache()
        except Exception:
            pass



__all__ = [name for name in globals() if not name.startswith("__")]
