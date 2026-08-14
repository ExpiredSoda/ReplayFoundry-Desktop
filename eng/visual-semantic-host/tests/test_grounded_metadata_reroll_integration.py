"""Model-free integration checks for grounded editorial reroll validation."""
from __future__ import annotations

from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from replayfoundry_visual_semantic import failure_envelope, failure_state
from replayfoundry_visual_semantic.editorial import grounded_metadata_pipeline
from replayfoundry_visual_semantic.editorial import grounded_metadata_contract
from replayfoundry_visual_semantic.editorial.grounded_metadata_pipeline import (
    _grounding_reuse_identity,
    _new_grounding_packet,
    _reroll_title_reference,
    _synthesize_case,
)
from replayfoundry_visual_semantic.editorial.grounded_metadata_generation import (
    MAXIMUM_REJECTED_JSON_UTF8_BYTES,
    _bounded_completed_json,
)
from replayfoundry_visual_semantic.editorial.grounded_metadata_reroll_similarity import (
    REROLL_DIVERSITY_POLICY_VERSION,
    normalize_terminal_single_period_title_body,
)
from replayfoundry_visual_semantic.editorial.grounded_metadata_synthesis import (
    _metadata_messages,
    _variant_intent_guidance,
)
from replayfoundry_visual_semantic.editorial.grounded_metadata_synthesis_decoding import (
    POLICY_SHA256 as SYNTHESIS_DECODING_POLICY_SHA256,
    POLICY_VERSION as SYNTHESIS_DECODING_POLICY_VERSION,
    SOURCE_REASON_CREATOR_AUTHORITY_REJECTED_COPY_WITHHELD,
    SOURCE_REASON_CROSS_DRAFT_REJECTED_COPY_WITHHELD,
    SOURCE_REASON_ORIGINAL_FIRST_REJECTED,
    SOURCE_REASON_PRIMARY_ONLY_CROSS_DRAFT_COPY_WITHHELD,
    SYNTHESIS_RECOVERY_POOL_DECODINGS,
)
from replayfoundry_visual_semantic.errors import (
    GenerationWallClockBudgetExceededError,
    InferenceError,
)
from replayfoundry_visual_semantic.commands import UsageOrInputError


def _request(attempt: int, intent: str) -> dict:
    return {
        "candidateId": "candidate-shared",
        "attempt": attempt,
        "priorAcceptedTitles": [],
        "game": {
            "name": "Example Game",
            "hashtag": "#ExampleGame",
            "source": "UserConfirmed",
            "notes": None,
        },
        "gameKnowledge": None,
        "visualText": None,
        "clip": {
            "startSeconds": 10.0,
            "endSeconds": 30.0,
            "sourceDurationSeconds": 200.0,
            "deterministicScore": 82.0,
            "deterministicReason": "Bounded evidence.",
        },
        "transcripts": [],
        "evidence": [],
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


def _visual_draft() -> dict:
    return {
        "environment": "An interior",
        "environmentUncertain": False,
        "subjectsAndObjects": ["A visible door"],
        "actions": ["A door opened"],
        "readableText": [],
        "uncertainties": [],
    }


def _packet(
    source: dict,
    draft: dict | None = None,
    actor_authority: str = "CreatorControlled",
    creator_experience_relation: str = "CreatorActed",
):
    draft = draft or _visual_draft()
    return _new_grounding_packet(
        source,
        2,
        0.5,
        {
            "visualDrafts": [draft],
            "visualDraftRecords": [
                {
                    "ordinal": 1,
                    "startSeconds": 0.0,
                    "endSeconds": 20.0,
                    **draft,
                }
            ],
            "stableReadableText": [],
            "visualEventSelectionApplied": False,
            "actorAuthorityAssessmentApplied": True,
            "primaryVisualDraftOrdinal": 1,
            "primaryActorAuthority": actor_authority,
            "primaryCreatorExperienceRelation": creator_experience_relation,
            "visualEventSelectionAssessments": [
                {
                    "ordinal": 1,
                    "distinctAction": True,
                    "objectInteraction": True,
                    "visibleOutcome": False,
                    "readableInterfaceChange": False,
                    "routineOnly": False,
                    "uncertain": False,
                    "actorAuthority": actor_authority,
                    "creatorExperienceRelation": creator_experience_relation,
                }
            ],
            "knowledgeSelectionApplied": False,
            "selectedCurrentPassageId": "None",
            "knowledgeSelectionAssessments": [],
        },
    )


class _Session:
    @staticmethod
    def compile_json_schema(*_args, **_kwargs):
        return object(), object()


class _Audit:
    @staticmethod
    def to_json():
        return {"strictParserAccepted": True}


def _generated(title: str):
    hashtag = "#ExampleGame"
    owns_hashtag = title.endswith(" " + hashtag)
    title_body = (
        title[: -(len(hashtag) + 1)]
        if owns_hashtag
        else title
    )
    normalized_title_body = normalize_terminal_single_period_title_body(title_body)
    normalized_title = (
        normalized_title_body + " " + hashtag
        if owns_hashtag
        else normalized_title_body
    )
    completed_json = json.dumps(
        {
            "titleBody": title_body,
            "description": "I opened the visible door.",
            "tags": ["door"],
            "grounding": [],
            "temporalVoice": "RetrospectivePast",
        },
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    )
    return (
        {
            "title": normalized_title,
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
        _Audit(),
        "b" * 64,
        None,
        completed_json,
    )


def _rejected_error(message: str, title_body: str) -> InferenceError:
    error = InferenceError(message)
    error.schema_valid_rejected_json = json.dumps(
        {
            "titleBody": title_body,
            "description": "A person stood beside the visible door.",
            "tags": ["door"],
            "grounding": [],
            "temporalVoice": "RetrospectivePast",
        },
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    )
    return error


def _fake_attestation(
    args,
    kwargs,
    output_sha256: str,
    completed_json_sha256: str,
) -> dict:
    context = kwargs["synthesis_attestation_context"]
    canonical_messages = json.dumps(
        args[2],
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    return {
        **context,
        "canonicalMessagesSha256": hashlib.sha256(canonical_messages).hexdigest(),
        "renderedPromptSha256": hashlib.sha256(canonical_messages).hexdigest(),
        "renderedPromptUtf8ByteCount": len(canonical_messages),
        "inputTokenIdsSha256": "c" * 64,
        "inputTokenCount": 100,
        "outputSha256": output_sha256,
        "completedJsonSha256": completed_json_sha256,
        "rejectionCode": None,
        "accepted": False,
    }


def _return_or_raise_attested(output, args, kwargs):
    completed_json = (
        output[5]
        if isinstance(output, tuple)
        else output.schema_valid_rejected_json
    )
    output_sha256 = (
        output[3]
        if isinstance(output, tuple)
        else hashlib.sha256(
            output.schema_valid_rejected_json.encode("utf-8")
        ).hexdigest()
    )
    attestation = _fake_attestation(
        args,
        kwargs,
        output_sha256,
        hashlib.sha256(completed_json.encode("utf-8")).hexdigest(),
    )
    if isinstance(output, Exception):
        output.synthesis_attestation = attestation
        raise output
    if len(output) == 7:
        return output
    return (*output, attestation)


def _synthesize(request: dict, packet, prior, outputs):
    retained_outputs = list(outputs)

    def generate(*args, **kwargs):
        return _return_or_raise_attested(retained_outputs.pop(0), args, kwargs)

    with patch.object(
        grounded_metadata_pipeline,
        "_generate_json_once",
        side_effect=generate,
    ):
        return _synthesize_case(
            request,
            request["attempt"] + 1,
            "prompt",
            packet,
            True,
            None,
            None,
            None,
            None,
            None,
            _Session(),
            prior,
        )


class GroundedMetadataRerollIntegrationTests(unittest.TestCase):
    def setUp(self) -> None:
        failure_state._reset_failure_context(
            "run-grounded-editorial-metadata-batch"
        )

    def test_first_pass_messages_remain_byte_equivalent(self):
        messages = _metadata_messages(
            _request(0, "DirectAction"),
            "prompt",
            grounded_drafts=[_visual_draft()],
        )
        canonical = json.dumps(
            messages,
            ensure_ascii=False,
            sort_keys=True,
            separators=(",", ":"),
        ).encode("utf-8")
        self.assertEqual(
            "5f90bb477dfb5876b620b9de6fbeba32b95427948c3414debb984090ad8b53e5",
            hashlib.sha256(canonical).hexdigest(),
        )
        self.assertEqual(["system", "user"], [item["role"] for item in messages])

    def test_completed_rejected_json_is_canonical_and_strictly_bounded(self):
        retained = _bounded_completed_json('{"b":2,"a":"value"}')
        self.assertEqual('{"a":"value","b":2}', retained)
        self.assertIsNone(_bounded_completed_json("not-json"))
        oversized = json.dumps(
            {"value": "x" * MAXIMUM_REJECTED_JSON_UTF8_BYTES},
            separators=(",", ":"),
        )
        self.assertIsNone(_bounded_completed_json(oversized))

    def test_unestablished_creator_retry_never_reuses_first_person_copy(self):
        source = _request(0, "ConcreteDetail")
        packet = _packet(
            source,
            actor_authority="Unknown",
            creator_experience_relation="Unestablished",
        )
        rejected = _rejected_error(
            "Grounded metadata used unsupported creator embodiment without an "
            "established creator-experience relation.",
            "I selected the Cerebrum Enhancer",
        )
        rejected.schema_valid_rejected_json = json.dumps(
            {
                "titleBody": "I selected the Cerebrum Enhancer",
                "description": "I triggered a glowing green structure.",
                "tags": ["Cerebrum Enhancer"],
                "grounding": [],
                "temporalVoice": "RetrospectivePast",
            },
            ensure_ascii=False,
            sort_keys=True,
            separators=(",", ":"),
        )
        outputs = [rejected, rejected, rejected, _generated(
            "A glowing green structure appeared #ExampleGame"
        )]
        calls: list[list[dict]] = []

        def generate(*args, **kwargs):
            calls.append(args[2])
            return _return_or_raise_attested(outputs.pop(0), args, kwargs)

        with patch.object(
            grounded_metadata_pipeline,
            "_generate_json_once",
            side_effect=generate,
        ):
            result = _synthesize_case(
                source,
                1,
                "prompt",
                packet,
                True,
                None,
                None,
                None,
                None,
                None,
                _Session(),
            )

        self.assertEqual(4, len(calls))
        for messages in calls[1:]:
            serialized = json.dumps(messages, ensure_ascii=False)
            self.assertNotIn("I selected the Cerebrum Enhancer", serialized)
            context_payload = messages[1]["content"][0]["text"]
            self.assertIn(
                '"voicePerspective":"NeutralNoSubject"',
                context_payload,
            )
            self.assertNotIn(
                '"voicePerspective":"CreatorFirstPerson"',
                context_payload,
            )
            self.assertFalse(any(
                message["role"] == "assistant" for message in messages
            ))
            correction = messages[-1]["content"][0]["text"]
            self.assertIn("primaryActorAuthority=Unknown", correction)
            self.assertIn(
                "primaryCreatorExperienceRelation=Unestablished",
                correction,
            )

        generation = result["generation"]
        self.assertEqual(
            SOURCE_REASON_CREATOR_AUTHORITY_REJECTED_COPY_WITHHELD,
            generation["synthesisRecoveryPoolSourceSelectionReason"],
        )
        self.assertEqual(1, generation["synthesisRecoveryPoolSourcePassOrdinal"])
        attestations = generation["synthesisPassAttestations"]
        self.assertEqual(
            SOURCE_REASON_CREATOR_AUTHORITY_REJECTED_COPY_WITHHELD,
            attestations[1]["sourceSelectionReason"],
        )
        self.assertEqual(
            SOURCE_REASON_CREATOR_AUTHORITY_REJECTED_COPY_WITHHELD,
            attestations[2]["sourceSelectionReason"],
        )
        self.assertEqual(
            SOURCE_REASON_CREATOR_AUTHORITY_REJECTED_COPY_WITHHELD,
            attestations[3]["sourceSelectionReason"],
        )

    def test_unestablished_creator_recovery_uses_all_four_neutral_candidates(self):
        source = _request(0, "ConcreteDetail")
        packet = _packet(
            source,
            actor_authority="Unknown",
            creator_experience_relation="Unestablished",
        )

        def unsupported_creator_copy() -> InferenceError:
            error = _rejected_error(
                "Grounded metadata used unsupported creator embodiment without "
                "an established creator-experience relation.",
                "I triggered the glowing structure",
            )
            error.schema_valid_rejected_json = json.dumps(
                {
                    "titleBody": "I triggered the glowing structure",
                    "description": "I activated a green structure in the chamber.",
                    "tags": ["Glowing structure"],
                    "grounding": [],
                    "temporalVoice": "RetrospectivePast",
                },
                ensure_ascii=False,
                sort_keys=True,
                separators=(",", ":"),
            )
            return error

        outputs = [
            unsupported_creator_copy()
            for _ in range(6)
        ] + [
            _generated("A glowing structure activated in the chamber #ExampleGame")
        ]
        calls: list[list[dict]] = []
        decodings = []

        def generate(*args, **kwargs):
            calls.append(args[2])
            decodings.append(kwargs.get("synthesis_decoding"))
            return _return_or_raise_attested(outputs.pop(0), args, kwargs)

        with patch.object(
            grounded_metadata_pipeline,
            "_generate_json_once",
            side_effect=generate,
        ):
            result = _synthesize_case(
                source,
                1,
                "prompt",
                packet,
                True,
                None,
                None,
                None,
                None,
                None,
                _Session(),
            )

        self.assertEqual(7, len(calls))
        self.assertEqual(
            [None, None, None, *SYNTHESIS_RECOVERY_POOL_DECODINGS],
            decodings,
        )
        pool_messages = calls[3:]
        canonical_pool_messages = {
            json.dumps(
                messages,
                ensure_ascii=False,
                sort_keys=True,
                separators=(",", ":"),
            )
            for messages in pool_messages
        }
        self.assertEqual(1, len(canonical_pool_messages))
        for messages in calls[1:]:
            serialized = json.dumps(messages, ensure_ascii=False)
            context_payload = messages[1]["content"][0]["text"]
            self.assertNotIn("I triggered the glowing structure", serialized)
            self.assertNotIn("I activated a green structure", serialized)
            self.assertNotIn(
                '"voicePerspective":"CreatorFirstPerson"',
                context_payload,
            )
            self.assertIn(
                '"voicePerspective":"NeutralNoSubject"',
                context_payload,
            )
            self.assertFalse(any(
                message["role"] == "assistant" for message in messages
            ))

        generation = result["generation"]
        self.assertTrue(generation["synthesisRecoveryPoolApplied"])
        self.assertEqual(
            SOURCE_REASON_CREATOR_AUTHORITY_REJECTED_COPY_WITHHELD,
            generation["synthesisRecoveryPoolSourceSelectionReason"],
        )
        self.assertEqual(
            4,
            generation["synthesisRecoveryPoolAttemptedCandidateCount"],
        )
        self.assertEqual(
            4,
            generation["synthesisRecoveryPoolSelectedCandidateOrdinal"],
        )
        self.assertEqual(
            [3407, 3408, 3409, 3410],
            [
                attestation["seed"]
                for attestation in generation["synthesisPassAttestations"][3:]
            ],
        )
        self.assertEqual(
            [
                "UnsupportedCreatorEmbodiment",
                "UnsupportedCreatorEmbodiment",
                "UnsupportedCreatorEmbodiment",
                None,
            ],
            [
                attestation["rejectionCode"]
                for attestation in generation["synthesisPassAttestations"][3:]
            ],
        )
        self.assertEqual(
            "A glowing structure activated in the chamber #ExampleGame",
            result["metadata"]["title"],
        )

    def test_cross_draft_retry_discards_rejected_audience_copy(self):
        source = _request(0, "DirectAction")
        primary = {
            "environment": "A stone chamber",
            "environmentUncertain": False,
            "subjectsAndObjects": ["A creature", "A visible target"],
            "actions": ["A creature struck a visible target."],
            "readableText": [],
            "uncertainties": [],
        }
        rejected = _rejected_error(
            "Grounded metadata title used content unique to a non-primary "
            "chronological draft.",
            "I activated the Cerebrum Enhancer",
        )
        rejected.schema_valid_rejected_json = json.dumps({
            "description": "I selected the Cerebrum Enhancer to add bio-synapses.",
            "grounding": [],
            "tags": ["Voidling Bound"],
            "temporalVoice": "RetrospectivePast",
            "titleBody": "I activated the Cerebrum Enhancer",
        }, sort_keys=True, separators=(",", ":"))
        outputs = [
            rejected,
            _generated("I struck the visible target #ExampleGame"),
        ]
        calls: list[list[dict]] = []

        def generate(*args, **kwargs):
            calls.append(args[2])
            return _return_or_raise_attested(outputs.pop(0), args, kwargs)

        with patch.object(
            grounded_metadata_pipeline,
            "_generate_json_once",
            side_effect=generate,
        ):
            result = _synthesize_case(
                source, 1, "prompt", _packet(source, primary), True,
                None, None, None, None, None, _Session(),
            )

        self.assertEqual(2, len(calls))
        retry_messages = calls[1]
        serialized_retry = json.dumps(retry_messages, ensure_ascii=False)
        self.assertEqual(["system", "user", "user"], [
            message["role"] for message in retry_messages
        ])
        self.assertNotIn("Cerebrum Enhancer", serialized_retry)
        self.assertNotIn("bio-synapses", serialized_retry)
        self.assertIn("creature struck a visible target", serialized_retry)
        self.assertIn("audience copy is intentionally withheld", serialized_retry)
        attestation = result["generation"]["synthesisPassAttestations"][1]
        self.assertEqual(
            SOURCE_REASON_CROSS_DRAFT_REJECTED_COPY_WITHHELD,
            attestation["sourceSelectionReason"],
        )
        self.assertEqual(1, attestation["sourcePassOrdinal"])

    def test_input_contract_accepts_only_bounded_unique_prior_titles(self):
        request = _request(1, "SpecificCuriosity")
        validated_video = request.pop("_validated")
        request["reviewVideo"] = {"placeholder": True}
        request["priorAcceptedTitles"] = [
            "Opened the visible door #ExampleGame",
            "Found the doorway open behind us #ExampleGame",
        ]
        with patch.object(
            grounded_metadata_contract,
            "_validate_review_video",
            return_value=validated_video,
        ):
            validated = grounded_metadata_contract.validate_request(
                request,
                0,
                {},
            )
        self.assertEqual(
            request["priorAcceptedTitles"],
            validated["priorAcceptedTitles"],
        )

        request["priorAcceptedTitles"] = [
            "Opened the visible door #ExampleGame",
            "opened the visible door #examplegame",
        ]
        with patch.object(
            grounded_metadata_contract,
            "_validate_review_video",
            return_value=validated_video,
        ), self.assertRaises(UsageOrInputError):
            grounded_metadata_contract.validate_request(request, 0, {})

        request["priorAcceptedTitles"] = [
            f"Supported title {index} #ExampleGame" for index in range(9)
        ]
        with patch.object(
            grounded_metadata_contract,
            "_validate_review_video",
            return_value=validated_video,
        ), self.assertRaises(UsageOrInputError):
            grounded_metadata_contract.validate_request(request, 0, {})

    def test_dynamic_intents_are_operational_and_prior_copy_is_exclusion_only(self):
        policies = {
            intent: _variant_intent_guidance(intent)
            for intent in (
                "DirectAction",
                "SpecificCuriosity",
                "OutcomeFocused",
                "ConcreteDetail",
            )
        }
        self.assertEqual(4, len(set(policies.values())))
        request = _request(1, "ConcreteDetail")
        payload = _metadata_messages(
            request,
            "prompt",
            grounded_drafts=[_visual_draft()],
            primary_actor_authority="OtherPerson",
            primary_creator_experience_relation="CreatorEncountered",
            prior_accepted_title_bodies=("Opened the visible door",),
        )[1]["content"][0]["text"]

        self.assertIn(REROLL_DIVERSITY_POLICY_VERSION, payload)
        self.assertIn("variantIntent=ConcreteDetail", payload)
        self.assertIn('["Opened the visible door"]', payload)
        self.assertIn("solely as editorial exclusions", payload)
        self.assertIn("They are not evidence", payload)
        self.assertIn("must never be quoted or echoed", payload)
        self.assertIn("actor-authority gate remains controlling", payload)
        self.assertIn("Never end titleBody with one sentence-style full stop", payload)

    def test_similar_title_retries_then_accepts_without_rebuilding_grounding(self):
        source = _request(0, "DirectAction")
        reroll = _request(1, "SpecificCuriosity")
        packet = _packet(source)
        prior = (
            _reroll_title_reference(
                source,
                "Opened the visible door #ExampleGame",
            ),
        )

        result = _synthesize(
            reroll,
            packet,
            prior,
            [
                _generated("Opened the visible door #ExampleGame"),
                _generated("Found the doorway open behind us #ExampleGame"),
            ],
        )

        generation = result["generation"]
        self.assertEqual(["RerollTitleTooSimilar"], generation["rejectedValidationRules"])
        self.assertEqual(2, generation["synthesisPassCount"])
        self.assertEqual(2, generation["generationPassCount"])
        self.assertFalse(generation["duplicateSynthesisRecoveryApplied"])
        self.assertIsNone(
            generation["duplicateSynthesisRecoverySourcePassOrdinal"]
        )
        self.assertIsNone(
            generation["duplicateSynthesisRecoveryRepeatedPassOrdinal"]
        )
        self.assertIsNone(
            generation[
                "duplicateSynthesisRecoverySourceRejectedJsonSha256"
            ]
        )
        self.assertIsNone(
            generation[
                "duplicateSynthesisRecoveryRepeatedRejectedJsonSha256"
            ]
        )
        self.assertTrue(generation["groundingPacketReused"])
        self.assertEqual(packet.fact_sha256, generation["groundingPacketFactSha256"])
        self.assertEqual(1, generation["priorAcceptedTitleCount"])
        self.assertEqual("MateriallyDistinct", generation["rerollTitleDiversityCode"])

    def test_external_and_host_prior_titles_are_all_excluded_without_becoming_facts(self):
        source = _request(0, "DirectAction")
        reroll = _request(3, "ConcreteDetail")
        reroll["priorAcceptedTitles"] = [
            "Opened the visible door #ExampleGame",
        ]
        packet = _packet(source)
        identity_without, facts_without = _grounding_reuse_identity(source)
        source["priorAcceptedTitles"] = [
            "A user-edited prior title #ExampleGame",
        ]
        identity_with, facts_with = _grounding_reuse_identity(source)
        host_prior = (
            _reroll_title_reference(
                reroll,
                "Opened the visible door #ExampleGame",
            ),
            _reroll_title_reference(
                reroll,
                "Found the doorway open behind us #ExampleGame",
            ),
        )

        result = _synthesize(
            reroll,
            packet,
            host_prior,
            [
                _generated("Opened the visible door #ExampleGame"),
                _generated("Walked through the open doorway #ExampleGame"),
            ],
        )

        self.assertEqual(identity_without, identity_with)
        self.assertEqual(facts_without, facts_with)
        self.assertEqual(
            ["RerollTitleTooSimilar"],
            result["generation"]["rejectedValidationRules"],
        )
        self.assertEqual(2, result["generation"]["priorAcceptedTitleCount"])

    def test_terminal_period_is_normalized_without_regeneration(self):
        source = _request(0, "DirectAction")
        packet = _packet(source)

        result = _synthesize(
            source,
            packet,
            (),
            [
                _generated("Opened the visible door. #ExampleGame"),
            ],
        )

        self.assertEqual([], result["generation"]["rejectedValidationRules"])
        self.assertEqual("NoComparablePrior", result["generation"]["rerollTitleDiversityCode"])
        self.assertEqual("Opened the visible door #ExampleGame", result["metadata"]["title"])

    def test_duplicate_final_similar_pass_is_retained_for_user_review(self):
        source = _request(0, "DirectAction")
        reroll = _request(1, "OutcomeFocused")
        packet = _packet(source)
        prior = (
            _reroll_title_reference(
                source,
                "Opened the visible door #ExampleGame",
            ),
        )

        result = _synthesize(
            reroll,
            packet,
            prior,
            [_generated("Opened the visible door #ExampleGame")] * 7,
        )

        self.assertEqual(
            ["RerollTitleTooSimilar"],
            result["generation"]["metadataReviewIssues"],
        )
        self.assertTrue(result["generation"]["metadataReviewRequired"])
        self.assertEqual(
            "Opened the visible door #ExampleGame",
            result["metadata"]["title"],
        )

    def test_duplicate_final_rejection_gets_one_materially_different_pass(self):
        source = _request(0, "DirectAction")
        packet = _packet(source)
        present_named = _rejected_error(
            "Grounded metadata used a command, present-tense, or gerund title opening.",
            "Lyra opens the visible door",
        )
        present_named.rejected_title_body = "Lyra opens the visible door"
        present_named.offending_action_field = "titleBody"
        present_named.offending_action_form = "opens"
        generic_person = _rejected_error(
            "Grounded metadata used third-person creator framing or generic "
            "observer-person framing.",
            "A person stood beside the visible door",
        )
        repeated_generic_person = _rejected_error(
            "Grounded metadata used third-person creator framing or generic "
            "observer-person framing.",
            "A person stood beside the visible door",
        )
        outputs = [
            present_named,
            generic_person,
            repeated_generic_person,
            _generated("Found the visible door open behind us #ExampleGame"),
        ]
        calls: list[list[dict]] = []
        decoding = []

        def generate(*args, **kwargs):
            calls.append(args[2])
            decoding.append(kwargs.get("synthesis_decoding"))
            return _return_or_raise_attested(outputs.pop(0), args, kwargs)

        with patch.object(
            grounded_metadata_pipeline,
            "_generate_json_once",
            side_effect=generate,
        ):
            result = _synthesize_case(
                source,
                1,
                "prompt",
                packet,
                True,
                None,
                None,
                None,
                None,
                None,
                _Session(),
            )

        self.assertEqual(4, len(calls))
        self.assertEqual(
            [None, None, None, SYNTHESIS_RECOVERY_POOL_DECODINGS[0]],
            decoding,
        )
        self.assertEqual(
            generic_person.schema_valid_rejected_json,
            repeated_generic_person.schema_valid_rejected_json,
        )
        self.assertEqual(
            [
                "NonRetrospectiveVoice",
                "ThirdPersonCreatorFraming",
                "ThirdPersonCreatorFraming",
            ],
            result["generation"]["rejectedValidationRules"],
        )
        self.assertEqual(4, result["generation"]["synthesisPassCount"])
        self.assertTrue(
            result["generation"]["duplicateSynthesisRecoveryApplied"]
        )
        self.assertEqual(
            2,
            result["generation"][
                "duplicateSynthesisRecoverySourcePassOrdinal"
            ],
        )
        self.assertEqual(
            3,
            result["generation"][
                "duplicateSynthesisRecoveryRepeatedPassOrdinal"
            ],
        )
        duplicate_sha256 = hashlib.sha256(
            generic_person.schema_valid_rejected_json.encode("utf-8")
        ).hexdigest()
        self.assertEqual(
            duplicate_sha256,
            result["generation"][
                "duplicateSynthesisRecoverySourceRejectedJsonSha256"
            ],
        )
        self.assertEqual(
            SYNTHESIS_DECODING_POLICY_VERSION,
            result["generation"]["synthesisDecodingPolicyVersion"],
        )
        self.assertEqual(
            SYNTHESIS_DECODING_POLICY_SHA256,
            result["generation"]["synthesisDecodingPolicySha256"],
        )
        first_sha256 = hashlib.sha256(
            present_named.schema_valid_rejected_json.encode("utf-8")
        ).hexdigest()
        self.assertTrue(result["generation"]["synthesisRecoveryPoolApplied"])
        self.assertEqual(
            1,
            result["generation"]["synthesisRecoveryPoolSourcePassOrdinal"],
        )
        self.assertEqual(
            SOURCE_REASON_ORIGINAL_FIRST_REJECTED,
            result["generation"]["synthesisRecoveryPoolSourceSelectionReason"],
        )
        self.assertEqual(
            "BoundedSemanticRecoveryActivated",
            result["generation"]["synthesisRecoveryPoolTrigger"],
        )
        self.assertEqual(
            first_sha256,
            result["generation"][
                "synthesisRecoveryPoolSourceRejectedJsonSha256"
            ],
        )
        self.assertEqual(1, result["generation"]["synthesisRecoveryPoolBatchSize"])
        self.assertTrue(result["generation"]["synthesisRecoveryPoolDoSample"])
        self.assertEqual(
            1,
            result["generation"]["synthesisRecoveryPoolNumberOfBeams"],
        )
        self.assertTrue(result["generation"]["synthesisRecoveryPoolUseCache"])
        self.assertEqual(
            [3407, 3408, 3409, 3410],
            result["generation"]["synthesisRecoveryPoolSeeds"],
        )
        self.assertEqual(
            1,
            result["generation"]["synthesisRecoveryPoolSelectedCandidateOrdinal"],
        )
        self.assertEqual(
            1,
            result["generation"]["synthesisRecoveryPoolAttemptedCandidateCount"],
        )
        self.assertEqual(0.7, result["generation"]["synthesisRecoveryPoolTemperature"])
        self.assertEqual(0.8, result["generation"]["synthesisRecoveryPoolTopP"])
        self.assertEqual(20, result["generation"]["synthesisRecoveryPoolTopK"])
        self.assertTrue(result["generation"]["synthesisRecoveryPoolFreshMatcher"])
        self.assertFalse(
            result["generation"][
                "synthesisRecoveryPoolUnconstrainedFallbackUsed"
            ]
        )
        self.assertFalse(
            result["generation"]["synthesisRecoveryPoolSemanticRepairApplied"]
        )
        self.assertEqual(
            duplicate_sha256,
            result["generation"][
                "duplicateSynthesisRecoveryRepeatedRejectedJsonSha256"
            ],
        )
        self.assertEqual(
            "Found the visible door open behind us #ExampleGame",
            result["metadata"]["title"],
        )
        final_correction = calls[3][-1]["content"][0]["text"]
        self.assertEqual(
            present_named.schema_valid_rejected_json,
            calls[3][-2]["content"][0]["text"],
        )
        self.assertIn(
            "materially different supported audience-copy angle",
            final_correction,
        )
        self.assertIn("do not add a new fact", final_correction)

    def test_unstable_readable_text_retry_names_exact_forbidden_phrase(self):
        source = _request(0, "DirectAction")
        packet = _packet(source)

        def rejected() -> InferenceError:
            error = _rejected_error(
                "Grounded metadata reused unstable readable text in audience copy.",
                "I approached the Cerebrum Enhancer",
            )
            error.offending_readable_text_phrases = (
                "approached the cerebrum enhancer",
            )
            error.offending_readable_text_fields = ("Title",)
            return error

        first = rejected()
        repeated = rejected()
        outputs = [
            first,
            repeated,
            _generated("Activated the visible device #ExampleGame"),
        ]
        calls: list[list[dict]] = []

        def generate(*args, **kwargs):
            calls.append(args[2])
            return _return_or_raise_attested(outputs.pop(0), args, kwargs)

        with patch.object(
            grounded_metadata_pipeline,
            "_generate_json_once",
            side_effect=generate,
        ):
            result = _synthesize_case(
                source,
                1,
                "prompt",
                packet,
                True,
                None,
                None,
                None,
                None,
                None,
                _Session(),
            )

        self.assertEqual(3, len(calls))
        for correction in (calls[1][-1], calls[2][-1]):
            text = correction["content"][0]["text"]
            self.assertIn(
                '"forbiddenReadableTextPhrases":'
                '["approached the cerebrum enhancer"]',
                text,
            )
            self.assertIn(
                '"affectedAudienceFields":["Title"]',
                text,
            )
            self.assertIn("must not be retained or paraphrased", text)
        self.assertEqual(
            "Activated the visible device #ExampleGame",
            result["metadata"]["title"],
        )

    def test_nonduplicate_final_semantic_rejection_enters_bounded_pool(self):
        source = _request(0, "DirectAction")
        packet = _packet(source)
        outputs = [
            _rejected_error(
                "Grounded metadata used a command, present-tense, or gerund title opening.",
                "Lyra opens the visible door",
            ),
            _rejected_error(
                "Grounded metadata used third-person creator framing or generic "
                "observer-person framing.",
                "A person stood beside the visible door",
            ),
            _rejected_error(
                "Grounded metadata used unsupported creator embodiment.",
                "I opened the visible door for Lyra",
            ),
            _generated("Opened the visible door beside Lyra #ExampleGame"),
        ]
        call_count = 0

        def generate(*args, **kwargs):
            nonlocal call_count
            call_count += 1
            return _return_or_raise_attested(outputs.pop(0), args, kwargs)

        with patch.object(
            grounded_metadata_pipeline,
            "_generate_json_once",
            side_effect=generate,
        ):
            result = _synthesize_case(
                source,
                1,
                "prompt",
                packet,
                True,
                None,
                None,
                None,
                None,
                None,
                _Session(),
            )

        self.assertEqual(4, call_count)
        self.assertFalse(
            result["generation"]["duplicateSynthesisRecoveryApplied"]
        )
        self.assertTrue(result["generation"]["synthesisRecoveryPoolApplied"])
        self.assertEqual(
            SOURCE_REASON_ORIGINAL_FIRST_REJECTED,
            result["generation"]["synthesisRecoveryPoolSourceSelectionReason"],
        )
        self.assertEqual(
            [
                "NonRetrospectiveVoice",
                "ThirdPersonCreatorFraming",
                "UnsupportedCreatorEmbodiment",
            ],
            result["generation"]["rejectedValidationRules"],
        )
        self.assertEqual(
            "Opened the visible door beside Lyra #ExampleGame",
            result["metadata"]["title"],
        )

    def test_voidling_cross_draft_sequence_recovers_without_reusing_copy(self):
        source = _request(0, "DirectAction")
        packet = _packet(source)
        first = _rejected_error(
            "Grounded metadata title used content unique to a non-primary "
            "chronological draft.",
            "I moved to the Cerebrum Enhancer",
        )
        second = _rejected_error(
            "Grounded metadata used third-person creator framing or generic "
            "observer-person framing.",
            "Leon appears in a laboratory",
        )
        third = _rejected_error(
            "Grounded metadata used a command, present-tense, or gerund title "
            "opening.",
            "Open the visible door",
        )
        third.rejected_title_body = "Open the visible door"
        third.offending_action_field = "titleBody"
        third.offending_action_form = "open"
        outputs = [
            first,
            second,
            third,
            _generated("Opened the visible door #ExampleGame"),
        ]
        calls: list[list[dict]] = []
        decodings = []

        def generate(*args, **kwargs):
            calls.append(args[2])
            decodings.append(kwargs.get("synthesis_decoding"))
            return _return_or_raise_attested(outputs.pop(0), args, kwargs)

        with patch.object(
            grounded_metadata_pipeline,
            "_generate_json_once",
            side_effect=generate,
        ):
            result = _synthesize_case(
                source,
                1,
                "prompt",
                packet,
                True,
                None,
                None,
                None,
                None,
                None,
                _Session(),
            )

        self.assertEqual(
            [None, None, None, SYNTHESIS_RECOVERY_POOL_DECODINGS[0]],
            decodings,
        )
        self.assertFalse(
            result["generation"]["duplicateSynthesisRecoveryApplied"]
        )
        self.assertTrue(result["generation"]["synthesisRecoveryPoolApplied"])
        self.assertEqual(
            3,
            result["generation"]["synthesisRecoveryPoolSourcePassOrdinal"],
        )
        self.assertEqual(
            SOURCE_REASON_PRIMARY_ONLY_CROSS_DRAFT_COPY_WITHHELD,
            result["generation"]["synthesisRecoveryPoolSourceSelectionReason"],
        )
        serialized_pool_messages = json.dumps(calls[3], ensure_ascii=False)
        self.assertNotIn(first.schema_valid_rejected_json, serialized_pool_messages)
        self.assertNotIn(second.schema_valid_rejected_json, serialized_pool_messages)
        self.assertNotIn(third.schema_valid_rejected_json, serialized_pool_messages)
        self.assertFalse(any(message["role"] == "assistant" for message in calls[3]))
        self.assertEqual(
            [
                "CrossDraftTitleContamination",
                "ThirdPersonCreatorFraming",
                "NonRetrospectiveVoice",
            ],
            result["generation"]["rejectedValidationRules"],
        )

    def test_real_generic_present_sequence_deduplicates_retry_guidance(self):
        source = _request(0, "DirectAction")
        source["game"]["notes"] = (
            "Hannya, identifiable by the horned mask, confronted Akito amid "
            "a glowing chain."
        )
        ghostwire_draft = {
            "environment": "A foggy open area",
            "environmentUncertain": False,
            "subjectsAndObjects": [
                "A masked figure",
                "Another person",
                "A glowing chain",
            ],
            "actions": ["A masked figure confronted another person."],
            "readableText": [],
            "uncertainties": [],
        }
        packet = _packet(
            source,
            ghostwire_draft,
            "OtherPerson",
            "Unestablished",
        )
        named_present = _rejected_error(
            "Grounded metadata used a command, present-tense, or gerund title opening.",
            "Hannya confronts Akito amid glowing chains",
        )
        named_present.rejected_title_body = (
            "Hannya confronts Akito amid glowing chains"
        )
        named_present.offending_action_field = "titleBody"
        named_present.offending_action_form = "confronts"
        generic_present = _rejected_error(
            "Grounded metadata used third-person creator framing or generic "
            "observer-person framing.",
            "A man in a skull mask stands in a foggy area",
        )
        generic_present.rejected_title_body = (
            "A man in a skull mask stands in a foggy area"
        )
        generic_present.offending_action_field = "titleBody"
        generic_present.offending_action_form = "stands"
        repeated_generic_present = _rejected_error(
            "Grounded metadata used third-person creator framing or generic "
            "observer-person framing.",
            "A man in a skull mask stands in a foggy area",
        )
        repeated_generic_present.rejected_title_body = (
            "A man in a skull mask stands in a foggy area"
        )
        repeated_generic_present.offending_action_field = "titleBody"
        repeated_generic_present.offending_action_form = "stands"
        self.assertEqual(
            generic_present.schema_valid_rejected_json,
            repeated_generic_present.schema_valid_rejected_json,
        )
        outputs = [
            named_present,
            generic_present,
            repeated_generic_present,
            _generated(
                "Hannya confronted Akito amid glowing chains #ExampleGame"
            ),
        ]
        calls: list[list[dict]] = []

        def generate(*args, **kwargs):
            calls.append(args[2])
            if len(calls) < 4:
                self.assertNotIn("synthesis_decoding", kwargs)
            else:
                self.assertIs(
                    SYNTHESIS_RECOVERY_POOL_DECODINGS[0],
                    kwargs.get("synthesis_decoding"),
                )
            return _return_or_raise_attested(outputs.pop(0), args, kwargs)

        with patch.object(
            grounded_metadata_pipeline,
            "_generate_json_once",
            side_effect=generate,
        ):
            result = _synthesize_case(
                source,
                1,
                "prompt",
                packet,
                True,
                None,
                None,
                None,
                None,
                None,
                _Session(),
            )

        self.assertEqual(4, len(calls))
        self.assertEqual(
            [
                "NonRetrospectiveVoice",
                "ThirdPersonCreatorFraming",
                "ThirdPersonCreatorFraming",
            ],
            result["generation"]["rejectedValidationRules"],
        )
        correction = calls[3][-1]["content"][0]["text"]
        cumulative_codes = json.loads(
            correction.split("Cumulative typed rejected rules: ", 1)[1]
            .split(". Correct only those rejected rules.", 1)[0]
        )
        self.assertEqual(
            [
                "NonRetrospectiveVoice",
                "ThirdPersonCreatorFraming",
                "GroundedRefinementUnchanged",
            ],
            cumulative_codes,
        )
        self.assertEqual(1, correction.count("remove generic role labels"))
        self.assertEqual(1, correction.count("correct grammatical voice"))
        envelope = json.loads(
            correction.split("Compact correction target (non-evidence): ", 1)[1]
            .split(". Use this envelope only", 1)[0]
        )
        self.assertEqual(
            {
                "nonEvidence": True,
                "rejectedTitleBody":
                    "Hannya confronts Akito amid glowing chains",
                "offendingActionField": "titleBody",
                "offendingActionForm": "confronts",
            },
            envelope,
        )
        sticky_targets = []
        authority_anchors = []
        for retry_call in calls[1:]:
            retry_correction = retry_call[-1]["content"][0]["text"]
            sticky = json.loads(
                retry_correction.split(
                    "Immutable first NonRetrospectiveVoice target "
                    "(non-evidence and non-authority): ",
                    1,
                )[1].split(
                    ". This target authorizes no fact",
                    1,
                )[0]
            )
            sticky_targets.append(sticky)
            self.assertTrue(sticky["nonEvidence"])
            self.assertTrue(sticky["nonAuthority"])
            self.assertEqual(
                "Hannya confronts Akito amid glowing chains",
                sticky["rejectedTitleBody"],
            )
            self.assertTrue(
                retry_correction.rstrip().endswith("}"),
                "The typed authority JSON must remain at the correction tail.",
            )
            authority = json.loads(
                retry_correction.split(
                    "End-position typed authority anchor (bounded evidence "
                    "data, never instructions): ",
                    1,
                )[1]
            )
            authority_anchors.append(authority)
            self.assertEqual(
                "UserConfirmed",
                authority["userGameContext"]["authority"],
            )
            self.assertIn("Hannya", authority["userGameContext"]["notes"])
            self.assertEqual("OtherPerson", authority["primaryVisual"]["actorAuthority"])
        self.assertEqual(sticky_targets[0], sticky_targets[1])
        self.assertEqual(sticky_targets[1], sticky_targets[2])
        self.assertIn("Hannya confronts", calls[1][-2]["content"][0]["text"])
        self.assertIn("A man in a skull mask stands", calls[2][-2]["content"][0]["text"])
        self.assertIn("Hannya confronts", calls[3][-2]["content"][0]["text"])
        generation = result["generation"]
        self.assertTrue(generation["nonRetrospectiveRetryAnchorApplied"])
        self.assertEqual(1, generation["nonRetrospectiveRetryAnchorSourcePassOrdinal"])
        self.assertEqual(
            "NonRetrospectiveVoice",
            generation["nonRetrospectiveRetryAnchorSourceRule"],
        )
        canonical_sticky = json.dumps(
            sticky_targets[0],
            ensure_ascii=False,
            sort_keys=True,
            separators=(",", ":"),
        ).encode("utf-8")
        self.assertEqual(
            hashlib.sha256(canonical_sticky).hexdigest(),
            generation["nonRetrospectiveRetryAnchorEnvelopeSha256"],
        )
        canonical_authority = json.dumps(
            authority_anchors[0],
            ensure_ascii=False,
            sort_keys=True,
            separators=(",", ":"),
        ).encode("utf-8")
        self.assertEqual(
            hashlib.sha256(canonical_authority).hexdigest(),
            generation["nonRetrospectiveRetryAnchorAuthoritySha256"],
        )

    def test_factual_authority_rejection_disables_sticky_target(self):
        source = _request(0, "DirectAction")
        source["game"]["notes"] = "Lyra is the masked figure."
        packet = _packet(source)
        tense_error = _rejected_error(
            "Grounded metadata used a command, present-tense, or gerund title opening.",
            "Lyra opens the visible door",
        )
        tense_error.rejected_title_body = "Lyra opens the visible door"
        tense_error.offending_action_field = "titleBody"
        tense_error.offending_action_form = "opens"
        authority_error = _rejected_error(
            "Grounded metadata used unsupported creator embodiment without an "
            "established creator-experience relation.",
            "I opened the visible door",
        )
        outputs = [
            tense_error,
            authority_error,
            _generated("Opened the visible door #ExampleGame"),
        ]
        calls: list[list[dict]] = []

        def generate(*args, **kwargs):
            calls.append(args[2])
            self.assertNotIn("synthesis_decoding", kwargs)
            return _return_or_raise_attested(outputs.pop(0), args, kwargs)

        with patch.object(
            grounded_metadata_pipeline,
            "_generate_json_once",
            side_effect=generate,
        ):
            result = _synthesize_case(
                source,
                1,
                "prompt",
                packet,
                True,
                None,
                None,
                None,
                None,
                None,
                _Session(),
            )

        self.assertIn(
            "Immutable first NonRetrospectiveVoice target",
            calls[1][-1]["content"][0]["text"],
        )
        self.assertNotIn(
            "Immutable first NonRetrospectiveVoice target",
            calls[2][-1]["content"][0]["text"],
        )
        self.assertNotIn(
            "End-position typed authority anchor",
            calls[2][-1]["content"][0]["text"],
        )
        self.assertTrue(
            result["generation"]["nonRetrospectiveRetryAnchorApplied"],
            "Provenance must report that the now-disabled anchor was used on pass 2.",
        )

    def test_ghostwire_first_error_masking_uses_attested_immutable_pool(self):
        source = _request(0, "DirectAction")
        source["game"]["notes"] = (
            "Hannya, identifiable by the horned mask, confronted Akito beside "
            "a glowing chain."
        )
        draft = {
            "environment": "A foggy open area",
            "environmentUncertain": False,
            "subjectsAndObjects": [
                "A horned masked figure",
                "Another person",
                "A glowing chain",
            ],
            "actions": ["A masked figure confronted another person."],
            "readableText": [],
            "uncertainties": [],
        }
        packet = _packet(source, draft, "OtherPerson", "Unestablished")
        cross_draft = _rejected_error(
            "Grounded metadata title reused a non-primary chronological draft.",
            "Hannya confronts Akito amid glowing chains",
        )
        generic = _rejected_error(
            "Grounded metadata used third-person creator framing or generic "
            "observer-person framing.",
            "A man in a skull mask stands in fog",
        )
        generic.rejected_title_body = "A man in a skull mask stands in fog"
        generic.offending_action_field = "titleBody"
        generic.offending_action_form = "stands"
        repeated_generic = _rejected_error(
            "Grounded metadata used third-person creator framing or generic "
            "observer-person framing.",
            "A man in a skull mask stands in fog",
        )
        repeated_generic.rejected_title_body = generic.rejected_title_body
        repeated_generic.offending_action_field = "titleBody"
        repeated_generic.offending_action_form = "stands"
        pool_cross_draft = _rejected_error(
            "Grounded metadata title used content unique to a non-primary "
            "chronological draft.",
            "Hannya confronted Akito before the blue chain appeared",
        )
        outputs = [
            cross_draft,
            generic,
            repeated_generic,
            pool_cross_draft,
            _generated(
                "Hannya confronted Akito beside a glowing chain #ExampleGame"
            ),
        ]
        calls: list[list[dict]] = []
        decodings = []

        def generate(*args, **kwargs):
            calls.append(args[2])
            decodings.append(kwargs.get("synthesis_decoding"))
            return _return_or_raise_attested(outputs.pop(0), args, kwargs)

        with patch.object(
            grounded_metadata_pipeline,
            "_generate_json_once",
            side_effect=generate,
        ):
            result = _synthesize_case(
                source,
                1,
                "prompt",
                packet,
                True,
                None,
                None,
                None,
                None,
                None,
                _Session(),
            )

        self.assertEqual(5, len(calls))
        self.assertEqual(
            [None, None, None, *SYNTHESIS_RECOVERY_POOL_DECODINGS[:2]],
            decodings,
        )
        pool_messages = calls[3:]
        canonical_pool_messages = [
            json.dumps(
                item,
                ensure_ascii=False,
                sort_keys=True,
                separators=(",", ":"),
            )
            for item in pool_messages
        ]
        self.assertEqual(1, len(set(canonical_pool_messages)))
        for messages in pool_messages:
            serialized_messages = json.dumps(messages, ensure_ascii=False)
            self.assertNotIn(
                repeated_generic.schema_valid_rejected_json,
                serialized_messages,
            )
            self.assertFalse(any(
                message["role"] == "assistant" for message in messages
            ))
            correction = messages[-1]["content"][0]["text"]
            self.assertNotIn("Compact correction target", correction)
            self.assertNotIn(
                "Immutable first NonRetrospectiveVoice target",
                correction,
            )
            self.assertNotIn("A man in a skull mask stands in fog", correction)
            self.assertIn("Hannya", correction)
            self.assertIn("CrossDraftTitleContamination", correction)
            self.assertIn("audience copy is intentionally withheld", correction)
        generation = result["generation"]
        self.assertEqual(5, generation["synthesisPassCount"])
        self.assertEqual(2, generation["synthesisRecoveryPoolAttemptedCandidateCount"])
        self.assertEqual(2, generation["synthesisRecoveryPoolSelectedCandidateOrdinal"])
        self.assertFalse(generation["nonRetrospectiveRetryAnchorApplied"])
        self.assertIsNone(
            generation["nonRetrospectiveRetryAnchorSourcePassOrdinal"]
        )
        self.assertIsNone(generation["nonRetrospectiveRetryAnchorSourceRule"])
        self.assertIsNone(
            generation["nonRetrospectiveRetryAnchorEnvelopeSha256"]
        )
        self.assertIsNone(
            generation["nonRetrospectiveRetryAnchorAuthoritySha256"]
        )
        attestations = generation["synthesisPassAttestations"]
        self.assertEqual(5, len(attestations))
        self.assertIsNone(attestations[0]["sourcePassOrdinal"])
        self.assertIsNone(attestations[0]["sourceRejectedJsonSha256"])
        self.assertEqual(1, attestations[1]["sourcePassOrdinal"])
        self.assertEqual(
            attestations[0]["completedJsonSha256"],
            attestations[1]["sourceRejectedJsonSha256"],
        )
        self.assertEqual(2, attestations[2]["sourcePassOrdinal"])
        self.assertEqual(
            attestations[1]["completedJsonSha256"],
            attestations[2]["sourceRejectedJsonSha256"],
        )
        self.assertFalse(attestations[1]["retryAnchorCaptured"])
        self.assertFalse(attestations[2]["retryAnchorCaptured"])
        self.assertEqual(
            "CrossDraftTitleContamination",
            attestations[1]["retryAnchorDisabledReason"],
        )
        self.assertEqual(
            "CrossDraftTitleContamination",
            attestations[2]["retryAnchorDisabledReason"],
        )
        self.assertFalse(attestations[3]["retryAnchorApplied"])
        self.assertFalse(attestations[4]["retryAnchorApplied"])
        self.assertIsNone(attestations[3]["retryAnchorEnvelopeSha256"])
        self.assertIsNone(attestations[4]["retryAnchorEnvelopeSha256"])
        self.assertIsNotNone(attestations[3]["retryAnchorAuthoritySha256"])
        self.assertEqual(
            attestations[3]["retryAnchorAuthoritySha256"],
            attestations[4]["retryAnchorAuthoritySha256"],
        )
        self.assertEqual([3407, 3408], [item["seed"] for item in attestations[3:]])
        self.assertTrue(all(
            item["sourceRejectedJsonSha256"] ==
                attestations[2]["completedJsonSha256"]
            for item in attestations[3:]
        ))
        self.assertTrue(all(
            item["sourcePassOrdinal"] == 3
            and item["sourceSelectionReason"] ==
                SOURCE_REASON_PRIMARY_ONLY_CROSS_DRAFT_COPY_WITHHELD
            for item in attestations[3:]
        ))
        self.assertEqual(
            3,
            generation["synthesisRecoveryPoolSourcePassOrdinal"],
        )
        self.assertEqual(
            SOURCE_REASON_PRIMARY_ONLY_CROSS_DRAFT_COPY_WITHHELD,
            generation["synthesisRecoveryPoolSourceSelectionReason"],
        )
        self.assertEqual(
            attestations[2]["completedJsonSha256"],
            generation["synthesisRecoveryPoolSourceRejectedJsonSha256"],
        )
        self.assertEqual(
            1,
            len({item["canonicalMessagesSha256"] for item in attestations[3:]}),
        )
        self.assertEqual(
            ["CrossDraftTitleContamination", None],
            [item["rejectionCode"] for item in attestations[3:]],
        )
        self.assertEqual(
            [
                {
                    "candidateOrdinal": 1,
                    "seed": 3407,
                    "sourceSelectionReason":
                        SOURCE_REASON_PRIMARY_ONLY_CROSS_DRAFT_COPY_WITHHELD,
                    "sourcePassOrdinal": 3,
                    "sourceRejectedJsonSha256": attestations[3][
                        "sourceRejectedJsonSha256"
                    ],
                    "canonicalMessagesSha256": attestations[3][
                        "canonicalMessagesSha256"
                    ],
                    "renderedPromptSha256": attestations[3][
                        "renderedPromptSha256"
                    ],
                    "renderedPromptUtf8ByteCount": attestations[3][
                        "renderedPromptUtf8ByteCount"
                    ],
                    "inputTokenIdsSha256": attestations[3][
                        "inputTokenIdsSha256"
                    ],
                    "inputTokenCount": attestations[3]["inputTokenCount"],
                    "outputSha256": attestations[3]["outputSha256"],
                    "completedJsonSha256": attestations[3][
                        "completedJsonSha256"
                    ],
                    "rejectionCode": "CrossDraftTitleContamination",
                    "accepted": False,
                },
                {
                    "candidateOrdinal": 2,
                    "seed": 3408,
                    "sourceSelectionReason":
                        SOURCE_REASON_PRIMARY_ONLY_CROSS_DRAFT_COPY_WITHHELD,
                    "sourcePassOrdinal": 3,
                    "sourceRejectedJsonSha256": attestations[4][
                        "sourceRejectedJsonSha256"
                    ],
                    "canonicalMessagesSha256": attestations[4][
                        "canonicalMessagesSha256"
                    ],
                    "renderedPromptSha256": attestations[4][
                        "renderedPromptSha256"
                    ],
                    "renderedPromptUtf8ByteCount": attestations[4][
                        "renderedPromptUtf8ByteCount"
                    ],
                    "inputTokenIdsSha256": attestations[4][
                        "inputTokenIdsSha256"
                    ],
                    "inputTokenCount": attestations[4]["inputTokenCount"],
                    "outputSha256": attestations[4]["outputSha256"],
                    "completedJsonSha256": attestations[4][
                        "completedJsonSha256"
                    ],
                    "rejectionCode": None,
                    "accepted": True,
                },
            ],
            failure_state._FAILURE_CONTEXT["recoveryPoolLedger"],
        )

    def test_every_frozen_semantic_code_advances_without_mutating_pool_input(self):
        retryable_codes = (
            "ThirdPersonCreatorFraming",
            "UnsupportedCreatorEmbodiment",
            "GenericOpening",
            "UnsupportedMentalState",
            "UnreviewedTranscriptReuse",
            "TitleDescriptionRepetition",
            "RedundantGameIdentity",
            "AnalysisBookkeeping",
            "OutputLanguage",
            "NonRetrospectiveVoice",
            "IncompleteTitle",
            "CrossDraftTitleContamination",
            "UnstableReadableTextReuse",
            "FirstPersonTitleSubject",
            "GameHashtag",
            "UncoupledKnowledgeReference",
            "UnsupportedTag",
            "TagShape",
            "UnsupportedKnowledgeGrounding",
            "GroundedRefinementUnchanged",
            "UnresolvedVisualGrounding",
            "RerollTitleTooSimilar",
        )
        original_failure_code = (
            grounded_metadata_pipeline._validation_failure_code
        )
        for forced_code in retryable_codes:
            with self.subTest(forced_code=forced_code):
                failure_state._reset_failure_context(
                    "run-grounded-editorial-metadata-batch"
                )
                source = _request(0, "DirectAction")
                packet = _packet(source)
                first = _rejected_error(
                    "Grounded metadata used a command, present-tense, or "
                    "gerund title opening.",
                    "Lyra opens the visible door",
                )
                first.rejected_title_body = "Lyra opens the visible door"
                first.offending_action_field = "titleBody"
                first.offending_action_form = "opens"
                repeated = _rejected_error(
                    "Grounded metadata used third-person creator framing.",
                    "A person stood beside the visible door",
                )
                repeated.rejected_title_body = (
                    "A person stood beside the visible door"
                )
                repeated.offending_action_field = "titleBody"
                repeated.offending_action_form = "stood"
                repeated_again = _rejected_error(
                    "Grounded metadata used third-person creator framing.",
                    "A person stood beside the visible door",
                )
                rejected_pool = _rejected_error(
                    "Forced completed semantic rejection.",
                    "A bounded semantic candidate",
                )
                rejected_pool.forced_code = forced_code
                outputs = [
                    first,
                    repeated,
                    repeated_again,
                    rejected_pool,
                    _generated(
                        "Opened the visible door behind us #ExampleGame"
                    ),
                ]
                pool_message_hashes: list[str] = []

                def generate(*args, **kwargs):
                    if kwargs.get("synthesis_decoding") is not None:
                        canonical = json.dumps(
                            args[2],
                            ensure_ascii=False,
                            sort_keys=True,
                            separators=(",", ":"),
                        ).encode("utf-8")
                        pool_message_hashes.append(
                            hashlib.sha256(canonical).hexdigest()
                        )
                    return _return_or_raise_attested(
                        outputs.pop(0),
                        args,
                        kwargs,
                    )

                def failure_code(error):
                    return getattr(
                        error,
                        "forced_code",
                        original_failure_code(error),
                    )

                with patch.object(
                    grounded_metadata_pipeline,
                    "_generate_json_once",
                    side_effect=generate,
                ), patch.object(
                    grounded_metadata_pipeline,
                    "_validation_failure_code",
                    side_effect=failure_code,
                ):
                    result = _synthesize_case(
                        source,
                        1,
                        "prompt",
                        packet,
                        True,
                        None,
                        None,
                        None,
                        None,
                        None,
                        _Session(),
                    )

                self.assertEqual(2, len(pool_message_hashes))
                self.assertEqual(1, len(set(pool_message_hashes)))
                self.assertEqual(
                    2,
                    result["generation"][
                        "synthesisRecoveryPoolSelectedCandidateOrdinal"
                    ],
                )
                self.assertEqual(
                    forced_code,
                    result["generation"]["rejectedValidationRules"][3],
                )

    def test_unknown_and_technical_pool_failures_stop_before_seed_two(self):
        def duplicate_prefix():
            first = _rejected_error(
                "Grounded metadata used a command, present-tense, or gerund "
                "title opening.",
                "Lyra opens the visible door",
            )
            first.rejected_title_body = "Lyra opens the visible door"
            first.offending_action_field = "titleBody"
            first.offending_action_form = "opens"
            repeated = _rejected_error(
                "Grounded metadata used third-person creator framing.",
                "A person stood beside the visible door",
            )
            repeated.rejected_title_body = "A person stood beside the visible door"
            repeated.offending_action_field = "titleBody"
            repeated.offending_action_form = "stood"
            repeated_again = _rejected_error(
                "Grounded metadata used third-person creator framing.",
                "A person stood beside the visible door",
            )
            return [first, repeated, repeated_again]

        for failure_kind in (
            "unknown",
            "technical",
            "usage",
            "attestation",
            "watchdog",
        ):
            with self.subTest(failure_kind=failure_kind):
                failure_state._reset_failure_context(
                    "run-grounded-editorial-metadata-batch"
                )
                source = _request(0, "DirectAction")
                packet = _packet(source)
                outputs = duplicate_prefix()
                call_count = 0

                def generate(*args, **kwargs):
                    nonlocal call_count
                    call_count += 1
                    if outputs:
                        return _return_or_raise_attested(
                            outputs.pop(0),
                            args,
                            kwargs,
                        )
                    if failure_kind == "technical":
                        raise InferenceError("GPU generation failed.")
                    if failure_kind == "usage":
                        raise UsageOrInputError("Parser contract failed.")
                    if failure_kind == "watchdog":
                        raise GenerationWallClockBudgetExceededError(
                            "Generation wall-clock budget expired."
                        )
                    rejected = _rejected_error(
                        "Unrecognized completed validator failure.",
                        "A bounded but rejected candidate",
                    )
                    try:
                        return _return_or_raise_attested(
                            rejected,
                            args,
                            kwargs,
                        )
                    except InferenceError as error:
                        if failure_kind == "attestation":
                            error.synthesis_attestation[
                                "canonicalMessagesSha256"
                            ] = "d" * 64
                        raise

                expected_error = (
                    UsageOrInputError
                    if failure_kind == "usage"
                    else GenerationWallClockBudgetExceededError
                    if failure_kind == "watchdog"
                    else AssertionError
                    if failure_kind == "attestation"
                    else InferenceError
                )
                with patch.object(
                    grounded_metadata_pipeline,
                    "_generate_json_once",
                    side_effect=generate,
                ), self.assertRaises(expected_error):
                    _synthesize_case(
                        source,
                        1,
                        "prompt",
                        packet,
                        True,
                        None,
                        None,
                        None,
                        None,
                        None,
                        _Session(),
                    )

                self.assertEqual(4, call_count)
                self.assertLessEqual(
                    len(failure_state._FAILURE_CONTEXT["recoveryPoolLedger"]),
                    1,
                )

    def test_recovery_pool_ledger_survives_full_diagnostic_capacity(self):
        source = _request(0, "DirectAction")
        packet = _packet(source)
        first = _rejected_error(
            "Grounded metadata used a command, present-tense, or gerund title opening.",
            "Lyra opens the visible door",
        )
        first.rejected_title_body = "Lyra opens the visible door"
        first.offending_action_field = "titleBody"
        first.offending_action_form = "opens"
        repeated = _rejected_error(
            "Grounded metadata used third-person creator framing.",
            "A person stood beside the visible door",
        )
        repeated.rejected_title_body = "A person stood beside the visible door"
        repeated.offending_action_field = "titleBody"
        repeated.offending_action_form = "stood"
        repeated_again = _rejected_error(
            "Grounded metadata used third-person creator framing.",
            "A person stood beside the visible door",
        )
        semantic_failures = [
            _rejected_error(
                "Grounded metadata title used content unique to a non-primary "
                "chronological draft.",
                f"Rejected pool candidate {ordinal}",
            )
            for ordinal in range(1, 5)
        ]
        outputs = [first, repeated, repeated_again, *semantic_failures]

        def generate(*args, **kwargs):
            return _return_or_raise_attested(outputs.pop(0), args, kwargs)

        for ordinal in range(8):
            failure_state._add_failure_diagnostic(f"filled-{ordinal}")
        with patch.object(
            grounded_metadata_pipeline,
            "_generate_json_once",
            side_effect=generate,
        ), self.assertRaises(InferenceError):
            _synthesize_case(
                source,
                1,
                "prompt",
                packet,
                True,
                None,
                None,
                None,
                None,
                None,
                _Session(),
            )

        payload = failure_envelope._failure_payload(
            "run-grounded-editorial-metadata-batch",
            "InferenceError",
            4,
            "semantic pool exhausted",
        )
        self.assertEqual(8, len(payload["diagnostics"]))
        self.assertEqual(4, len(payload["recoveryPoolLedger"]))
        self.assertEqual([1, 2, 3, 4], [
            item["candidateOrdinal"] for item in payload["recoveryPoolLedger"]
        ])
        self.assertTrue(all(
            item["rejectionCode"] == "CrossDraftTitleContamination"
            and not item["accepted"]
            for item in payload["recoveryPoolLedger"]
        ))
        ledger_text = json.dumps(payload["recoveryPoolLedger"])
        self.assertNotIn("A:\\", ledger_text)
        self.assertNotIn("Rejected pool candidate", ledger_text)

    def test_actor_authority_failure_and_diversity_retry_share_bounded_passes(self):
        source = _request(0, "DirectAction")
        reroll = _request(1, "ConcreteDetail")
        packet = _packet(source)
        prior = (
            _reroll_title_reference(
                source,
                "Opened the visible door #ExampleGame",
            ),
        )

        result = _synthesize(
            reroll,
            packet,
            prior,
            [
                _rejected_error(
                    "Grounded metadata used unsupported creator embodiment.",
                    "I opened the visible door",
                ),
                _generated("Opened the visible door #ExampleGame"),
                _generated("Found the doorway open behind us #ExampleGame"),
            ],
        )

        self.assertEqual(
            ["UnsupportedCreatorEmbodiment", "RerollTitleTooSimilar"],
            result["generation"]["rejectedValidationRules"],
        )
        self.assertEqual(3, result["generation"]["synthesisPassCount"])
        self.assertEqual(packet.fact_sha256, result["generation"]["groundingPacketFactSha256"])

    def test_retry_history_keeps_only_immediately_previous_rejected_draft(self):
        source = _request(0, "DirectAction")
        packet = _packet(source)
        named_present = _rejected_error(
            "Grounded metadata used a command, present-tense, or gerund title opening.",
            "Lyra confronts Rowan beside the doorway",
        )
        named_present.rejected_title_body = "Lyra confronts Rowan beside the doorway"
        named_present.offending_action_form = "confronts"
        generic_present = _rejected_error(
            "Grounded metadata used third-person creator framing or generic "
            "observer-person framing.",
            "A person stood beside the doorway",
        )
        outputs = [
            named_present,
            generic_present,
            _generated("Lyra confronted Rowan beside the doorway #ExampleGame"),
        ]
        messages = []

        def generate(*args, **kwargs):
            messages.append(args[2])
            return _return_or_raise_attested(outputs.pop(0), args, kwargs)

        with patch.object(
            grounded_metadata_pipeline,
            "_generate_json_once",
            side_effect=generate,
        ):
            result = _synthesize_case(
                source,
                1,
                "prompt",
                packet,
                True,
                None,
                None,
                None,
                None,
                None,
                _Session(),
            )

        self.assertEqual(
            ["NonRetrospectiveVoice", "ThirdPersonCreatorFraming"],
            result["generation"]["rejectedValidationRules"],
        )
        self.assertEqual(3, result["generation"]["synthesisPassCount"])
        final_messages = messages[2]
        self.assertEqual(
            ["system", "user", "assistant", "user"],
            [message["role"] for message in final_messages],
        )
        final_retry = final_messages[-1]["content"][0]["text"]
        self.assertIn("not factual evidence", final_retry)
        self.assertIn("exact canonical identity", final_retry)
        self.assertIn("required grounding binding", final_retry)
        self.assertIn("retrospective completed action", final_retry)
        self.assertIn("use the neutral phrase a person", final_retry)
        self.assertIn("never substitute man, woman, guy", final_retry)
        self.assertNotIn("Ghostwire", final_retry)

        first_rejected_json = named_present.schema_valid_rejected_json
        immediately_previous_json = generic_present.schema_valid_rejected_json
        self.assertEqual(
            first_rejected_json,
            messages[1][2]["content"][0]["text"],
        )
        self.assertEqual(
            immediately_previous_json,
            final_messages[2]["content"][0]["text"],
        )
        self.assertNotIn(
            "Lyra confronts Rowan",
            final_messages[2]["content"][0]["text"],
        )
        self.assertLessEqual(
            len(final_messages[2]["content"][0]["text"].encode("utf-8")),
            MAXIMUM_REJECTED_JSON_UTF8_BYTES,
        )
        self.assertEqual(
            ["NonRetrospectiveVoice", "ThirdPersonCreatorFraming"],
            json.loads(
                final_retry.split("Cumulative typed rejected rules: ", 1)[1]
                .split(". Correct only those rejected rules.", 1)[0]
            ),
        )

    def test_fresh_ghostwire_failure_sequence_uses_only_the_previous_draft(self):
        source = _request(0, "DirectAction")
        packet = _packet(source)
        present_named = _rejected_error(
            "Grounded metadata used a command, present-tense, or gerund title opening.",
            "Hannya confronts Akito amid glowing chains",
        )
        present_named.rejected_title_body = (
            "Hannya confronts Akito amid glowing chains"
        )
        present_named.offending_action_form = "confronts"
        generic_person = _rejected_error(
            "Grounded metadata used third-person creator framing or generic "
            "observer-person framing.",
            "A man in a skull mask stands in fog",
        )
        generated = _generated(
            "Glowing chains tightened around the masked figure #ExampleGame"
        )
        outputs = [present_named, generic_person, generated]
        calls: list[list[dict]] = []

        def generate(*args, **kwargs):
            calls.append(args[2])
            return _return_or_raise_attested(outputs.pop(0), args, kwargs)

        with patch.object(
            grounded_metadata_pipeline,
            "_generate_json_once",
            side_effect=generate,
        ):
            result = _synthesize_case(
                source,
                1,
                "prompt",
                packet,
                True,
                None,
                None,
                None,
                None,
                None,
                _Session(),
            )

        self.assertEqual(
            ["NonRetrospectiveVoice", "ThirdPersonCreatorFraming"],
            result["generation"]["rejectedValidationRules"],
        )
        self.assertEqual(
            generic_person.schema_valid_rejected_json,
            calls[2][2]["content"][0]["text"],
        )
        self.assertNotIn("Hannya confronts", calls[2][2]["content"][0]["text"])
        correction = calls[2][-1]["content"][0]["text"]
        self.assertTrue(correction.endswith(
            "Return one complete replacement JSON object under the unchanged schema."
        ))
        self.assertIn("Correct only those rejected rules", correction)
        self.assertIn("Preserve every independently grounded fact", correction)
        self.assertIn("not factual evidence", correction)



if __name__ == "__main__":
    unittest.main()
