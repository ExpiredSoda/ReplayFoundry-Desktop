#!/usr/bin/env python3
"""Model-free regression tests for the frozen Qwen output boundary."""

from __future__ import annotations

import copy
import hashlib
import json
import unittest
from decimal import Decimal
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import mock

from replayfoundry_visual_semantic import observation_validation as host
from replayfoundry_visual_semantic import path_policy


class QwenOutputContractTests(unittest.TestCase):
    def setUp(self) -> None:
        self.request = {
            "caseId": "case-1",
            "candidate": {"id": "candidate-1"},
            "_validated": {"videoDuration": Decimal("10")},
        }
        self.observation = {
            "caseId": "case-1",
            "candidateId": "candidate-1",
            "schemaVersion": host.OBSERVATION_SCHEMA,
            "observableContentType": "Action",
            "visibleStateChange": "The visible state changes.",
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
                    "description": "Visible state change.",
                }
            ],
            "uncertainties": [
                {
                    "code": host.UNCERTAINTY_CODE_ORDER[0],
                    "description": "Bounded visual evidence.",
                }
            ],
            "limitations": ["Bounded sampled video."],
            "conciseRationale": "The bounded video shows a visible change.",
        }

    def parse(self, observation: dict | None = None) -> dict:
        value = self.observation if observation is None else observation
        return host._parse_provider_observation(
            json.dumps(
                value,
                ensure_ascii=False,
                separators=(",", ":"),
                allow_nan=False,
            ),
            self.request,
        )

    def assert_observation_rejected(self, observation: dict) -> None:
        with self.assertRaises(host.HostError):
            self.parse(observation)

    def test_canonical_collections_remain_unchanged(self) -> None:
        output = self.parse()
        self.assertEqual(
            self.observation["evidenceIntervals"],
            output["evidenceIntervals"],
        )
        self.assertEqual(self.observation["limitations"], output["limitations"])
        self.assertEqual(
            self.observation["uncertainties"],
            output["uncertainties"],
        )
        self.assertIsNone(output["normalizationAudit"])

    def test_limitations_sort_exact_dedupe_and_preserve_text(self) -> None:
        observation = copy.deepcopy(self.observation)
        observation["limitations"] = ["Zulu", "Alpha"]
        output = self.parse(observation)
        self.assertEqual(["Alpha", "Zulu"], output["limitations"])
        audit = output["normalizationAudit"]
        self.assertEqual(["LimitationsCanonicalized"], audit["normalizationKinds"])
        self.assertTrue(audit["limitationOrderChanged"])
        self.assertEqual(0, audit["exactDuplicateLimitationCount"])

        observation["limitations"] = ["Same", "Same"]
        output = self.parse(observation)
        self.assertEqual(["Same"], output["limitations"])
        self.assertEqual(
            1,
            output["normalizationAudit"]["exactDuplicateLimitationCount"],
        )

        observation["limitations"] = [
            "alpha",
            "Alpha",
            "alpha.",
            "alpha!",
        ]
        output = self.parse(observation)
        self.assertEqual(
            ["Alpha", "alpha", "alpha!", "alpha."],
            output["limitations"],
        )

    def test_limitations_validate_before_dedupe(self) -> None:
        for invalid in (
            ["same"] * 5,
            [""],
            ["x" * (host.MAX_DETAIL_TEXT + 1)],
        ):
            with self.subTest(invalid_length=len(invalid)):
                observation = copy.deepcopy(self.observation)
                observation["limitations"] = invalid
                self.assert_observation_rejected(observation)

    def test_uncertainties_sort_exact_dedupe_and_preserve_distinct(self) -> None:
        first = host.UNCERTAINTY_CODE_ORDER[0]
        last = host.UNCERTAINTY_CODE_ORDER[-1]
        observation = copy.deepcopy(self.observation)
        observation["uncertainties"] = [
            {"code": last, "description": "Zulu"},
            {"code": first, "description": "Zulu"},
            {"code": first, "description": "Alpha"},
        ]
        output = self.parse(observation)
        self.assertEqual(
            [
                {"code": first, "description": "Alpha"},
                {"code": first, "description": "Zulu"},
                {"code": last, "description": "Zulu"},
            ],
            output["uncertainties"],
        )
        self.assertTrue(
            output["normalizationAudit"]["uncertaintyOrderChanged"]
        )

        observation["uncertainties"] = [
            {"code": first, "description": "Same"},
            {"code": first, "description": "Same"},
        ]
        output = self.parse(observation)
        self.assertEqual(1, len(output["uncertainties"]))
        self.assertEqual(
            1,
            output["normalizationAudit"]["exactDuplicateUncertaintyCount"],
        )

        observation["uncertainties"] = [
            {"code": first, "description": "Alpha"},
            {"code": first, "description": "Beta"},
        ]
        output = self.parse(observation)
        self.assertEqual(2, len(output["uncertainties"]))

    def test_uncertainties_validate_before_dedupe(self) -> None:
        first = host.UNCERTAINTY_CODE_ORDER[0]
        invalid_arrays = (
            [{"code": "Unsupported", "description": "Detail"}],
            [{"code": first, "description": "Same"}] * 9,
            [{"code": first, "description": ""}],
            [
                {
                    "code": first,
                    "description": "x" * (host.MAX_DETAIL_TEXT + 1),
                }
            ],
        )
        for invalid in invalid_arrays:
            with self.subTest(invalid_length=len(invalid)):
                observation = copy.deepcopy(self.observation)
                observation["uncertainties"] = invalid
                self.assert_observation_rejected(observation)

    def test_missing_extra_and_invalid_enum_reject(self) -> None:
        mutations = []
        missing = copy.deepcopy(self.observation)
        del missing["conciseRationale"]
        mutations.append(missing)
        extra = copy.deepcopy(self.observation)
        extra["reasoning"] = "not allowed"
        mutations.append(extra)
        invalid_enum = copy.deepcopy(self.observation)
        invalid_enum["observableContentType"] = "Excitement"
        mutations.append(invalid_enum)

        for observation in mutations:
            with self.subTest(keys=tuple(observation.keys())):
                self.assert_observation_rejected(observation)

    def test_markdown_and_malformed_json_reject(self) -> None:
        valid_text = json.dumps(self.observation, ensure_ascii=False)
        for invalid in (f"```json\n{valid_text}\n```", '{"caseId":'):
            with self.subTest(invalid=invalid[:20]):
                with self.assertRaises(host.HostError):
                    host._parse_provider_observation(
                        invalid,
                        self.request,
                    )

    def test_evidence_intervals_sort_by_numeric_and_utf16_ordinal(self) -> None:
        later = {
            "startSeconds": 2,
            "endSeconds": 3,
            "description": "Later.",
        }
        longer = {
            "startSeconds": 1,
            "endSeconds": 3,
            "description": "Longer.",
        }
        astral = {
            "startSeconds": 1,
            "endSeconds": 2,
            "description": "\U00010000",
        }
        private_use = {
            "startSeconds": 1,
            "endSeconds": 2,
            "description": "\ue000",
        }
        observation = copy.deepcopy(self.observation)
        observation["evidenceIntervals"] = [
            later,
            private_use,
            longer,
            astral,
        ]

        output = self.parse(observation)

        self.assertEqual(
            [astral, private_use, longer, later],
            output["evidenceIntervals"],
        )
        audit = output["normalizationAudit"]
        self.assertEqual(
            ["EvidenceIntervalsCanonicalized"],
            audit["normalizationKinds"],
        )
        self.assertTrue(audit["evidenceIntervalOrderChanged"])
        self.assertEqual(4, audit["rawEvidenceIntervalCount"])
        self.assertEqual(4, audit["canonicalEvidenceIntervalCount"])
        self.assertEqual(
            0,
            audit["exactDuplicateEvidenceIntervalCount"],
        )

    def test_evidence_intervals_exact_numeric_duplicates_deduplicate(
        self,
    ) -> None:
        observation = copy.deepcopy(self.observation)
        observation["evidenceIntervals"] = [
            {
                "startSeconds": 1,
                "endSeconds": 2.0,
                "description": "Same.",
            },
            {
                "startSeconds": 1.0,
                "endSeconds": 2,
                "description": "Same.",
            },
        ]

        output = self.parse(observation)

        self.assertEqual(1, len(output["evidenceIntervals"]))
        audit = output["normalizationAudit"]
        self.assertEqual(2, audit["rawEvidenceIntervalCount"])
        self.assertEqual(1, audit["canonicalEvidenceIntervalCount"])
        self.assertEqual(
            1,
            audit["exactDuplicateEvidenceIntervalCount"],
        )
        self.assertFalse(audit["evidenceIntervalOrderChanged"])

    def test_evidence_only_normalization_hashes_raw_and_canonical_arrays(
        self,
    ) -> None:
        observation = copy.deepcopy(self.observation)
        later = {
            "startSeconds": 2,
            "endSeconds": 3,
            "description": "Later.",
        }
        earlier = {
            "startSeconds": 1,
            "endSeconds": 2,
            "description": "Earlier.",
        }
        observation["evidenceIntervals"] = [later, earlier]

        output = self.parse(observation)
        audit = output["normalizationAudit"]
        canonical_without_audit = dict(output)
        del canonical_without_audit["normalizationAudit"]
        del canonical_without_audit["identityBindingAudit"]
        raw_without_audit = dict(canonical_without_audit)
        raw_without_audit["evidenceIntervals"] = [
            {
                "startSeconds": 2.0,
                "endSeconds": 3.0,
                "description": "Later.",
            },
            {
                "startSeconds": 1.0,
                "endSeconds": 2.0,
                "description": "Earlier.",
            },
        ]

        self.assertEqual(
            host._canonical_json_sha256(raw_without_audit),
            audit["rawOutputSha256"],
        )
        self.assertEqual(
            host._canonical_json_sha256(canonical_without_audit),
            audit["canonicalOutputSha256"],
        )
        self.assertNotEqual(
            audit["rawOutputSha256"],
            audit["canonicalOutputSha256"],
        )

    def test_same_time_evidence_with_distinct_descriptions_remains(
        self,
    ) -> None:
        observation = copy.deepcopy(self.observation)
        observation["evidenceIntervals"] = [
            {
                "startSeconds": 1,
                "endSeconds": 2,
                "description": "Alpha.",
            },
            {
                "startSeconds": 1,
                "endSeconds": 2,
                "description": "Beta.",
            },
        ]

        output = self.parse(observation)

        self.assertEqual(
            observation["evidenceIntervals"],
            output["evidenceIntervals"],
        )
        self.assertIsNone(output["normalizationAudit"])

    def test_overlapping_touching_and_point_intervals_remain_distinct(
        self,
    ) -> None:
        intervals = [
            {
                "startSeconds": 1,
                "endSeconds": 3,
                "description": "Overlap one.",
            },
            {
                "startSeconds": 2,
                "endSeconds": 4,
                "description": "Overlap two.",
            },
            {
                "startSeconds": 4,
                "endSeconds": 5,
                "description": "Touching.",
            },
            {
                "startSeconds": 5,
                "endSeconds": 5,
                "description": "Point.",
            },
        ]
        observation = copy.deepcopy(self.observation)
        observation["evidenceIntervals"] = intervals

        output = self.parse(observation)

        self.assertEqual(intervals, output["evidenceIntervals"])
        self.assertIsNone(output["normalizationAudit"])

    def test_evidence_raw_count_rejects_before_deduplication(self) -> None:
        observation = copy.deepcopy(self.observation)
        observation["evidenceIntervals"] = [
            copy.deepcopy(self.observation["evidenceIntervals"][0])
            for _ in range(host.MAX_EVIDENCE_INTERVALS + 1)
        ]
        self.assert_observation_rejected(observation)

    def test_invalid_evidence_range_rejects(self) -> None:
        invalid_intervals = (
            {
                "startSeconds": -0.1,
                "endSeconds": 1,
                "description": "Negative.",
            },
            {
                "startSeconds": 2,
                "endSeconds": 1,
                "description": "Reversed.",
            },
            {
                "startSeconds": 9,
                "endSeconds": 11,
                "description": "Outside.",
            },
        )
        for invalid in invalid_intervals:
            observation = copy.deepcopy(self.observation)
            observation["evidenceIntervals"] = [invalid]
            with self.subTest(interval=invalid):
                self.assert_observation_rejected(observation)

    def test_invalid_evidence_description_rejects(self) -> None:
        for description in (
            "",
            " ",
            "x" * (host.MAX_DETAIL_TEXT + 1),
        ):
            observation = copy.deepcopy(self.observation)
            observation["evidenceIntervals"][0]["description"] = (
                description
            )
            with self.subTest(description_length=len(description)):
                self.assert_observation_rejected(observation)

    def test_evidence_timestamps_are_not_clamped_or_rounded(self) -> None:
        observation = copy.deepcopy(self.observation)
        observation["evidenceIntervals"] = [
            {
                "startSeconds": 0.12345678901234566,
                "endSeconds": 9.876543210987654,
                "description": "Precise binary64 timestamps.",
            }
        ]

        output = self.parse(observation)
        interval = output["evidenceIntervals"][0]

        self.assertEqual(
            observation["evidenceIntervals"][0]["startSeconds"],
            interval["startSeconds"],
        )
        self.assertEqual(
            observation["evidenceIntervals"][0]["endSeconds"],
            interval["endSeconds"],
        )
        self.assertIsNone(output["normalizationAudit"])

    def test_overlong_rationale_and_hidden_reasoning_reject(self) -> None:
        invalid_values = (
            "x" * (host.MAX_RATIONALE + 1),
            "My hidden reasoning should not be transported.",
        )
        for value in invalid_values:
            observation = copy.deepcopy(self.observation)
            observation["conciseRationale"] = value
            self.assert_observation_rejected(observation)

        observation = copy.deepcopy(self.observation)
        observation["thinking"] = "private scratchpad"
        self.assert_observation_rejected(observation)

    def test_normalization_audit_hashes_counts_and_semantics(self) -> None:
        observation = copy.deepcopy(self.observation)
        observation["evidenceIntervals"] = [
            {
                "startSeconds": 2,
                "endSeconds": 3,
                "description": "Later.",
            },
            {
                "startSeconds": 1,
                "endSeconds": 2,
                "description": "Earlier.",
            },
            {
                "startSeconds": 1.0,
                "endSeconds": 2.0,
                "description": "Earlier.",
            },
        ]
        observation["limitations"] = ["Zulu", "Alpha", "Alpha"]
        observation["uncertainties"] = [
            self.observation["uncertainties"][0],
            self.observation["uncertainties"][0],
        ]
        output = self.parse(observation)
        audit = output["normalizationAudit"]
        self.assertNotEqual(
            audit["rawOutputSha256"],
            audit["canonicalOutputSha256"],
        )
        self.assertEqual(3, audit["rawEvidenceIntervalCount"])
        self.assertEqual(2, audit["canonicalEvidenceIntervalCount"])
        self.assertEqual(
            1,
            audit["exactDuplicateEvidenceIntervalCount"],
        )
        self.assertTrue(audit["evidenceIntervalOrderChanged"])
        self.assertEqual(3, audit["rawLimitationCount"])
        self.assertEqual(2, audit["canonicalLimitationCount"])
        self.assertEqual(1, audit["exactDuplicateLimitationCount"])
        self.assertEqual(2, audit["rawUncertaintyCount"])
        self.assertEqual(1, audit["canonicalUncertaintyCount"])
        self.assertEqual(1, audit["exactDuplicateUncertaintyCount"])
        self.assertFalse(audit["semanticTextChanged"])
        self.assertEqual(
            [
                "EvidenceIntervalsCanonicalized",
                "LimitationsCanonicalized",
                "UncertaintiesCanonicalized",
            ],
            audit["normalizationKinds"],
        )

    def test_phase_a_classification_uses_shared_canonicalizer(self) -> None:
        observation = copy.deepcopy(self.observation)
        later = {
            "startSeconds": 2,
            "endSeconds": 3,
            "description": "Later.",
        }
        earlier = {
            "startSeconds": 1,
            "endSeconds": 2,
            "description": "Earlier.",
        }
        observation["evidenceIntervals"] = [
            later,
            earlier,
            copy.deepcopy(earlier),
        ]
        observation["limitations"] = ["Zulu", "Alpha", "Alpha"]
        observation["uncertainties"] = [
            {"code": host.UNCERTAINTY_CODE_ORDER[-1], "description": "Zulu"},
            {"code": host.UNCERTAINTY_CODE_ORDER[0], "description": "Alpha"},
            {"code": host.UNCERTAINTY_CODE_ORDER[0], "description": "Alpha"},
        ]
        invariants, failure = host._classify_provider_observation_for_audit(
            observation,
            self.request,
        )
        self.assertEqual(
            [
                "EvidenceIntervalsOutOfOrder",
                "ExactDuplicateEvidenceIntervals",
                "LimitationsOutOfOrder",
                "ExactDuplicateLimitations",
                "UncertaintiesOutOfOrder",
                "ExactDuplicateUncertainties",
            ],
            invariants,
        )
        self.assertIsNone(failure)

    def test_phase_a_invalid_evidence_is_other_schema_violation(
        self,
    ) -> None:
        invalid_observations: list[dict] = []
        blank = copy.deepcopy(self.observation)
        blank["evidenceIntervals"][0]["description"] = ""
        invalid_observations.append(blank)
        extra = copy.deepcopy(self.observation)
        extra["evidenceIntervals"][0]["unknown"] = "not allowed"
        invalid_observations.append(extra)

        for observation in invalid_observations:
            invariants, failure = (
                host._classify_provider_observation_for_audit(
                    observation,
                    self.request,
                )
            )
            with self.subTest(
                interval=observation["evidenceIntervals"][0]
            ):
                self.assertEqual(
                    ["OtherSchemaViolation"],
                    invariants,
                )
                self.assertIsNotNone(failure)

    def test_raw_audit_captures_raw_evidence_intervals(self) -> None:
        observation = copy.deepcopy(self.observation)
        later = {
            "startSeconds": 2,
            "endSeconds": 3,
            "description": "Later.",
        }
        earlier = {
            "startSeconds": 1,
            "endSeconds": 2,
            "description": "Earlier.",
        }
        observation["evidenceIntervals"] = [
            later,
            earlier,
            copy.deepcopy(earlier),
        ]
        raw_text = json.dumps(
            observation,
            ensure_ascii=False,
            separators=(",", ":"),
            allow_nan=False,
        )

        with (
            mock.patch.object(
                host,
                "_revalidate_media_inputs",
            ),
            mock.patch.object(
                host,
                "_write_json_atomic",
            ) as write_json,
            self.assertRaises(host.RawAuditCaptured),
        ):
            host._capture_provider_output_audit(
                raw_text,
                self.request,
                Path("raw-audit.json"),
                {"identity": "test"},
                1.25,
                {
                    "caseId": "case-1",
                    "candidateId": "candidate-1",
                    "caseOrdinal": 1,
                    "inputTokenCount": 10,
                    "generatedTokenCount": 2,
                    "maximumNewTokens":
                        host.LEGACY_DIAGNOSTIC_MAX_NEW_TOKENS,
                    "endOfSequenceTokenIds": [2],
                    "firstEndOfSequenceGeneratedIndex": 1,
                    "terminalTokenId": 2,
                    "terminationReason": "EndOfSequence",
                    "generatedTokenIdsSha256": "a" * 64,
                    "legacyPrefixTokenCount": 2,
                    "legacyPrefixTokenIdsSha256": "a" * 64,
                    "decodedTextSha256":
                        hashlib.sha256(
                            raw_text.encode("utf-8")
                        ).hexdigest(),
                    "decodedTextUtf8ByteCount":
                        len(raw_text.encode("utf-8")),
                },
            )

        payload = write_json.call_args.args[1]
        self.assertEqual(
            observation["evidenceIntervals"],
            payload["rawEvidenceIntervals"],
        )
        self.assertEqual(
            [
                "EvidenceIntervalsOutOfOrder",
                "ExactDuplicateEvidenceIntervals",
            ],
            payload["failedInvariants"],
        )
        self.assertIsNone(payload["strictValidationFailure"])
        self.assertEqual(
            "visual-semantic-raw-output-audit-1.2",
            payload["schemaVersion"],
        )

    def test_utf16_ordinal_order_matches_dotnet(self) -> None:
        self.assertLess(
            host._ordinal_string_key("\ud7ff"),
            host._ordinal_string_key("\U00010000"),
        )
        self.assertLess(
            host._ordinal_string_key("\U00010000"),
            host._ordinal_string_key("\ue000"),
        )
        self.assertNotEqual(
            host._ordinal_string_key("\u00e9"),
            host._ordinal_string_key("e\u0301"),
        )

    def test_frozen_schema_prompt_and_policy_constants(self) -> None:
        self.assertEqual("0.5A.9", host.HOST_VERSION)
        self.assertEqual(
            "visual-semantic-observation-1.0",
            host.OBSERVATION_SCHEMA,
        )
        self.assertEqual(
            "visual-semantic-observation-batch-1.5",
            host.OUTPUT_SCHEMA,
        )
        self.assertEqual(
            "visual-semantic-raw-output-audit-1.2",
            host.RAW_OUTPUT_AUDIT_SCHEMA,
        )
        self.assertEqual(
            "18c738c006b638e770ee0e69efafe43770939ae3528d79220ef253679564e8c9",
            host.PROMPT_SHA256,
        )
        self.assertEqual(host.PROMPT_SHA256, host._prompt_source()[1])
        self.assertEqual(
            host.NORMALIZATION_POLICY_SHA256,
            host._normalization_policy_source()[1],
        )
        self.assertEqual(
            "51a3d6b67ca18546b38aa4c63d698bd1f499fc2d7330bf9090c83dfa429c98d8",
            host.NORMALIZATION_POLICY_SHA256,
        )
        self.assertEqual(
            "visual-semantic-provider-attempt-batch-1.0",
            host.ATTEMPT_SCHEMA,
        )
        self.assertEqual(
            "3512b5e94caaa50f8eb6d241d02048a02424ebb078076489fe84599349b309c6",
            host.IDENTITY_BINDING_POLICY_SHA256,
        )
        self.assertEqual(
            host.IDENTITY_BINDING_POLICY_SHA256,
            host._identity_binding_policy_source()[1],
        )
        policy_1_0_path = (
            host.HOST_DIRECTORY /
            (
                "replayfoundry-visual-semantic-output-normalization-policy-1.0.txt"
            )
        )
        self.assertEqual(
            "4653736ac153561cc3d91764a4d30fd93d0e1e1f154d1b0715c1d6a498c3c777",
            hashlib.sha256(policy_1_0_path.read_bytes()).hexdigest(),
        )

    def test_repository_root_is_discovered_from_source_marker(self) -> None:
        with TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "ReplayFoundry.slnx").write_text("<Solution />", encoding="utf-8")
            host_path = root / "eng" / "visual-semantic-host" / "qwen3_vl_batch_host.py"
            host_path.parent.mkdir(parents=True)
            host_path.write_text("", encoding="utf-8")
            with mock.patch.object(path_policy, "HOST_ENTRY_PATH", host_path):
                self.assertEqual(root.resolve(), path_policy._repository_root())

    def test_packaged_host_has_no_fabricated_repository_root(self) -> None:
        with TemporaryDirectory() as temporary:
            host_path = Path(temporary) / "host" / "qwen3_vl_batch_host.py"
            host_path.parent.mkdir(parents=True)
            host_path.write_text("", encoding="utf-8")
            with mock.patch.object(path_policy, "HOST_ENTRY_PATH", host_path):
                self.assertIsNone(path_policy._repository_root())


if __name__ == "__main__":
    unittest.main(verbosity=2)
