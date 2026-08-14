"""ReplayFoundry local visual-semantic host implementation module."""
from __future__ import annotations

from .attempt_batch import *  # noqa: F401,F403

def _probe(
    model_path: Path,
    input_path: Path,
    output_path: Path,
    ffmpeg_directory: Path,
    failure_output_path: Path | None = None,
) -> None:
    _set_failure_stage("InputLoading")
    prompt_text, _ = _prompt_source()
    _normalization_policy_source()
    _generation_policy_source()
    _identity_binding_policy_source()
    _validate_model_directory(model_path)
    batch_value = _load_strict_json(input_path)
    _record_input_failure_identity(input_path, batch_value)
    case_hashes = _input_case_hashes(batch_value)
    _set_failure_stage("InputValidation")
    requests = _validate_input_batch(batch_value)
    _validate_failure_output_against_media(
        failure_output_path,
        requests,
    )
    request = requests[0]
    _set_failure_case(request, 1, case_hashes[0])

    _set_failure_stage("RuntimeInitialization")
    torch, torchcodec, transformers, process_vision_info = _load_runtime(
        ffmpeg_directory
    )
    _set_failure_identity(
        environmentSha256=_canonical_json_sha256(
            _runtime_package_manifest(
                torch,
                torchcodec,
                transformers,
                ffmpeg_directory,
            )
        )
    )
    messages = _messages_for_request(request, prompt_text)
    first_started = time.perf_counter()
    _set_failure_stage("VideoSampling")
    try:
        (
            first_videos,
            _,
            _,
            first,
            first_timing,
        ) = _process_video_inputs(
            request,
            1,
            messages,
            process_vision_info,
            torchcodec,
        )
        first_elapsed = time.perf_counter() - first_started
        second_started = time.perf_counter()
        (
            second_videos,
            _,
            _,
            second,
            second_timing,
        ) = _process_video_inputs(
            request,
            1,
            messages,
            process_vision_info,
            torchcodec,
        )
        second_elapsed = time.perf_counter() - second_started
    except InferenceError as error:
        raise InitializationError(
            f"Bounded TorchCodec capability decode failed: {error}"
        ) from error

    deterministic_fields = (
        "frameCount",
        "tensorShape",
        "tensorDataType",
        "sourceFramesPerSecond",
        "frameIndices",
        "inferredTimestampsSeconds",
        "sampledTensorSha256",
        "sampledFrameSha256",
        "totalNumFrames",
        "videoBackend",
        "videoDecodeDevice",
    )
    differences = [
        field
        for field in deterministic_fields
        if first[field] != second[field]
    ]
    if differences:
        _fail(
            InitializationError,
            "Repeated bounded TorchCodec decode differs in fields: "
            + ", ".join(differences),
        )
    if first_timing != second_timing:
        _fail(
            InitializationError,
            "Repeated bounded TorchCodec actual-PTS verification differs.",
        )
    _set_failure_stage("MediaRevalidation")
    _revalidate_media_inputs([request])

    packages = _runtime_package_manifest(
        torch,
        torchcodec,
        transformers,
        ffmpeg_directory,
    )
    packages.update(
        {
            "probeModelLoaded": "false",
            "selectedVideoBackend": VIDEO_BACKEND,
            "probeCaseId": request["caseId"],
            "probeCandidateId": request["candidate"]["id"],
            "decodedFrameCount": str(first["frameCount"]),
            "decodedTensorShape": "x".join(
                str(value)
                for value in first["tensorShape"]
            ),
            "decodedTensorDataType": first["tensorDataType"],
            "decodedSourceFramesPerSecond":
                str(first["sourceFramesPerSecond"]),
            "decodedFirstTimestampSeconds":
                str(first["inferredTimestampsSeconds"][0]),
            "decodedLastTimestampSeconds":
                str(first["inferredTimestampsSeconds"][-1]),
            "decodedFirstActualPtsSeconds":
                str(first_timing["actualPtsSeconds"][0]),
            "decodedLastActualPtsSeconds":
                str(first_timing["actualPtsSeconds"][-1]),
            "decodedLastActualFrameEndSeconds": str(
                first_timing["actualPtsSeconds"][-1]
                + first_timing["actualFrameDurationsSeconds"][-1]
            ),
            "decodedSampleCanonicalSha256":
                first["sampledTensorSha256"],
            "decodedRepeatIdentityEqual": "true",
            "executionTimingSchemaVersion":
                EXECUTION_TIMING_SCHEMA,
            "candidateSamplingCoveragePolicyVersion":
                CANDIDATE_SAMPLING_COVERAGE_POLICY,
            "authoritativeSamplingTimingSource":
                AUTHORITATIVE_SAMPLING_TIMING_SOURCE,
            "firstDecodeElapsedSeconds": f"{first_elapsed:.6f}",
            "secondDecodeElapsedSeconds": f"{second_elapsed:.6f}",
        }
    )
    payload = {
        "schemaVersion": PROBE_SCHEMA,
        "modelRepository": MODEL_REPOSITORY,
        "modelRevision": MODEL_REVISION,
        "device": DEVICE,
        "backend": BACKEND,
        "packages": packages,
    }
    _clear_failure_case()
    _set_failure_stage("OutputWrite")
    _write_json_atomic(output_path, payload)
    del first_videos
    del second_videos


def _audit_video_sampling(
    input_path: Path,
    output_path: Path,
    ffmpeg_directory: Path,
    failure_output_path: Path | None = None,
) -> None:
    _set_failure_stage("InputLoading")
    prompt_text, _ = _prompt_source()
    _normalization_policy_source()
    _generation_policy_source()
    _identity_binding_policy_source()
    batch_value = _load_strict_json(input_path)
    _record_input_failure_identity(input_path, batch_value)
    case_hashes = _input_case_hashes(batch_value)

    _set_failure_stage("InputValidation")
    requests = _validate_input_batch(batch_value)
    _validate_failure_output_against_media(
        failure_output_path,
        requests,
    )
    if len(requests) != 30:
        _fail(
            UsageOrInputError,
            "audit-video-sampling requires the exact 30-case primary input.",
        )

    _set_failure_stage("RuntimeInitialization")
    torch, torchcodec, transformers, process_vision_info = _load_runtime(
        ffmpeg_directory
    )
    runtime_packages = _runtime_package_manifest(
        torch,
        torchcodec,
        transformers,
        ffmpeg_directory,
    )
    _set_failure_identity(
        environmentSha256=_canonical_json_sha256(runtime_packages)
    )

    results: list[dict[str, Any]] = []
    for index, request in enumerate(requests):
        case_ordinal = index + 1
        _set_failure_case(
            request,
            case_ordinal,
            case_hashes[index],
        )
        try:
            result = _audit_sampling_case(
                request,
                case_ordinal,
                case_hashes[index],
                prompt_text,
                torch,
                torchcodec,
                process_vision_info,
            )
        except KeyboardInterrupt:
            raise
        except Exception as error:
            stage = _FAILURE_CONTEXT["stage"]
            result = _failed_sampling_case(
                request,
                case_ordinal,
                case_hashes[index],
                stage,
                error,
            )
        finally:
            # No decoded tensor is retained in the serialized case result.
            # CPython releases the case-local decoders/batches on stack unwind;
            # collect here as a bounded, explicit per-case cleanup boundary.
            gc.collect()
            try:
                torch.cuda.empty_cache()
            except Exception:
                pass
        results.append(result)

    _set_failure_stage("MediaRevalidation")
    _revalidate_media_inputs(requests, case_hashes)
    _clear_failure_case()
    payload = _sampling_audit_payload(results)
    _set_failure_stage("OutputWrite")
    _write_json_atomic(output_path, payload)


def _run(
    model_path: Path,
    input_path: Path,
    output_path: Path,
    attempt_output_path: Path,
    ffmpeg_directory: Path,
    raw_audit_output_path: Path | None = None,
    failure_output_path: Path | None = None,
) -> None:
    # The diagnostic attempt batch is exhaustive for safe case-local provider
    # failures. The completed batch remains all-or-nothing.
    run_started = time.perf_counter()
    _set_failure_stage("InputLoading")
    prompt_text, _ = _prompt_source()
    _normalization_policy_source()
    _generation_policy_source()
    _identity_binding_policy_source()
    _validate_model_directory(model_path)
    batch_value = _load_strict_json(input_path)
    _record_input_failure_identity(input_path, batch_value)
    case_hashes = _input_case_hashes(batch_value)
    raw_audit_batch = (
        copy.deepcopy(batch_value)
        if raw_audit_output_path is not None
        else None
    )
    _set_failure_stage("InputValidation")
    requests = _validate_input_batch(batch_value)
    _validate_failure_output_against_media(
        failure_output_path,
        requests,
    )
    _require_path_outside_roots(
        attempt_output_path,
        [
            (
                "review-source directory",
                request["_validated"]["videoPath"].parent,
            )
            for request in requests
        ],
        "--attempt-output",
    )
    if raw_audit_output_path is not None and len(requests) != 1:
        _fail(
            UsageOrInputError,
            "--raw-audit-output requires exactly one provider request.",
        )
    if raw_audit_output_path is not None:
        _require_path_outside_roots(
            raw_audit_output_path,
            [
                (
                    "review-source directory",
                    request["_validated"]["videoPath"].parent,
                )
                for request in requests
            ],
            "--raw-audit-output",
        )

    _set_failure_stage("RuntimeInitialization")
    torch, torchcodec, transformers, process_vision_info = _load_runtime(
        ffmpeg_directory
    )
    runtime_packages = _runtime_package_manifest(
        torch,
        torchcodec,
        transformers,
        ffmpeg_directory,
    )
    _set_failure_identity(
        environmentSha256=_canonical_json_sha256(runtime_packages)
    )
    raw_audit_identity = None
    if raw_audit_output_path is not None:
        raw_audit_identity = _raw_audit_identity(
            raw_audit_batch,
            input_path,
            runtime_packages,
        )
    torch.cuda.empty_cache()
    torch.cuda.reset_peak_memory_stats(0)
    _set_failure_stage("ModelInitialization")
    model, processor = _load_model_and_processor(model_path, torch, transformers)

    outcomes: list[dict[str, Any]] = []
    successful_results: list[dict[str, Any]] = []
    successful_timing: list[dict[str, Any]] = []
    successful_generation: list[dict[str, Any]] = []
    try:
        for index, request in enumerate(requests):
            case_ordinal = index + 1
            _set_failure_case(
                request,
                case_ordinal,
                case_hashes[index],
            )
            _set_failure_stage("Inference")
            case_started = time.perf_counter()
            try:
                (
                    observation,
                    elapsed,
                    execution_timing,
                    generation_case,
                ) = _infer_one(
                    request,
                    case_ordinal,
                    prompt_text,
                    model,
                    processor,
                    torch,
                    torchcodec,
                    process_vision_info,
                    raw_audit_output_path,
                    raw_audit_identity,
                )
                observation["elapsedSeconds"] = round(elapsed, 6)
                outcomes.append(
                    _provider_case_success(
                        request,
                        case_ordinal,
                        observation,
                        elapsed,
                        generation_case,
                        execution_timing,
                    )
                )
                successful_results.append(observation)
                successful_timing.append(execution_timing)
                successful_generation.append(generation_case)
            except KeyboardInterrupt:
                raise
            except HostError as error:
                if not _is_case_local_provider_failure(error):
                    raise
                outcomes.append(
                    _provider_case_failure(
                        request,
                        case_ordinal,
                        error,
                        elapsed_seconds=
                            time.perf_counter() - case_started,
                    )
                )
            finally:
                gc.collect()
                torch.cuda.empty_cache()

        _set_failure_stage("MediaRevalidation")
        _revalidate_media_inputs(requests, case_hashes)
        _clear_failure_case()
        peak_allocated = int(torch.cuda.max_memory_allocated(0))
        total_elapsed = time.perf_counter() - run_started
        attempt_payload = _attempt_batch_payload(
            outcomes,
            peak_allocated,
            total_elapsed,
        )
        _set_failure_stage("OutputWrite")
        _write_json_atomic(attempt_output_path, attempt_payload)
        if attempt_payload["failureCount"] > 0:
            _set_failure_stage("OutputValidation")
            _fail(
                ProviderCaseFailuresDetected,
                "Provider attempt completed with "
                f"{attempt_payload['failureCount']} case-local failure(s) "
                f"across {attempt_payload['requestCount']} request(s).",
            )

        payload = {
            "schemaVersion": OUTPUT_SCHEMA,
            "modelRepository": MODEL_REPOSITORY,
            "modelRevision": MODEL_REVISION,
            "device": DEVICE,
            "backend": BACKEND,
            "peakAllocatedGpuBytes": peak_allocated,
            "totalElapsedSeconds": round(total_elapsed, 6),
            "executionTiming":
                _execution_timing_payload(successful_timing),
            "generation":
                _generation_manifest_payload(successful_generation),
            "results": successful_results,
        }
        _set_failure_stage("OutputWrite")
        _write_json_atomic(output_path, payload)
    finally:
        del processor
        del model
        torch.cuda.empty_cache()



__all__ = [name for name in globals() if not name.startswith("__")]
