"""Model-free exhaustive Prompt 2.0 attempt tests."""
from __future__ import annotations

import sys
import unittest
from pathlib import Path
from unittest.mock import patch

HOST_ROOT = Path(__file__).resolve().parents[1]
if str(HOST_ROOT) not in sys.path:
    sys.path.insert(0, str(HOST_ROOT))

from replayfoundry_visual_semantic.editorial import attempts
from replayfoundry_visual_semantic.editorial import development_command
from replayfoundry_visual_semantic.editorial import protocol
from replayfoundry_visual_semantic import model_runtime
from replayfoundry_visual_semantic import video_sampling


class _Cuda:
    @staticmethod
    def empty_cache() -> None:
        pass

    @staticmethod
    def is_available() -> bool:
        return False


class _Torch:
    cuda = _Cuda()


def _request(index: int) -> dict[str, object]:
    return {
        "caseId": f"case-{index:02d}",
        "caseHash": f"{index:064x}",
        "candidate": {"id": f"candidate-{index:02d}"},
        "_validated": {
            "videoDuration": 10,
            "candidateStart": 2,
            "candidateEnd": 5,
            "sourceAbsoluteOffset": 0,
            "expectedVideoHash": "a" * 64,
            "expectedVideoLength": 1,
            "expectedLastWriteUtc": None,
        },
    }


def _success(index: int) -> dict[str, object]:
    return {
        "observation": {"editorialDisposition": "Keep"},
        "canonicalizationAudit": {
            "policyVersion":
                "visual-semantic-editorial-canonicalization-1.2",
            "syntacticCanonicalizationCount": 0,
            "schemaShapeCanonicalizationCount": 0,
            "semanticRepairCount": 0,
            "kinds": [],
        },
        "requestBinding": {"caseOrdinal": index},
        "generation": {
            "generatedTokenCount": 10,
            "terminationReason": "EndOfSequence",
        },
        "executionTiming": {},
        "sampling": {},
        "elapsedSeconds": 1.0,
    }


def _attempt_set(
    run_kind: str,
    count: int,
    failed_ordinals: set[int] | None = None,
) -> dict[str, object]:
    failed_ordinals = failed_ordinals or set()
    outcomes: list[dict[str, object]] = []
    for ordinal in range(1, count + 1):
        if ordinal in failed_ordinals:
            outcomes.append(
                {
                    "caseId": f"case-{ordinal:02d}",
                    "candidateId": f"candidate-{ordinal:02d}",
                    "caseOrdinal": ordinal,
                    "runKind": run_kind,
                    "status": "Failed",
                }
            )
            continue
        row = {
            "caseId": f"case-{ordinal:02d}",
            "candidateId": f"candidate-{ordinal:02d}",
            "caseOrdinal": ordinal,
            "runKind": run_kind,
            "status": "Succeeded",
        }
        row.update(_success(ordinal))
        outcomes.append(row)
    return {
        "schemaVersion": "visual-semantic-editorial-attempt-set-1.0",
        "runKind": run_kind,
        "requestedCount": count,
        "succeededCount": count - len(failed_ordinals),
        "failedCount": len(failed_ordinals),
        "notRunCount": 0,
        "outcomes": outcomes,
        "canonicalHash": "a" * 64,
    }


class EditorialDevelopmentAttemptTests(unittest.TestCase):
    def setUp(self) -> None:
        attempts._reset_failure_context("run-editorial-development")

    def test_primary_attempt_continues_after_multiple_case_failures(self):
        requests = [_request(index) for index in range(1, 31)]
        seen: list[int] = []

        def infer(request, ordinal, *_args):
            seen.append(ordinal)
            if ordinal in {2, 7, 29}:
                attempts._set_failure_stage("OutputValidation")
                raise attempts.InferenceError("invalid semantic output")
            return _success(ordinal)

        with patch.object(attempts, "infer_editorial_case", infer):
            result = attempts.attempt_editorial_set(
                "Primary",
                requests,
                "prompt",
                object(),
                object(),
                _Torch(),
                object(),
                object(),
            )

        self.assertEqual(list(range(1, 31)), seen)
        self.assertEqual(30, result["requestedCount"])
        self.assertEqual(27, result["succeededCount"])
        self.assertEqual(3, result["failedCount"])
        self.assertEqual(
            [2, 7, 29],
            [
                row["caseOrdinal"]
                for row in result["outcomes"]
                if row["status"] == "Failed"
            ],
        )

    def test_not_run_sets_preserve_every_frozen_identity(self):
        requests = [_request(index) for index in range(1, 13)]
        result = attempts.not_run_editorial_set(
            "VisualOnly",
            requests,
            "NotRunPrimaryIncomplete",
        )
        self.assertEqual(12, result["notRunCount"])
        self.assertTrue(
            all(
                row["notRunReason"] == "NotRunPrimaryIncomplete"
                for row in result["outcomes"]
            )
        )

    def test_case_failure_retains_generation_hash_when_available(self):
        request = _request(1)

        def infer(*_args):
            attempts._set_failure_stage("OutputValidation")
            attempts._set_failure_provider_output(
                rawGeneratedTextSha256="b" * 64
            )
            raise attempts.InferenceError("malformed JSON")

        with patch.object(attempts, "infer_editorial_case", infer):
            result = attempts.attempt_editorial_set(
                "Primary",
                [request],
                "prompt",
                object(),
                object(),
                _Torch(),
                object(),
                object(),
            )
        failure = result["outcomes"][0]["failure"]
        self.assertEqual("b" * 64, failure["rawGeneratedTextSha256"])
        self.assertEqual("OutputValidation", failure["stage"])

    def test_attempt_hash_is_deterministic(self):
        requests = [_request(1), _request(2)]
        first = attempts.not_run_editorial_set(
            "Repeat",
            requests,
            "NotRunPrimaryIncomplete",
        )
        second = attempts.not_run_editorial_set(
            "Repeat",
            requests,
            "NotRunPrimaryIncomplete",
        )
        self.assertEqual(first["canonicalHash"], second["canonicalHash"])

    def test_frozen_canonicalization_policy_hash_matches_file(self):
        policy = HOST_ROOT / (
            "replayfoundry-visual-semantic-editorial-"
            "canonicalization-policy-1.3.txt"
        )
        import hashlib

        self.assertEqual(
            hashlib.sha256(policy.read_bytes()).hexdigest(),
            protocol.CANONICALIZATION_SHA256,
        )

    def test_cuda_model_validation_is_owned_by_model_runtime(self):
        self.assertTrue(
            callable(model_runtime._assert_cuda_only_model)
        )
        self.assertIs(
            video_sampling._assert_cuda_only_model,
            model_runtime._assert_cuda_only_model,
        )

    def test_development_command_loads_once_and_runs_every_complete_set(self):
        plan = {
            "configurationLockCanonicalHash": "a" * 64,
            "_validated": {
                "promptText": "prompt",
                "sets": {
                    "Primary": [_request(index) for index in range(1, 31)],
                    "Repeat": [_request(index) for index in range(1, 7)],
                    "VisualOnly": [_request(index) for index in range(1, 13)],
                },
            },
        }
        calls: list[str] = []
        writes: list[str] = []
        model_load_count = 0

        def load_model(*_args):
            nonlocal model_load_count
            model_load_count += 1
            return object(), object()

        def attempt_set(run_kind, requests, *_args):
            calls.append(run_kind)
            return _attempt_set(run_kind, len(requests))

        with (
            patch.object(
                development_command,
                "_load_strict_json",
                return_value={},
            ),
            patch.object(
                development_command,
                "validate_editorial_plan",
                return_value=plan,
            ),
            patch.object(
                development_command,
                "_validate_failure_output_against_media",
            ),
            patch.object(
                development_command,
                "_load_runtime",
                return_value=(
                    _Torch(),
                    object(),
                    object(),
                    object(),
                ),
            ),
            patch.object(
                development_command,
                "_validate_model_directory",
            ),
            patch.object(
                development_command,
                "_authorize_sampling",
                return_value={
                    "sourceArtifactSha256": "b" * 64,
                    "parityCaseCount": 30,
                    "parityCanonicalHash": "c" * 64,
                },
            ),
            patch.object(
                development_command,
                "_load_model_and_processor",
                side_effect=load_model,
            ),
            patch.object(
                development_command,
                "attempt_editorial_set",
                side_effect=attempt_set,
            ),
            patch.object(
                development_command,
                "_revalidate_media_inputs",
            ),
            patch.object(
                development_command,
                "_write_json_atomic",
                side_effect=lambda path, _value: writes.append(str(path)),
            ),
        ):
            development_command.run_editorial_development(
                Path("model"),
                Path("plan.json"),
                Path("completed.json"),
                Path("attempt.json"),
                Path("ffmpeg"),
                None,
            )

        self.assertEqual(1, model_load_count)
        self.assertEqual(["Primary", "Repeat", "VisualOnly"], calls)
        self.assertEqual(["attempt.json", "completed.json"], writes)

    def test_incomplete_primary_does_not_start_secondary_sets(self):
        plan = {
            "configurationLockCanonicalHash": "a" * 64,
            "_validated": {
                "promptText": "prompt",
                "sets": {
                    "Primary": [_request(index) for index in range(1, 31)],
                    "Repeat": [_request(index) for index in range(1, 7)],
                    "VisualOnly": [_request(index) for index in range(1, 13)],
                },
            },
        }
        attempted: list[str] = []
        not_run: list[str] = []

        def attempt_set(run_kind, requests, *_args):
            attempted.append(run_kind)
            return _attempt_set(run_kind, len(requests), {2, 17})

        def mark_not_run(run_kind, requests, reason):
            not_run.append(run_kind)
            return attempts.not_run_editorial_set(
                run_kind,
                requests,
                reason,
            )

        with (
            patch.object(
                development_command,
                "_load_strict_json",
                return_value={},
            ),
            patch.object(
                development_command,
                "validate_editorial_plan",
                return_value=plan,
            ),
            patch.object(
                development_command,
                "_validate_failure_output_against_media",
            ),
            patch.object(
                development_command,
                "_load_runtime",
                return_value=(
                    _Torch(),
                    object(),
                    object(),
                    object(),
                ),
            ),
            patch.object(
                development_command,
                "_validate_model_directory",
            ),
            patch.object(
                development_command,
                "_authorize_sampling",
                return_value={
                    "sourceArtifactSha256": "b" * 64,
                    "parityCaseCount": 30,
                    "parityCanonicalHash": "c" * 64,
                },
            ),
            patch.object(
                development_command,
                "_load_model_and_processor",
                return_value=(object(), object()),
            ),
            patch.object(
                development_command,
                "attempt_editorial_set",
                side_effect=attempt_set,
            ),
            patch.object(
                development_command,
                "not_run_editorial_set",
                side_effect=mark_not_run,
            ),
            patch.object(
                development_command,
                "_revalidate_media_inputs",
            ),
            patch.object(
                development_command,
                "_write_json_atomic",
            ),
        ):
            with self.assertRaises(
                development_command.ProviderCaseFailuresDetected
            ):
                development_command.run_editorial_development(
                    Path("model"),
                    Path("plan.json"),
                    Path("completed.json"),
                    Path("attempt.json"),
                    Path("ffmpeg"),
                    None,
                )

        self.assertEqual(["Primary"], attempted)
        self.assertEqual(["Repeat", "VisualOnly"], not_run)


if __name__ == "__main__":
    unittest.main()
