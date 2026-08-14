"""Model-free tests for the label-blind Prompt 2.0 contract pilot."""
from __future__ import annotations

import sys
import unittest
from pathlib import Path
from unittest.mock import patch

HOST_ROOT = Path(__file__).resolve().parents[1]
if str(HOST_ROOT) not in sys.path:
    sys.path.insert(0, str(HOST_ROOT))

from replayfoundry_visual_semantic import failure_state
from replayfoundry_visual_semantic.editorial import pilot_command
from replayfoundry_visual_semantic.editorial import pilot_protocol


class _Cuda:
    @staticmethod
    def is_available() -> bool:
        return False


class _Torch:
    cuda = _Cuda()


def _request(case_id: str, candidate_id: str) -> dict[str, object]:
    return {
        "caseId": case_id,
        "candidate": {"id": candidate_id},
        "_validated": {},
    }


def _attempt_set(
    requests: list[dict[str, object]],
    failed: int = 0,
) -> dict[str, object]:
    return {
        "schemaVersion": "visual-semantic-editorial-attempt-set-1.0",
        "runKind": "Primary",
        "requestedCount": len(requests),
        "succeededCount": len(requests) - failed,
        "failedCount": failed,
        "notRunCount": 0,
        "outcomes": [],
        "canonicalHash": "a" * 64,
    }


def _plan(requests: list[dict[str, object]]) -> dict[str, object]:
    return {
        "phase": "Pilot" if len(requests) == 3 else "Canary",
        "configurationLockCanonicalHash": "a" * 64,
        "_validated": {
            "promptText": "prompt",
            "requests": requests,
            "samplingBaseline": {},
        },
    }


class EditorialContractPilotTests(unittest.TestCase):
    def setUp(self) -> None:
        failure_state._reset_failure_context(
            "run-editorial-contract-pilot"
        )

    def test_policy_freezes_exact_case_identities_and_limits(self) -> None:
        self.assertEqual(
            ("review-c4bfbdec6bc32d3f",),
            pilot_protocol.PILOT_CANARY,
        )
        self.assertEqual(
            (
                "review-c4bfbdec6bc32d3f",
                "review-a80601ff85e908a1",
                "review-5530b3d1d93d03af",
            ),
            pilot_protocol.PILOT_THREE,
        )

    def test_success_loads_once_writes_attempt_then_completed(self) -> None:
        requests = [
            _request(case_id, f"candidate-{index}")
            for index, case_id in enumerate(
                pilot_protocol.PILOT_THREE,
                start=1,
            )
        ]
        model_loads = 0
        attempted: list[list[str]] = []
        writes: list[str] = []

        def load_model(*_args):
            nonlocal model_loads
            model_loads += 1
            return object(), object()

        def attempt_set(_kind, values, *_args):
            attempted.append([item["caseId"] for item in values])
            return _attempt_set(values)

        with (
            patch.object(pilot_command, "_load_strict_json", return_value={}),
            patch.object(
                pilot_command,
                "validate_pilot_plan",
                return_value=_plan(requests),
            ),
            patch.object(
                pilot_command,
                "_validate_failure_output_against_media",
            ),
            patch.object(
                pilot_command,
                "_load_runtime",
                return_value=(_Torch(), object(), object(), object()),
            ),
            patch.object(pilot_command, "_validate_model_directory"),
            patch.object(
                pilot_command,
                "authorize_sampling",
                return_value={},
            ),
            patch.object(
                pilot_command,
                "_load_model_and_processor",
                side_effect=load_model,
            ),
            patch.object(
                pilot_command,
                "attempt_editorial_set",
                side_effect=attempt_set,
            ),
            patch.object(pilot_command, "_revalidate_media_inputs"),
            patch.object(
                pilot_command,
                "_write_json_atomic",
                side_effect=lambda path, _value: writes.append(str(path)),
            ),
        ):
            pilot_command.run_editorial_contract_pilot(
                Path("model"),
                Path("plan.json"),
                Path("completed.json"),
                Path("attempt.json"),
                Path("ffmpeg"),
                None,
            )

        self.assertEqual(1, model_loads)
        self.assertEqual([list(pilot_protocol.PILOT_THREE)], attempted)
        self.assertEqual(["attempt.json", "completed.json"], writes)

    def test_case_failures_write_attempt_and_use_post_write_stage(self) -> None:
        requests = [
            _request(case_id, f"candidate-{index}")
            for index, case_id in enumerate(
                pilot_protocol.PILOT_THREE,
                start=1,
            )
        ]
        writes: list[str] = []

        with (
            patch.object(pilot_command, "_load_strict_json", return_value={}),
            patch.object(
                pilot_command,
                "validate_pilot_plan",
                return_value=_plan(requests),
            ),
            patch.object(
                pilot_command,
                "_validate_failure_output_against_media",
            ),
            patch.object(
                pilot_command,
                "_load_runtime",
                return_value=(_Torch(), object(), object(), object()),
            ),
            patch.object(pilot_command, "_validate_model_directory"),
            patch.object(
                pilot_command,
                "authorize_sampling",
                return_value={},
            ),
            patch.object(
                pilot_command,
                "_load_model_and_processor",
                return_value=(object(), object()),
            ),
            patch.object(
                pilot_command,
                "attempt_editorial_set",
                return_value=_attempt_set(requests, failed=2),
            ),
            patch.object(pilot_command, "_revalidate_media_inputs"),
            patch.object(
                pilot_command,
                "_write_json_atomic",
                side_effect=lambda path, _value: writes.append(str(path)),
            ),
        ):
            with self.assertRaises(
                pilot_command.ProviderCaseFailuresDetected
            ):
                pilot_command.run_editorial_contract_pilot(
                    Path("model"),
                    Path("plan.json"),
                    Path("completed.json"),
                    Path("attempt.json"),
                    Path("ffmpeg"),
                    None,
                )

        self.assertEqual(["attempt.json"], writes)
        self.assertEqual(
            "AttemptCompletedWithCaseFailures",
            failure_state._FAILURE_CONTEXT["stage"],
        )

    def test_pilot_plan_forbids_labels_metrics_and_holdout(self) -> None:
        for key in ("labels", "score", "expectedOutcome", "holdoutData"):
            with self.assertRaises(pilot_protocol.UsageOrInputError):
                pilot_protocol._scan_forbidden_pilot_fields(
                    {key: True}
                )


if __name__ == "__main__":
    unittest.main()
