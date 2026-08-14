#!/usr/bin/env python3
"""Model-free checks for the grounded Qwen wall-clock watchdog."""
from __future__ import annotations

from types import SimpleNamespace
import unittest
from unittest import mock

from replayfoundry_visual_semantic import failure_envelope, generation
from replayfoundry_visual_semantic import generation_watchdog as watchdog


class _FakeRow:
    def __init__(self, values: list[int]) -> None:
        self.values = list(values)

    def detach(self):
        return self

    def cpu(self):
        return self

    def tolist(self):
        return list(self.values)


class _FakeTensor:
    def __init__(self, values: list[int]) -> None:
        self.row = _FakeRow(values)

    def __len__(self):
        return 1

    def __getitem__(self, index: int):
        if index != 0:
            raise IndexError(index)
        return self.row


class _FakeInputs(dict):
    def __init__(self, values: list[int]) -> None:
        tensor = _FakeTensor(values)
        super().__init__(input_ids=tensor)
        self.input_ids = tensor


class _FakeModel:
    def __init__(
        self,
        generated: list[int],
        error: Exception | None = None,
    ) -> None:
        self.generated = generated
        self.error = error
        self.calls: list[dict] = []
        self.generation_config = SimpleNamespace(
            eos_token_id=99,
            forced_eos_token_id=None,
            stop_strings=None,
        )

    def generate(self, **kwargs):
        self.calls.append(dict(kwargs))
        if self.error is not None:
            raise self.error
        prefix = kwargs["input_ids"][0].tolist()
        return _FakeTensor(prefix + self.generated)


class GenerationWatchdogTests(unittest.TestCase):
    def setUp(self) -> None:
        generation._reset_failure_context("run-grounded-editorial-metadata-batch")

    def test_policy_source_and_frozen_bounds_are_exact(self) -> None:
        _, actual_hash = watchdog._normalized_policy_source()
        self.assertEqual(watchdog.POLICY_SHA256, actual_hash)
        self.assertEqual(240.0, watchdog.MAXIMUM_GENERATION_WALL_CLOCK_SECONDS)
        self.assertEqual(
            900.0,
            watchdog.MAXIMUM_GROUNDED_CASE_WALL_CLOCK_SECONDS,
        )
        self.assertEqual("FailClosed", watchdog.TIMEOUT_BEHAVIOR)

    def test_legacy_generation_is_unchanged_outside_explicit_context(self) -> None:
        model = _FakeModel([12, 99])
        trace = generation._generate_with_trace(
            model,
            _FakeInputs([1]),
            generation.ACTIVE_POLICY_MAX_NEW_TOKENS,
        )
        self.assertNotIn("max_time", model.calls[0])
        self.assertIsNone(trace.generation_wall_clock_seconds)
        self.assertIsNone(trace.maximum_generation_wall_clock_seconds)
        self.assertFalse(trace.generation_watchdog_triggered)

    def test_generation_limit_is_forwarded_and_fails_closed(self) -> None:
        model = _FakeModel([12])
        with (
            mock.patch.object(
                watchdog,
                "_watchdog_clock",
                side_effect=[0.0, 1.0, 241.0],
            ),
            watchdog.grounded_case_watchdog("case-1", "candidate-1", 1),
        ):
            trace = generation._generate_with_trace(
                model,
                _FakeInputs([1]),
                generation.ACTIVE_POLICY_MAX_NEW_TOKENS,
            )
        self.assertEqual(240.0, model.calls[0]["max_time"])
        self.assertTrue(trace.generation_watchdog_triggered)
        self.assertEqual(
            watchdog.GENERATION_TIMEOUT_REASON,
            trace.generation_watchdog_timeout_reason,
        )
        with self.assertRaises(
            generation.GenerationWallClockBudgetExceededError,
        ):
            generation._require_completed_generation(trace)

    def test_case_time_remaining_clamps_generation_limit(self) -> None:
        model = _FakeModel([12])
        with (
            mock.patch.object(
                watchdog,
                "_watchdog_clock",
                side_effect=[0.0, 800.0, 901.0],
            ),
            watchdog.grounded_case_watchdog("case-1", "candidate-1", 1),
        ):
            trace = generation._generate_with_trace(
                model,
                _FakeInputs([1]),
                generation.ACTIVE_POLICY_MAX_NEW_TOKENS,
            )
        self.assertEqual(100.0, model.calls[0]["max_time"])
        self.assertTrue(trace.generation_watchdog_triggered)
        self.assertEqual(
            watchdog.CASE_TIMEOUT_REASON,
            trace.generation_watchdog_timeout_reason,
        )

    def test_late_eos_and_late_token_ceiling_are_never_accepted(self) -> None:
        cases = (
            ([99], generation.ACTIVE_POLICY_MAX_NEW_TOKENS, "EndOfSequence"),
            (
                [12] * generation.LEGACY_DIAGNOSTIC_MAX_NEW_TOKENS,
                generation.LEGACY_DIAGNOSTIC_MAX_NEW_TOKENS,
                "MaximumNewTokensReached",
            ),
        )
        for generated, maximum, expected_reason in cases:
            with self.subTest(expected_reason=expected_reason):
                model = _FakeModel(generated)
                with (
                    mock.patch.object(
                        watchdog,
                        "_watchdog_clock",
                        side_effect=[0.0, 1.0, 241.0],
                    ),
                    watchdog.grounded_case_watchdog(
                        "case-1", "candidate-1", 1
                    ),
                ):
                    trace = generation._generate_with_trace(
                        model,
                        _FakeInputs([1]),
                        maximum,
                    )
                self.assertEqual(expected_reason, trace.termination_reason)
                self.assertTrue(trace.generation_watchdog_triggered)
                with self.assertRaises(
                    generation.GenerationWallClockBudgetExceededError,
                ):
                    generation._require_completed_generation(trace)

    def test_expired_case_stops_before_model_generate(self) -> None:
        model = _FakeModel([99])
        with (
            mock.patch.object(
                watchdog,
                "_watchdog_clock",
                side_effect=[0.0, 901.0],
            ),
            watchdog.grounded_case_watchdog("case-1", "candidate-1", 1),
            self.assertRaises(
                generation.GenerationWallClockBudgetExceededError,
            ),
        ):
            generation._generate_with_trace(
                model,
                _FakeInputs([1]),
                generation.ACTIVE_POLICY_MAX_NEW_TOKENS,
            )
        self.assertEqual([], model.calls)
        telemetry = generation._FAILURE_CONTEXT["generationWatchdog"]
        self.assertTrue(telemetry["triggered"])
        self.assertEqual(watchdog.CASE_TIMEOUT_REASON, telemetry["timeoutReason"])
        self.assertEqual(0.0, telemetry["effectiveMaximumGenerationWallClockSeconds"])

    def test_model_exception_retains_non_timeout_watchdog_telemetry(self) -> None:
        model = _FakeModel([], RuntimeError("synthetic CUDA failure"))
        with (
            mock.patch.object(
                watchdog,
                "_watchdog_clock",
                side_effect=[0.0, 1.0, 5.0],
            ),
            watchdog.grounded_case_watchdog("case-1", "candidate-1", 1),
            self.assertRaisesRegex(RuntimeError, "synthetic CUDA failure"),
        ):
            generation._generate_with_trace(
                model,
                _FakeInputs([1]),
                generation.ACTIVE_POLICY_MAX_NEW_TOKENS,
            )
        telemetry = generation._FAILURE_CONTEXT["generationWatchdog"]
        self.assertFalse(telemetry["triggered"])
        self.assertIsNone(telemetry["timeoutReason"])
        self.assertEqual(4.0, telemetry["elapsedGenerationWallClockSeconds"])
        self.assertEqual(5.0, telemetry["elapsedCaseWallClockSeconds"])

    def test_success_provenance_is_complete_and_non_triggered(self) -> None:
        model = _FakeModel([12, 99])
        with (
            mock.patch.object(
                watchdog,
                "_watchdog_clock",
                side_effect=[0.0, 1.0, 2.0, 3.0],
            ),
            watchdog.grounded_case_watchdog(
                "case-1", "candidate-1", 1
            ) as state,
        ):
            trace = generation._generate_with_trace(
                model,
                _FakeInputs([1]),
                generation.ACTIVE_POLICY_MAX_NEW_TOKENS,
            )
            generation._require_completed_generation(trace)
            payload = watchdog.grounded_case_watchdog_success_payload(state)
        self.assertEqual(1, payload["generationInvocationCount"])
        self.assertEqual(3.0, payload["elapsedCaseWallClockSeconds"])
        self.assertFalse(payload["triggered"])
        self.assertIsNone(payload["timeoutReason"])

    def test_failure_schema_carries_typed_watchdog_provenance(self) -> None:
        generation._FAILURE_CONTEXT["case"] = {
            "caseId": "case-1",
            "candidateId": "candidate-1",
            "caseOrdinal": 1,
        }
        with (
            mock.patch.object(
                watchdog,
                "_watchdog_clock",
                side_effect=[0.0, 901.0],
            ),
            watchdog.grounded_case_watchdog("case-1", "candidate-1", 1),
            self.assertRaises(
                generation.GenerationWallClockBudgetExceededError,
            ),
        ):
            watchdog.prepare_generation_watchdog()
        payload = failure_envelope._failure_payload(
            "run-grounded-editorial-metadata-batch",
            "GenerationWallClockBudgetExceededError",
            10,
            "case timed out",
        )
        self.assertEqual(
            "visual-semantic-host-failure-1.4",
            payload["schemaVersion"],
        )
        self.assertTrue(payload["generationWatchdog"]["triggered"])
        self.assertNotIn(
            "text",
            str(payload["generationWatchdog"]).lower(),
        )

    def test_clear_case_removes_stale_watchdog_before_output_write(self) -> None:
        generation._FAILURE_CONTEXT["generationWatchdog"] = {
            "caseId": "case-1"
        }
        generation._clear_failure_case()
        self.assertIsNone(
            generation._FAILURE_CONTEXT["generationWatchdog"]
        )
        payload = failure_envelope._failure_payload(
            "run-grounded-editorial-metadata-batch",
            "OutputError",
            5,
            "output write failed",
        )
        self.assertIsNone(payload["case"])
        self.assertIsNone(payload["generationWatchdog"])

    def test_timeout_is_not_a_retryable_semantic_inference_error(self) -> None:
        self.assertFalse(
            issubclass(
                generation.GenerationWallClockBudgetExceededError,
                generation.InferenceError,
            )
        )


if __name__ == "__main__":
    unittest.main()
