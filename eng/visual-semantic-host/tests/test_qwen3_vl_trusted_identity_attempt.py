#!/usr/bin/env python3
"""Model-free tests for trusted identity and exhaustive attempt batches."""

from __future__ import annotations

import copy
import json
import sys
import unittest
from contextlib import ExitStack
from decimal import Decimal
from pathlib import Path
from types import SimpleNamespace
from unittest import mock


HOST_DIRECTORY = Path(__file__).resolve().parent.parent
if str(HOST_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(HOST_DIRECTORY))

from replayfoundry_visual_semantic import canonical_json
from replayfoundry_visual_semantic import commands as host
from replayfoundry_visual_semantic import trusted_identity


def _request(
    case_id: str = "case-1",
    candidate_id: str = "candidate-1",
) -> dict:
    return {
        "caseId": case_id,
        "candidate": {"id": candidate_id},
        "_validated": {
            "videoDuration": Decimal("10"),
            "videoPath": Path("A:/outside/review.mkv"),
            "sourceAbsoluteOffset": Decimal("100"),
            "candidateStart": Decimal("1"),
            "candidateEnd": Decimal("2"),
            "expectedVideoHash": "f" * 64,
            "expectedVideoLength": 1_000,
        },
    }


def _provider_observation(
    case_id: str = "case-1",
    candidate_id: str = "candidate-1",
) -> dict:
    return {
        "caseId": case_id,
        "candidateId": candidate_id,
        "schemaVersion": host.OBSERVATION_SCHEMA,
        "observableContentType": "Action",
        "visibleStateChange": "A visible state changes.",
        "hasClearBeginning": "Yes",
        "hasClearOutcome": "Unsure",
        "menuOrTraversalPresent": "No",
        "spokenContentAppearsRelevant": "Unknown",
        "suggestedWorthReviewing": "Yes",
        "reviewCertainty": "Medium",
        "evidenceIntervals": [
            {
                "startSeconds": 1,
                "endSeconds": 2,
                "description": "The visible state changes.",
            }
        ],
        "uncertainties": [],
        "limitations": ["Only sampled frames were observed."],
        "conciseRationale": "The bounded video contains a visible change.",
    }


def _parse(
    observation: dict,
    request: dict | None = None,
    ordinal: int = 1,
) -> dict:
    return host._parse_provider_observation(
        json.dumps(
            observation,
            ensure_ascii=False,
            separators=(",", ":"),
            allow_nan=False,
        ),
        _request() if request is None else request,
        ordinal,
    )


class TrustedIdentityBindingTests(unittest.TestCase):
    def setUp(self) -> None:
        host._reset_failure_context("run")

    def test_exact_echo_binds_with_equal_hashes(self) -> None:
        output = _parse(_provider_observation(), ordinal=7)
        audit = output["identityBindingAudit"]

        self.assertEqual("case-1", output["caseId"])
        self.assertEqual("candidate-1", output["candidateId"])
        self.assertTrue(audit["caseEchoMatched"])
        self.assertTrue(audit["candidateEchoMatched"])
        self.assertEqual(
            audit["providerPayloadSha256"],
            audit["trustedBoundPayloadSha256"],
        )
        self.assertEqual(7, audit["caseOrdinal"])
        self.assertTrue(audit["boundAtUtc"].endswith("Z"))

    def test_foreign_echoes_bind_to_request_without_routing(self) -> None:
        request = _request("trusted-case", "trusted-candidate")
        output = _parse(
            _provider_observation("foreign-case", "foreign-candidate"),
            request,
            ordinal=9,
        )
        audit = output["identityBindingAudit"]

        self.assertEqual("trusted-case", output["caseId"])
        self.assertEqual("trusted-candidate", output["candidateId"])
        self.assertEqual("foreign-case", audit["providerEchoCaseId"])
        self.assertEqual(
            "foreign-candidate",
            audit["providerEchoCandidateId"],
        )
        self.assertFalse(audit["caseEchoMatched"])
        self.assertFalse(audit["candidateEchoMatched"])
        self.assertNotEqual(
            audit["providerPayloadSha256"],
            audit["trustedBoundPayloadSha256"],
        )
        self.assertEqual(9, audit["caseOrdinal"])
        self.assertIsNone(output["normalizationAudit"])

    def test_one_foreign_echo_records_only_that_mismatch(self) -> None:
        candidate = _parse(
            _provider_observation(candidate_id="foreign"),
        )["identityBindingAudit"]
        self.assertTrue(candidate["caseEchoMatched"])
        self.assertFalse(candidate["candidateEchoMatched"])

        case = _parse(
            _provider_observation(case_id="foreign"),
        )["identityBindingAudit"]
        self.assertFalse(case["caseEchoMatched"])
        self.assertTrue(case["candidateEchoMatched"])

    def test_normalized_foreign_echo_hashes_canonical_projection(
        self,
    ) -> None:
        request = _request("trusted-case", "trusted-candidate")
        provider = _provider_observation(
            "foreign-case",
            "foreign-candidate",
        )
        provider["limitations"] = [
            "Only sampled frames were observed.",
            "Only sampled frames were observed.",
        ]
        output = _parse(provider, request)
        audit = output["identityBindingAudit"]
        provider_projection = {
            key: output[key]
            for key in host.PROVIDER_OBSERVATION_KEYS
        }
        provider_projection["caseId"] = "foreign-case"
        provider_projection["candidateId"] = "foreign-candidate"
        trusted_projection = dict(provider_projection)
        trusted_projection["caseId"] = "trusted-case"
        trusted_projection["candidateId"] = "trusted-candidate"

        self.assertIsNotNone(output["normalizationAudit"])
        self.assertEqual(
            host._canonical_json_sha256(provider_projection),
            audit["providerPayloadSha256"],
        )
        self.assertEqual(
            host._canonical_json_sha256(trusted_projection),
            audit["trustedBoundPayloadSha256"],
        )
        self.assertEqual(
            output["normalizationAudit"]["canonicalOutputSha256"],
            audit["trustedBoundPayloadSha256"],
        )

    def test_missing_blank_and_malformed_echoes_reject(self) -> None:
        cases = []
        missing = _provider_observation()
        del missing["caseId"]
        cases.append(missing)
        blank = _provider_observation(candidate_id=" ")
        cases.append(blank)
        malformed = _provider_observation(case_id="../../foreign")
        cases.append(malformed)

        for observation in cases:
            with self.subTest(observation=observation):
                with self.assertRaises(host.HostError):
                    _parse(observation)

    def test_malformed_echo_is_not_retained_as_valid_attempt_identity(
        self,
    ) -> None:
        observation = _provider_observation(
            candidate_id=" unsafe candidate ",
        )
        with self.assertRaises(host.HostError):
            _parse(observation)

        partial = host._FAILURE_CONTEXT["providerOutput"]
        self.assertEqual("case-1", partial["providerEchoCaseId"])
        self.assertNotIn("providerEchoCandidateId", partial)

    def test_binding_hash_consistency_is_enforced(self) -> None:
        payload = _provider_observation()
        with (
            mock.patch.object(
                trusted_identity,
                "_canonical_json_sha256",
                side_effect=["a" * 64, "b" * 64],
            ),
            self.assertRaises(host.OutputError),
        ):
            trusted_identity._bind_trusted_identity(
                payload,
                _request(),
                1,
            )

        payload["candidateId"] = "foreign"
        with (
            mock.patch.object(
                trusted_identity,
                "_canonical_json_sha256",
                side_effect=["a" * 64, "a" * 64],
            ),
            self.assertRaises(host.OutputError),
        ):
            trusted_identity._bind_trusted_identity(
                payload,
                _request(),
                1,
            )

    def test_policy_identity_and_frozen_predecessors_are_exact(self) -> None:
        self.assertEqual("0.5A.9", host.HOST_VERSION)
        self.assertEqual(
            "visual-semantic-observation-batch-1.5",
            host.OUTPUT_SCHEMA,
        )
        self.assertEqual(
            "visual-semantic-provider-attempt-batch-1.0",
            host.ATTEMPT_SCHEMA,
        )
        self.assertEqual(
            host.IDENTITY_BINDING_POLICY_SHA256,
            host._identity_binding_policy_source()[1],
        )
        self.assertEqual(
            "18c738c006b638e770ee0e69efafe43770939ae3528d79220ef253679564e8c9",
            host.PROMPT_SHA256,
        )
        self.assertEqual(
            "51a3d6b67ca18546b38aa4c63d698bd1f499fc2d7330bf9090c83dfa429c98d8",
            host.NORMALIZATION_POLICY_SHA256,
        )
        self.assertEqual(
            "42813a9b29ff774343cf9a2fa149d53cef780e1ad7a7fd0ad3e3312858ee9bbd",
            host.GENERATION_POLICY_SHA256,
        )

    def test_invalid_policy_hash_rejects(self) -> None:
        with (
            mock.patch.object(
                canonical_json,
                "IDENTITY_BINDING_POLICY_SHA256",
                "0" * 64,
            ),
            self.assertRaises(host.InitializationError),
        ):
            canonical_json._identity_binding_policy_source()


class ProviderAttemptContractTests(unittest.TestCase):
    def setUp(self) -> None:
        host._reset_failure_context("run")
        self.request = _request()
        self.observation = _parse(_provider_observation())
        self.generation = {"caseOrdinal": 1}
        self.timing = {"caseOrdinal": 1}

    def _success(self, ordinal: int = 1) -> dict:
        request = _request(
            f"case-{ordinal}",
            f"candidate-{ordinal}",
        )
        observation = _parse(
            _provider_observation(
                request["caseId"],
                request["candidate"]["id"],
            ),
            request,
            ordinal,
        )
        return host._provider_case_success(
            request,
            ordinal,
            observation,
            1.25,
            {"caseOrdinal": ordinal},
            {"caseOrdinal": ordinal},
        )

    def test_attempt_counts_order_and_canonical_hash_reconcile(self) -> None:
        outcomes = [self._success(1), self._success(2)]
        payload = host._attempt_batch_payload(outcomes, 123, 2.5)

        self.assertEqual(2, payload["requestCount"])
        self.assertEqual(2, payload["successCount"])
        self.assertEqual(0, payload["failureCount"])
        canonical_hash = payload["canonicalAttemptSha256"]
        unhashed = dict(payload)
        del unhashed["canonicalAttemptSha256"]
        self.assertEqual(
            canonical_hash,
            host._canonical_json_sha256(unhashed),
        )

    def test_attempt_rejects_duplicate_identity_and_reordered_ordinal(
        self,
    ) -> None:
        duplicate = [self._success(1), self._success(2)]
        duplicate[1]["caseId"] = duplicate[0]["caseId"]
        with self.assertRaises(host.OutputError):
            host._attempt_batch_payload(duplicate, 0, 0)

        reordered = [self._success(1), self._success(2)]
        reordered[1]["caseOrdinal"] = 3
        with self.assertRaises(host.OutputError):
            host._attempt_batch_payload(reordered, 0, 0)

    def test_failure_is_bounded_and_contains_no_raw_text(self) -> None:
        host._set_failure_case(
            self.request,
            1,
            "a" * 64,
        )
        host._set_failure_stage("OutputValidation")
        host._set_failure_provider_output(
            rawGeneratedTextSha256="b" * 64,
            providerEchoCaseId="foreign-case",
            providerEchoCandidateId="foreign-candidate",
        )
        outcome = host._provider_case_failure(
            self.request,
            1,
            host.InferenceError("x" * 5_000),
            elapsed_seconds=1.0,
        )

        self.assertEqual("Failed", outcome["status"])
        self.assertIsNone(outcome["observation"])
        self.assertLessEqual(
            len(outcome["failure"]["message"]),
            host.MAX_FAILURE_MESSAGE_LENGTH,
        )
        serialized = json.dumps(outcome)
        self.assertNotIn('"rawGeneratedText":', serialized)
        self.assertEqual(
            "b" * 64,
            outcome["failure"]["rawGeneratedTextSha256"],
        )

    def test_case_local_and_global_failure_boundaries(self) -> None:
        host._set_failure_stage("OutputSafety")
        self.assertTrue(
            host._is_case_local_provider_failure(
                host.InferenceError("bad output")
            )
        )
        host._set_failure_stage("Inference")
        self.assertFalse(
            host._is_case_local_provider_failure(
                host.InferenceError("CUDA input failure")
            )
        )
        host._set_failure_stage("Generation")
        self.assertTrue(
            host._is_case_local_provider_failure(
                host.GenerationTokenBudgetExceededError("budget")
            )
        )
        self.assertFalse(
            host._is_case_local_provider_failure(
                RuntimeError("CUDA out of memory")
            )
        )

    def test_provider_case_failures_exit_code_is_nine(self) -> None:
        self.assertEqual(9, host.ProviderCaseFailuresDetected.exit_code)


class _FakeCuda:
    def empty_cache(self) -> None:
        pass

    def reset_peak_memory_stats(self, _device: int) -> None:
        pass

    def max_memory_allocated(self, _device: int) -> int:
        return 456


class ExhaustiveRunTests(unittest.TestCase):
    def setUp(self) -> None:
        host._reset_failure_context("run")
        self.requests = [
            _request("case-1", "candidate-1"),
            _request("case-2", "candidate-2"),
        ]
        self.torch = SimpleNamespace(cuda=_FakeCuda())

    def _success_tuple(
        self,
        request: dict,
        ordinal: int,
    ) -> tuple[dict, float, dict, dict]:
        observation = _parse(
            _provider_observation(
                request["caseId"],
                request["candidate"]["id"],
            ),
            request,
            ordinal,
        )
        return (
            observation,
            0.5,
            {"caseOrdinal": ordinal},
            {"caseOrdinal": ordinal},
        )

    def _run_with_infer(self, infer) -> tuple[list, Exception | None]:
        writes: list[tuple[Path, dict]] = []

        def capture_write(path: Path, payload: dict) -> None:
            writes.append((path, payload))

        patches = (
            mock.patch.object(host, "_prompt_source", return_value=("p", "h")),
            mock.patch.object(host, "_normalization_policy_source"),
            mock.patch.object(host, "_generation_policy_source"),
            mock.patch.object(host, "_identity_binding_policy_source"),
            mock.patch.object(host, "_validate_model_directory"),
            mock.patch.object(host, "_load_strict_json", return_value={}),
            mock.patch.object(host, "_record_input_failure_identity"),
            mock.patch.object(
                host,
                "_input_case_hashes",
                return_value=["a" * 64, "b" * 64],
            ),
            mock.patch.object(
                host,
                "_validate_input_batch",
                return_value=self.requests,
            ),
            mock.patch.object(
                host,
                "_validate_failure_output_against_media",
            ),
            mock.patch.object(host, "_require_path_outside_roots"),
            mock.patch.object(
                host,
                "_load_runtime",
                return_value=(
                    self.torch,
                    object(),
                    object(),
                    object(),
                ),
            ),
            mock.patch.object(
                host,
                "_runtime_package_manifest",
                return_value={"runtime": "test"},
            ),
            mock.patch.object(
                host,
                "_load_model_and_processor",
                return_value=(object(), object()),
            ),
            mock.patch.object(host, "_revalidate_media_inputs"),
            mock.patch.object(
                host,
                "_execution_timing_payload",
                side_effect=lambda cases: {"cases": cases},
            ),
            mock.patch.object(
                host,
                "_generation_manifest_payload",
                side_effect=lambda cases: {"cases": cases},
            ),
            mock.patch.object(host, "_infer_one", side_effect=infer),
            mock.patch.object(host, "_write_json_atomic", side_effect=capture_write),
        )
        error: Exception | None = None
        with ExitStack() as stack:
            for patcher in patches:
                stack.enter_context(patcher)
            try:
                host._run(
                    Path("A:/outside/model"),
                    Path("A:/outside/input.json"),
                    Path("A:/outside/completed.json"),
                    Path("A:/outside/attempt.json"),
                    Path("A:/outside/ffmpeg"),
                )
            except Exception as caught:
                error = caught
        return writes, error

    def test_case_failure_does_not_stop_later_case(self) -> None:
        calls: list[int] = []

        def infer(request, ordinal, *_args, **_kwargs):
            calls.append(ordinal)
            if ordinal == 1:
                host._set_failure_stage("OutputValidation")
                host._set_failure_provider_output(
                    rawGeneratedTextSha256="c" * 64,
                )
                raise host.InferenceError("invalid semantic enum")
            return self._success_tuple(request, ordinal)

        writes, error = self._run_with_infer(infer)

        self.assertEqual([1, 2], calls)
        self.assertIsInstance(error, host.ProviderCaseFailuresDetected)
        self.assertEqual(1, len(writes))
        attempt = writes[0][1]
        self.assertEqual(["Failed", "Succeeded"], [
            item["status"] for item in attempt["outcomes"]
        ])
        self.assertEqual(1, attempt["successCount"])
        self.assertEqual(1, attempt["failureCount"])

    def test_all_success_writes_attempt_and_completed_without_rerun(
        self,
    ) -> None:
        calls: list[int] = []

        def infer(request, ordinal, *_args, **_kwargs):
            calls.append(ordinal)
            return self._success_tuple(request, ordinal)

        writes, error = self._run_with_infer(infer)

        self.assertIsNone(error)
        self.assertEqual([1, 2], calls)
        self.assertEqual(2, len(writes))
        attempt = writes[0][1]
        completed = writes[1][1]
        self.assertEqual(0, attempt["failureCount"])
        self.assertEqual(
            [
                item["observation"]["caseId"]
                for item in attempt["outcomes"]
            ],
            [item["caseId"] for item in completed["results"]],
        )

    def test_multiple_typed_failures_are_retained_in_stable_order(
        self,
    ) -> None:
        calls: list[int] = []

        def infer(_request, ordinal, *_args, **_kwargs):
            calls.append(ordinal)
            host._set_failure_execution_timing(
                {"caseOrdinal": ordinal}
            )
            host._set_case_generation(
                {"caseOrdinal": ordinal}
            )
            if ordinal == 1:
                host._set_failure_stage("Generation")
                raise host.GenerationTokenBudgetExceededError("budget")
            host._set_failure_stage("OutputSafety")
            raise host.InferenceError("invalid JSON")

        writes, error = self._run_with_infer(infer)

        self.assertIsInstance(error, host.ProviderCaseFailuresDetected)
        self.assertEqual([1, 2], calls)
        self.assertEqual(1, len(writes))
        outcomes = writes[0][1]["outcomes"]
        self.assertEqual([1, 2], [
            item["caseOrdinal"] for item in outcomes
        ])
        self.assertEqual(
            [
                "GenerationTokenBudgetExceededError",
                "InferenceError",
            ],
            [item["failure"]["errorCode"] for item in outcomes],
        )
        self.assertTrue(
            all(item["generation"] is not None for item in outcomes)
        )
        self.assertTrue(
            all(item["executionTiming"] is not None for item in outcomes)
        )

    def test_unexpected_runtime_failure_stops_without_attempt(self) -> None:
        calls: list[int] = []

        def infer(_request, ordinal, *_args, **_kwargs):
            calls.append(ordinal)
            raise RuntimeError("CUDA out of memory")

        writes, error = self._run_with_infer(infer)

        self.assertIsInstance(error, RuntimeError)
        self.assertEqual([1], calls)
        self.assertEqual([], writes)


if __name__ == "__main__":
    unittest.main(verbosity=2)
