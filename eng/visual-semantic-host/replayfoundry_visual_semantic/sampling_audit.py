"""ReplayFoundry local visual-semantic host implementation module."""
from __future__ import annotations

from .sampling_timing import *  # noqa: F401,F403

def _primary_sampling_root_cause(causes: list[str]) -> str | None:
    priority = (
        "QwenTensorAndDirectTorchCodecFrameMismatch",
        "SamplingCardinalityMismatch",
        "CandidateCoordinateMismatch",
        "ReviewMediaTimelineMismatch",
        "RequestedTrimNotHonored",
        "ActualPtsOutsideReview",
        "CandidateHasNoSampledFrame",
        "CandidateHasOnlyOneSampledFrame",
        "CandidateStartCoverageInsufficient",
        "CandidateEndCoverageInsufficient",
        "SourceFrameMetadataMismatch",
        "AverageFpsInvalidForPtsMapping",
        "InferredTimestampDrift",
        "Other",
    )
    return next((code for code in priority if code in causes), None)


def _add_legacy_actual_timing_cause(
    causes: list[str],
    *,
    legacy_error: HostError | None,
    corrected_passed: bool,
) -> list[str]:
    if (
        legacy_error is None
        or not corrected_passed
        or "InferredTimestampDrift" in causes
        or getattr(legacy_error, "legacy_timing_reason", None)
            not in LEGACY_TIMING_VALIDATION_REASONS
    ):
        return causes
    # Only the two isolated nominal index/FPS timing checks can select the
    # timestamp-only branch. An arbitrary legacy HostError must remain
    # inconclusive even when the independent actual-PTS checks pass.
    return [
        code
        for code in SAMPLING_ROOT_CAUSE_CODES
        if code in {*causes, "InferredTimestampDrift"}
    ]


def _decision_support_code(
    legacy_passed: bool,
    corrected_passed: bool,
) -> str:
    if legacy_passed and corrected_passed:
        return "LegacyAndActualCoveragePass"
    if legacy_passed:
        return "LegacyPassActualPtsFail"
    if corrected_passed:
        return "LegacyFailActualPtsPass"
    return "LegacyAndActualCoverageFail"


def _case_audit_sha256(case_result: dict[str, Any]) -> str:
    identity = copy.deepcopy(case_result)
    identity.pop("caseAuditSha256", None)
    return _canonical_json_sha256(identity)


def _failed_sampling_case(
    request: dict[str, Any],
    case_ordinal: int,
    input_case_sha256: str,
    stage: str,
    error: Exception,
) -> dict[str, Any]:
    timing = _sampling_timing(request)
    partial = copy.deepcopy(_FAILURE_CONTEXT["caseAuditSections"])
    error_code = (
        type(error).__name__
        if isinstance(error, HostError)
        else "UnexpectedHostFailure"
    )
    case_result = {
        "caseId": request["caseId"],
        "candidateId": request["candidate"]["id"],
        "caseOrdinal": case_ordinal,
        "mode": request["candidate"]["mode"],
        "inputCaseSha256": input_case_sha256,
        "sourceVideoSha256":
            request["_validated"]["expectedVideoHash"],
        "status": "Failed",
        "timing": timing,
        "qwenMetadata": partial["qwenMetadata"],
        "directTorchCodecMetadata": partial["directTorchCodecMetadata"],
        "comparison": partial["comparison"],
        "candidateVisibility": partial["candidateVisibility"],
        "reviewCoverage": partial["reviewCoverage"],
        "legacyValidation": partial["legacyValidation"],
        "correctedPolicyValidation": {
            "passed": False,
            "errorCode": "Other",
            "message": "Sampling audit did not complete for this case.",
        },
        "rootCauseCodes": ["Other"],
        "primaryRootCause": "Other",
        "decisionSupportCode": "AuditFailed",
        "warnings": ["SamplingAuditCaseFailed"],
        "failure": {
            "stage": stage,
            "errorCode": error_code,
            "message": _bounded_failure_message(error),
        },
    }
    case_result["caseAuditSha256"] = _case_audit_sha256(case_result)
    return case_result


def _audit_sampling_case(
    request: dict[str, Any],
    case_ordinal: int,
    input_case_sha256: str,
    prompt_text: str,
    torch: Any,
    torchcodec: Any,
    process_vision_info: Any,
) -> dict[str, Any]:
    timing = _sampling_timing(request)
    messages = _messages_for_request(request, prompt_text)
    video_element = messages[1]["content"][0]

    _set_failure_stage("VideoSampling")
    final_video, qwen_metadata, legacy = _capture_qwen_sampling(
        request,
        messages,
        process_vision_info,
    )
    selected_indices = _integer_list(
        qwen_metadata.get("frames_indices"),
        "Qwen selected frame indices",
    )
    source_fps = float(qwen_metadata.get("fps"))
    if not math.isfinite(source_fps) or source_fps <= 0:
        _fail(InferenceError, "Qwen returned an invalid source average FPS.")
    inferred_timestamps = [
        index / source_fps
        for index in selected_indices
    ]
    inferred_timestamps_serialized = [
        round(value, 9)
        for value in inferred_timestamps
    ]

    qwen_final_tensor_hash, qwen_final_frame_hashes = _tensor_identity(
        final_video
    )
    legacy_error = legacy["error"]
    legacy_passed = legacy_error is None
    legacy_result = {
        "passed": legacy_passed,
        "errorCode": (
            None
            if legacy_error is None
            else getattr(
                legacy_error,
                "legacy_timing_reason",
                type(legacy_error).__name__,
            )
        ),
        "message": (
            None
            if legacy_error is None
            else _bounded_failure_message(legacy_error)
        ),
    }
    qwen_metadata_result = {
        "videoBackend": qwen_metadata["video_backend"],
        "decodeDevice": getattr(final_video.device, "type", None),
        "sourceAverageFramesPerSecond": source_fps,
        "sourceFrameCountMetadata": float(
            qwen_metadata["total_num_frames"]
        ),
        "selectedFrameIndices": selected_indices,
        "inferredTimestampsSeconds": inferred_timestamps_serialized,
        "finalTensorShape": _tensor_shape(final_video),
        "finalTensorDataType": str(final_video.dtype),
        "finalTensorSha256": qwen_final_tensor_hash,
        "finalFrameSha256": qwen_final_frame_hashes,
    }
    _set_case_audit_section("qwenMetadata", qwen_metadata_result)
    _set_case_audit_section("legacyValidation", legacy_result)

    qwen_identity = legacy["identity"]
    if qwen_identity is None:
        if legacy_error is not None:
            raise legacy_error
        _fail(InferenceError, "Qwen sampling identity is unavailable.")
    verification = _verify_actual_pts_sampling(
        request,
        case_ordinal,
        final_video,
        qwen_metadata,
        qwen_identity,
        video_element,
        torchcodec,
    )
    decoder = verification["decoder"]
    direct_batch = verification["directBatch"]
    direct_pts = verification["directPts"]
    direct_durations = verification["directDurations"]
    direct_raw_hash = verification["directRawTensorSha256"]
    direct_frame_hashes = verification["directRawFrameSha256"]
    direct_indices = list(selected_indices)
    repeat_decoder = torchcodec.decoders.VideoDecoder(
        str(request["_validated"]["videoPath"]),
        num_ffmpeg_threads=int(os.environ.get("TORCHCODEC_NUM_THREADS", 8)),
        seek_mode="exact",
    )
    repeat_batch = repeat_decoder.get_frames_at(indices=selected_indices)
    repeat_pts = _finite_float_list(
        repeat_batch.pts_seconds,
        "Repeated direct TorchCodec PTS",
    )
    repeat_durations = _finite_float_list(
        repeat_batch.duration_seconds,
        "Repeated direct TorchCodec frame durations",
    )
    repeat_raw_hash, repeat_frame_hashes = _tensor_identity(
        repeat_batch.data
    )
    direct_pts_serialized = [
        round(value, 9)
        for value in direct_pts
    ]
    direct_durations_serialized = [
        round(value, 9)
        for value in direct_durations
    ]
    repeat_pts_serialized = [
        round(value, 9)
        for value in repeat_pts
    ]
    repeat_durations_serialized = [
        round(value, 9)
        for value in repeat_durations
    ]
    direct_frame_ends = [
        round(pts + duration, 9)
        for pts, duration in zip(
            direct_pts_serialized,
            direct_durations_serialized,
        )
    ]
    compatible_hash = verification[
        "directCompatibleTensorSha256"
    ]
    compatible_frame_hashes = verification[
        "directCompatibleFrameSha256"
    ]
    compatible_frames_equal = verification[
        "compatibleFrameIdentityEqual"
    ]
    direct_repeat_raw_equal = direct_raw_hash == repeat_raw_hash
    direct_repeat_frames_equal = [
        first == second
        for first, second in zip(direct_frame_hashes, repeat_frame_hashes)
    ]
    frame_indices_equal = selected_indices == direct_indices
    pts_equal = direct_pts_serialized == repeat_pts_serialized
    durations_equal = (
        direct_durations_serialized == repeat_durations_serialized
    )
    (
        drift,
        maximum_absolute_drift,
        mean_absolute_drift,
        _,
    ) = _canonical_execution_timing_drift(
        inferred_timestamps_serialized,
        direct_pts_serialized,
        direct_durations_serialized,
    )
    visibility = verification["visibility"]
    _set_case_audit_section("candidateVisibility", visibility)
    source_begin = verification["sourceBeginStreamSeconds"]
    source_end = verification["sourceEndStreamSeconds"]
    coverage = verification["coverage"]
    _set_case_audit_section("reviewCoverage", coverage)
    causes = _sampling_root_causes(
        visibility=visibility,
        review_coverage=coverage,
        maximum_absolute_drift=maximum_absolute_drift,
        qwen_tensor_equal=(
            verification["compatibleTensorIdentityEqual"]
            and compatible_frames_equal
        ),
        frame_indices_equal=frame_indices_equal,
        qwen_frame_count=len(selected_indices),
        direct_frame_count=len(direct_pts),
        timing=timing,
        source_begin=source_begin,
        source_end=source_end,
        source_average_fps=source_fps,
    )
    direct_source_fps = verification[
        "sourceAverageFramesPerSecond"
    ]
    direct_source_frame_count = verification["sourceFrameCount"]
    source_metadata_mismatch = not verification[
        "sourceMetadataMatches"
    ]
    if source_metadata_mismatch:
        causes = [
            code
            for code in SAMPLING_ROOT_CAUSE_CODES
            if code in {*causes, "SourceFrameMetadataMismatch"}
        ]
    direct_repeat_tensor_equal = (
        direct_repeat_raw_equal
        and len(direct_repeat_frames_equal) == len(direct_frame_hashes)
        and all(direct_repeat_frames_equal)
    )
    if (
        not pts_equal
        or not durations_equal
        or not direct_repeat_tensor_equal
    ):
        causes = [
            code
            for code in SAMPLING_ROOT_CAUSE_CODES
            if code in {*causes, "Other"}
        ]
    direct_metadata_result = {
        "streamIndex": int(decoder.stream_index),
        "seekMode": "exact",
        "sourceBeginStreamSeconds": source_begin,
        "sourceEndStreamSeconds": source_end,
        "sourceFrameCount": direct_source_frame_count,
        "sourceAverageFramesPerSecond":
            direct_source_fps,
        "selectedFrameIndices": direct_indices,
        "rawTensorShape": _tensor_shape(direct_batch.data),
        "rawTensorDataType": str(direct_batch.data.dtype),
        "rawTensorSha256": direct_raw_hash,
        "rawFrameSha256": direct_frame_hashes,
        "repeatRawTensorSha256": repeat_raw_hash,
        "repeatRawFrameSha256": repeat_frame_hashes,
        "compatibleResizedTensorSha256": compatible_hash,
        "compatibleResizedFrameSha256": compatible_frame_hashes,
        "actualPtsSeconds": direct_pts_serialized,
        "actualFrameDurationsSeconds": direct_durations_serialized,
        "repeatActualPtsSeconds": repeat_pts_serialized,
        "repeatActualFrameDurationsSeconds":
            repeat_durations_serialized,
        "actualFrameEndSeconds": direct_frame_ends,
        "ptsStrictlyIncreasing": all(
            second > first
            for first, second in zip(
                direct_pts_serialized,
                direct_pts_serialized[1:],
            )
        ),
        "firstPtsSeconds": (
            direct_pts_serialized[0]
            if direct_pts_serialized
            else None
        ),
        "lastPtsSeconds": (
            direct_pts_serialized[-1]
            if direct_pts_serialized
            else None
        ),
        "firstFrameEndSeconds":
            direct_frame_ends[0] if direct_frame_ends else None,
        "lastFrameEndSeconds":
            direct_frame_ends[-1] if direct_frame_ends else None,
    }
    comparison = {
        "sameSelectedFrameIndices": frame_indices_equal,
        "rawTensorByteIdentical": None,
        "rawFrameByteIdentical": None,
        "compatibleResizedTensorIdentityEqual":
            compatible_hash == qwen_final_tensor_hash,
        "compatibleResizedFrameIdentityEqual": compatible_frames_equal,
        "directRepeatTensorIdentityEqual": direct_repeat_raw_equal,
        "directRepeatFrameIdentityEqual": (
            len(direct_repeat_frames_equal) == len(direct_frame_hashes)
            and all(direct_repeat_frames_equal)
        ),
        "ptsRepeatEqual": pts_equal,
        "durationsRepeatEqual": durations_equal,
        "perFrameNominalPtsErrorSeconds": drift,
        "maximumAbsoluteNominalPtsErrorSeconds":
            round(maximum_absolute_drift, 9),
        "meanAbsoluteNominalPtsErrorSeconds":
            round(mean_absolute_drift, 9),
    }
    _set_case_audit_section(
        "directTorchCodecMetadata",
        direct_metadata_result,
    )
    _set_case_audit_section("comparison", comparison)
    corrected_passed = (
        verification["manifest"]["passed"]
        and pts_equal
        and durations_equal
        and direct_repeat_tensor_equal
    )
    causes = _add_legacy_actual_timing_cause(
        causes,
        legacy_error=legacy_error,
        corrected_passed=corrected_passed,
    )
    warnings = (
        list(visibility["warnings"])
        + list(verification["manifest"]["warningCodes"])
    )
    if not pts_equal:
        warnings.append("DirectTorchCodecRepeatPtsMismatch")
    if not durations_equal:
        warnings.append("DirectTorchCodecRepeatDurationsMismatch")
    if not direct_repeat_tensor_equal:
        warnings.append("DirectTorchCodecRepeatTensorMismatch")
    corrected_result = {
        "passed": corrected_passed,
        "errorCode":
            None if corrected_passed else (
                _primary_sampling_root_cause(causes) or "Other"
            ),
        "message": (
            None
            if corrected_passed
            else (
                "Actual-PTS candidate coverage, direct repeat parity, or "
                "Qwen/direct tensor identity is insufficient."
            )
        ),
    }
    case_result = {
        "caseId": request["caseId"],
        "candidateId": request["candidate"]["id"],
        "caseOrdinal": case_ordinal,
        "mode": request["candidate"]["mode"],
        "inputCaseSha256": input_case_sha256,
        "sourceVideoSha256":
            request["_validated"]["expectedVideoHash"],
        "status": "Succeeded",
        "timing": timing,
        "qwenMetadata": qwen_metadata_result,
        "directTorchCodecMetadata": direct_metadata_result,
        "comparison": comparison,
        "candidateVisibility": visibility,
        "reviewCoverage": coverage,
        "legacyValidation": legacy_result,
        "correctedPolicyValidation": corrected_result,
        "rootCauseCodes": causes,
        "primaryRootCause": _primary_sampling_root_cause(causes),
        "decisionSupportCode": _decision_support_code(
            legacy_passed,
            corrected_passed,
        ),
        "warnings": warnings,
        "failure": None,
    }
    case_result["caseAuditSha256"] = _case_audit_sha256(case_result)
    return case_result


def _input_policy_decision(cases: list[dict[str, Any]]) -> tuple[bool, str]:
    if any(case["status"] != "Succeeded" for case in cases):
        return False, "SamplingAuditInconclusive"
    causes = {
        cause
        for case in cases
        for cause in case["rootCauseCodes"]
    }
    if causes.intersection(
        {
            "CandidateCoordinateMismatch",
            "ReviewMediaTimelineMismatch",
        }
    ):
        return False, "CoordinateMappingDefect"
    if causes.intersection(
        {
            "CandidateHasNoSampledFrame",
            "CandidateHasOnlyOneSampledFrame",
            "CandidateStartCoverageInsufficient",
            "CandidateEndCoverageInsufficient",
            "SamplingCardinalityMismatch",
            "QwenTensorAndDirectTorchCodecFrameMismatch",
        }
    ):
        return False, "FrozenSamplingPolicyInvalid"
    if causes.intersection(
        {
            "RequestedTrimNotHonored",
        }
    ):
        return (
            all(
                case["correctedPolicyValidation"]["passed"]
                for case in cases
            ),
            "CoordinateMappingDefect",
        )
    if (
        causes
        and causes.issubset(
            {
                "InferredTimestampDrift",
                "AverageFpsInvalidForPtsMapping",
                "ActualPtsOutsideReview",
            }
        )
        and all(
            case["correctedPolicyValidation"]["passed"]
            for case in cases
        )
        and all(
            case["legacyValidation"] is not None
            and (
                case["legacyValidation"]["passed"]
                or case["legacyValidation"]["errorCode"]
                    in LEGACY_TIMING_VALIDATION_REASONS
            )
            for case in cases
        )
        and all(
            case["comparison"] is not None
            and case["comparison"]["directRepeatTensorIdentityEqual"]
            and case["comparison"]["directRepeatFrameIdentityEqual"]
            and case["comparison"]["ptsRepeatEqual"]
            and case["comparison"]["durationsRepeatEqual"]
            for case in cases
        )
    ):
        return True, "TimestampValidationDefectOnly"
    return False, "SamplingAuditInconclusive"


def _sampling_audit_payload(
    cases: list[dict[str, Any]],
) -> dict[str, Any]:
    root_counts = {
        code: 0
        for code in SAMPLING_ROOT_CAUSE_CODES
    }
    decision_counts = {
        code: 0
        for code in SAMPLING_DECISION_SUPPORT_CODES
    }
    cardinality_counts: dict[int, int] = {}
    for case in cases:
        for code in case["rootCauseCodes"]:
            root_counts[code] += 1
        decision_counts[case["decisionSupportCode"]] += 1
        visibility = case["candidateVisibility"]
        if visibility is not None:
            count = int(visibility["intersectingFrameCount"])
            cardinality_counts[count] = cardinality_counts.get(count, 0) + 1

    input_policy_valid, decision = _input_policy_decision(cases)
    payload = {
        "schemaVersion": SAMPLING_AUDIT_SCHEMA,
        "hostVersion": HOST_VERSION,
        "inputSchemaVersion": INPUT_SCHEMA,
        "backend": BACKEND,
        "videoBackend": VIDEO_BACKEND,
        "videoDecodeDevice": VIDEO_DECODE_DEVICE,
        "samplingPolicy": {
            "name": VIDEO_SAMPLING_POLICY,
            "framesPerSecond": VIDEO_FPS,
            "minimumFrames": VIDEO_MIN_FRAMES,
            "maximumFrames": VIDEO_MAX_FRAMES,
            "maximumPixelsPerFrame": VIDEO_MAX_PIXELS_PER_FRAME,
            "totalPixelBudget": VIDEO_TOTAL_PIXEL_BUDGET,
            "trimPolicy": VIDEO_TRIM_POLICY,
        },
        "caseCount": len(cases),
        "succeededCaseCount": sum(
            case["status"] == "Succeeded"
            for case in cases
        ),
        "failedCaseCount": sum(
            case["status"] == "Failed"
            for case in cases
        ),
        "inputPolicyValid": input_policy_valid,
        "inputPolicyValidityDecision": decision,
        "rootCauseCounts": [
            {"code": code, "count": root_counts[code]}
            for code in SAMPLING_ROOT_CAUSE_CODES
        ],
        "candidateIntersectingFrameCountDistribution": [
            {"frameCount": count, "caseCount": cardinality_counts[count]}
            for count in sorted(cardinality_counts)
        ],
        "decisionSupportCounts": [
            {"code": code, "count": decision_counts[code]}
            for code in SAMPLING_DECISION_SUPPORT_CODES
        ],
        "cases": cases,
    }
    payload["canonicalAuditSha256"] = _canonical_json_sha256(payload)
    return payload



__all__ = [name for name in globals() if not name.startswith("__")]
