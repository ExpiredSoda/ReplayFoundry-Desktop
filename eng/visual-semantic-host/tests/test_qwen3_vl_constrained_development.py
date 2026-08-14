"""Model-free checks for constrained Prompt 2.3 Development execution."""
from __future__ import annotations

import sys
import unittest
from contextlib import ExitStack
from pathlib import Path
from unittest.mock import patch

HOST_ROOT = Path(__file__).resolve().parents[1]
if str(HOST_ROOT) not in sys.path:
    sys.path.insert(0, str(HOST_ROOT))

from replayfoundry_visual_semantic.editorial import attempts
from replayfoundry_visual_semantic.editorial import (
    constrained_development_command as command,
)


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


def _set(run_kind: str, count: int, failures: int = 0):
    outcomes = []
    for ordinal in range(1, count + 1):
        status = "Failed" if ordinal <= failures else "Succeeded"
        outcomes.append(
            {
                "caseId": f"case-{ordinal:02d}",
                "candidateId": f"candidate-{ordinal:02d}",
                "caseOrdinal": ordinal,
                "runKind": run_kind,
                "status": status,
                "observation": (
                    {"editorialDisposition": "Keep"}
                    if status == "Succeeded"
                    else None
                ),
                "canonicalizationAudit": (
                    {} if status == "Succeeded" else None
                ),
                "requestBinding": (
                    {} if status == "Succeeded" else None
                ),
                "generation": None,
                "executionTiming": None,
                "sampling": None,
                "elapsedSeconds": 1,
                "failure": (
                    None
                    if status == "Succeeded"
                    else {
                        "errorCode":
                            "StructuredDecodingSchemaCompilationError",
                        "stage": "Inference",
                        "message": "case schema failed",
                        "rawGeneratedTextSha256": None,
                    }
                ),
                "notRunReason": None,
                "structuredDecodingAudit": {
                    "policyVersion":
                        "visual-semantic-editorial-structured-decoding-1.0",
                    "backendName": "XGrammar",
                    "backendVersion": "0.2.2",
                    "schemaVersion":
                        "visual-semantic-editorial-constrained-schema-1.0",
                    "schemaSha256": "9" * 64,
                    "representation": "JsonSchema",
                    "cudaMaskBackend": "torch_native",
                    "compileElapsedSeconds": 0.01,
                    "generatedTokenCount": None,
                    "grammarTerminationState": None,
                    "strictParserAccepted":
                        status == "Succeeded",
                    "unconstrainedFallbackUsed": False,
                    "semanticRepairApplied": False,
                },
            }
        )
    result = {
        "schemaVersion":
            "visual-semantic-editorial-constrained-attempt-set-1.0",
        "runKind": run_kind,
        "requestedCount": count,
        "succeededCount": count - failures,
        "failedCount": failures,
        "notRunCount": 0,
        "outcomes": outcomes,
    }
    result["canonicalHash"] = command._canonical_json_sha256(result)
    return result


def _plan():
    return {
        "configurationLockCanonicalHash": "a" * 64,
        "_validated": {
            "promptText": "prompt",
            "sets": {
                "Primary": [_request(i) for i in range(1, 31)],
                "Repeat": [_request(i) for i in range(1, 7)],
                "VisualOnly": [_request(i) for i in range(1, 13)],
            },
        },
    }


class ConstrainedDevelopmentTests(unittest.TestCase):
    def setUp(self) -> None:
        attempts._reset_failure_context(
            "run-editorial-constrained-development"
        )

    def _patches(self, attempt_side_effect):
        plan = _plan()
        return (
            patch.object(command, "_load_strict_json", return_value={}),
            patch.object(
                command,
                "validate_editorial_plan",
                return_value=plan,
            ),
            patch.object(
                command,
                "validate_qualification_lock",
                return_value={"canonicalHash": "b" * 64},
            ),
            patch.object(command, "require_frozen_packages"),
            patch.object(
                command,
                "_validate_failure_output_against_media",
            ),
            patch.object(
                command,
                "_load_runtime",
                return_value=(_Torch(), object(), object(), object()),
            ),
            patch.object(command, "_validate_model_directory"),
            patch.object(
                command,
                "_authorize_sampling",
                return_value={
                    "sourceArtifactSha256": "c" * 64,
                    "parityCaseCount": 30,
                    "parityCanonicalHash": "d" * 64,
                },
            ),
            patch.object(
                command,
                "_load_model_and_processor",
                return_value=(
                    object(),
                    type(
                        "Processor",
                        (),
                        {"tokenizer": object()},
                    )(),
                ),
            ),
            patch.object(command, "StructuredDecodingSession"),
            patch.object(command, "model_vocab_size", return_value=10),
            patch.object(
                command,
                "attempt_editorial_set",
                side_effect=attempt_side_effect,
            ),
            patch.object(command, "_revalidate_media_inputs"),
        )

    def test_one_load_runs_primary_repeat_and_visual_only(self):
        calls = []
        writes = []

        def attempt(run_kind, requests, *_args):
            calls.append(run_kind)
            return _set(run_kind, len(requests))

        patches = self._patches(attempt)
        with ExitStack() as stack:
            for item in patches:
                stack.enter_context(item)
            stack.enter_context(
                patch.object(
                command,
                "_write_json_atomic",
                side_effect=lambda path, value:
                    writes.append((str(path), value)),
                )
            )
            command.run_editorial_constrained_development(
                Path("model"),
                Path("plan"),
                Path("completed"),
                Path("attempt"),
                Path("lock"),
                Path("ffmpeg"),
                None,
            )

        self.assertEqual(["Primary", "Repeat", "VisualOnly"], calls)
        self.assertEqual(["attempt", "completed"], [p for p, _ in writes])
        attempt_payload = writes[0][1]
        self.assertEqual(
            command.CONSTRAINED_DEVELOPMENT_ATTEMPT_SCHEMA,
            attempt_payload["schemaVersion"],
        )
        self.assertEqual("b" * 64, attempt_payload[
            "qualificationLockCanonicalHash"
        ])

    def test_incomplete_primary_retains_all_and_skips_secondary(self):
        calls = []
        writes = []

        def attempt(run_kind, requests, *_args):
            calls.append(run_kind)
            return _set(run_kind, len(requests), failures=2)

        patches = self._patches(attempt)
        with ExitStack() as stack:
            for item in patches:
                stack.enter_context(item)
            stack.enter_context(
                patch.object(
                    command,
                    "not_run_editorial_set",
                    side_effect=attempts.not_run_editorial_set,
                )
            )
            stack.enter_context(
                patch.object(
                    command,
                    "_write_json_atomic",
                    side_effect=lambda path, value:
                        writes.append((str(path), value)),
                )
            )
            with self.assertRaises(
                command.ProviderCaseFailuresDetected
            ):
                command.run_editorial_constrained_development(
                    Path("model"),
                    Path("plan"),
                    Path("completed"),
                    Path("attempt"),
                    Path("lock"),
                    Path("ffmpeg"),
                    None,
                )

        self.assertEqual(["Primary"], calls)
        self.assertEqual(["attempt"], [p for p, _ in writes])
        self.assertEqual(6, writes[0][1]["repeat"]["notRunCount"])
        self.assertEqual(12, writes[0][1]["visualOnly"]["notRunCount"])
        self.assertEqual(
            "visual-semantic-editorial-constrained-attempt-set-1.0",
            writes[0][1]["repeat"]["schemaVersion"],
        )
        self.assertEqual(
            "visual-semantic-editorial-constrained-attempt-set-1.0",
            writes[0][1]["visualOnly"]["schemaVersion"],
        )


if __name__ == "__main__":
    unittest.main()
