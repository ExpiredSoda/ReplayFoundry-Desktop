"""Model-free checks for shared grounded-metadata fact packets."""
from __future__ import annotations

import copy
from dataclasses import FrozenInstanceError
from datetime import datetime, timezone
from decimal import Decimal
import hashlib
import json
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from replayfoundry_visual_semantic.editorial import grounded_metadata_command
from replayfoundry_visual_semantic.editorial import grounded_metadata_pipeline
from replayfoundry_visual_semantic.editorial.grounded_metadata_pipeline import (
    GROUNDING_PACKET_SCHEMA_VERSION,
    _grounding_reuse_identity,
    _new_grounding_packet,
)


def _request(attempt: int, intent: str) -> dict:
    return {
        "candidateId": "candidate-shared",
        "attempt": attempt,
        "game": {
            "name": "Example Game",
            "hashtag": "#ExampleGame",
            "source": "UserConfirmed",
            "notes": "User supplied context.",
        },
        "gameKnowledge": None,
        "visualText": {
            "provider": "WindowsMediaOcr",
            "groundingAnchors": [
                {"text": "OBJECTIVE UPDATED", "occurrenceCount": 2}
            ],
        },
        "clip": {
            "startSeconds": 10.0,
            "endSeconds": 30.0,
            "sourceDurationSeconds": 200.0,
            "deterministicScore": 82.0,
            "deterministicReason": "Bounded evidence.",
        },
        "transcripts": [],
        "evidence": [
            {
                "id": "visual-1",
                "kind": "VisualObservation",
                "description": "A visible object changed state.",
            }
        ],
        "profile": {
            "audienceAddress": "Viewers",
            "namingGuidance": None,
            "reusableDescriptionSignature": None,
            "defaultTags": ["gaming"],
            "voicePerspective": "CreatorFirstPerson",
            "variantIntent": intent,
        },
        "_validated": {
            "videoPath": Path("A:/external/review.mp4"),
            "videoDuration": 20.0,
            "expectedVideoHash": "a" * 64,
            "expectedVideoLength": 1200,
            "expectedLastWriteUtc": datetime(
                2026, 8, 10, 12, 0, tzinfo=timezone.utc
            ),
            "sourceAbsoluteOffset": 0,
            "candidateStart": 0,
            "candidateEnd": 20.0,
        },
    }


class GroundedMetadataPacketReuseTests(unittest.TestCase):
    def test_38_second_grounding_builds_four_drafts_and_selects_one_event(self) -> None:
        request = _request(0, "DirectAction")
        request["_validated"]["videoDuration"] = 38.0
        request["_validated"]["candidateEnd"] = 38.0
        request["clip"]["endSeconds"] = 48.0
        generated_visual_drafts = 0

        class Session:
            @staticmethod
            def compile_json_schema(*_args, **_kwargs):
                return object(), object()

        def generate(*args):
            nonlocal generated_visual_drafts
            messages = args[2]
            first_content = messages[1]["content"][0]
            trace = SimpleNamespace(generated_token_count=12)
            if first_content["type"] == "video":
                generated_visual_drafts += 1
                return (
                    {
                        "environment": "A visible gameplay area",
                        "environmentUncertain": False,
                        "subjectsAndObjects": ["A visible object"],
                        "actions": ["The object changed"],
                        "readableText": [],
                        "uncertainties": [],
                    },
                    trace,
                    None,
                    str(generated_visual_drafts) * 64,
                    {
                        "tensorShape": [1, 8, 3, 352, 640],
                        "frameCount": 8,
                    },
                    None,
                    None,
                )
            assessments = [
                {
                    "ordinal": ordinal,
                    "distinctAction": ordinal == 2,
                    "objectInteraction": ordinal == 2,
                    "visibleOutcome": False,
                    "readableInterfaceChange": False,
                    "routineOnly": ordinal != 2,
                    "uncertain": False,
                    "actorAuthority": "Unknown",
                    "creatorExperienceRelation": "Unestablished",
                }
                for ordinal in range(1, 5)
            ]
            return (
                {
                    "primaryVisualDraftOrdinal": 2,
                    "assessments": assessments,
                },
                trace,
                None,
                "f" * 64,
                None,
                None,
                None,
            )

        with patch.object(
            grounded_metadata_pipeline,
            "_generate_json_once",
            side_effect=generate,
        ):
            packet = grounded_metadata_pipeline._build_grounding_packet(
                request,
                1,
                None,
                None,
                None,
                None,
                None,
                Session(),
            )

        facts = packet.materialize_facts()
        self.assertEqual(4, generated_visual_drafts)
        self.assertEqual(4, len(facts["visualDrafts"]))
        self.assertEqual(4, len(facts["visualEventSelectionAssessments"]))
        self.assertEqual(2, facts["primaryVisualDraftOrdinal"])
        self.assertEqual(
            "grounded-editorial-visual-event-selection-json-schema-1.2",
            facts["visualEventSelectionSchemaVersion"],
        )
        self.assertEqual(5, packet.grounding_pass_count)

    def test_attempt_and_variant_intent_do_not_change_shared_fact_identity(self) -> None:
        identities = [
            _grounding_reuse_identity(_request(attempt, intent))[0]
            for attempt, intent in enumerate(
                [
                    "DirectAction",
                    "SpecificCuriosity",
                    "OutcomeFocused",
                    "ConcreteDetail",
                ]
            )
        ]

        self.assertEqual(1, len(set(identities)))

    def test_review_and_context_decimals_remain_exact_json_numbers(self) -> None:
        request = _request(0, "DirectAction")
        request["_validated"]["videoDuration"] = Decimal("20.1250000000000000001")
        request["_validated"]["candidateStart"] = Decimal("0.0000000000000000001")
        request["_validated"]["candidateEnd"] = Decimal("20.1250000000000000001")
        request["clip"]["startSeconds"] = Decimal("10.1250000000000000001")

        _, canonical = _grounding_reuse_identity(request)
        packet = _new_grounding_packet(
            request,
            1,
            0.1,
            {"contextSecond": Decimal("10.1250000000000000001")},
        )

        self.assertIn('"durationSeconds":20.1250000000000000001', canonical)
        self.assertIn('"startSeconds":10.1250000000000000001', canonical)
        self.assertIn(
            '"contextSecond":10.1250000000000000001',
            packet.canonical_facts,
        )
        self.assertNotIn('"20.1250000000000000001"', canonical)

    def test_every_other_factual_or_profile_change_prevents_reuse(self) -> None:
        original = _request(0, "DirectAction")
        original_identity, _ = _grounding_reuse_identity(original)
        mutations = []
        changed = copy.deepcopy(original)
        changed["_validated"]["videoPath"] = Path("A:/external/other.mp4")
        mutations.append(changed)
        changed = copy.deepcopy(original)
        changed["_validated"]["expectedVideoHash"] = "b" * 64
        mutations.append(changed)
        changed = copy.deepcopy(original)
        changed["game"]["name"] = "Different Game"
        mutations.append(changed)
        changed = copy.deepcopy(original)
        changed["evidence"][0]["description"] = "Different evidence."
        mutations.append(changed)
        changed = copy.deepcopy(original)
        changed["profile"]["namingGuidance"] = "Different guidance."
        mutations.append(changed)

        self.assertTrue(all(
            _grounding_reuse_identity(value)[0] != original_identity
            for value in mutations
        ))

    def test_packet_is_frozen_and_materializes_fresh_fact_snapshots(self) -> None:
        request = _request(0, "DirectAction")
        facts = {"visualDrafts": [{"actions": ["A door opened."]}]}
        packet = _new_grounding_packet(request, 2, 0.25, facts)
        identical = _new_grounding_packet(
            copy.deepcopy(request),
            2,
            99.0,
            copy.deepcopy(facts),
        )
        changed = _new_grounding_packet(
            request,
            2,
            0.25,
            {"visualDrafts": [{"actions": ["A window opened."]}]},
        )
        first = packet.materialize_facts()
        first["visualDrafts"][0]["actions"][0] = "Mutated"

        self.assertEqual(GROUNDING_PACKET_SCHEMA_VERSION, packet.schema_version)
        self.assertEqual(packet.fact_sha256, identical.fact_sha256)
        self.assertNotEqual(packet.fact_sha256, changed.fact_sha256)
        self.assertEqual(
            "A door opened.",
            packet.materialize_facts()["visualDrafts"][0]["actions"][0],
        )
        with self.assertRaises(FrozenInstanceError):
            packet.source_attempt = 7  # type: ignore[misc]

    def test_four_rerolls_build_grounding_once_and_keep_each_intent(self) -> None:
        requests = [
            _request(attempt, intent)
            for attempt, intent in enumerate(
                [
                    "DirectAction",
                    "SpecificCuriosity",
                    "OutcomeFocused",
                    "ConcreteDetail",
                ]
            )
        ]
        built_packets = []
        synthesis_calls = []

        def build(request, *_args):
            packet = _new_grounding_packet(
                request,
                3,
                0.5,
                {"visualDrafts": [{"actions": ["A door opened."]}]},
            )
            built_packets.append(packet)
            return packet

        def synthesize(request, _ordinal, _prompt, packet, reused, *_args):
            prior_titles = _args[-1]
            synthesis_calls.append(
                (
                    request["attempt"],
                    request["profile"]["variantIntent"],
                    packet,
                    reused,
                    prior_titles,
                )
            )
            return {
                "attempt": request["attempt"],
                "reused": reused,
                "metadata": {
                    "title":
                        f"Opened supported object angle {request['attempt']} "
                        "#ExampleGame"
                },
            }

        with (
            patch.object(grounded_metadata_command, "_set_failure_case"),
            patch.object(grounded_metadata_command, "_set_failure_stage"),
            patch.object(
                grounded_metadata_command,
                "_build_grounding_packet",
                side_effect=build,
            ),
            patch.object(
                grounded_metadata_command,
                "_synthesize_case",
                side_effect=synthesize,
            ),
            patch.object(
                grounded_metadata_command,
                "grounded_case_watchdog_success_payload",
                return_value={"generationInvocationCount": 1},
            ),
        ):
            results = grounded_metadata_command._infer_grouped_requests(
                requests,
                [str(index) * 64 for index in range(1, 5)],
                "prompt",
                None,
                None,
                None,
                None,
                None,
                None,
            )

        self.assertEqual(1, len(built_packets))
        self.assertEqual([False, True, True, True], [item[3] for item in synthesis_calls])
        self.assertTrue(all(item[2] is built_packets[0] for item in synthesis_calls))
        self.assertEqual(
            [0, 1, 2, 3],
            [len(item[4]) for item in synthesis_calls],
        )
        self.assertEqual(
            [
                "DirectAction",
                "SpecificCuriosity",
                "OutcomeFocused",
                "ConcreteDetail",
            ],
            [item[1] for item in synthesis_calls],
        )
        self.assertEqual([0, 1, 2, 3], [item["attempt"] for item in results])

    def test_changed_evidence_builds_a_separate_packet(self) -> None:
        first = _request(0, "DirectAction")
        second = _request(1, "SpecificCuriosity")
        second["evidence"][0]["description"] = "Different evidence."
        build_count = 0

        def build(request, *_args):
            nonlocal build_count
            build_count += 1
            return _new_grounding_packet(
                request,
                1,
                0.1,
                {"visualDrafts": [{"ordinal": build_count}]},
            )

        with (
            patch.object(grounded_metadata_command, "_set_failure_case"),
            patch.object(grounded_metadata_command, "_set_failure_stage"),
            patch.object(
                grounded_metadata_command,
                "_build_grounding_packet",
                side_effect=build,
            ),
            patch.object(
                grounded_metadata_command,
                "_synthesize_case",
                side_effect=lambda request, *_args: {
                    "attempt": request["attempt"],
                    "metadata": {
                        "title":
                            f"Opened supported object angle {request['attempt']} "
                            "#ExampleGame"
                    },
                },
            ),
            patch.object(
                grounded_metadata_command,
                "grounded_case_watchdog_success_payload",
                return_value={"generationInvocationCount": 1},
            ),
        ):
            grounded_metadata_command._infer_grouped_requests(
                [first, second],
                ["1" * 64, "2" * 64],
                "prompt",
                None,
                None,
                None,
                None,
                None,
                None,
            )

        self.assertEqual(2, build_count)

    def test_synthesis_reports_physical_reuse_and_exact_packet_hashes(self) -> None:
        source = _request(0, "DirectAction")
        reroll = _request(1, "SpecificCuriosity")
        visual_draft = {
            "environment": "An interior",
            "environmentUncertain": False,
            "subjectsAndObjects": ["A visible door"],
            "actions": ["A door opened"],
            "readableText": [],
            "uncertainties": [],
        }
        packet = _new_grounding_packet(
            source,
            2,
            0.5,
            {
                "visualDrafts": [visual_draft],
                "visualDraftRecords": [
                    {
                        "ordinal": 1,
                        "startSeconds": 0.0,
                        "endSeconds": 20.0,
                        **visual_draft,
                    }
                ],
                "stableReadableText": [],
                "visualEventSelectionApplied": False,
                "actorAuthorityAssessmentApplied": True,
                "primaryVisualDraftOrdinal": 1,
                "primaryActorAuthority": "CreatorControlled",
                "primaryCreatorExperienceRelation": "CreatorActed",
                "visualEventSelectionAssessments": [{
                    "ordinal": 1,
                    "distinctAction": True,
                    "objectInteraction": True,
                    "visibleOutcome": False,
                    "readableInterfaceChange": False,
                    "routineOnly": False,
                    "uncertain": False,
                    "actorAuthority": "CreatorControlled",
                    "creatorExperienceRelation": "CreatorActed",
                }],
                "knowledgeSelectionApplied": False,
                "selectedCurrentPassageId": "None",
                "knowledgeSelectionAssessments": [],
            },
        )

        class Session:
            @staticmethod
            def compile_json_schema(*_args, **_kwargs):
                return object(), object()

        class Audit:
            @staticmethod
            def to_json():
                return {"strictParserAccepted": True}

        generated = (
            {
                "title": "Opened the visible door #ExampleGame",
                "description": "I opened the visible door.",
                "tags": ["door"],
                "grounding": [],
            },
            SimpleNamespace(
                generated_token_count=20,
                maximum_new_tokens=768,
                termination_reason="EndOfSequence",
                first_eos_generated_index=19,
            ),
            Audit(),
            "b" * 64,
            None,
            (
                '{"description":"I opened the visible door.","grounding":[],'
                '"tags":["door"],"temporalVoice":"RetrospectivePast",'
                '"titleBody":"Opened the visible door"}'
            ),
        )
        def generate_with_attestation(*args, **kwargs):
            context = kwargs["synthesis_attestation_context"]
            canonical_messages = json.dumps(
                args[2],
                ensure_ascii=False,
                sort_keys=True,
                separators=(",", ":"),
            ).encode("utf-8")
            attestation = {
                **context,
                "canonicalMessagesSha256":
                    hashlib.sha256(canonical_messages).hexdigest(),
                "renderedPromptSha256":
                    hashlib.sha256(canonical_messages).hexdigest(),
                "renderedPromptUtf8ByteCount": len(canonical_messages),
                "inputTokenIdsSha256": "c" * 64,
                "inputTokenCount": 100,
                "outputSha256": "b" * 64,
                "completedJsonSha256": hashlib.sha256(
                    generated[5].encode("utf-8")
                ).hexdigest(),
                "rejectionCode": None,
                "accepted": False,
            }
            return (*generated, attestation)

        with patch.object(
            grounded_metadata_pipeline,
            "_generate_json_once",
            side_effect=generate_with_attestation,
        ):
            first = grounded_metadata_pipeline._synthesize_case(
                source,
                1,
                "prompt",
                packet,
                False,
                None,
                None,
                None,
                None,
                None,
                Session(),
            )
            reused = grounded_metadata_pipeline._synthesize_case(
                reroll,
                2,
                "prompt",
                packet,
                True,
                None,
                None,
                None,
                None,
                None,
                Session(),
            )

        self.assertEqual(3, first["generation"]["generationPassCount"])
        self.assertEqual(1, reused["generation"]["generationPassCount"])
        self.assertEqual(2, reused["generation"]["groundingPassCount"])
        self.assertEqual(1, reused["generation"]["synthesisPassCount"])
        self.assertTrue(
            reused["generation"]["actorAuthorityAssessmentApplied"]
        )
        self.assertEqual(
            "CreatorControlled",
            reused["generation"]["primaryActorAuthority"],
        )
        self.assertEqual(
            "CreatorActed",
            reused["generation"]["primaryCreatorExperienceRelation"],
        )
        self.assertTrue(reused["generation"]["groundingPacketReused"])
        self.assertEqual(
            packet.fact_sha256,
            reused["generation"]["groundingPacketFactSha256"],
        )
        self.assertEqual(0, reused["generation"]["groundingPacketSourceAttempt"])


if __name__ == "__main__":
    unittest.main()
