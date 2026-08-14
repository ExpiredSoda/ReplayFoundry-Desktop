"""Model-free contract tests for the bounded editorial rephrase pass."""
from __future__ import annotations

from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
from types import SimpleNamespace
import unittest

from replayfoundry_visual_semantic.errors import InferenceError
from replayfoundry_visual_semantic.editorial.grounded_metadata_pipeline_state import (
    SynthesisFunctions,
    SynthesisProgress,
)
from replayfoundry_visual_semantic.editorial.grounded_metadata_rephrase import (
    OUTCOME_APPLIED,
    OUTCOME_NO_CHANGE,
    OUTCOME_SEMANTIC_REJECTION,
    _rephrase_messages,
    require_policy,
    run_editorial_rephrase,
)
def _request() -> dict:
    return {
        "candidateId": "candidate-rephrase",
        "attempt": 0,
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
            "defaultTags": ["door"],
            "voicePerspective": "CreatorFirstPerson",
            "variantIntent": "DirectAction",
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


def _json(title: str, description: str, tags: list[str] | None = None) -> str:
    return json.dumps(
        {
            "titleBody": title,
            "description": description,
            "tags": tags or ["door"],
            "grounding": [],
            "temporalVoice": "RetrospectivePast",
        },
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    )


def _attestation(args, kwargs, completed_json: str) -> dict:
    context = kwargs["synthesis_attestation_context"]
    messages = json.dumps(
        args[2], ensure_ascii=False, sort_keys=True, separators=(",", ":")
    ).encode("utf-8")
    completed_sha = hashlib.sha256(completed_json.encode("utf-8")).hexdigest()
    return {
        **context,
        "canonicalMessagesSha256": hashlib.sha256(messages).hexdigest(),
        "renderedPromptSha256": hashlib.sha256(messages).hexdigest(),
        "renderedPromptUtf8ByteCount": len(messages),
        "inputTokenIdsSha256": "c" * 64,
        "inputTokenCount": 100,
        "outputSha256": completed_sha,
        "completedJsonSha256": completed_sha,
        "rejectionCode": None,
        "accepted": False,
    }


def _context():
    request = _request()
    draft = {
        "environment": "An interior",
        "environmentUncertain": False,
        "subjectsAndObjects": ["A visible door"],
        "actions": ["A door opened"],
        "readableText": [],
        "uncertainties": [],
    }
    return SimpleNamespace(
        request=request,
        synthesis_request=request,
        visual_drafts=[draft],
        primary_visual_draft_ordinal=1,
        primary_actor_authority="CreatorControlled",
        primary_creator_experience_relation="CreatorActed",
        all_prior_accepted_titles=(),
        model=object(),
        processor=object(),
        torch=None,
        torchcodec=None,
        process_vision_info=None,
        session=object(),
        grammar=object(),
        base_audit=object(),
        case_ordinal=1,
    )


def _progress() -> SynthesisProgress:
    source = _json(
        "I opened the visible door",
        "The visible door opened inside the interior.",
    )
    return SynthesisProgress(
        metadata={
            "title": "I opened the visible door #ExampleGame",
            "description": "The visible door opened inside the interior.",
            "tags": ["door"],
            "grounding": [],
        },
        completed_json=source,
        diversity_result=None,
    )


def _functions(output: str | Exception) -> SynthesisFunctions:
    def generate(*args, **kwargs):
        if isinstance(output, Exception):
            raise output
        sha = hashlib.sha256(output.encode("utf-8")).hexdigest()
        attestation = _attestation(args, kwargs, output)
        try:
            metadata = args[12](output)
        except InferenceError as error:
            error.schema_valid_rejected_json = output
            error.synthesis_attestation = attestation
            raise
        return (
            metadata,
            SimpleNamespace(),
            SimpleNamespace(),
            sha,
            None,
            output,
            attestation,
        )

    return SynthesisFunctions(
        generate,
        generate,
        lambda _error: "StrictOutputValidation",
        lambda: [],
    )


class GroundedMetadataRephraseTests(unittest.TestCase):
    def test_policy_and_messages_keep_the_pass_text_only_and_bounded(self):
        require_policy()
        messages = _rephrase_messages(
            _json("A door opened", "The door opened."),
            {"primaryVisual": {"actions": ["A door opened"]}},
            "DirectAction",
        )
        rendered = json.dumps(messages, ensure_ascii=False)
        self.assertNotIn('"type": "video"', rendered)
        self.assertIn("Rewrite only titleBody and description", rendered)
        self.assertIn("Copy tags, grounding, and temporalVoice exactly", rendered)
        self.assertIn("rather than swapping synonyms", rendered)
        self.assertIn("change the opening, clause order, title", rendered)
        self.assertIn("description sentence plan", rendered)

    def test_creator_controlled_third_person_recovery_requires_first_person(self):
        source = _json(
            "Man with bloodied arm raised hand",
            "A man raised his bloodied arm, then turned toward a banner.",
        )
        authority = {
            "primaryVisual": {
                "subjectsAndObjects": [
                    "A camouflage uniform",
                    "A bloodied arm",
                    "A banner",
                ],
                "actions": [
                    "The controlled figure raised a hand",
                    "The controlled figure turned toward a banner",
                ],
                "actorAuthority": "CreatorControlled",
                "creatorExperienceRelation": "CreatorActed",
            },
        }
        messages = _rephrase_messages(
            source,
            authority,
            "DirectAction",
            "ReviewRequiredMetadata",
            "ThirdPersonCreatorFraming",
        )
        rendered = json.dumps(messages, ensure_ascii=False)
        self.assertIn("Narrate only the supported controlled action", rendered)
        self.assertIn("retrospectively as I or my", rendered)
        self.assertIn("explicit I title is authorized here", rendered)
        self.assertNotIn("Do not invent I or we", rendered)

    def test_creator_controlled_non_retrospective_recovery_requires_past_tense(self):
        messages = _rephrase_messages(
            _json(
                "Raise my hand toward the banner",
                "I raise my hand and turn toward the banner.",
            ),
            {
                "primaryVisual": {
                    "subjectsAndObjects": ["A raised hand", "A banner"],
                    "actions": [
                        "The controlled figure raised a hand",
                        "The controlled figure turned toward a banner",
                    ],
                    "actorAuthority": "CreatorControlled",
                    "creatorExperienceRelation": "CreatorActed",
                },
            },
            "DirectAction",
            "ReviewRequiredMetadata",
            "NonRetrospectiveVoice",
        )
        rendered = json.dumps(messages, ensure_ascii=False)
        self.assertIn("Mandatory retrospective grammar form", rendered)
        self.assertIn("both be grammatically retrospective", rendered)
        self.assertIn("may begin with I or we", rendered)
        self.assertIn("unmistakable past-tense action", rendered)
        self.assertIn("Do not begin titleBody with a command", rendered)
        self.assertIn("Do not describe any action in present tense", rendered)

    def test_unestablished_non_retrospective_recovery_stays_neutral(self):
        messages = _rephrase_messages(
            _json("Turning toward a banner", "A man turns toward a banner."),
            {
                "primaryVisual": {
                    "actions": ["A figure turned toward a banner"],
                    "actorAuthority": "OtherPerson",
                    "creatorExperienceRelation": "Unestablished",
                },
            },
            "DirectAction",
            "ReviewRequiredMetadata",
            "NonRetrospectiveVoice",
        )
        rendered = json.dumps(messages, ensure_ascii=False)
        self.assertIn("Creator embodiment is not established", rendered)
        self.assertIn("Do not invent I or we", rendered)

    def test_unestablished_third_person_recovery_requires_neutral_action(self):
        messages = _rephrase_messages(
            _json("A man raised a hand", "A man turned toward a banner."),
            {
                "primaryVisual": {
                    "actions": ["A hand rose toward a head"],
                    "actorAuthority": "OtherPerson",
                    "creatorExperienceRelation": "Unestablished",
                },
            },
            "DirectAction",
            "ReviewRequiredMetadata",
            "ThirdPersonCreatorFraming",
        )
        rendered = json.dumps(messages, ensure_ascii=False)
        self.assertIn("a neutral human subject such as a person is permitted", rendered)
        self.assertIn("Player, character, streamer, creator, and camera wearer remain forbidden", rendered)
        self.assertIn("unmistakable retrospective past tense", rendered)
        self.assertIn("Do not invent I or we", rendered)
        self.assertNotIn("explicit I title is authorized here", rendered)

    def test_unestablished_creator_embodiment_removes_first_person_and_roles(self):
        messages = _rephrase_messages(
            _json(
                "I aimed a revolver down the corridor",
                "I raised my revolver while moving through a green corridor.",
            ),
            {
                "primaryVisual": {
                    "subjectsAndObjects": [
                        "A revolver",
                        "Green lockers",
                        "A dim corridor",
                    ],
                    "actions": ["A revolver aimed down a corridor"],
                    "actorAuthority": "Unknown",
                    "creatorExperienceRelation": "Unestablished",
                },
            },
            "DirectAction",
            "ReviewRequiredMetadata",
            "UnsupportedCreatorEmbodiment",
        )
        rendered = json.dumps(messages, ensure_ascii=False)
        self.assertIn("Remove I, we, my, and our", rendered)
        self.assertIn("A neutral human subject such as a person is permitted", rendered)
        self.assertIn("player, character, streamer, creator, and camera wearer remain forbidden", rendered)
        self.assertIn("unmistakable retrospective past tense", rendered)
        self.assertIn("visible person's body, weapon", rendered)
        self.assertNotIn("explicit I title is authorized here", rendered)
        self.assertIn('\\"rejectedAudienceCopyWithheld\\":true', rendered)
        self.assertNotIn("I aimed a revolver down the corridor", rendered)
        self.assertNotIn("I raised my revolver", rendered)
        self.assertIn('\\"temporalVoice\\":\\"RetrospectivePast\\"', rendered)

    def test_creator_controlled_embodiment_separates_other_person_details(self):
        messages = _rephrase_messages(
            _json(
                "I crossed the hall",
                "I crossed the hall as another person raised a weapon.",
            ),
            {
                "primaryVisual": {
                    "actions": ["The controlled view crossed a hall"],
                    "actorAuthority": "CreatorControlled",
                    "creatorExperienceRelation": "CreatorActed",
                },
            },
            "DirectAction",
            "ReviewRequiredMetadata",
            "UnsupportedCreatorEmbodiment",
        )
        rendered = json.dumps(messages, ensure_ascii=False)
        self.assertIn("retrospectively as I or my", rendered)
        self.assertIn("Keep another person's body detail", rendered)
        self.assertIn("never convert those into my body", rendered)

    def test_unsupported_interpretation_rebuilds_only_from_literal_authority(self):
        source = _json(
            "I finished the fight at the doorway",
            "I defeated the threat and finally made the area safe.",
        )
        messages = _rephrase_messages(
            source,
            {
                "primaryVisual": {
                    "environment": "A concrete room",
                    "subjectsAndObjects": ["A doorway", "A raised hand"],
                    "actions": ["The controlled figure raised a hand"],
                    "actorAuthority": "CreatorControlled",
                    "creatorExperienceRelation": "CreatorActed",
                },
            },
            "DirectAction",
            "ReviewRequiredMetadata",
            "UnsupportedMentalState",
        )
        rendered = json.dumps(messages, ensure_ascii=False)
        self.assertIn('\\"rejectedAudienceCopyWithheld\\":true', rendered)
        self.assertNotIn("I finished the fight", rendered)
        self.assertNotIn("made the area safe", rendered)
        self.assertIn("Mandatory literal action form", rendered)
        self.assertIn("completed physical actions already stated", rendered)
        self.assertIn("Omit emotion, intent, reaction, causality", rendered)
        self.assertIn("The controlled figure raised a hand", rendered)

    def test_valid_material_rephrase_applies(self):
        context = _context()
        progress = _progress()
        output = _json(
            "The visible door opened",
            "I opened the visible door inside the interior.",
        )
        run_editorial_rephrase(context, _functions(output), progress)
        self.assertTrue(progress.editorial_rephrase_attempted)
        self.assertTrue(progress.editorial_rephrase_applied)
        self.assertEqual(OUTCOME_APPLIED, progress.editorial_rephrase_outcome)
        self.assertEqual(
            "The visible door opened #ExampleGame",
            progress.metadata["title"],
        )

    def test_one_terminal_period_uses_canonical_validator_result(self):
        context = _context()
        progress = _progress()
        output = _json(
            "The visible door opened.",
            "I opened the visible door inside the interior.",
        )
        run_editorial_rephrase(context, _functions(output), progress)
        self.assertTrue(progress.editorial_rephrase_applied)
        self.assertEqual(OUTCOME_APPLIED, progress.editorial_rephrase_outcome)
        self.assertEqual(
            "The visible door opened #ExampleGame",
            progress.metadata["title"],
        )
        self.assertEqual(
            hashlib.sha256(output.encode("utf-8")).hexdigest(),
            progress.editorial_rephrase_output_json_sha256,
        )

    def test_identical_rephrase_retains_original(self):
        context = _context()
        progress = _progress()
        original = progress.metadata
        run_editorial_rephrase(
            context,
            _functions(progress.completed_json),
            progress,
        )
        self.assertFalse(progress.editorial_rephrase_applied)
        self.assertEqual(OUTCOME_NO_CHANGE, progress.editorial_rephrase_outcome)
        self.assertIs(original, progress.metadata)

    def test_immutable_field_change_retains_original(self):
        context = _context()
        progress = _progress()
        original = progress.metadata
        output = _json(
            "The visible door opened",
            "I opened the visible door inside the interior.",
            ["door", "interior"],
        )
        run_editorial_rephrase(context, _functions(output), progress)
        self.assertFalse(progress.editorial_rephrase_applied)
        self.assertEqual(
            OUTCOME_SEMANTIC_REJECTION,
            progress.editorial_rephrase_outcome,
        )
        self.assertEqual(
            "ImmutableFieldsChanged",
            progress.editorial_rephrase_rejection_code,
        )
        self.assertIs(original, progress.metadata)

    def test_strict_semantic_rejection_retains_original(self):
        context = _context()
        progress = _progress()
        original = progress.metadata
        output = _json(
            "A man opened the visible door",
            "A man opened the visible door inside the interior.",
        )
        run_editorial_rephrase(context, _functions(output), progress)
        self.assertFalse(progress.editorial_rephrase_applied)
        self.assertEqual(
            OUTCOME_SEMANTIC_REJECTION,
            progress.editorial_rephrase_outcome,
        )
        self.assertEqual(
            "ThirdPersonCreatorFraming",
            progress.editorial_rephrase_rejection_code,
        )
        self.assertIs(original, progress.metadata)

    def test_technical_failure_remains_terminal(self):
        progress = _progress()
        with self.assertRaisesRegex(InferenceError, "technical generation failure"):
            run_editorial_rephrase(
                _context(),
                _functions(InferenceError("technical generation failure")),
                progress,
            )
        self.assertTrue(progress.editorial_rephrase_attempted)
        self.assertFalse(progress.editorial_rephrase_applied)

if __name__ == "__main__":
    unittest.main()
