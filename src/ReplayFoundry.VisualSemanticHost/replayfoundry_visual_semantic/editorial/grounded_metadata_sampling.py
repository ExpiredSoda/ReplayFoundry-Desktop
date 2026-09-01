"""Bounded adaptive video sampling for grounded editorial metadata."""
from __future__ import annotations

import math
from dataclasses import dataclass


SAMPLING_POLICY_VERSION = "grounded-editorial-adaptive-sampling-1.2"

# Moment candidates are shaped around their representative event. Keep a
# higher-detail chronological core while retaining sparse lead-in/recovery
# context for longer clips. These are pixel budgets, not forced dimensions;
# Qwen's official processor preserves the source aspect ratio.
CORE_REGION_MAXIMUM_DURATION_SECONDS = 24.0
FULL_REVIEW_CORE_MAXIMUM_DURATION_SECONDS = 32.0
CORE_WINDOW_MAXIMUM_DURATION_SECONDS = 16.0
CORE_WINDOW_OVERLAP_SECONDS = 2.0

CORE_FRAMES_PER_SECOND = 0.5
CORE_MINIMUM_FRAMES = 4
CORE_MAXIMUM_FRAMES = 6
CORE_MAXIMUM_PIXELS_PER_FRAME = 512 * 288
CORE_MAXIMUM_TOTAL_VIDEO_PIXELS = (
    CORE_MAXIMUM_FRAMES * CORE_MAXIMUM_PIXELS_PER_FRAME
)

CONTEXT_FRAMES_PER_SECOND = 0.2
CONTEXT_MINIMUM_FRAMES = 4
CONTEXT_MAXIMUM_FRAMES = 6
CONTEXT_MAXIMUM_PIXELS_PER_FRAME = 131_072
CONTEXT_MAXIMUM_TOTAL_VIDEO_PIXELS = (
    CONTEXT_MAXIMUM_FRAMES * CONTEXT_MAXIMUM_PIXELS_PER_FRAME
)

CORE_TIER = "CandidateCore"
CONTEXT_TIER = "SparseContext"


@dataclass(frozen=True)
class GroundedMetadataSamplingWindow:
    start_seconds: float
    end_seconds: float
    tier: str
    frames_per_second: float
    minimum_frames: int
    maximum_frames: int
    maximum_pixels_per_frame: int
    maximum_total_video_pixels: int

    @property
    def duration_seconds(self) -> float:
        return self.end_seconds - self.start_seconds

    def video_options(self) -> dict[str, float | int]:
        return {
            "max_pixels": self.maximum_pixels_per_frame,
            "total_pixels": self.maximum_total_video_pixels,
            "fps": self.frames_per_second,
            "min_frames": self.minimum_frames,
            "max_frames": self.maximum_frames,
            "video_start": self.start_seconds,
            "video_end": self.end_seconds,
        }


def _window(start: float, end: float, tier: str) -> GroundedMetadataSamplingWindow:
    if tier == CORE_TIER:
        return GroundedMetadataSamplingWindow(
            start,
            end,
            tier,
            CORE_FRAMES_PER_SECOND,
            CORE_MINIMUM_FRAMES,
            CORE_MAXIMUM_FRAMES,
            CORE_MAXIMUM_PIXELS_PER_FRAME,
            CORE_MAXIMUM_TOTAL_VIDEO_PIXELS,
        )
    if tier == CONTEXT_TIER:
        return GroundedMetadataSamplingWindow(
            start,
            end,
            tier,
            CONTEXT_FRAMES_PER_SECOND,
            CONTEXT_MINIMUM_FRAMES,
            CONTEXT_MAXIMUM_FRAMES,
            CONTEXT_MAXIMUM_PIXELS_PER_FRAME,
            CONTEXT_MAXIMUM_TOTAL_VIDEO_PIXELS,
        )
    raise ValueError(f"Unsupported grounded metadata sampling tier: {tier}")


def _split_core_region(
    start: float,
    end: float,
) -> tuple[GroundedMetadataSamplingWindow, ...]:
    duration = end - start
    if duration <= CORE_WINDOW_MAXIMUM_DURATION_SECONDS:
        return (_window(start, end, CORE_TIER),)

    stride_capacity = (
        CORE_WINDOW_MAXIMUM_DURATION_SECONDS - CORE_WINDOW_OVERLAP_SECONDS
    )
    window_count = math.ceil(
        (duration - CORE_WINDOW_OVERLAP_SECONDS) / stride_capacity
    )
    window_duration = (
        duration + (window_count - 1) * CORE_WINDOW_OVERLAP_SECONDS
    ) / window_count
    if window_duration > CORE_WINDOW_MAXIMUM_DURATION_SECONDS:
        raise AssertionError("Grounded core-window split exceeded its duration bound.")

    stride = window_duration - CORE_WINDOW_OVERLAP_SECONDS
    windows: list[GroundedMetadataSamplingWindow] = []
    for index in range(window_count):
        window_start = start + index * stride
        window_end = (
            end if index == window_count - 1 else window_start + window_duration
        )
        windows.append(_window(window_start, window_end, CORE_TIER))
    return tuple(windows)


def adaptive_sampling_plan(duration_seconds: float) -> tuple[GroundedMetadataSamplingWindow, ...]:
    """Return a deterministic, chronological, aspect-ratio-preserving plan."""

    duration = float(duration_seconds)
    if not math.isfinite(duration) or duration <= 0:
        raise ValueError("Grounded metadata review duration must be positive and finite.")

    if duration <= FULL_REVIEW_CORE_MAXIMUM_DURATION_SECONDS:
        return _split_core_region(0.0, duration)

    core_start = (duration - CORE_REGION_MAXIMUM_DURATION_SECONDS) / 2.0
    core_end = core_start + CORE_REGION_MAXIMUM_DURATION_SECONDS
    return (
        _window(0.0, core_start, CONTEXT_TIER),
        *_split_core_region(core_start, core_end),
        _window(core_end, duration, CONTEXT_TIER),
    )


__all__ = [name for name in globals() if not name.startswith("__")]
