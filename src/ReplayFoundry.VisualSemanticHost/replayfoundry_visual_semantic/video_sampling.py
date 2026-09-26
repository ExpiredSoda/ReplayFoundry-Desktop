"""ReplayFoundry local visual-semantic host implementation module."""
from __future__ import annotations

from .observation_validation import *  # noqa: F401,F403


def _tensor_identity(video: Any) -> tuple[str, list[str]]:
    try:
        contiguous = video.detach().cpu().contiguous()
        combined = hashlib.sha256(contiguous.numpy().tobytes()).hexdigest()
        frames = [
            hashlib.sha256(frame.contiguous().numpy().tobytes()).hexdigest()
            for frame in contiguous
        ]
    except Exception as error:
        _fail(
            InferenceError,
            "Could not calculate decoded-frame identity: "
            f"{type(error).__name__}: {error}",
        )
    return combined, frames


def _effective_video_sampling_limits(request: dict[str, Any]) -> dict[str, Any]:
    limits = request.get("_visualSamplingLimits")
    if limits is None:
        return {
            "policyVersion": VIDEO_SAMPLING_POLICY,
            "minimumFrames": VIDEO_MIN_FRAMES,
            "maximumFrames": VIDEO_MAX_FRAMES,
            "maximumPixelsPerFrame": VIDEO_MAX_PIXELS_PER_FRAME,
            "maximumTotalVideoPixels": VIDEO_TOTAL_PIXEL_BUDGET,
        }

    expected_limit_keys = {
        "policyVersion",
        "minimumFrames",
        "maximumFrames",
        "maximumPixelsPerFrame",
        "maximumTotalVideoPixels",
    }
    if not isinstance(limits, dict) or set(limits) != expected_limit_keys:
        _fail(InferenceError, "The video sampling limit override is invalid.")
    minimum_frames = limits["minimumFrames"]
    maximum_frames = limits["maximumFrames"]
    maximum_pixels_per_frame = limits["maximumPixelsPerFrame"]
    maximum_total_video_pixels = limits["maximumTotalVideoPixels"]
    if (
        not isinstance(limits["policyVersion"], str)
        or not limits["policyVersion"].strip()
        or isinstance(minimum_frames, bool)
        or not isinstance(minimum_frames, int)
        or isinstance(maximum_frames, bool)
        or not isinstance(maximum_frames, int)
        or isinstance(maximum_pixels_per_frame, bool)
        or not isinstance(maximum_pixels_per_frame, int)
        or isinstance(maximum_total_video_pixels, bool)
        or not isinstance(maximum_total_video_pixels, int)
        or minimum_frames < 2
        or maximum_frames < minimum_frames
        or maximum_frames > VIDEO_MAX_FRAMES
        or maximum_pixels_per_frame <= 0
        or maximum_pixels_per_frame > VIDEO_MAX_WIDTH * VIDEO_MAX_HEIGHT
        or maximum_total_video_pixels < maximum_pixels_per_frame
        or maximum_total_video_pixels > VIDEO_TOTAL_PIXEL_BUDGET
    ):
        _fail(InferenceError, "The video sampling limit override exceeds host bounds.")
    return dict(limits)


def _validate_qwen_sampling_structure(
    video: Any,
    metadata: Any,
    request: dict[str, Any],
) -> dict[str, Any]:
    limits = _effective_video_sampling_limits(request)
    minimum_frames = limits["minimumFrames"]
    maximum_frames = limits["maximumFrames"]
    maximum_pixels_per_frame = limits["maximumPixelsPerFrame"]
    maximum_total_video_pixels = limits["maximumTotalVideoPixels"]
    shape = getattr(video, "shape", None)
    if shape is None or len(shape) != 4:
        _fail(
            InferenceError,
            "The TorchCodec video backend returned an unsupported tensor shape.",
        )

    frame_count = int(shape[0])
    channels = int(shape[-3])
    height = int(shape[-2])
    width = int(shape[-1])
    decode_device = getattr(
        getattr(video, "device", None),
        "type",
        None,
    )
    if decode_device != VIDEO_DECODE_DEVICE:
        _fail(
            InferenceError,
            "TorchCodec video decoding must remain on the CPU.",
        )
    pixels_per_frame = height * width
    total_pixels = frame_count * pixels_per_frame
    if frame_count < minimum_frames or frame_count > maximum_frames:
        _fail(
            InferenceError,
            f"Decoded review window contains {frame_count} frames; policy "
            f"{limits['policyVersion']} requires {minimum_frames} through "
            f"{maximum_frames}.",
        )
    if channels != 3 or height <= 0 or width <= 0:
        _fail(InferenceError, "Decoded review-window dimensions must be positive.")
    if height > VIDEO_MAX_HEIGHT or width > VIDEO_MAX_WIDTH:
        _fail(
            InferenceError,
            f"Decoded review window is {width}x{height}; the frozen policy "
            f"limits each dimension to {VIDEO_MAX_WIDTH}x{VIDEO_MAX_HEIGHT}.",
        )
    if pixels_per_frame > maximum_pixels_per_frame:
        _fail(
            InferenceError,
            f"Decoded review-window frames contain {pixels_per_frame} pixels; "
            f"the policy limit is {maximum_pixels_per_frame}.",
        )
    if total_pixels > maximum_total_video_pixels:
        _fail(
            InferenceError,
            f"Decoded review window contains {total_pixels} total pixels; "
            f"the policy limit is {maximum_total_video_pixels}.",
        )

    if not isinstance(metadata, dict):
        _fail(InferenceError, "The video backend returned malformed metadata.")
    if metadata.get("video_backend") != VIDEO_BACKEND:
        _fail(
            InferenceError,
            f"The frozen host requires the {VIDEO_BACKEND} video backend "
            "and prohibits fallback.",
        )
    raw_total_frames = metadata.get("total_num_frames")
    if (
        isinstance(raw_total_frames, bool)
        or not isinstance(raw_total_frames, (int, float))
        or not math.isfinite(float(raw_total_frames))
        or float(raw_total_frames) < frame_count
    ):
        _fail(InferenceError, "The video backend returned an invalid frame count.")
    raw_indices = metadata.get("frames_indices")
    if hasattr(raw_indices, "tolist"):
        raw_indices = raw_indices.tolist()
    if not isinstance(raw_indices, list) or len(raw_indices) != frame_count:
        _fail(InferenceError, "The video backend returned invalid frame indices.")
    indices: list[int] = []
    for raw_index in raw_indices:
        try:
            numeric_index = float(raw_index)
        except (TypeError, ValueError, OverflowError):
            numeric_index = math.nan
        if (
            isinstance(raw_index, bool)
            or not math.isfinite(numeric_index)
            or numeric_index < 0
            or not numeric_index.is_integer()
        ):
            _fail(
                InferenceError,
                "The video backend returned an out-of-range frame index.",
            )
        indices.append(int(numeric_index))

    if indices != sorted(indices) or len(indices) != len(set(indices)):
        _fail(
            InferenceError,
            "Decoded frame indices must be strictly increasing.",
        )

    raw_fps = metadata.get("fps")
    try:
        frames_per_second = float(raw_fps)
    except (TypeError, ValueError, OverflowError):
        frames_per_second = math.nan
    if not math.isfinite(frames_per_second) or frames_per_second <= 0:
        _fail(
            InferenceError,
            "The video backend returned invalid source-frame timing.",
        )

    # TorchCodec reports absolute source-frame indices for trimmed windows,
    # while total_num_frames may describe only the decoded window. Therefore,
    # validate indices against the absolute source interval rather than
    # comparing them to the window-scoped total.
    timestamps = [
        index / frames_per_second
        for index in indices
    ]
    validated = request["_validated"]
    review_start = float(validated["sourceAbsoluteOffset"])
    candidate_start = review_start + float(validated["candidateStart"])
    candidate_end = review_start + float(validated["candidateEnd"])
    _set_failure_sampling(
        sourceAverageFramesPerSecond=frames_per_second,
        frameIndices=list(indices),
        inferredTimestampsSeconds=[
            round(timestamp, 9)
            for timestamp in timestamps
        ],
        frameCount=frame_count,
        candidateIntersectingFrameCount=sum(
            1
            for timestamp in timestamps
            if candidate_start <= timestamp < candidate_end
        ),
    )
    tensor_hash, frame_hashes = _tensor_identity(video)
    return {
        "frameCount": frame_count,
        "tensorShape": [
            int(value)
            for value in shape
        ],
        "tensorDataType": str(getattr(video, "dtype", "unknown")),
        "sourceFramesPerSecond": frames_per_second,
        "frameIndices": indices,
        "inferredTimestampsSeconds": [
            round(timestamp, 9)
            for timestamp in timestamps
        ],
        "sampledTensorSha256": tensor_hash,
        "sampledFrameSha256": frame_hashes,
        "totalNumFrames": float(raw_total_frames),
        "videoBackend": metadata["video_backend"],
        "videoDecodeDevice": decode_device,
        "samplingPolicyVersion": str(limits["policyVersion"]),
        "minimumFrames": minimum_frames,
        "maximumFrames": maximum_frames,
        "maximumPixelsPerFrame": maximum_pixels_per_frame,
        "maximumTotalVideoPixels": maximum_total_video_pixels,
    }


def _validate_legacy_nominal_coverage(
    sampling: dict[str, Any],
    request: dict[str, Any],
) -> None:
    """Retain the frozen pre-Branch-1 index/FPS checks for audit only."""

    timestamps = sampling["inferredTimestampsSeconds"]
    frames_per_second = sampling["sourceFramesPerSecond"]
    validated = request["_validated"]
    review_start = float(validated["sourceAbsoluteOffset"])
    review_end = review_start + float(validated["videoDuration"])
    candidate_start = review_start + float(validated["candidateStart"])
    candidate_end = review_start + float(validated["candidateEnd"])
    tolerance = max(1e-6, 1.0 / frames_per_second)

    if any(
        not math.isfinite(timestamp)
        or timestamp < review_start - 1e-6
        or timestamp > review_end + 1e-6
        for timestamp in timestamps
    ):
        _fail_legacy_timing_validation(
            "NominalTimestampOutsideReview",
            "A decoded frame lies outside the bounded source review interval.",
        )

    if (
        timestamps[0] > candidate_start + tolerance
        or timestamps[-1] < candidate_end - tolerance
    ):
        _fail_legacy_timing_validation(
            "NominalCandidateCoverageInsufficient",
            "Decoded sampling does not cover the bounded candidate interval.",
        )


def _sampling_timing(request: dict[str, Any]) -> dict[str, float]:
    validated = request["_validated"]
    review_start = float(validated["sourceAbsoluteOffset"])
    review_duration = float(validated["videoDuration"])
    candidate_relative_start = float(validated["candidateStart"])
    candidate_relative_end = float(validated["candidateEnd"])
    return {
        "sourceAbsoluteOffsetSeconds": review_start,
        "reviewDurationSeconds": review_duration,
        "requestedAbsoluteReviewStartSeconds": review_start,
        "requestedAbsoluteReviewEndSeconds": review_start + review_duration,
        "candidateRelativeStartSeconds": candidate_relative_start,
        "candidateRelativeEndSeconds": candidate_relative_end,
        "candidateAbsoluteStartSeconds":
            review_start + candidate_relative_start,
        "candidateAbsoluteEndSeconds":
            review_start + candidate_relative_end,
    }


def _finite_float_list(value: Any, location: str) -> list[float]:
    if hasattr(value, "detach"):
        value = value.detach().cpu()
    if hasattr(value, "tolist"):
        value = value.tolist()
    if not isinstance(value, list):
        _fail(InferenceError, f"{location} must be a list.")
    result: list[float] = []
    for index, item in enumerate(value):
        try:
            numeric = float(item)
        except (TypeError, ValueError, OverflowError):
            numeric = math.nan
        if not math.isfinite(numeric):
            _fail(
                InferenceError,
                f"{location}[{index}] must be finite.",
            )
        result.append(numeric)
    return result


def _integer_list(value: Any, location: str) -> list[int]:
    if hasattr(value, "detach"):
        value = value.detach().cpu()
    if hasattr(value, "tolist"):
        value = value.tolist()
    if not isinstance(value, list):
        _fail(InferenceError, f"{location} must be a list.")
    result: list[int] = []
    for index, item in enumerate(value):
        try:
            numeric = float(item)
        except (TypeError, ValueError, OverflowError):
            numeric = math.nan
        if (
            isinstance(item, bool)
            or not math.isfinite(numeric)
            or not numeric.is_integer()
            or numeric < 0
        ):
            _fail(
                InferenceError,
                f"{location}[{index}] must be a nonnegative integer.",
            )
        result.append(int(numeric))
    return result


def _tensor_shape(value: Any) -> list[int]:
    shape = getattr(value, "shape", None)
    if shape is None:
        _fail(InferenceError, "Decoded tensor has no shape.")
    return [int(dimension) for dimension in shape]



__all__ = [name for name in globals() if not name.startswith("__")]
