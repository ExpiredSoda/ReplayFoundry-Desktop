from __future__ import annotations

import unittest

from replayfoundry_visual_semantic.constants import (
    VIDEO_FPS,
    VIDEO_MAX_FRAMES,
    VIDEO_MAX_PIXELS_PER_FRAME,
    VIDEO_TOTAL_PIXEL_BUDGET,
)
from replayfoundry_visual_semantic.commands import InferenceError
from replayfoundry_visual_semantic.editorial.grounded_metadata_sampling import (
    CONTEXT_TIER,
    CORE_MAXIMUM_FRAMES,
    CORE_MAXIMUM_PIXELS_PER_FRAME,
    CORE_TIER,
    CORE_WINDOW_MAXIMUM_DURATION_SECONDS,
    CORE_WINDOW_OVERLAP_SECONDS,
    SAMPLING_POLICY_VERSION,
    adaptive_sampling_plan,
)
from replayfoundry_visual_semantic.editorial.grounded_visual_drafts import (
    _visual_draft_messages,
)
from replayfoundry_visual_semantic.editorial.grounded_visual_event_selection import (
    _visual_event_selection_schema,
)
from replayfoundry_visual_semantic.video_sampling import (
    _effective_video_sampling_limits,
)


class GroundedMetadataAdaptiveSamplingTests(unittest.TestCase):
    def test_bounded_review_uses_one_full_aspect_preserving_core(self) -> None:
        plan = adaptive_sampling_plan(16.0)

        self.assertEqual(1, len(plan))
        core = plan[0]
        self.assertEqual((0.0, 16.0), (core.start_seconds, core.end_seconds))
        self.assertEqual(CORE_TIER, core.tier)
        self.assertEqual(0.5, core.frames_per_second)
        self.assertEqual(6, core.maximum_frames)
        self.assertEqual(512 * 288, core.maximum_pixels_per_frame)
        self.assertEqual(
            6 * 512 * 288,
            core.maximum_total_video_pixels,
        )

    def test_mid_length_review_uses_overlapped_peak_bounded_cores(self) -> None:
        plan = adaptive_sampling_plan(20.0)

        self.assertEqual(
            [(0.0, 11.0), (9.0, 20.0)],
            [(value.start_seconds, value.end_seconds) for value in plan],
        )
        self.assertTrue(all(value.tier == CORE_TIER for value in plan))
        self.assertEqual(
            CORE_WINDOW_OVERLAP_SECONDS,
            plan[0].end_seconds - plan[1].start_seconds,
        )

    def test_long_review_uses_sparse_context_around_bounded_core(self) -> None:
        plan = adaptive_sampling_plan(40.0)

        self.assertEqual(
            [(0.0, 8.0), (8.0, 21.0), (19.0, 32.0), (32.0, 40.0)],
            [(value.start_seconds, value.end_seconds) for value in plan],
        )
        self.assertEqual(
            [CONTEXT_TIER, CORE_TIER, CORE_TIER, CONTEXT_TIER],
            [value.tier for value in plan],
        )
        self.assertEqual(
            [0.2, 0.5, 0.5, 0.2],
            [value.frames_per_second for value in plan],
        )
        self.assertEqual([6, 6, 6, 6], [value.maximum_frames for value in plan])
        self.assertEqual(
            [131_072, 512 * 288, 512 * 288, 131_072],
            [value.maximum_pixels_per_frame for value in plan],
        )

    def test_failed_38_second_shape_halves_each_core_visual_token_budget(self) -> None:
        plan = adaptive_sampling_plan(38.0)

        self.assertEqual(
            [(0.0, 7.0), (7.0, 20.0), (18.0, 31.0), (31.0, 38.0)],
            [(value.start_seconds, value.end_seconds) for value in plan],
        )
        core_windows = [value for value in plan if value.tier == CORE_TIER]
        self.assertEqual([13.0, 13.0], [value.duration_seconds for value in core_windows])
        self.assertTrue(
            all(
                value.duration_seconds <= CORE_WINDOW_MAXIMUM_DURATION_SECONDS
                and value.maximum_frames == CORE_MAXIMUM_FRAMES
                for value in core_windows
            )
        )
        previous_spatial_patch_tokens = (352 // 16) * (640 // 16)
        current_spatial_patch_tokens = (288 // 16) * (512 // 16)
        previous_vision_tokens = (8 // 2) * previous_spatial_patch_tokens
        current_vision_tokens = (
            CORE_MAXIMUM_FRAMES // 2
        ) * current_spatial_patch_tokens
        self.assertLessEqual(current_vision_tokens, previous_vision_tokens // 2)
        self.assertEqual(432, current_vision_tokens // (2 * 2))
        schema, _ = _visual_event_selection_schema(len(plan))
        self.assertIn('"maxItems":4', schema)

    def test_every_adaptive_plan_fits_the_visual_event_selector(self) -> None:
        for duration in (1.0, 16.0, 20.0, 32.0, 38.0, 70.0, 180.0):
            with self.subTest(duration=duration):
                plan = adaptive_sampling_plan(duration)
                self.assertGreaterEqual(len(plan), 1)
                self.assertLessEqual(len(plan), 4)
                _visual_event_selection_schema(len(plan))

        with self.assertRaises(ValueError):
            _visual_event_selection_schema(5)

    def test_maximum_review_stays_chronological_and_bounded(self) -> None:
        plan = adaptive_sampling_plan(70.0)

        self.assertEqual(
            [(0.0, 23.0), (23.0, 36.0), (34.0, 47.0), (47.0, 70.0)],
            [(value.start_seconds, value.end_seconds) for value in plan],
        )
        for previous, current in zip(plan, plan[1:]):
            expected_overlap = (
                CORE_WINDOW_OVERLAP_SECONDS
                if previous.tier == CORE_TIER and current.tier == CORE_TIER
                else 0.0
            )
            self.assertEqual(
                expected_overlap,
                previous.end_seconds - current.start_seconds,
            )
        self.assertTrue(
            all(
                value.maximum_total_video_pixels <= VIDEO_TOTAL_PIXEL_BUDGET
                for value in plan
            )
        )

    def test_visual_message_uses_pixel_budget_without_forced_square_resize(self) -> None:
        request = {"_validated": {"videoPath": "C:/external/review.mp4"}}
        core = adaptive_sampling_plan(16.0)[0]

        messages = _visual_draft_messages(
            request,
            "frozen prompt",
            (0.0, 16.0),
            1,
            1,
            core,
        )
        video = messages[1]["content"][0]

        self.assertEqual(SAMPLING_POLICY_VERSION, "grounded-editorial-adaptive-sampling-1.2")
        self.assertEqual(512 * 288, video["max_pixels"])
        self.assertNotIn("resized_width", video)
        self.assertNotIn("resized_height", video)
        self.assertNotEqual(1_000 * 1_000, video["max_pixels"])

    def test_prompt_2_3_frozen_sampling_constants_do_not_change(self) -> None:
        self.assertEqual(0.5, VIDEO_FPS)
        self.assertEqual(32, VIDEO_MAX_FRAMES)
        self.assertEqual(131_072, VIDEO_MAX_PIXELS_PER_FRAME)
        self.assertEqual(4_194_304, VIDEO_TOTAL_PIXEL_BUDGET)
        frozen = _effective_video_sampling_limits({})
        self.assertEqual(131_072, frozen["maximumPixelsPerFrame"])

    def test_metadata_override_is_bounded_by_the_unchanged_host_ceiling(self) -> None:
        limits = _effective_video_sampling_limits(
            {
                "_visualSamplingLimits": {
                    "policyVersion": SAMPLING_POLICY_VERSION,
                    "minimumFrames": 4,
                    "maximumFrames": CORE_MAXIMUM_FRAMES,
                    "maximumPixelsPerFrame": CORE_MAXIMUM_PIXELS_PER_FRAME,
                    "maximumTotalVideoPixels": (
                        CORE_MAXIMUM_FRAMES * CORE_MAXIMUM_PIXELS_PER_FRAME
                    ),
                }
            }
        )
        self.assertEqual(512 * 288, limits["maximumPixelsPerFrame"])

        with self.assertRaises(InferenceError):
            _effective_video_sampling_limits(
                {
                    "_visualSamplingLimits": {
                        "policyVersion": SAMPLING_POLICY_VERSION,
                        "minimumFrames": 4,
                        "maximumFrames": 16,
                        "maximumPixelsPerFrame": 1_000 * 1_000,
                        "maximumTotalVideoPixels": 4_194_304,
                    }
                }
            )

    def test_invalid_duration_is_rejected(self) -> None:
        for value in (0.0, -1.0, float("nan"), float("inf")):
            with self.subTest(duration=value), self.assertRaises(ValueError):
                adaptive_sampling_plan(value)


if __name__ == "__main__":
    unittest.main()
