#!/usr/bin/env python3
"""Model-free tests for the frozen Prompt 2.3 editorial contract."""

from __future__ import annotations

import copy
import hashlib
import json
import sys
import unittest
from decimal import Decimal
from pathlib import Path


HOST_DIRECTORY = Path(__file__).resolve().parent.parent
if str(HOST_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(HOST_DIRECTORY))

from replayfoundry_visual_semantic.editorial.contract import (
    EditorialContractError,
    parse_and_canonicalize_editorial_output,
)
from replayfoundry_visual_semantic.editorial import protocol
from replayfoundry_visual_semantic.editorial import pilot_protocol


def _keep() -> dict:
    return {
        "observableContentType": "Action",
        "hasDistinctEvent": "Yes",
        "hasObservablePayoff": "Yes",
        "routineTraversalOrMenuOnly": "No",
        "candidateRequiresMissingContext": "No",
        "candidateContainsOnlyAmbientChange": "No",
        "transcriptContextSupport": "NotSupplied",
        "observedChanges": [
            {
                "description": "A visible action begins and resolves.",
                "evidenceBasis": "Visual",
                "evidenceIntervalIds": ["e1"],
            }
        ],
        "evidenceIntervals": [
            {
                "id": "e1",
                "startSeconds": 5.125,
                "endSeconds": 7.875,
                "description": "The visible action changes and resolves.",
                "evidenceBasis": "Visual",
            }
        ],
        "uncertaintyReasons": [],
        "editorialDisposition": "Keep",
        "rejectReason": "None",
        "dispositionRationale": (
            "The distinct visible action has an in-context payoff."
        ),
    }


def _parse(value: dict):
    return parse_and_canonicalize_editorial_output(
        json.dumps(value, separators=(",", ":"), ensure_ascii=False),
        review_duration_seconds=20,
        candidate_start_seconds=4,
        candidate_end_seconds=12,
    )


class EditorialContractTests(unittest.TestCase):
    def test_expanded_pilot_is_fixed_and_label_blind(self) -> None:
        self.assertEqual(
            "visual-semantic-prompt2-contract-pilot-1.2",
            pilot_protocol.PILOT_POLICY_VERSION,
        )
        self.assertEqual(8, len(pilot_protocol.PILOT_EXPANDED))
        self.assertEqual(
            len(pilot_protocol.PILOT_EXPANDED),
            len(set(pilot_protocol.PILOT_EXPANDED)),
        )

    def test_prompt_27_freezes_the_observation_only_wire_contract(
        self,
    ) -> None:
        path = (
            HOST_DIRECTORY
            / "replayfoundry-visual-semantic-editorial-prompt-2.7.txt"
        )
        raw = path.read_bytes()
        text = raw.decode("utf-8").replace("\r\n", "\n").replace(
            "\r", "\n"
        ).strip()

        self.assertEqual("2.7", protocol.PROMPT_VERSION)
        self.assertEqual(
            protocol.PROMPT_FILE_SHA256,
            hashlib.sha256(raw).hexdigest(),
        )
        self.assertEqual(
            protocol.PROMPT_SHA256,
            hashlib.sha256(text.encode("utf-8")).hexdigest(),
        )
        self.assertIn("Return exactly one compact JSON object", text)
        self.assertIn(
            "The exact keys are t, v, x, e in that order.",
            text,
        )
        self.assertIn(
            "ReplayFoundry derives disposition deterministically",
            text,
        )
        self.assertIn(
            "Use U only when sampled evidence cannot establish Yes or No.",
            text,
        )
        self.assertIn(
            "do not output a decision, reason, prose",
            text,
        )

    def test_compact_wire_expands_without_semantic_repair(self) -> None:
        source = '{"t":"A","v":["Y","Y","N","N","N"],"x":"N","e":[["e0",5.125,"V"]]}'
        result, audit = parse_and_canonicalize_editorial_output(
            source,
            review_duration_seconds=20,
            candidate_start_seconds=4,
            candidate_end_seconds=12,
        )
        self.assertEqual("Action", result["observableContentType"])
        self.assertEqual("Keep", result["editorialDisposition"])
        self.assertEqual(Decimal("5.125"), result["evidenceIntervals"][0]["startSeconds"])
        self.assertEqual(
            "visual-semantic-editorial-wire-1.1",
            audit["wireRepresentationVersion"],
        )
        self.assertEqual(1, audit["schemaShapeCanonicalizationCount"])
        self.assertEqual(0, audit["semanticRepairCount"])

    def test_valid_keep_is_model_free_and_needs_no_repair(self) -> None:
        result, audit = _parse(_keep())
        self.assertEqual("Keep", result["editorialDisposition"])
        self.assertEqual(0, audit["semanticRepairCount"])
        self.assertEqual(0, audit["schemaShapeCanonicalizationCount"])
        self.assertIsNone(audit["wireRepresentationVersion"])
        self.assertFalse(audit["outerWhitespaceTrimmed"])

    def test_outer_json_whitespace_is_audited_but_wrappers_reject(
        self,
    ) -> None:
        source = json.dumps(_keep(), separators=(",", ":"))
        result, audit = parse_and_canonicalize_editorial_output(
            "\n  " + source + "\r\n",
            review_duration_seconds=20,
            candidate_start_seconds=4,
            candidate_end_seconds=12,
        )
        self.assertEqual("Keep", result["editorialDisposition"])
        self.assertTrue(audit["outerWhitespaceTrimmed"])
        self.assertEqual(1, audit["syntacticCanonicalizationCount"])

        for wrapped in (f"```json\n{source}\n```", f"result: {source}"):
            with self.subTest(wrapped=wrapped[:8]):
                with self.assertRaises(EditorialContractError):
                    parse_and_canonicalize_editorial_output(
                        wrapped,
                        review_duration_seconds=20,
                        candidate_start_seconds=4,
                        candidate_end_seconds=12,
                    )

    def test_unknown_missing_and_duplicate_properties_reject(self) -> None:
        extra = _keep()
        extra["score"] = 0.9
        with self.assertRaises(EditorialContractError):
            _parse(extra)

        missing = _keep()
        del missing["hasDistinctEvent"]
        with self.assertRaises(EditorialContractError):
            _parse(missing)

        text = json.dumps(_keep())
        text = text.replace(
            '"hasDistinctEvent": "Yes"',
            '"hasDistinctEvent": "Yes", "hasDistinctEvent": "No"',
        )
        with self.assertRaises(EditorialContractError):
            parse_and_canonicalize_editorial_output(
                text,
                review_duration_seconds=20,
                candidate_start_seconds=4,
                candidate_end_seconds=12,
            )

    def test_keep_rejects_transcript_only_and_missing_context(self) -> None:
        transcript = _keep()
        transcript["transcriptContextSupport"] = "Supports"
        transcript["observedChanges"][0]["evidenceBasis"] = (
            "TranscriptContext"
        )
        transcript["evidenceIntervals"][0]["evidenceBasis"] = (
            "TranscriptContext"
        )
        with self.assertRaises(EditorialContractError):
            _parse(transcript)

        missing = _keep()
        missing["candidateRequiresMissingContext"] = "Yes"
        with self.assertRaises(EditorialContractError):
            _parse(missing)

    def test_each_highest_priority_reject_reason_is_valid(self) -> None:
        variants = {
            "RoutineTraversal": lambda value: value.update(
                routineTraversalOrMenuOnly="Yes"
            ),
            "AmbientChangeOnly": lambda value: value.update(
                candidateContainsOnlyAmbientChange="Yes"
            ),
            "NoDistinctEvent": lambda value: value.update(
                hasDistinctEvent="No"
            ),
            "NoObservablePayoff": lambda value: value.update(
                hasObservablePayoff="No"
            ),
            "MissingRequiredContext": lambda value: value.update(
                candidateRequiresMissingContext="Yes"
            ),
        }
        for reason, establish in variants.items():
            with self.subTest(reason=reason):
                value = _keep()
                value["editorialDisposition"] = "Reject"
                value["rejectReason"] = reason
                establish(value)
                _parse(value)

        menu = _keep()
        menu.update(
            editorialDisposition="Reject",
            rejectReason="MenuOrInventoryOnly",
            routineTraversalOrMenuOnly="Yes",
            observableContentType="MenuOrTraversal",
        )
        menu["observedChanges"][0]["description"] = (
            "The inventory menu remains on screen."
        )
        _parse(menu)

    def test_unsure_requires_real_ambiguity(self) -> None:
        value = _keep()
        value.update(
            editorialDisposition="Unsure",
            rejectReason="InsufficientEvidence",
            hasDistinctEvent="Unsure",
            uncertaintyReasons=[
                {
                    "code": "AmbiguousEventBoundary",
                    "description": "The visible boundary is obscured.",
                }
            ],
        )
        _parse(value)

        value["hasDistinctEvent"] = "Yes"
        value["uncertaintyReasons"] = []
        with self.assertRaises(EditorialContractError):
            _parse(value)

        all_keep = _keep()
        all_keep.update(
            editorialDisposition="Unsure",
            rejectReason="InsufficientEvidence",
            transcriptContextSupport="UnreliableOrAmbiguous",
            uncertaintyReasons=[
                {
                    "code": "TranscriptMayBeInaccurate",
                    "description": (
                        "Approximate transcript context is ambiguous."
                    ),
                }
            ],
        )
        with self.assertRaises(EditorialContractError):
            _parse(all_keep)

    def test_canonicalization_only_orders_and_deduplicates_exact_items(self) -> None:
        value = _keep()
        value["evidenceIntervals"].append(
            copy.deepcopy(value["evidenceIntervals"][0])
        )
        value["observedChanges"].append(
            copy.deepcopy(value["observedChanges"][0])
        )
        result, audit = _parse(value)
        self.assertEqual(1, len(result["evidenceIntervals"]))
        self.assertEqual(1, len(result["observedChanges"]))
        self.assertGreater(audit["syntacticCanonicalizationCount"], 0)
        self.assertEqual(Decimal("5.125"), result["evidenceIntervals"][0]["startSeconds"])

    def test_distinct_overlaps_and_exact_timestamps_are_preserved(self) -> None:
        value = _keep()
        value["evidenceIntervals"].append(
            {
                "id": "e2",
                "startSeconds": 4.125,
                "endSeconds": 7.875,
                "description": "A distinct overlapping visual change.",
                "evidenceBasis": "Visual",
            }
        )
        result, _ = _parse(value)
        self.assertEqual(2, len(result["evidenceIntervals"]))
        self.assertEqual(
            Decimal("4.125"),
            result["evidenceIntervals"][0]["startSeconds"],
        )

    def test_timestamp_precision_and_bounds_are_strict(self) -> None:
        exponent_text = json.dumps(_keep(), separators=(",", ":")).replace(
            '"startSeconds":5.125',
            '"startSeconds":5.125e0',
        )
        exponent, _ = parse_and_canonicalize_editorial_output(
            exponent_text,
            review_duration_seconds=20,
            candidate_start_seconds=4,
            candidate_end_seconds=12,
        )
        self.assertEqual(
            Decimal("5.125"),
            exponent["evidenceIntervals"][0]["startSeconds"],
        )

        precision = _keep()
        precision["evidenceIntervals"][0]["startSeconds"] = 1.2345
        with self.assertRaises(EditorialContractError):
            _parse(precision)

        outside = _keep()
        outside["evidenceIntervals"][0]["endSeconds"] = 21
        with self.assertRaises(EditorialContractError):
            _parse(outside)


if __name__ == "__main__":
    unittest.main()
