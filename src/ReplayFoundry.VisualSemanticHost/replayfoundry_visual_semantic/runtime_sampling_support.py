"""Runtime helpers for qualified video sampling."""
from __future__ import annotations

from .video_sampling import *  # noqa: F401,F403


def _resize_direct_frames_like_qwen(
    raw_video: Any,
    video_element: dict[str, Any],
) -> Any:
    try:
        from qwen_vl_utils.vision_process import (
            FRAME_FACTOR,
            SPATIAL_MERGE_SIZE,
            VIDEO_MAX_TOKEN_NUM,
            VIDEO_MIN_TOKEN_NUM,
            smart_resize,
        )
        from torchvision import transforms
        from torchvision.transforms import InterpolationMode
    except Exception as error:
        _fail(
            InitializationError,
            "Could not import the pinned Qwen resize implementation: "
            f"{type(error).__name__}: {error}",
        )

    image_factor = 16 * SPATIAL_MERGE_SIZE
    frame_count, _, height, width = raw_video.shape
    min_pixels = video_element.get(
        "min_pixels",
        VIDEO_MIN_TOKEN_NUM * image_factor * image_factor,
    )
    total_pixels = video_element.get(
        "total_pixels",
        float("inf"),
    )
    max_pixels = max(
        min(
            VIDEO_MAX_TOKEN_NUM * image_factor * image_factor,
            total_pixels / frame_count * FRAME_FACTOR,
        ),
        int(min_pixels * 1.05),
    )
    max_pixels = min(video_element.get("max_pixels", max_pixels), max_pixels)
    if "resized_height" in video_element and "resized_width" in video_element:
        resized_height, resized_width = smart_resize(
            video_element["resized_height"],
            video_element["resized_width"],
            factor=image_factor,
        )
    else:
        resized_height, resized_width = smart_resize(
            height,
            width,
            factor=image_factor,
            min_pixels=min_pixels,
            max_pixels=max_pixels,
        )
    return transforms.functional.resize(
        raw_video,
        [resized_height, resized_width],
        interpolation=InterpolationMode.BICUBIC,
        antialias=True,
    ).float()


def _candidate_visibility(
    frame_indices: list[int],
    pts_seconds: list[float],
    duration_seconds: list[float],
    candidate_start: float,
    candidate_end: float,
) -> dict[str, Any]:
    frame_ends = [
        pts + duration
        for pts, duration in zip(pts_seconds, duration_seconds)
    ]
    intersecting_positions = [
        index
        for index, (pts, frame_end) in enumerate(zip(pts_seconds, frame_ends))
        if pts < candidate_end and frame_end > candidate_start
    ]
    intersecting_pts = [
        pts_seconds[index]
        for index in intersecting_positions
    ]
    sampled_pts_inside_candidate = [
        pts
        for pts in pts_seconds
        if candidate_start <= pts < candidate_end
    ]
    intersecting_indices = [
        frame_indices[index]
        for index in intersecting_positions
    ]
    source_frame_tolerance = (
        max(duration_seconds)
        if duration_seconds
        else 0.0
    )
    sampling_interval = 1.0 / VIDEO_FPS
    start_distance = (
        min(abs(pts - candidate_start) for pts in pts_seconds)
        if pts_seconds
        else None
    )
    end_distance = (
        min(abs(frame_end - candidate_end) for frame_end in frame_ends)
        if frame_ends
        else None
    )

    around_positions = list(intersecting_positions)
    before = [
        index
        for index, pts in enumerate(pts_seconds)
        if pts < candidate_start
    ]
    after = [
        index
        for index, pts in enumerate(pts_seconds)
        if pts >= candidate_end
    ]
    if before:
        around_positions.append(before[-1])
    if after:
        around_positions.append(after[0])
    around_pts = sorted(
        {
            pts_seconds[index]
            for index in around_positions
        }
    )
    maximum_gap = (
        max(
            second - first
            for first, second in zip(around_pts, around_pts[1:])
        )
        if len(around_pts) >= 2
        else None
    )
    distinct_count = len(
        {
            round(pts_seconds[index], 9)
            for index in intersecting_positions
        }
    )
    has_two = distinct_count >= 2
    allowed_distance = sampling_interval + source_frame_tolerance
    beginning_supportable = (
        has_two
        and start_distance is not None
        and start_distance <= allowed_distance + 1e-9
    )
    outcome_supportable = (
        has_two
        and end_distance is not None
        and end_distance <= allowed_distance + 1e-9
    )
    warnings: list[str] = []
    if not intersecting_positions:
        warnings.append("CandidateHasNoSampledFrame")
    elif not has_two:
        warnings.append("CandidateHasOnlyOneSampledFrame")
    if not beginning_supportable:
        warnings.append("CandidateStartCoverageInsufficient")
    if not outcome_supportable:
        warnings.append("CandidateEndCoverageInsufficient")

    return {
        "intersectingFrameCount": len(intersecting_positions),
        "intersectingFrameIndices": intersecting_indices,
        "intersectingPtsSeconds": [
            round(value, 9)
            for value in intersecting_pts
        ],
        "sampledPtsInsideCandidateSeconds": [
            round(value, 9)
            for value in sampled_pts_inside_candidate
        ],
        "nearestSampleDistanceToCandidateStartSeconds":
            None if start_distance is None else round(start_distance, 9),
        "nearestFrameEndDistanceToCandidateEndSeconds":
            None if end_distance is None else round(end_distance, 9),
        "maximumGapSeconds":
            None if maximum_gap is None else round(maximum_gap, 9),
        "hasAtLeastTwoTemporallyDistinctFrames": has_two,
        "beginningJudgmentSupportable": beginning_supportable,
        "outcomeJudgmentSupportable": outcome_supportable,
        "frozenSamplingIntervalSeconds": sampling_interval,
        "sourceFrameDurationToleranceSeconds":
            round(source_frame_tolerance, 9),
        "warnings": warnings,
    }


def _review_coverage(
    pts_seconds: list[float],
    duration_seconds: list[float],
    review_start: float,
    review_end: float,
    source_begin: float | None,
    source_end: float | None,
    maximum_absolute_drift: float,
) -> dict[str, Any]:
    frame_ends = [
        pts + duration
        for pts, duration in zip(pts_seconds, duration_seconds)
    ]
    frame_tolerance = max(duration_seconds) if duration_seconds else 0.0
    all_pts_inside = bool(pts_seconds) and all(
        review_start - frame_tolerance - 1e-9
        <= pts
        < review_end + frame_tolerance + 1e-9
        for pts in pts_seconds
    )
    all_intervals_inside = bool(frame_ends) and all(
        review_start - frame_tolerance - 1e-9 <= pts
        and frame_end <= review_end + frame_tolerance + 1e-9
        for pts, frame_end in zip(pts_seconds, frame_ends)
    )
    allowed_edge_distance = (1.0 / VIDEO_FPS) + frame_tolerance
    first_relation = (
        pts_seconds[0] - review_start
        if pts_seconds
        else None
    )
    last_relation = (
        frame_ends[-1] - review_end
        if frame_ends
        else None
    )
    requested_trim_honored = (
        all_pts_inside
        and all_intervals_inside
        and first_relation is not None
        and last_relation is not None
        and first_relation <= allowed_edge_distance + 1e-9
        and last_relation >= -allowed_edge_distance - 1e-9
    )
    duration_variation = (
        max(duration_seconds) - min(duration_seconds)
        if duration_seconds
        else 0.0
    )
    return {
        "requestedTrimHonored": requested_trim_honored,
        "allActualPtsInsideRequestedReview": all_pts_inside,
        "allActualFrameIntervalsInsideRequestedReview":
            all_intervals_inside,
        "firstPtsMinusReviewStartSeconds":
            None if first_relation is None else round(first_relation, 9),
        "lastFrameEndMinusReviewEndSeconds":
            None if last_relation is None else round(last_relation, 9),
        "sourceBeginStreamSeconds": source_begin,
        "sourceEndStreamSeconds": source_end,
        "sourceTimestampOriginNonZero": (
            source_begin is not None and abs(source_begin) > 1e-9
        ),
        "variableFrameDurationsObserved": duration_variation > 1e-9,
        "averageFpsValidForPtsMapping":
            maximum_absolute_drift <= frame_tolerance + 1e-9,
    }


def _source_timeline_relation(
    timing: dict[str, float],
    source_begin: float | None,
    source_end: float | None,
    tolerance: float,
    source_average_fps: float,
) -> tuple[bool, bool, bool]:
    if source_begin is None or source_end is None:
        return False, True, False

    review_start_outside_source = (
        timing["requestedAbsoluteReviewStartSeconds"]
        < source_begin - tolerance - 1e-9
    )
    review_end_outside_source = (
        timing["requestedAbsoluteReviewEndSeconds"]
        > source_end + tolerance + 1e-9
    )
    review_outside_source = (
        review_start_outside_source
        or review_end_outside_source
    )
    candidate_inside_source = (
        timing["candidateAbsoluteStartSeconds"]
        >= source_begin - tolerance - 1e-9
        and timing["candidateAbsoluteEndSeconds"]
        <= source_end + tolerance + 1e-9
    )
    nominal_frame_period = (
        1.0 / source_average_fps
        if math.isfinite(source_average_fps)
        and source_average_fps > 0
        else tolerance
    )
    container_tail_tolerance = (
        max(tolerance, nominal_frame_period)
        + CONTAINER_TIMESTAMP_RESOLUTION_TOLERANCE_SECONDS
    )
    container_tail_within_tolerance = (
        not review_start_outside_source
        and review_end_outside_source
        and timing["requestedAbsoluteReviewEndSeconds"]
        <= source_end + container_tail_tolerance + 1e-9
    )
    return (
        review_outside_source,
        candidate_inside_source,
        container_tail_within_tolerance,
    )



__all__ = [name for name in globals() if not name.startswith("__")]
