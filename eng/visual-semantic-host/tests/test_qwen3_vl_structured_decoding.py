"""Model-free checks for Prompt 2.3 constrained decoding."""
from __future__ import annotations

import json
import sys
import unittest
from decimal import Decimal
from pathlib import Path
from types import SimpleNamespace

HOST_ROOT = Path(__file__).resolve().parents[1]
if str(HOST_ROOT) not in sys.path:
    sys.path.insert(0, str(HOST_ROOT))

from replayfoundry_visual_semantic.editorial.constraint_schema import (
    build_editorial_schema,
    build_editorial_schema_artifact,
    canonical_schema_json,
)
from replayfoundry_visual_semantic.editorial.structured_decoding import (
    StructuredDecodingAudit,
    StructuredDecodingSession,
    model_vocab_size,
)
from replayfoundry_visual_semantic.editorial.structured_decoding_capability import (
    _fixture_corpus,
    _strict_parser_accepts,
)
from replayfoundry_visual_semantic.editorial.structured_decoding_policy import (
    BACKEND_NAME,
    BACKEND_VERSION,
    REPRESENTATION,
    SEMANTIC_REPAIR_PERMITTED,
    UNCONSTRAINED_FALLBACK_PERMITTED,
    StructuredDecodingUnavailableError,
    StructuredDecodingSchemaCompilationError,
)


class StructuredDecodingTests(unittest.TestCase):
    def test_representation_and_no_fallback_policy_are_frozen(self) -> None:
        self.assertEqual("XGrammar", BACKEND_NAME)
        self.assertEqual("0.2.2", BACKEND_VERSION)
        self.assertEqual("JsonSchema", REPRESENTATION)
        self.assertFalse(UNCONSTRAINED_FALLBACK_PERMITTED)
        self.assertFalse(SEMANTIC_REPAIR_PERMITTED)

    def test_exact_decimal_bound_is_a_canonical_json_number(self) -> None:
        _, text, _ = build_editorial_schema_artifact(
            Decimal("10.125"), Decimal("2"), Decimal("6")
        )
        self.assertIn('"maximum":6', text)
        self.assertIn('"minimum":2', text)
        self.assertNotIn('"maximum":"6"', text)
        self.assertEqual(
            text,
            build_editorial_schema_artifact(
                Decimal("10.125"), Decimal("2"), Decimal("6"))[1],
        )

    def test_binary_float_schema_values_are_rejected(self) -> None:
        with self.assertRaises(TypeError):
            canonical_schema_json({"maximum": 10.125})

    def test_duration_requires_positive_finite_decimal(self) -> None:
        for value in (
            Decimal("0"),
            Decimal("-1"),
            Decimal("NaN"),
            Decimal("Infinity"),
            10,
        ):
            with self.subTest(value=value):
                with self.assertRaises(ValueError):
                    build_editorial_schema(  # type: ignore[arg-type]
                        value, Decimal("2"), Decimal("6"))

    def test_one_exact_root_schema_avoids_xgrammar_union_dead_state(self) -> None:
        schema = build_editorial_schema(
            Decimal("10"), Decimal("2"), Decimal("6"))
        self.assertNotIn("oneOf", schema)
        self.assertFalse(schema["additionalProperties"])
        self.assertEqual(
            set(schema["properties"]),
            set(schema["required"]),
        )

    def test_schema_hash_is_deterministic_and_candidate_specific(self) -> None:
        first = build_editorial_schema_artifact(
            Decimal("10.125"), Decimal("2"), Decimal("6"))
        second = build_editorial_schema_artifact(
            Decimal("10.125"), Decimal("2"), Decimal("6"))
        other = build_editorial_schema_artifact(
            Decimal("10.125"), Decimal("2"), Decimal("6.001"))
        self.assertEqual(first[1:], second[1:])
        self.assertNotEqual(first[2], other[2])

    def test_nested_qwen_text_vocabulary_is_supported(self) -> None:
        model = SimpleNamespace(
            config=SimpleNamespace(
                text_config=SimpleNamespace(vocab_size=151936)
            )
        )
        self.assertEqual(151936, model_vocab_size(model))

    def test_direct_vocabulary_takes_precedence(self) -> None:
        model = SimpleNamespace(
            config=SimpleNamespace(
                vocab_size=42,
                text_config=SimpleNamespace(vocab_size=151936),
            )
        )
        self.assertEqual(42, model_vocab_size(model))

    def test_missing_invalid_vocabulary_rejects(self) -> None:
        for value in (None, 0, -1, True, "151936"):
            model = SimpleNamespace(
                config=SimpleNamespace(
                    text_config=SimpleNamespace(vocab_size=value)
                )
            )
            with self.subTest(value=value):
                with self.assertRaises(
                    StructuredDecodingUnavailableError
                ):
                    model_vocab_size(model)

    def test_fixture_corpus_covers_valid_and_invalid_paths(self) -> None:
        rows = _fixture_corpus()
        self.assertEqual(15, len(rows))
        self.assertEqual(8, sum(expected for _, _, expected, _ in rows))
        self.assertEqual(7, sum(not expected for _, _, expected, _ in rows))
        self.assertEqual(len(rows), len({name for name, _, _, _ in rows}))

    def test_strict_parser_accepts_every_positive_fixture(self) -> None:
        for name, value, _, parser_expected in _fixture_corpus():
            if parser_expected:
                with self.subTest(name=name):
                    self.assertTrue(
                        _strict_parser_accepts(canonical_schema_json(value))
                    )

    def test_strict_parser_rejects_every_negative_fixture(self) -> None:
        for name, value, _, parser_expected in _fixture_corpus():
            if not parser_expected:
                with self.subTest(name=name):
                    self.assertFalse(
                        _strict_parser_accepts(canonical_schema_json(value))
                    )

    def test_audit_is_immutable_and_records_no_repair(self) -> None:
        audit = StructuredDecodingAudit(
            "policy",
            "XGrammar",
            "0.2.2",
            "schema",
            "a" * 64,
            "JsonSchema",
            "torch_native",
            0.1,
            None,
            None,
            None,
            False,
            False,
        )
        generated = audit.with_generation(12, "EndOfSequence")
        parsed = generated.with_parser_outcome(True)
        self.assertIsNone(audit.generated_token_count)
        self.assertEqual(12, parsed.generated_token_count)
        self.assertTrue(parsed.strict_parser_accepted)
        self.assertFalse(parsed.to_json()["unconstrainedFallbackUsed"])
        self.assertFalse(parsed.to_json()["semanticRepairApplied"])

    def test_case_schema_compilation_failure_is_not_global(self) -> None:
        class _Compiler:
            @staticmethod
            def compile_json_schema(*_args, **_kwargs):
                raise RuntimeError("fixture compile failure")

        session = StructuredDecodingSession.__new__(
            StructuredDecodingSession
        )
        session._compiler = _Compiler()  # type: ignore[attr-defined]
        with self.assertRaises(
                StructuredDecodingSchemaCompilationError) as raised:
            session.compile_case(
                Decimal("10.125"), Decimal("2"), Decimal("6"))
        self.assertTrue(hasattr(raised.exception, "audit"))
        self.assertEqual(
            build_editorial_schema_artifact(
                Decimal("10.125"), Decimal("2"), Decimal("6"))[2],
            raised.exception.audit.schema_sha256,
        )


if __name__ == "__main__":
    unittest.main()
