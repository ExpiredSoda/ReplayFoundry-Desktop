"""Model-free coverage for grounded JSON whitespace progress."""
from __future__ import annotations

import hashlib
from pathlib import Path
import unittest

from replayfoundry_visual_semantic.editorial.grounded_metadata_json_whitespace import (
    ANY_WHITESPACE,
    POLICY_SHA256,
    POLICY_VERSION,
    require_policy,
)
from replayfoundry_visual_semantic.editorial.structured_decoding import (
    StructuredDecodingSession,
)


class GroundedMetadataJsonWhitespaceTests(unittest.TestCase):
    def test_policy_is_frozen_and_disallows_arbitrary_whitespace(self) -> None:
        self.assertEqual("grounded-editorial-json-whitespace-1.0", POLICY_VERSION)
        self.assertFalse(ANY_WHITESPACE)
        require_policy()
        path = Path(__file__).resolve().parents[1] / (
            "replayfoundry-grounded-editorial-json-whitespace-policy-1.0.txt"
        )
        normalized = path.read_text(encoding="utf-8").replace(
            "\r\n", "\n"
        ).replace("\r", "\n").strip()
        self.assertEqual(
            POLICY_SHA256,
            hashlib.sha256(normalized.encode("utf-8")).hexdigest(),
        )

    def test_session_forwards_canonical_whitespace_to_xgrammar(self) -> None:
        calls: list[dict[str, object]] = []

        class Compiler:
            @staticmethod
            def compile_json_schema(*_args, **kwargs):
                calls.append(kwargs)
                return object()

        session = StructuredDecodingSession.__new__(StructuredDecodingSession)
        session._compiler = Compiler()  # type: ignore[attr-defined]
        _, audit = session.compile_json_schema(
            '{"type":"object"}',
            "fixture-schema",
            "a" * 64,
            any_whitespace=ANY_WHITESPACE,
        )
        self.assertEqual(
            [{"any_whitespace": False, "strict_mode": True}],
            calls,
        )
        self.assertEqual("fixture-schema", audit.schema_version)


if __name__ == "__main__":
    unittest.main()
