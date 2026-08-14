#!/usr/bin/env python3
"""Model-free tests for the Qwen generation-budget boundary."""

from __future__ import annotations

import hashlib
import json
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock


HOST_DIRECTORY = Path(__file__).resolve().parent.parent
if str(HOST_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(HOST_DIRECTORY))

from replayfoundry_visual_semantic import generation as host
from replayfoundry_visual_semantic import observation_validation


class _FakeRow:
    def __init__(self, values: list[int]) -> None:
        self._values = list(values)

    def detach(self) -> "_FakeRow":
        return self

    def cpu(self) -> "_FakeRow":
        return self

    def tolist(self) -> list[int]:
        return list(self._values)


class _FakeTensor:
    def __init__(self, values: list[int]) -> None:
        self._row = _FakeRow(values)

    def __len__(self) -> int:
        return 1

    def __getitem__(self, index: int) -> _FakeRow:
        if index != 0:
            raise IndexError(index)
        return self._row


class _FakeInputs(dict):
    def __init__(self, token_ids: list[int]) -> None:
        tensor = _FakeTensor(token_ids)
        super().__init__(input_ids=tensor)
        self.input_ids = tensor


class _FakeModel:
    def __init__(
        self,
        generated_token_ids: list[int],
        *,
        eos_token_id: int | list[int] = 99,
        returned_prefix: list[int] | None = None,
        forced_eos_token_id: int | None = None,
        stop_strings: list[str] | None = None,
    ) -> None:
        self.generated_token_ids = list(generated_token_ids)
        self.returned_prefix = returned_prefix
        self.generation_config = SimpleNamespace(
            eos_token_id=eos_token_id,
            forced_eos_token_id=forced_eos_token_id,
            stop_strings=stop_strings,
        )
        self.calls: list[dict] = []

    def generate(self, **kwargs):
        self.calls.append(dict(kwargs))
        input_ids = kwargs["input_ids"][0].tolist()
        prefix = (
            input_ids
            if self.returned_prefix is None
            else self.returned_prefix
        )
        return _FakeTensor(prefix + self.generated_token_ids)


def _request() -> dict:
    return {
        "caseId": "case-1",
        "candidate": {"id": "candidate-1"},
        "_validated": {"videoDuration": 10},
    }


def _generation_case(
    *,
    reason: str = "EndOfSequence",
    maximum: int = host.ACTIVE_POLICY_MAX_NEW_TOKENS,
) -> dict:
    decoded = '{"caseId":"case-1"}'
    return {
        "caseId": "case-1",
        "candidateId": "candidate-1",
        "caseOrdinal": 1,
        "inputTokenCount": 10,
        "generatedTokenCount": 2,
        "maximumNewTokens": maximum,
        "endOfSequenceTokenIds": [98, 99],
        "firstEndOfSequenceGeneratedIndex":
            1 if reason == "EndOfSequence" else None,
        "terminalTokenId": 99,
        "terminationReason": reason,
        "generatedTokenIdsSha256":
            host._token_ids_sha256([12, 99]),
        "legacyPrefixTokenCount": 2,
        "legacyPrefixTokenIdsSha256":
            host._token_ids_sha256([12, 99]),
        "decodedTextSha256":
            hashlib.sha256(decoded.encode("utf-8")).hexdigest(),
        "decodedTextUtf8ByteCount": len(decoded.encode("utf-8")),
    }


class GenerationBudgetTests(unittest.TestCase):
    def setUp(self) -> None:
        host._reset_failure_context("run")

    def test_policy_file_hash_and_phase_a_gate_are_exact(self) -> None:
        self.assertEqual("0.5A.9", host.HOST_VERSION)
        self.assertEqual(
            "visual-semantic-observation-batch-1.5",
            host.OUTPUT_SCHEMA,
        )
        self.assertEqual(
            "visual-semantic-host-failure-1.4",
            host.FAILURE_SCHEMA,
        )
        self.assertEqual(
            "visual-semantic-raw-output-audit-1.2",
            host.RAW_OUTPUT_AUDIT_SCHEMA,
        )
        self.assertEqual(
            host.GENERATION_POLICY_SHA256,
            host._generation_policy_source()[1],
        )
        self.assertEqual(
            768,
            host.LEGACY_DIAGNOSTIC_MAX_NEW_TOKENS,
        )
        self.assertEqual(
            2048,
            host.ACTIVE_POLICY_MAX_NEW_TOKENS,
        )
        self.assertEqual(
            host.ACTIVE_POLICY_MAX_NEW_TOKENS,
            host.MAX_NEW_TOKENS,
            "Phase B must use only the active 2048-token ceiling.",
        )

    def test_eos_scalar_and_list_normalize_without_mutating_config(self) -> None:
        scalar = _FakeModel([], eos_token_id=99)
        self.assertEqual(
            [99],
            host._normalized_eos_token_ids(scalar),
        )

        raw = [151645, 151643, 151645]
        listed = _FakeModel([], eos_token_id=raw)
        self.assertEqual(
            [151643, 151645],
            host._normalized_eos_token_ids(listed),
        )
        self.assertEqual(raw, listed.generation_config.eos_token_id)

    def test_missing_invalid_forced_eos_and_stop_strings_reject(self) -> None:
        invalid_models = [
            SimpleNamespace(generation_config=SimpleNamespace()),
            _FakeModel([], eos_token_id=[]),
            _FakeModel([], eos_token_id=[True]),
            _FakeModel([], eos_token_id=[-1]),
            _FakeModel([], forced_eos_token_id=99),
            _FakeModel([], stop_strings=["}"]),
        ]
        for model in invalid_models:
            with self.subTest(model=model):
                with self.assertRaises(host.InitializationError):
                    host._normalized_eos_token_ids(model)

    def test_plain_sequence_tensor_eos_trace_and_options_are_exact(self) -> None:
        model = _FakeModel([12, 99], eos_token_id=[99, 98])
        trace = host._generate_with_trace(
            model,
            _FakeInputs([1, 2, 3]),
            host.ACTIVE_POLICY_MAX_NEW_TOKENS,
        )

        self.assertEqual(3, trace.input_token_count)
        self.assertEqual(2, trace.generated_token_count)
        self.assertEqual([98, 99], trace.eos_token_ids)
        self.assertEqual(1, trace.first_eos_generated_index)
        self.assertEqual(99, trace.terminal_token_id)
        self.assertEqual("EndOfSequence", trace.termination_reason)
        self.assertEqual(1, len(model.calls))
        call = model.calls[0]
        self.assertEqual(
            host.ACTIVE_POLICY_MAX_NEW_TOKENS,
            call["max_new_tokens"],
        )
        self.assertFalse(call["do_sample"])
        self.assertEqual(1, call["num_beams"])
        self.assertTrue(call["use_cache"])
        self.assertNotIn(
            "logits_processor",
            call,
            "The historical path must remain byte-for-byte argument exact.",
        )
        for prohibited in (
            "return_dict_in_generate",
            "output_scores",
            "output_attentions",
            "output_hidden_states",
        ):
            self.assertNotIn(prohibited, call)

    def test_explicit_logits_processor_is_forwarded_without_substitution(
        self,
    ) -> None:
        processors = [object()]
        model = _FakeModel([12, 99])
        host._generate_with_trace(
            model,
            _FakeInputs([1]),
            host.ACTIVE_POLICY_MAX_NEW_TOKENS,
            logits_processor=processors,
        )
        self.assertIs(processors, model.calls[0]["logits_processor"])

    def test_offloaded_dynamic_cache_is_narrowly_opt_in(self) -> None:
        model = _FakeModel([12, 99])
        host._generate_with_trace(
            model,
            _FakeInputs([1]),
            host.ACTIVE_POLICY_MAX_NEW_TOKENS,
            cache_implementation="offloaded",
        )
        self.assertEqual(
            "offloaded",
            model.calls[0]["cache_implementation"],
        )

        with self.assertRaises(host.UsageOrInputError):
            host._generate_with_trace(
                _FakeModel([12, 99]),
                _FakeInputs([1]),
                host.ACTIVE_POLICY_MAX_NEW_TOKENS,
                cache_implementation="quantized",
            )

    def test_maximum_budget_without_eos_is_classified(self) -> None:
        model = _FakeModel(
            [12] * host.LEGACY_DIAGNOSTIC_MAX_NEW_TOKENS,
            eos_token_id=99,
        )
        trace = host._generate_with_trace(
            model,
            _FakeInputs([1, 2]),
            host.LEGACY_DIAGNOSTIC_MAX_NEW_TOKENS,
        )
        self.assertEqual(
            host.LEGACY_DIAGNOSTIC_MAX_NEW_TOKENS,
            trace.generated_token_count,
        )
        self.assertIsNone(trace.first_eos_generated_index)
        self.assertEqual(
            "MaximumNewTokensReached",
            trace.termination_reason,
        )
        self.assertEqual(
            trace.generated_token_ids_sha256,
            trace.legacy_prefix_token_ids_sha256,
        )

    def test_early_non_eos_stop_is_classified(self) -> None:
        trace = host._generate_with_trace(
            _FakeModel([12, 13], eos_token_id=99),
            _FakeInputs([1]),
            host.ACTIVE_POLICY_MAX_NEW_TOKENS,
        )
        self.assertEqual("UnexpectedStop", trace.termination_reason)
        self.assertIsNone(trace.first_eos_generated_index)

    def test_typed_completion_gate_distinguishes_both_non_eos_stops(
        self,
    ) -> None:
        maximum = host._generate_with_trace(
            _FakeModel(
                [12] *
                host.LEGACY_DIAGNOSTIC_MAX_NEW_TOKENS,
                eos_token_id=99,
            ),
            _FakeInputs([1]),
            host.LEGACY_DIAGNOSTIC_MAX_NEW_TOKENS,
        )
        with self.assertRaises(
            host.GenerationTokenBudgetExceededError,
        ):
            host._require_completed_generation(maximum)

        early = host._generate_with_trace(
            _FakeModel([12], eos_token_id=99),
            _FakeInputs([1]),
            host.ACTIVE_POLICY_MAX_NEW_TOKENS,
        )
        with self.assertRaises(
            host.UnexpectedGenerationTerminationError,
        ):
            host._require_completed_generation(early)

    def test_terminal_eos_at_the_ceiling_is_not_a_completed_case(self) -> None:
        trace = host._generate_with_trace(
            _FakeModel(
                [12] *
                (host.LEGACY_DIAGNOSTIC_MAX_NEW_TOKENS - 1)
                + [99],
                eos_token_id=99,
            ),
            _FakeInputs([1]),
            host.LEGACY_DIAGNOSTIC_MAX_NEW_TOKENS,
        )
        self.assertEqual("EndOfSequence", trace.termination_reason)
        self.assertEqual(
            host.LEGACY_DIAGNOSTIC_MAX_NEW_TOKENS - 1,
            trace.first_eos_generated_index,
        )
        with self.assertRaises(
            host.GenerationTokenBudgetExceededError,
        ):
            host._require_completed_generation(trace)

    def test_tokens_after_first_eos_reject(self) -> None:
        with self.assertRaises(host.InferenceError):
            host._generate_with_trace(
                _FakeModel([12, 99, 13], eos_token_id=99),
                _FakeInputs([1]),
                host.ACTIVE_POLICY_MAX_NEW_TOKENS,
            )

    def test_changed_prompt_prefix_and_zero_generation_reject(self) -> None:
        with self.assertRaises(host.InferenceError):
            host._generate_with_trace(
                _FakeModel(
                    [99],
                    eos_token_id=99,
                    returned_prefix=[7],
                ),
                _FakeInputs([1]),
                host.ACTIVE_POLICY_MAX_NEW_TOKENS,
            )
        with self.assertRaises(host.InferenceError):
            host._generate_with_trace(
                _FakeModel([], eos_token_id=99),
                _FakeInputs([1]),
                host.ACTIVE_POLICY_MAX_NEW_TOKENS,
            )

    def test_token_and_legacy_prefix_hashes_are_deterministic(self) -> None:
        generated = list(range(1000, 1768)) + [99]
        trace = host._generate_with_trace(
            _FakeModel(generated, eos_token_id=99),
            _FakeInputs([1, 2]),
            host.ACTIVE_POLICY_MAX_NEW_TOKENS,
        )
        self.assertEqual(768, trace.legacy_prefix_token_count)
        self.assertEqual(
            host._token_ids_sha256(generated),
            trace.generated_token_ids_sha256,
        )
        self.assertEqual(
            host._token_ids_sha256(generated[:768]),
            trace.legacy_prefix_token_ids_sha256,
        )
        self.assertNotEqual(
            trace.generated_token_ids_sha256,
            trace.legacy_prefix_token_ids_sha256,
        )

    def test_decoded_text_hash_and_failure_payload_are_exact(self) -> None:
        text = '{"visibleStateChange":"niño"}'
        trace = host._generate_with_trace(
            _FakeModel([12, 99], eos_token_id=99),
            _FakeInputs([1]),
            host.ACTIVE_POLICY_MAX_NEW_TOKENS,
        )
        case = host._generation_case_payload(
            _request(),
            1,
            trace,
            text,
        )
        self.assertEqual(
            hashlib.sha256(text.encode("utf-8")).hexdigest(),
            case["decodedTextSha256"],
        )
        self.assertEqual(
            len(text.encode("utf-8")),
            case["decodedTextUtf8ByteCount"],
        )
        failure = host._failure_generation_payload(case)
        self.assertEqual(
            host.GENERATION_POLICY_SHA256,
            failure["policySha256"],
        )
        self.assertFalse(failure["doSample"])
        self.assertEqual(1, failure["numberOfBeams"])
        self.assertTrue(failure["useCache"])
        self.assertNotIn("rawGeneratedText", failure)
        self.assertNotIn("generatedTokenIds", failure)

    def test_failure_context_clears_generation_between_cases(self) -> None:
        host._set_failure_generation(
            host._failure_generation_payload(_generation_case())
        )
        self.assertIsNotNone(host._FAILURE_CONTEXT["generation"])
        host._clear_failure_case()
        self.assertIsNone(host._FAILURE_CONTEXT["generation"])

    def test_success_manifest_is_ordered_and_hashed(self) -> None:
        cases = [
            _generation_case(),
            {
                **_generation_case(),
                "caseId": "case-2",
                "candidateId": "candidate-2",
                "caseOrdinal": 2,
            },
        ]
        manifest = host._generation_manifest_payload(cases)
        self.assertEqual(2, manifest["caseCount"])
        self.assertEqual(
            ["case-1", "case-2"],
            [case["caseId"] for case in manifest["cases"]],
        )
        canonical = dict(manifest)
        actual_hash = canonical.pop("canonicalGenerationSha256")
        self.assertEqual(
            host._canonical_json_sha256(canonical),
            actual_hash,
        )

    def test_success_manifest_rejects_non_eos_case(self) -> None:
        with mock.patch.object(
            host,
            "MAX_NEW_TOKENS",
            host.ACTIVE_POLICY_MAX_NEW_TOKENS,
        ):
            with self.assertRaises(host.OutputError):
                host._generation_manifest_payload(
                    [
                        _generation_case(
                            reason="UnexpectedStop",
                        )
                    ]
                )

    def test_malformed_json_raw_audit_records_boundary_and_generation(
        self,
    ) -> None:
        raw_text = (
            '{"caseId":"case-1","candidateId":"candidate-1",'
            '"conciseRationale":"unfinished'
        )
        generation = _generation_case()
        generation["decodedTextSha256"] = hashlib.sha256(
            raw_text.encode("utf-8")
        ).hexdigest()
        generation["decodedTextUtf8ByteCount"] = len(
            raw_text.encode("utf-8")
        )

        with (
            mock.patch.object(
                observation_validation,
                "_revalidate_media_inputs",
            ),
            mock.patch.object(
                observation_validation,
                "_write_json_atomic",
            ) as write_json,
            self.assertRaises(host.RawAuditCaptured),
        ):
            host._capture_provider_output_audit(
                raw_text,
                _request(),
                Path("raw-audit.json"),
                {"identity": "test"},
                1.25,
                generation,
            )

        payload = write_json.call_args.args[1]
        self.assertFalse(payload["jsonParse"]["succeeded"])
        self.assertEqual(
            "Unterminated string starting at",
            payload["jsonParse"]["message"],
        )
        self.assertTrue(
            payload["jsonParse"][
                "failureAtGeneratedTextBoundary"
            ]
        )
        self.assertEqual(["InvalidJson"], payload["failedInvariants"])
        self.assertEqual(generation, payload["generation"])
        self.assertIsNone(payload["parsedPropertyNames"])

    def test_raw_audit_safety_rejects_markdown_and_reasoning_first(
        self,
    ) -> None:
        unsafe = (
            "```json\n{}\n```",
            '{"analysis":"private scratchpad"}',
            '{"visibleStateChange":"my hidden reasoning"}',
        )
        for raw_text in unsafe:
            with self.subTest(raw_text=raw_text):
                with self.assertRaises(host.InferenceError):
                    host._provider_output_text_safety_gate(raw_text)


if __name__ == "__main__":
    unittest.main()
