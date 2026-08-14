"""ReplayFoundry local visual-semantic host implementation module."""
from __future__ import annotations

from .sampling_capture import *  # noqa: F401,F403

def _candidate_sampling_coverage_policy_manifest() -> dict[str, Any]:
    return {
        "version": CANDIDATE_SAMPLING_COVERAGE_POLICY,
        "frozenSamplingFramesPerSecond": VIDEO_FPS,
        "frozenSamplingIntervalSeconds": 1.0 / VIDEO_FPS,
        "minimumDistinctCandidateFrames": 2,
        "candidateIntervalSemantics": "HalfOpenFrameIntervals",
        "reviewFrameIntervalTolerance": "MaximumActualFrameDuration",
        "candidateEdgeDistanceTolerance": (
            "FrozenSamplingIntervalPlusMaximumActualFrameDuration"
        ),
        "inferredTimestampUse": "DiagnosticsOnly",
        "inferredActualDriftWarningTolerance": (
            "MaximumActualFrameDuration"
        ),
        "containerTimestampResolutionToleranceSeconds":
            CONTAINER_TIMESTAMP_RESOLUTION_TOLERANCE_SECONDS,
        "candidateMutationPermitted": False,
    }


def _execution_timing_case_sha256(case: dict[str, Any]) -> str:
    identity = copy.deepcopy(case)
    identity.pop("canonicalCaseTimingSha256", None)
    return _canonical_json_sha256(identity)


def _execution_timing_payload(
    cases: list[dict[str, Any]],
) -> dict[str, Any]:
    expected_ordinals = list(range(1, len(cases) + 1))
    actual_ordinals = [case["caseOrdinal"] for case in cases]
    if actual_ordinals != expected_ordinals:
        _fail(
            OutputError,
            "Execution-timing cases must preserve stable request order.",
        )
    payload = {
        "schemaVersion": EXECUTION_TIMING_SCHEMA,
        "coveragePolicy": _candidate_sampling_coverage_policy_manifest(),
        "timingSource": AUTHORITATIVE_SAMPLING_TIMING_SOURCE,
        "caseCount": len(cases),
        "cases": cases,
    }
    payload["canonicalExecutionTimingSha256"] = (
        _canonical_json_sha256(payload)
    )
    return payload


def _canonical_execution_timing_inputs(
    inferred_timestamps: list[float],
    direct_pts: list[float],
    direct_durations: list[float],
) -> tuple[list[float], list[float], list[float]]:
    """Freeze the numeric inputs used by both telemetry and its verifier."""

    if (
        not inferred_timestamps
        or len(inferred_timestamps) != len(direct_pts)
        or len(direct_pts) != len(direct_durations)
    ):
        _fail(
            InferenceError,
            "Execution timing requires equal nonempty timestamp arrays.",
        )

    return (
        [round(value, 9) for value in inferred_timestamps],
        [round(value, 9) for value in direct_pts],
        [round(value, 9) for value in direct_durations],
    )


def _canonical_execution_timing_drift(
    inferred_timestamps: list[float],
    direct_pts: list[float],
    direct_durations: list[float],
) -> tuple[list[float], float, float, float]:
    """Mirror the verifier's per-frame rounding before aggregation."""

    if (
        not inferred_timestamps
        or len(inferred_timestamps) != len(direct_pts)
        or len(direct_pts) != len(direct_durations)
    ):
        _fail(
            InferenceError,
            "Execution timing requires equal nonempty timestamp arrays.",
        )

    per_frame = [
        round(inferred - actual, 9)
        for inferred, actual in zip(
            inferred_timestamps,
            direct_pts,
        )
    ]
    return (
        per_frame,
        round(max(abs(value) for value in per_frame), 9),
        round(
            sum(abs(value) for value in per_frame) /
            len(per_frame),
            9,
        ),
        round(max(direct_durations), 9),
    )


def _verify_actual_pts_sampling(
    request: dict[str, Any],
    case_ordinal: int,
    qwen_video: Any,
    qwen_metadata: dict[str, Any],
    qwen_identity: dict[str, Any],
    video_element: dict[str, Any],
    torchcodec: Any,
) -> dict[str, Any]:
    """Verify exact Qwen-selected indices without replacing Qwen model input."""

    timing = _sampling_timing(request)
    selected_indices = list(qwen_identity["frameIndices"])
    if (
        _integer_list(
            qwen_metadata.get("frames_indices"),
            "Qwen selected frame indices",
        )
        != selected_indices
    ):
        _fail(
            InferenceError,
            "Qwen selected frame indices changed during timing verification.",
        )
    source_fps = float(qwen_identity["sourceFramesPerSecond"])
    inferred_timestamps = [
        index / source_fps
        for index in selected_indices
    ]

    _set_failure_stage("DirectTorchCodecDecode")
    decoder = torchcodec.decoders.VideoDecoder(
        str(request["_validated"]["videoPath"]),
        num_ffmpeg_threads=int(os.environ.get("TORCHCODEC_NUM_THREADS", 8)),
        seek_mode="exact",
    )
    direct_batch = decoder.get_frames_at(indices=selected_indices)
    direct_pts = _finite_float_list(
        direct_batch.pts_seconds,
        "Direct TorchCodec PTS",
    )
    direct_durations = _finite_float_list(
        direct_batch.duration_seconds,
        "Direct TorchCodec frame durations",
    )
    if (
        len(direct_pts) != len(selected_indices)
        or len(direct_durations) != len(selected_indices)
    ):
        _fail(
            InferenceError,
            "Direct TorchCodec timing cardinality does not match the exact "
            "Qwen-selected frame indices.",
        )
    if any(duration <= 0 for duration in direct_durations):
        _fail(
            InferenceError,
            "Direct TorchCodec returned a non-positive frame duration.",
        )
    if any(
        second <= first
        for first, second in zip(direct_pts, direct_pts[1:])
    ):
        _fail(
            InferenceError,
            "Direct TorchCodec PTS must be strictly increasing.",
        )

    (
        inferred_serialized,
        direct_pts_serialized,
        direct_durations_serialized,
    ) = _canonical_execution_timing_inputs(
        inferred_timestamps,
        direct_pts,
        direct_durations,
    )
    _set_failure_sampling(
        sourceAverageFramesPerSecond=source_fps,
        frameIndices=list(selected_indices),
        inferredTimestampsSeconds=inferred_serialized,
        actualPtsSeconds=direct_pts_serialized,
        actualFrameDurationsSeconds=direct_durations_serialized,
        frameCount=len(selected_indices),
    )

    _set_failure_stage("SamplingComparison")
    compatible_resized = _resize_direct_frames_like_qwen(
        direct_batch.data,
        video_element,
    )
    direct_compatible_hash, direct_compatible_frame_hashes = (
        _tensor_identity(compatible_resized)
    )
    direct_raw_hash, direct_raw_frame_hashes = _tensor_identity(
        direct_batch.data
    )
    qwen_tensor_hash, qwen_frame_hashes = _tensor_identity(qwen_video)
    if (
        qwen_tensor_hash != qwen_identity["sampledTensorSha256"]
        or qwen_frame_hashes != qwen_identity["sampledFrameSha256"]
    ):
        _fail(
            InferenceError,
            "Qwen sampled tensor identity changed during timing verification.",
        )
    compatible_tensor_equal = (
        direct_compatible_hash == qwen_tensor_hash
    )
    compatible_frames_equal = (
        len(direct_compatible_frame_hashes) == len(qwen_frame_hashes)
        and all(
            first == second
            for first, second in zip(
                direct_compatible_frame_hashes,
                qwen_frame_hashes,
            )
        )
    )

    source_begin_raw = decoder.metadata.begin_stream_seconds
    source_end_raw = decoder.metadata.end_stream_seconds
    source_begin = (
        None if source_begin_raw is None else float(source_begin_raw)
    )
    source_end = (
        None if source_end_raw is None else float(source_end_raw)
    )
    direct_source_fps = float(decoder.metadata.average_fps)
    direct_source_frame_count = int(decoder.metadata.num_frames)
    if (
        source_begin is None
        or source_end is None
        or not math.isfinite(source_begin)
        or not math.isfinite(source_end)
        or source_end <= source_begin
        or not math.isfinite(direct_source_fps)
        or direct_source_fps <= 0
        or direct_source_frame_count <= 0
    ):
        _fail(
            InferenceError,
            "Direct TorchCodec returned incomplete source timing metadata.",
        )
    source_metadata_matches = (
        abs(source_fps - direct_source_fps) <= 1e-9
        and all(
            index < direct_source_frame_count
            for index in selected_indices
        )
    )

    (
        drift,
        maximum_absolute_drift,
        mean_absolute_drift,
        drift_warning_tolerance,
    ) = _canonical_execution_timing_drift(
        inferred_serialized,
        direct_pts_serialized,
        direct_durations_serialized,
    )
    visibility = _candidate_visibility(
        selected_indices,
        direct_pts_serialized,
        direct_durations_serialized,
        timing["candidateAbsoluteStartSeconds"],
        timing["candidateAbsoluteEndSeconds"],
    )
    coverage = _review_coverage(
        direct_pts_serialized,
        direct_durations_serialized,
        timing["requestedAbsoluteReviewStartSeconds"],
        timing["requestedAbsoluteReviewEndSeconds"],
        source_begin,
        source_end,
        maximum_absolute_drift,
    )
    _set_failure_sampling(
        candidateIntersectingFrameCount=
            visibility["intersectingFrameCount"],
    )
    (
        review_outside_source,
        candidate_inside_source,
        container_tail_within_tolerance,
    ) = _source_timeline_relation(
        timing,
        source_begin,
        source_end,
        visibility["sourceFrameDurationToleranceSeconds"],
        source_fps,
    )
    container_tail = (
        review_outside_source
        and coverage["requestedTrimHonored"]
        and candidate_inside_source
        and container_tail_within_tolerance
    )
    candidate_inside_requested_review = (
        timing["candidateAbsoluteStartSeconds"]
        >= timing["requestedAbsoluteReviewStartSeconds"] - 1e-9
        and timing["candidateAbsoluteEndSeconds"]
        <= timing["requestedAbsoluteReviewEndSeconds"] + 1e-9
    )
    source_timeline_valid = (
        not review_outside_source
        or container_tail
    )
    passed = (
        coverage["requestedTrimHonored"]
        and coverage["allActualPtsInsideRequestedReview"]
        and coverage["allActualFrameIntervalsInsideRequestedReview"]
        and visibility["intersectingFrameCount"] >= 2
        and visibility["hasAtLeastTwoTemporallyDistinctFrames"]
        and visibility["beginningJudgmentSupportable"]
        and visibility["outcomeJudgmentSupportable"]
        and compatible_tensor_equal
        and compatible_frames_equal
        and source_metadata_matches
        and candidate_inside_requested_review
        and candidate_inside_source
        and source_timeline_valid
    )

    warning_codes: list[str] = []
    if maximum_absolute_drift > drift_warning_tolerance + 1e-9:
        warning_codes.append("InferredTimestampDrift")
    if container_tail:
        warning_codes.append("ContainerDurationExceedsVideoStreamEnd")

    case_manifest = {
        "caseId": request["caseId"],
        "candidateId": request["candidate"]["id"],
        "caseOrdinal": case_ordinal,
        "reviewVideoSha256":
            request["_validated"]["expectedVideoHash"],
        "requestedAbsoluteReviewStartSeconds":
            timing["requestedAbsoluteReviewStartSeconds"],
        "requestedAbsoluteReviewEndSeconds":
            timing["requestedAbsoluteReviewEndSeconds"],
        "candidateAbsoluteStartSeconds":
            timing["candidateAbsoluteStartSeconds"],
        "candidateAbsoluteEndSeconds":
            timing["candidateAbsoluteEndSeconds"],
        "sourceBeginStreamSeconds": source_begin,
        "sourceEndStreamSeconds": source_end,
        "sourceAverageFramesPerSecond": direct_source_fps,
        "selectedFrameIndices": selected_indices,
        "inferredTimestampsSeconds": inferred_serialized,
        "actualPtsSeconds": direct_pts_serialized,
        "actualFrameDurationsSeconds": direct_durations_serialized,
        "qwenFinalTensorSha256": qwen_tensor_hash,
        "qwenFinalFrameSha256": qwen_frame_hashes,
        "directCompatibleTensorSha256": direct_compatible_hash,
        "directCompatibleFrameSha256":
            direct_compatible_frame_hashes,
        "compatibleTensorIdentityEqual": compatible_tensor_equal,
        "compatibleFrameIdentityEqual": compatible_frames_equal,
        "candidateIntersectingFrameCount":
            visibility["intersectingFrameCount"],
        "hasAtLeastTwoTemporallyDistinctFrames":
            visibility["hasAtLeastTwoTemporallyDistinctFrames"],
        "beginningJudgmentSupportable":
            visibility["beginningJudgmentSupportable"],
        "outcomeJudgmentSupportable":
            visibility["outcomeJudgmentSupportable"],
        "nearestSampleDistanceToCandidateStartSeconds":
            visibility[
                "nearestSampleDistanceToCandidateStartSeconds"
            ],
        "nearestFrameEndDistanceToCandidateEndSeconds":
            visibility[
                "nearestFrameEndDistanceToCandidateEndSeconds"
            ],
        "maximumGapSeconds": visibility["maximumGapSeconds"],
        "allActualPtsInsideRequestedReview":
            coverage["allActualPtsInsideRequestedReview"],
        "allActualFrameIntervalsInsideRequestedReview":
            coverage[
                "allActualFrameIntervalsInsideRequestedReview"
            ],
        "requestedTrimHonored": coverage["requestedTrimHonored"],
        "maximumAbsoluteInferredPtsDriftSeconds":
            round(maximum_absolute_drift, 9),
        "meanAbsoluteInferredPtsDriftSeconds":
            round(mean_absolute_drift, 9),
        "inferredPtsDriftWarningToleranceSeconds":
            round(drift_warning_tolerance, 9),
        "containerDurationExceedsVideoStreamEnd": container_tail,
        "warningCodes": warning_codes,
        "passed": passed,
    }
    case_manifest["canonicalCaseTimingSha256"] = (
        _execution_timing_case_sha256(case_manifest)
    )
    return {
        "manifest": case_manifest,
        "decoder": decoder,
        "directBatch": direct_batch,
        "directPts": direct_pts,
        "directDurations": direct_durations,
        "directRawTensorSha256": direct_raw_hash,
        "directRawFrameSha256": direct_raw_frame_hashes,
        "directCompatibleTensorSha256": direct_compatible_hash,
        "directCompatibleFrameSha256":
            direct_compatible_frame_hashes,
        "qwenTensorSha256": qwen_tensor_hash,
        "qwenFrameSha256": qwen_frame_hashes,
        "compatibleTensorIdentityEqual": compatible_tensor_equal,
        "compatibleFrameIdentityEqual": compatible_frames_equal,
        "sourceBeginStreamSeconds": source_begin,
        "sourceEndStreamSeconds": source_end,
        "sourceAverageFramesPerSecond": direct_source_fps,
        "sourceFrameCount": direct_source_frame_count,
        "sourceMetadataMatches": source_metadata_matches,
        "visibility": visibility,
        "coverage": coverage,
        "drift": drift,
        "maximumAbsoluteDrift": maximum_absolute_drift,
        "meanAbsoluteDrift": mean_absolute_drift,
        "candidateInsideSource": candidate_inside_source,
        "sourceTimelineValid": source_timeline_valid,
    }


def _require_actual_pts_sampling(
    verification: dict[str, Any],
) -> None:
    if verification["manifest"]["passed"]:
        return
    _fail(
        InferenceError,
        "Actual TorchCodec PTS/duration validation did not satisfy "
        f"{CANDIDATE_SAMPLING_COVERAGE_POLICY}.",
    )


def _process_video_inputs(
    request: dict[str, Any],
    case_ordinal: int,
    messages: list[dict[str, Any]],
    process_vision_info: Any,
    torchcodec: Any,
) -> tuple[
    list[Any],
    list[Any],
    dict[str, Any],
    dict[str, Any],
    dict[str, Any],
]:
    image_inputs, video_inputs, video_kwargs = process_vision_info(
        messages,
        image_patch_size=16,
        return_video_kwargs=True,
        return_video_metadata=True,
    )
    if image_inputs:
        _fail(InferenceError, "Video-only host unexpectedly produced image inputs.")
    if not video_inputs or len(video_inputs) != 1:
        _fail(InferenceError, "Exactly one local video input is required.")
    if video_kwargs != {"do_sample_frames": False}:
        _fail(
            InferenceError,
            "The Qwen utility requested an unsupported processor-side "
            "video-sampling policy.",
        )

    videos, video_metadatas = zip(*video_inputs)
    video_values = list(videos)
    metadata_values = list(video_metadatas)
    identity = _validate_qwen_sampling_structure(
        video_values[0],
        metadata_values[0],
        request,
    )
    verification = _verify_actual_pts_sampling(
        request,
        case_ordinal,
        video_values[0],
        metadata_values[0],
        identity,
        messages[1]["content"][0],
        torchcodec,
    )
    _require_actual_pts_sampling(verification)
    timing_manifest = verification["manifest"]
    del verification
    return (
        video_values,
        metadata_values,
        video_kwargs,
        identity,
        timing_manifest,
    )



__all__ = [name for name in globals() if not name.startswith("__")]
