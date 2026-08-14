from __future__ import annotations

import re
import unittest
import hashlib

from replayfoundry_visual_semantic.editorial.grounded_metadata_command import (
    _duplicates_prior_synthesis,
    _knowledge_selection_messages,
    _knowledge_selection_prompt_text,
    _knowledge_selection_schema,
    _metadata_schema,
    _metadata_messages,
    _requires_primary_only_synthesis_evidence,
    _prompt_text,
    _retry_correction_envelope,
    _retry_feedback,
    _strict_metadata,
    _strict_knowledge_selection,
    _strict_visual_draft,
    _validation_failure_code,
    _validation_feedback,
    _visual_draft_messages,
    _visual_draft_prompt_text,
    _visual_draft_schema,
    _strict_visual_event_selection,
    _visual_event_selection_messages,
    _visual_event_selection_prompt_text,
    _visual_event_selection_schema,
    _visual_windows,
)
from replayfoundry_visual_semantic.editorial.grounded_knowledge_selection import (
    _current_knowledge_candidates,
    _request_with_selected_knowledge,
)
import json
import xgrammar as xgr
from xgrammar.testing import _is_grammar_accept_string
from replayfoundry_visual_semantic.commands import InferenceError, UsageOrInputError
from replayfoundry_visual_semantic.errors import NoDistinctPrimaryVisualEventError
from replayfoundry_visual_semantic.editorial.grounded_metadata_synthesis import (
    _model_context,
    _synthesis_draft,
    _stable_readable_text,
    _typed_retry_authority_anchor,
)
from replayfoundry_visual_semantic.editorial.grounded_metadata_contract import (
    validate_game_knowledge,
    validate_variant_intent,
    validate_visual_text,
)
from replayfoundry_visual_semantic.editorial.grounded_metadata_validation import (
    grounding_binding_id,
    reviewable_metadata,
)


def _visual_draft(
    environment: str,
    action: str,
    *,
    environment_uncertain: bool = False,
) -> dict[str, object]:
    return {
        "environment": environment,
        "environmentUncertain": environment_uncertain,
        "subjectsAndObjects": ["A directly visible object"],
        "actions": [action],
        "readableText": [],
        "uncertainties": [],
    }


def _event_assessment(
    ordinal: int,
    *,
    distinct_action: bool = False,
    object_interaction: bool = False,
    visible_outcome: bool = False,
    readable_interface_change: bool = False,
    routine_only: bool = False,
    uncertain: bool = False,
    actor_authority: str = "Unknown",
    creator_experience_relation: str = "Unestablished",
) -> dict[str, object]:
    return {
        "ordinal": ordinal,
        "distinctAction": distinct_action,
        "objectInteraction": object_interaction,
        "visibleOutcome": visible_outcome,
        "readableInterfaceChange": readable_interface_change,
        "routineOnly": routine_only,
        "uncertain": uncertain,
        "actorAuthority": actor_authority,
        "creatorExperienceRelation": creator_experience_relation,
    }


class GroundedMetadataPromptTests(unittest.TestCase):
    def test_reviewable_metadata_retains_schema_valid_copy_policy_failure(self):
        request = {
            "game": {"name": "Example Game", "hashtag": "#ExampleGame"},
            "transcripts": [],
            "profile": {"defaultTags": []},
            "gameKnowledge": None,
        }
        result = reviewable_metadata(
            '{"titleBody":"A person walks along the path",'
            '"description":"A person walks beside the rocks.",'
            '"tags":["path"],"grounding":[],'
            '"temporalVoice":"RetrospectivePast"}',
            request,
            primary_actor_authority="CreatorControlled",
            primary_creator_experience_relation="CreatorActed",
        )

        self.assertEqual(
            "A person walks along the path #ExampleGame",
            result["title"],
        )
        self.assertEqual(
            ["ThirdPersonCreatorFraming"],
            result["_reviewIssues"],
        )

    def test_reviewable_metadata_still_rejects_malformed_json(self):
        request = {
            "game": {"name": "Example Game", "hashtag": "#ExampleGame"},
            "transcripts": [],
            "profile": {"defaultTags": []},
            "gameKnowledge": None,
        }
        with self.assertRaises(InferenceError):
            reviewable_metadata('{"titleBody":', request)

    def test_variant_intents_keep_four_transcript_free_packages_distinct(self) -> None:
        for intent in (
            "DirectAction",
            "SpecificCuriosity",
            "OutcomeFocused",
            "ConcreteDetail",
        ):
            self.assertEqual(
                intent,
                validate_variant_intent(intent, [], "$.profile.variantIntent"),
            )
        with self.assertRaises(UsageOrInputError):
            validate_variant_intent(
                "CommentaryLed",
                [],
                "$.profile.variantIntent",
            )
        self.assertEqual(
            "CommentaryLed",
            validate_variant_intent(
                "CommentaryLed",
                [{"authority": "HumanReviewed"}],
                "$.profile.variantIntent",
            ),
        )

    def test_prompt_guides_complete_metadata_package_for_each_intent(self) -> None:
        prompt = _prompt_text()
        self.assertIn("guides titleBody, description, and tags", prompt)
        self.assertNotIn(
            "begin the description directly with that person",
            prompt,
        )
        self.assertIn(
            "a neutral human subject",
            prompt,
        )
        self.assertIn(
            "Keep that wording neutral and retrospective",
            prompt,
        )
        self.assertIn(
            "UserConfirmed or ReusedUserMemory game notes plus the bounded review",
            prompt,
        )
        self.assertIn(
            "SourcePathHint notes, automatic transcript text, and path wording never authorize",
            prompt,
        )
        self.assertIn(
            "any canonical claim taken from game knowledge still does",
            prompt,
        )
        for intent in (
            "DirectAction",
            "SpecificCuriosity",
            "OutcomeFocused",
            "ConcreteDetail",
            "CommentaryLed",
        ):
            self.assertIn(intent, prompt)

    def test_visual_text_contract_requires_repeated_frame_provenance(self) -> None:
        value = {
            "samplingPolicyVersion": "visual-text-sampling-1.0",
            "stabilityPolicyVersion": "visual-text-stability-1.1",
            "provider": {
                "name": "Windows OCR",
                "version": "1.0",
                "backend": "CPU",
                "runtimeVersion": "Windows",
                "languageTag": "en-US",
            },
            "sampledFrameCount": 3,
            "groundingAnchors": [{
                "text": "Objective Updated",
                "sourceKind": "Line",
                "occurrenceCount": 2,
                "sourceTimestampsSeconds": [11.0, 12.0],
            }],
            "diagnosticAnchors": [{
                "text": "One Frame",
                "sourceKind": "Line",
                "occurrenceCount": 1,
                "sourceTimestampsSeconds": [13.0],
            }],
        }
        validated = validate_visual_text(value, "$.visualText", 10.0, 20.0)
        self.assertEqual("Objective Updated", validated["groundingAnchors"][0]["text"])
        value["groundingAnchors"][0]["occurrenceCount"] = 1
        with self.assertRaises(UsageOrInputError):
            validate_visual_text(value, "$.visualText", 10.0, 20.0)
        value["groundingAnchors"][0]["occurrenceCount"] = 2
        value["groundingAnchors"][0]["text"] = "Objective-Updated"
        with self.assertRaises(UsageOrInputError):
            validate_visual_text(value, "$.visualText", 10.0, 20.0)

    def test_strict_metadata_requires_retrospective_creator_voice(self) -> None:
        request = {
            "game": {
                "name": "Example Game",
                "hashtag": "#ExampleGame",
                "notes": None,
            },
            "transcripts": [],
            "evidence": [],
            "visualText": None,
            "gameKnowledge": None,
            "evidence": [],
            "gameKnowledge": None,
            "profile": {
                "audienceAddress": "Chat",
                "namingGuidance": None,
                "defaultTags": [],
                "voicePerspective": "CreatorFirstPerson",
                "variantIntent": "DirectAction",
            },
        }
        result = _strict_metadata(
            '{"titleBody":"Upgraded my equipment",'
            '"description":"I upgraded my equipment before entering the next area.",'
            '"tags":["equipment"],"grounding":[],'
            '"temporalVoice":"RetrospectivePast"}',
            request,
        )
        self.assertTrue(result["title"].startswith("Upgraded"))
        for opening in ("Upgrade my equipment", "Upgrading my equipment"):
            with self.assertRaises(InferenceError):
                _strict_metadata(
                    json.dumps({
                        "titleBody": opening,
                        "description": "I upgraded my equipment before entering the next area.",
                        "tags": ["equipment"],
                        "grounding": [],
                        "temporalVoice": "RetrospectivePast",
                    }),
                    request,
                )
        with self.assertRaises(InferenceError):
            _strict_metadata(
                '{"titleBody":"Spacecraft enters orbit",'
                '"description":"A spacecraft descends through cloud cover. A checkpoint appears.",'
                '"tags":["spacecraft"],"grounding":[],'
                '"temporalVoice":"RetrospectivePast"}',
                request,
            )
        first_person = _strict_metadata(
            '{"titleBody":"I upgraded my equipment",'
            '"description":"I upgraded my equipment before entering the next area.",'
            '"tags":["equipment"],"grounding":[],'
            '"temporalVoice":"RetrospectivePast"}',
            request,
        )
        self.assertTrue(first_person["title"].startswith("I upgraded"))
        first_person_with_present_form_noun = _strict_metadata(
            '{"titleBody":"Chose the next skill",'
            '"description":"I opened the skill menu, compared the available upgrades, and confirmed one.",'
            '"tags":["skill menu"],"grounding":[],'
            '"temporalVoice":"RetrospectivePast"}',
            request,
        )
        self.assertTrue(
            first_person_with_present_form_noun["title"].startswith("Chose")
        )
        noun_subject = _strict_metadata(
            '{"titleBody":"A blue figure pulsed beneath the sign",'
            '"description":"A blue figure pulsed beneath the illuminated sign.",'
            '"tags":["figure"],"grounding":[],'
            '"temporalVoice":"RetrospectivePast"}',
            request,
        )
        self.assertTrue(noun_subject["title"].startswith("A blue figure pulsed"))
        nominal_title = _strict_metadata(
            '{"titleBody":"The Dark Hospital Corridor",'
            '"description":"I crossed the dark corridor and reached the stairwell.",'
            '"tags":["corridor"],"grounding":[],'
            '"temporalVoice":"RetrospectivePast"}',
            request,
        )
        self.assertTrue(nominal_title["title"].startswith("The Dark Hospital Corridor"))
        with self.assertRaises(InferenceError):
            _strict_metadata(
                '{"titleBody":"Hand holds the note",'
                '"description":"A hand held the note near the door.",'
                '"tags":["note"],"grounding":[],'
                '"temporalVoice":"RetrospectivePast"}',
                request,
            )
        with self.assertRaises(InferenceError):
            _strict_metadata(
                '{"titleBody":"Opened the hidden door then a",'
                '"description":"I opened the hidden door.",'
                '"tags":["door"],"grounding":[],'
                '"temporalVoice":"RetrospectivePast"}',
                request,
            )

    def test_strict_metadata_rejects_non_primary_title_content(self) -> None:
        request = {
            "game": {"name": "Example Game", "hashtag": "#ExampleGame"},
            "transcripts": [],
            "visualTextAnchors": [],
        }
        drafts = [
            {
                "environment": "an elevator",
                "subjectsAndObjects": ["portal"],
                "actions": ["entered portal"],
            },
            {
                "environment": "an alley",
                "subjectsAndObjects": ["spectral worker"],
                "actions": ["spoke with worker"],
            },
        ]
        with self.assertRaises(InferenceError):
            _strict_metadata(
                '{"titleBody":"Entered the portal",'
                '"description":"I later spoke with a spectral worker.",'
                '"tags":["portal"],"grounding":[],'
                '"temporalVoice":"RetrospectivePast"}',
                request,
                drafts,
                2,
            )

    def test_strict_metadata_rejects_actions_stronger_than_primary_draft(self) -> None:
        request = {
            "game": {"name": "Example Game", "hashtag": "#ExampleGame"},
            "transcripts": [],
            "visualTextAnchors": [],
        }
        cases = (
            (
                "Defeated the Greater Festering Hives",
                "I attacked the Greater Festering Hives while its health bar remained visible.",
                "The viewpoint attacked the Greater Festering Hives.",
                "defeated",
            ),
            (
                "Entered the tropical landscape",
                "I selected NEW GAME while a tropical backdrop remained behind the menu.",
                "The viewpoint selected NEW GAME and opened a save-slot menu.",
                "entered",
            ),
            (
                "Passed through the yellow archway",
                "A purple creature remained beneath the archway during combat.",
                "The creature remained beneath a yellow archway while attacks continued.",
                "passed through",
            ),
            (
                "The creature detonated and reappeared",
                "The creature remained visible while red particles surrounded it.",
                "Red particles surrounded the creature while it remained visible.",
                "detonated",
            ),
        )
        for title_body, description, primary_action, offending_form in cases:
            with self.subTest(title_body=title_body):
                with self.assertRaises(InferenceError) as rejected:
                    _strict_metadata(
                        json.dumps({
                            "titleBody": title_body,
                            "description": description,
                            "tags": ["combat"],
                            "grounding": [],
                            "temporalVoice": "RetrospectivePast",
                        }),
                        request,
                        [_visual_draft("A visible gameplay area", primary_action)],
                        1,
                    )
                self.assertEqual(
                    "UnsupportedMentalState",
                    _validation_failure_code(rejected.exception),
                )
                self.assertEqual(
                    offending_form,
                    _retry_correction_envelope(rejected.exception)[
                        "offendingActionForm"
                    ].casefold(),
                )

        supported = _strict_metadata(
            json.dumps({
                "titleBody": "Defeated the Greater Festering Hives",
                "description": "The enemy collapsed after its health bar reached zero.",
                "tags": ["combat"],
                "grounding": [],
                "temporalVoice": "RetrospectivePast",
            }),
            request,
            [_visual_draft(
                "A visible gameplay area",
                "The enemy collapsed after its health bar reached zero.",
            )],
            1,
        )
        self.assertTrue(supported["title"].startswith("Defeated"))

    def test_strict_metadata_rejects_unstable_readable_text_reuse(self) -> None:
        request = {
            "game": {
                "name": "Example Game",
                "hashtag": "#ExampleGame",
                "source": "UserConfirmed",
                "notes": None,
            },
            "transcripts": [],
            "visualTextAnchors": [],
            "visualText": None,
            "evidence": [],
            "gameKnowledge": None,
            "profile": {
                "audienceAddress": "Viewers",
                "namingGuidance": None,
                "reusableDescriptionSignature": None,
                "defaultTags": [],
                "voicePerspective": "CreatorFirstPerson",
                "variantIntent": "DirectAction",
            },
        }
        drafts = [
            {
                "environment": "an alley",
                "subjectsAndObjects": ["illuminated figure"],
                "actions": ["figure pulsed"],
                "readableText": ["UNSTABLE LONG SIGN WORDING"],
            },
        ]
        with self.assertRaises(InferenceError) as rejected:
            _strict_metadata(
                '{"titleBody":"An illuminated figure pulsed",'
                '"description":"An illuminated figure pulsed beneath UNSTABLE LONG SIGN WORDING.",'
                '"tags":["figure"],"grounding":[],'
                '"temporalVoice":"RetrospectivePast"}',
                request,
                drafts,
                1,
            )
        self.assertEqual(
            "UnstableReadableTextReuse",
            _validation_failure_code(rejected.exception),
        )
        self.assertEqual(
            {
                "nonEvidence": True,
                "forbiddenReadableTextPhrases": [
                    "unstable long sign wording",
                ],
                "affectedAudienceFields": ["Description"],
            },
            _retry_correction_envelope(rejected.exception),
        )
        retry_messages = _metadata_messages(
            request,
            _prompt_text(),
            validation_feedback=_validation_feedback(
                "UnstableReadableTextReuse"
            ),
            grounded_drafts=drafts,
            schema_valid_rejected_json=(
                '{"titleBody":"An illuminated figure pulsed",'
                '"description":"An illuminated figure pulsed beneath '
                'UNSTABLE LONG SIGN WORDING.","tags":["figure"],'
                '"grounding":[],"temporalVoice":"RetrospectivePast"}'
            ),
            rejected_rule_codes=("UnstableReadableTextReuse",),
            retry_correction_envelope=_retry_correction_envelope(
                rejected.exception
            ),
        )
        retry_text = retry_messages[-1]["content"][0]["text"]
        self.assertIn(
            '"forbiddenReadableTextPhrases":["unstable long sign wording"]',
            retry_text,
        )
        self.assertIn("must not be retained or paraphrased", retry_text)

    def test_stable_readable_text_rejects_a_near_spelling_drift(self) -> None:
        request = {
            "game": {
                "name": "Voidling Bound",
                "hashtag": "#VoidlingBound",
                "source": "UserConfirmed",
                "notes": None,
            },
            "transcripts": [],
            "visualText": {
                "groundingAnchors": [
                    {"text": "DESERT KWIPECK", "occurrenceCount": 4},
                ],
            },
            "evidence": [],
            "gameKnowledge": None,
            "profile": {
                "audienceAddress": "Viewers",
                "namingGuidance": None,
                "reusableDescriptionSignature": None,
                "defaultTags": [],
                "voicePerspective": "NeutralNoSubject",
                "variantIntent": "DirectAction",
            },
        }
        drafts = [
            {
                "environment": "a dark mountainous backdrop",
                "environmentUncertain": False,
                "subjectsAndObjects": ["a glowing green creature"],
                "actions": ["the creature was displayed on a circular platform"],
                "readableText": ["DESERT KWIPECK"],
                "uncertainties": [],
            },
        ]
        with self.assertRaises(InferenceError) as rejected:
            _strict_metadata(
                '{"titleBody":"Desert Knippeck appeared on platform",'
                '"description":"A glowing green creature named Desert Knippeck was displayed on a circular platform.",'
                '"tags":["Voidling Bound"],"grounding":[],'
                '"temporalVoice":"RetrospectivePast"}',
                request,
                drafts,
                1,
            )
        self.assertEqual(
            "UnstableReadableTextReuse",
            _validation_failure_code(rejected.exception),
        )
        self.assertEqual(
            {
                "nonEvidence": True,
                "forbiddenReadableTextPhrases": ["knippeck"],
                "affectedAudienceFields": ["Title", "Description"],
            },
            _retry_correction_envelope(rejected.exception),
        )

        accepted = _strict_metadata(
            '{"titleBody":"Desert Kwipeck appeared on platform",'
            '"description":"The glowing green creature was displayed on a circular platform.",'
            '"tags":["Voidling Bound"],"grounding":[],'
            '"temporalVoice":"RetrospectivePast"}',
            request,
            drafts,
            1,
        )
        self.assertEqual(
            "Desert Kwipeck appeared on platform #VoidlingBound",
            accepted["title"],
        )

    def test_unstable_three_word_ocr_identity_cannot_gain_subject_authority(self) -> None:
        request = {
            "game": {
                "name": "Voidling Bound",
                "hashtag": "#VoidlingBound",
                "source": "UserConfirmed",
                "notes": None,
            },
            "transcripts": [],
            "visualTextAnchors": [],
            "visualText": None,
            "evidence": [],
            "gameKnowledge": None,
            "profile": {
                "audienceAddress": "Viewers",
                "namingGuidance": None,
                "reusableDescriptionSignature": None,
                "defaultTags": [],
                "voicePerspective": "NeutralNoSubject",
                "variantIntent": "DirectAction",
            },
        }
        drafts = [
            {
                "environment": "a grassy clearing",
                "environmentUncertain": False,
                "subjectsAndObjects": ["A Greater Festering Wives enemy"],
                "actions": ["The enemy stood beside a wooden ruin"],
                "readableText": ["GREATER FESTERING WIVES 1"],
                "uncertainties": [],
            }
        ]

        self.assertEqual(
            [],
            _synthesis_draft(drafts[0], [])["subjectsAndObjects"],
        )
        with self.assertRaises(InferenceError) as rejected:
            _strict_metadata(
                '{"titleBody":"Greater Festering Wives stood in the clearing",'
                '"description":"Palm trees surrounded the grassy clearing.",'
                '"tags":["Voidling Bound"],"grounding":[],'
                '"temporalVoice":"RetrospectivePast"}',
                request,
                drafts,
                1,
            )
        self.assertEqual(
            "UnstableReadableTextReuse",
            _validation_failure_code(rejected.exception),
        )
        malformed_but_correlated = [
            {
                "environment": "an alley",
                "subjectsAndObjects": ["illuminated figure"],
                "actions": ["an illuminated figure pulsed beneath the sign"],
                "readableText": ["an illuminated figure pulsed beneath the sign"],
            },
        ]
        with self.assertRaises(InferenceError) as correlated_rejected:
            _strict_metadata(
                '{"titleBody":"An illuminated figure pulsed",'
                '"description":"An illuminated figure pulsed beneath the sign.",'
                '"tags":["figure"],"grounding":[],'
                '"temporalVoice":"RetrospectivePast"}',
                request,
                malformed_but_correlated,
                1,
            )
        self.assertEqual(
            "UnstableReadableTextReuse",
            _validation_failure_code(correlated_rejected.exception),
        )

    def test_game_knowledge_contract_accepts_only_current_policy(self) -> None:
        passage = "A masked visitor waits inside the clinic."
        knowledge = {
            "policyVersion": "1.4",
            "snapshotSha256": "a" * 64,
            "provider": {"name": "Provider", "version": "1.0"},
            "sources": [
                {
                    "id": "source-1",
                    "kind": "Wikipedia",
                    "role": "PrimaryArticle",
                    "title": "Example Game",
                    "pageUri": "https://example.org/game",
                    "revisionId": "1",
                    "revisionTimestampUtc": "2026-08-05T00:00:00Z",
                    "licenseIdentifier": "CC-BY-SA-4.0",
                    "licenseUri": "https://creativecommons.org/licenses/by-sa/4.0/",
                    "attribution": "Example contributors",
                    "contentSha256": "b" * 64,
                }
            ],
            "matches": [
                {
                    "id": "match-1",
                    "sourceId": "source-1",
                    "section": "Plot",
                    "text": passage,
                    "contentSha256": hashlib.sha256(passage.encode("utf-8")).hexdigest(),
                    "strength": "ClipLinked",
                    "temporalRelation": "CurrentEventCandidate",
                    "relevance": 0.8,
                    "matchedTerms": ["masked", "clinic"],
                    "clipEvidenceIds": ["visual-change-1"],
                }
            ],
        }

        validated = validate_game_knowledge(
            knowledge,
            "$.gameKnowledge",
            {"visual-change-1"},
        )
        self.assertEqual("1.4", validated["policyVersion"])
        knowledge["policyVersion"] = "1.3"
        with self.assertRaises(UsageOrInputError):
            validate_game_knowledge(
                knowledge,
                "$.gameKnowledge",
                {"visual-change-1"},
            )

    def test_general_game_context_survives_without_event_selection(self) -> None:
        request = {
            "gameKnowledge": {
                "policyVersion": "1.4",
                "matches": [
                    {
                        "id": "event-candidate",
                        "strength": "CandidateForVisualGrounding",
                        "temporalRelation": "CurrentEventCandidate",
                    },
                    {
                        "id": "broad-context",
                        "strength": "GeneralContext",
                        "temporalRelation": "Unspecified",
                        "section": "Overview",
                        "text": "Players control Ivo, a courier crossing Aurora City.",
                    },
                ],
            },
        }

        filtered = _request_with_selected_knowledge(request, "None")

        self.assertEqual(
            ["broad-context"],
            [item["id"] for item in filtered["gameKnowledge"]["matches"]],
        )

    def test_rephrase_prompt_prefers_gameplay_summary_over_object_inventory(self) -> None:
        from replayfoundry_visual_semantic.editorial.grounded_metadata_rephrase_messages import (
            _rephrase_messages,
        )

        messages = _rephrase_messages(
            '{"description":"A gate was crossed.","grounding":[],"tags":[],"temporalVoice":"RetrospectivePast","titleBody":"Crossed the gate"}',
            {"primaryVisual": {"actions": ["A figure crossed a gate"]}},
            "DirectAction",
        )
        policy = messages[0]["content"][0]["text"].casefold()
        self.assertIn("dominant gameplay beat", policy)
        self.assertIn("not a frame-by-frame report", policy)
        self.assertIn("not a", policy)
        self.assertIn("inventory", policy)

    def test_metadata_prompt_preserves_cross_chunk_continuity_without_examples(self) -> None:
        prompt = _prompt_text()
        lowered = prompt.casefold()
        self.assertIn("successive views of one continuous bounded clip", lowered)
        self.assertIn("do not rewrite successive actions as simultaneous actions", lowered)
        self.assertIn("do not rename one subject into several generic people", lowered)
        self.assertNotIn("hospital", lowered)
        self.assertNotIn("captain", lowered)

    def test_visual_event_selection_is_typed_and_deterministic(self) -> None:
        schema, schema_hash = _visual_event_selection_schema(3)
        self.assertIn('"maxItems":3', schema)
        self.assertEqual(64, len(schema_hash))
        selected = _strict_visual_event_selection(
            json.dumps(
                {
                    "assessments": [
                        _event_assessment(1, routine_only=True),
                        _event_assessment(
                            2,
                            distinct_action=True,
                            object_interaction=True,
                        ),
                        _event_assessment(
                            3,
                            distinct_action=True,
                            object_interaction=True,
                            visible_outcome=True,
                        ),
                    ]
                },
                separators=(",", ":"),
            ),
            [
                _visual_draft("Room", "A person walks."),
                _visual_draft("Room", "A hand opens an object."),
                _visual_draft("Room", "An object changes state."),
            ],
        )
        self.assertEqual(3, selected["primaryVisualDraftOrdinal"])
        with self.assertRaises((InferenceError, UsageOrInputError)):
            _strict_visual_event_selection(
                json.dumps({"assessments": [
                    _event_assessment(
                        2,
                        distinct_action=True,
                        object_interaction=True,
                        visible_outcome=True,
                    ),
                    _event_assessment(1, routine_only=True),
                ]}),
                [
                    _visual_draft("Room", "A person walks."),
                    _visual_draft("Room", "A hand opens an object."),
                ],
            )

    def test_visual_event_selection_cannot_override_typed_draft_uncertainty(self) -> None:
        drafts = [
            _visual_draft(
                "Unclear space",
                "A person walks.",
                environment_uncertain=True,
            ),
            _visual_draft("Room", "A person opens an object."),
        ]
        selected = _strict_visual_event_selection(
            json.dumps({"assessments": [
                _event_assessment(
                    1,
                    distinct_action=True,
                    object_interaction=True,
                    visible_outcome=True,
                ),
                _event_assessment(
                    2,
                    distinct_action=True,
                    object_interaction=True,
                    visible_outcome=True,
                ),
            ]}),
            drafts,
        )
        self.assertTrue(selected["assessments"][0]["uncertain"])
        self.assertEqual(2, selected["primaryVisualDraftOrdinal"])

    def test_visual_event_selection_does_not_promote_later_dialogue_by_ordinal(self) -> None:
        drafts = [
            _visual_draft("Exterior", "A person climbed a ladder."),
            _visual_draft("Interior", "A person spoke beside a doorway."),
            _visual_draft("Interior", "A person walked away from the doorway."),
        ]
        selected = _strict_visual_event_selection(
            json.dumps({"assessments": [
                _event_assessment(
                    1,
                    distinct_action=True,
                    object_interaction=True,
                ),
                _event_assessment(2),
                _event_assessment(3),
            ]}),
            drafts,
        )
        self.assertEqual(1, selected["primaryVisualDraftOrdinal"])

        all_unsupported = json.dumps({
            "assessments": [
                _event_assessment(1),
                _event_assessment(2),
                _event_assessment(3),
            ]
        })
        with self.assertRaises(NoDistinctPrimaryVisualEventError):
            _strict_visual_event_selection(all_unsupported, drafts)

    def test_visual_event_selection_prompt_and_messages_are_domain_neutral(self) -> None:
        prompt = _visual_event_selection_prompt_text()
        lowered = prompt.casefold()
        self.assertIn("one continuous clip", lowered)
        self.assertIn("do not use game knowledge", lowered)
        self.assertNotIn("hospital", lowered)
        messages = _visual_event_selection_messages(
            prompt,
            [
                _visual_draft("Room", "A person walks."),
                _visual_draft("Room", "A hand opens an object."),
            ],
        )
        self.assertFalse(
            any(
                item.get("type") == "video"
                for message in messages
                for item in message["content"]
            )
        )

    def test_single_visual_draft_retains_actor_authority_without_forcing_event(self) -> None:
        selected = _strict_visual_event_selection(
            json.dumps({
                "assessments": [_event_assessment(
                    1,
                    routine_only=True,
                    actor_authority="OtherPerson",
                    creator_experience_relation="Unestablished",
                )]
            }),
            [_visual_draft("Interior", "A masked figure stood still.")],
            require_distinct_primary=False,
        )
        self.assertEqual(1, selected["primaryVisualDraftOrdinal"])
        self.assertEqual(
            "OtherPerson",
            selected["assessments"][0]["actorAuthority"],
        )
        with self.assertRaises(InferenceError):
            _strict_visual_event_selection(
                json.dumps({
                    "assessments": [_event_assessment(
                        1,
                        distinct_action=True,
                        actor_authority="OtherPerson",
                        creator_experience_relation="CreatorActed",
                    )]
                }),
                [_visual_draft("Interior", "A masked figure moved.")],
                require_distinct_primary=False,
            )

    def test_metadata_messages_bind_actor_authority_to_complete_package(self) -> None:
        request = {
            "game": {
                "name": "Example Game",
                "hashtag": "#ExampleGame",
                "source": "UserConfirmed",
                "notes": "Lyra is the masked figure.",
            },
            "gameKnowledge": None,
            "visualTextAnchors": [],
            "clip": {},
            "transcripts": [],
            "evidence": [],
            "profile": {
                "audienceAddress": "Chat",
                "namingGuidance": None,
                "defaultTags": [],
                "voicePerspective": "CreatorFirstPerson",
                "variantIntent": "OutcomeFocused",
            },
        }
        messages = _metadata_messages(
            request,
            _prompt_text(),
            grounded_drafts=[
                _visual_draft("Interior", "A masked figure transformed.")
            ],
            primary_actor_authority="OtherPerson",
            primary_creator_experience_relation="CreatorEncountered",
        )
        text = messages[1]["content"][0]["text"]
        self.assertIn('"primaryActorAuthority":"OtherPerson"', text)
        self.assertIn(
            '"primaryCreatorExperienceRelation":"CreatorEncountered"',
            text,
        )
        self.assertIn('"notesAuthority":"UserConfirmed"', text)
        self.assertIn("title body, description, and tags as one package", text)
        self.assertIn(
            "must be no stronger than the literal action clauses",
            text,
        )
        self.assertIn(
            "Never upgrade an attempt, attack, ongoing interaction",
            text,
        )
        self.assertNotIn("Ghostwire", text)

    def test_metadata_synthesis_withholds_unreviewed_text_and_uncertain_environment(self) -> None:
        request = {
            "game": {
                "name": "Example Game",
                "hashtag": "#ExampleGame",
                "source": "UserConfirmed",
                "notes": None,
            },
            "clip": {
                "startSeconds": 0,
                "endSeconds": 10,
                "sourceDurationSeconds": 10,
                "deterministicScore": 80,
                "deterministicReason": "Bounded evidence.",
            },
            "transcripts": [],
            "evidence": [],
            "profile": {
                "audienceAddress": "Viewers",
                "namingGuidance": None,
                "reusableDescriptionSignature": None,
                "defaultTags": [],
                "voicePerspective": "CreatorFirstPerson",
                "variantIntent": "DirectAction",
            },
            "gameKnowledge": None,
        }
        draft = _visual_draft(
            "Uncertain exterior",
            "A person opens a visible object.",
            environment_uncertain=True,
        )
        draft["readableText"] = ["UNREVIEWED WORDS"]
        messages = _metadata_messages(request, _prompt_text(), grounded_drafts=[draft])
        payload = messages[1]["content"][0]["text"]
        self.assertNotIn("Uncertain exterior", payload)
        self.assertNotIn("UNREVIEWED WORDS", payload)
        self.assertIn("A person opens a visible object.", payload)

    def test_cross_draft_retry_exposes_only_the_selected_primary_packet(self) -> None:
        request = {
            "game": {
                "name": "Example Game",
                "hashtag": "#ExampleGame",
                "source": "UserConfirmed",
                "notes": "WITHHELD GAME NOTE",
            },
            "transcripts": [{
                "role": "CreatorSpeech",
                "authority": "AutomaticUnreviewed",
                "text": "WITHHELD TRANSCRIPT WORDS",
            }],
            "evidence": [{
                "kind": "VisualObservation",
                "description": "WITHHELD CLIP OBSERVATION",
            }],
            "visualText": None,
            "gameKnowledge": None,
            "profile": {
                "audienceAddress": "Viewers",
                "namingGuidance": None,
                "reusableDescriptionSignature": None,
                "defaultTags": [],
                "voicePerspective": "CreatorFirstPerson",
                "variantIntent": "OutcomeFocused",
            },
        }
        non_primary = _visual_draft(
            "A red chamber",
            "A masked figure raised a glowing chain.",
        )
        primary = _visual_draft(
            "A stone passage",
            "A hand opened a wooden gate.",
        )
        drafts = [non_primary, primary]

        default_messages = _metadata_messages(
            request,
            _prompt_text(),
            grounded_drafts=drafts,
            primary_visual_draft_ordinal=2,
        )
        explicit_broad_messages = _metadata_messages(
            request,
            _prompt_text(),
            grounded_drafts=drafts,
            primary_visual_draft_ordinal=2,
            primary_only_evidence=False,
        )
        self.assertEqual(default_messages, explicit_broad_messages)
        broad_payload = default_messages[1]["content"][0]["text"]
        self.assertIn("masked figure raised a glowing chain", broad_payload)
        self.assertIn("hand opened a wooden gate", broad_payload)
        self.assertNotIn("SelectedPrimaryOnly", broad_payload)

        retry_messages = _metadata_messages(
            request,
            _prompt_text(),
            validation_feedback="use only the selected primary event",
            grounded_drafts=drafts,
            primary_visual_draft_ordinal=2,
            primary_only_evidence=True,
            schema_valid_rejected_json=(
                '{"description":"I selected the Cerebrum Enhancer.",'
                '"grounding":[],"tags":["Voidling Bound"],'
                '"temporalVoice":"RetrospectivePast",'
                '"titleBody":"I activated the Cerebrum Enhancer"}'
            ),
            rejected_rule_codes=("CrossDraftTitleContamination",),
            withhold_rejected_audience_copy=True,
        )
        retry_payload = retry_messages[1]["content"][0]["text"]
        self.assertNotIn("masked figure raised a glowing chain", retry_payload)
        self.assertIn("hand opened a wooden gate", retry_payload)
        self.assertNotIn("WITHHELD GAME NOTE", retry_payload)
        self.assertNotIn("WITHHELD TRANSCRIPT WORDS", retry_payload)
        self.assertNotIn("WITHHELD CLIP OBSERVATION", retry_payload)
        self.assertIn('"evidenceScope":"SelectedPrimaryOnly"', retry_payload)
        retry_text = json.dumps(retry_messages, ensure_ascii=False)
        self.assertNotIn("Cerebrum Enhancer", retry_text)
        self.assertNotIn('"role": "assistant"', retry_text)
        self.assertIn("audience copy is intentionally withheld", retry_text)
        self.assertEqual(
            "A hand opened a wooden gate.",
            primary["actions"][0],
            "Retry projection must not rewrite or repair provider facts.",
        )

    def test_only_cross_draft_contamination_narrows_retry_evidence(self) -> None:
        self.assertTrue(
            _requires_primary_only_synthesis_evidence(
                "CrossDraftTitleContamination"
            )
        )
        for code in (
            "NonRetrospectiveVoice",
            "UnsupportedMentalState",
            "UnreviewedTranscriptReuse",
            "StrictOutputValidation",
        ):
            self.assertFalse(_requires_primary_only_synthesis_evidence(code))

    def test_creator_authority_retry_withholds_first_person_copy(self) -> None:
        request = {
            "game": {
                "name": "Voidling Bound",
                "hashtag": "#VoidlingBound",
                "source": "UserConfirmed",
                "notes": None,
            },
            "transcripts": [],
            "evidence": [],
            "visualText": None,
            "gameKnowledge": None,
            "profile": {
                "audienceAddress": "Viewers",
                "namingGuidance": None,
                "reusableDescriptionSignature": None,
                "defaultTags": [],
                "voicePerspective": "CreatorFirstPerson",
                "variantIntent": "ConcreteDetail",
            },
        }
        rejected_json = (
            '{"description":"I selected the Cerebrum Enhancer.",'
            '"grounding":[],"tags":["Voidling Bound"],'
            '"temporalVoice":"RetrospectivePast",'
            '"titleBody":"I selected the Cerebrum Enhancer"}'
        )
        messages = _metadata_messages(
            request,
            _prompt_text(),
            validation_feedback="remove unsupported creator embodiment",
            grounded_drafts=[
                _visual_draft(
                    "A green interface",
                    "A glowing geometric structure appeared.",
                )
            ],
            primary_actor_authority="Unknown",
            primary_creator_experience_relation="Unestablished",
            schema_valid_rejected_json=rejected_json,
            rejected_rule_codes=("UnsupportedCreatorEmbodiment",),
            withhold_rejected_audience_copy=True,
        )
        serialized = json.dumps(messages, ensure_ascii=False)
        self.assertNotIn("I selected the Cerebrum Enhancer", serialized)
        self.assertFalse(any(message["role"] == "assistant" for message in messages))
        correction = messages[-1]["content"][0]["text"]
        self.assertIn("creator embodiment", correction)
        self.assertIn("primaryActorAuthority=Unknown", correction)
        self.assertIn("primaryCreatorExperienceRelation=Unestablished", correction)
        self.assertIn("must contain no I, me, my, mine, we, us, our, or ours", correction)

        prompt = _prompt_text()
        self.assertNotIn("gameplay viewpoint and actions", prompt)
        self.assertIn(
            "Unestablished creator-experience relation authorizes no first-person",
            prompt,
        )

    def test_model_context_suppresses_unestablished_first_person_preference(self) -> None:
        request = {
            "game": {
                "name": "Voidling Bound",
                "hashtag": "#VoidlingBound",
                "source": "UserConfirmed",
                "notes": None,
            },
            "transcripts": [],
            "evidence": [],
            "visualText": None,
            "gameKnowledge": None,
            "profile": {
                "audienceAddress": "Viewers",
                "namingGuidance": None,
                "reusableDescriptionSignature": None,
                "defaultTags": [],
                "voicePerspective": "CreatorFirstPerson",
                "variantIntent": "ConcreteDetail",
            },
        }

        neutral = _model_context(
            request,
            primary_actor_authority="Unknown",
            primary_creator_experience_relation="Unestablished",
        )
        encountered = _model_context(
            request,
            primary_actor_authority="OtherPerson",
            primary_creator_experience_relation="CreatorEncountered",
        )
        controlled = _model_context(
            request,
            primary_actor_authority="CreatorControlled",
            primary_creator_experience_relation="CreatorActed",
        )

        self.assertEqual(
            "CreatorFirstPerson",
            request["profile"]["voicePerspective"],
            "Effective synthesis voice must not mutate the saved profile.",
        )
        self.assertEqual(
            "NeutralNoSubject",
            neutral["profile"]["voicePerspective"],
        )
        self.assertEqual(
            "CreatorFirstPerson",
            encountered["profile"]["voicePerspective"],
        )
        self.assertEqual(
            "CreatorFirstPerson",
            controlled["profile"]["voicePerspective"],
        )

    def test_retry_duplicate_check_handles_no_prior_success(self) -> None:
        draft = {"title": "bounded draft"}
        self.assertFalse(_duplicates_prior_synthesis(draft, []))
        self.assertTrue(_duplicates_prior_synthesis(draft, [draft]))
        self.assertFalse(
            _duplicates_prior_synthesis(draft, [{"title": "different"}])
        )

    def test_stable_readable_text_requires_separate_draft_agreement(self) -> None:
        first = _visual_draft("A room", "A hand opens a door")
        first["readableText"] = [
            "  OBJECTIVE   UPDATED  ",
            "SINGLE DRAFT",
            "single draft",
            "71",
        ]
        second = _visual_draft("A room", "The door remains open")
        second["readableText"] = ["objective updated"]
        third = _visual_draft("A hall", "A person enters the hall")
        third["readableText"] = ["UNSTABLE LABEL"]

        self.assertEqual(
            ["OBJECTIVE UPDATED"],
            _stable_readable_text([first, second, third]),
        )
        request = {
            "game": {
                "name": "Example Game",
                "hashtag": "#ExampleGame",
                "source": "UserConfirmed",
                "notes": None,
            },
            "transcripts": [],
            "evidence": [],
            "gameKnowledge": None,
            "profile": {
                "audienceAddress": "Viewers",
                "namingGuidance": None,
                "reusableDescriptionSignature": None,
                "defaultTags": [],
                "voicePerspective": "CreatorFirstPerson",
                "variantIntent": "DirectAction",
            },
        }
        synthesis = _metadata_messages(
            request,
            _prompt_text(),
            grounded_drafts=[first, second, third],
        )[1]["content"][0]["text"]
        self.assertIn(
            '\"stableReadableText\":[\"OBJECTIVE UPDATED\"]',
            synthesis,
        )
        self.assertIn(
            "content unique to other drafts is forbidden from the title",
            synthesis,
        )
        self.assertIn(
            "use its exact wording in the title or description",
            synthesis,
        )
        self.assertNotIn("UNSTABLE LABEL", synthesis)
        self.assertNotIn("SINGLE DRAFT", synthesis)
        self.assertNotIn('\"71\"', synthesis)

    def test_synthesis_draft_never_promotes_uncertainty_or_inferred_intent(self) -> None:
        draft = {
            "environment": "A bounded industrial corridor",
            "environmentUncertain": False,
            "subjectsAndObjects": [
                "A person beside a metal door",
                "A person appears anxious",
            ],
            "actions": [
                "The person opened the metal door",
                "The person prepared to enter",
            ],
            "readableText": [],
            "uncertainties": [
                "The person may be waiting for something unseen",
            ],
        }

        synthesized = _synthesis_draft(draft, [])

        self.assertNotIn("uncertainties", synthesized)
        self.assertEqual(
            ["A person beside a metal door"],
            synthesized["subjectsAndObjects"],
        )
        self.assertEqual(
            ["The person opened the metal door"],
            synthesized["actions"],
        )

    def test_knowledge_selector_compares_all_candidates_without_examples(self) -> None:
        prompt = _knowledge_selection_prompt_text()
        lowered = prompt.casefold()
        self.assertIn("candidate order and retrieval score have no authority", lowered)
        self.assertIn("assess every candidate independently", lowered)
        self.assertIn("set conflict true", lowered)
        self.assertIn("uncertain environment", lowered)
        for example_marker in (
            "for example",
            "for instance",
            "e.g.",
            "example:",
        ):
            self.assertNotIn(example_marker, lowered)

    def test_knowledge_selector_is_video_bound_and_identity_bounded(self) -> None:
        candidates = [
            {
                "id": "gkp-a",
                "section": "First section",
                "text": "A location contains a sealed gate and a bronze lever.",
            },
            {
                "id": "gkp-b",
                "section": "Second section",
                "text": "A courtyard contains a stone arch and a hanging bell.",
            },
        ]
        canonical, _ = _knowledge_selection_schema(candidates)
        assessments = json.loads(canonical)["properties"]["assessments"]
        self.assertEqual(
            ["gkp-a", "gkp-b"],
            list(assessments["properties"].keys()),
        )

        messages = _knowledge_selection_messages(
            {
                "_validated": {
                    "videoPath": "C:/external/review.mp4",
                    "videoDuration": 20.0,
                }
            },
            "Select only directly supported context.",
            [
                _visual_draft("An enclosed area", "A sealed gate fills the view."),
                _visual_draft("An enclosed area", "A hand pulls a bronze lever."),
            ],
            candidates,
        )
        content = messages[1]["content"]
        self.assertEqual("video", content[0]["type"])
        self.assertEqual("C:/external/review.mp4", content[0]["video"])
        self.assertEqual("text", content[1]["type"])
        self.assertLess(
            content[1]["text"].index("A sealed gate fills the view."),
            content[1]["text"].index("A hand pulls a bronze lever."),
        )
        self.assertIn('"environmentUncertain":false', content[1]["text"])
        self.assertEqual(
            "gkp-a",
            _strict_knowledge_selection(
                '{"assessments":{'
                '"gkp-a":{"setting":true,'
                '"entity":false,'
                '"object":true,'
                '"action":false,'
                '"order":false,'
                '"conflict":false},'
                '"gkp-b":{"setting":false,'
                '"entity":false,'
                '"object":false,'
                '"action":false,'
                '"order":false,'
                '"conflict":false}}}',
                candidates,
            )["currentPassageId"],
        )
        with self.assertRaises((InferenceError, UsageOrInputError)):
            _strict_knowledge_selection(
                '{"assessments":{"gkp-foreign":{}}}', candidates
            )

    def test_knowledge_selector_assesses_both_authorized_current_strengths(self) -> None:
        request = {
            "gameKnowledge": {
                "matches": [
                    {
                        "id": "gkp-linked-current",
                        "strength": "ClipLinked",
                        "temporalRelation": "CurrentEventCandidate",
                    },
                    {
                        "id": "gkp-visual-current",
                        "strength": "CandidateForVisualGrounding",
                        "temporalRelation": "CurrentEventCandidate",
                    },
                    {
                        "id": "gkp-general-current",
                        "strength": "GeneralContext",
                        "temporalRelation": "CurrentEventCandidate",
                    },
                    {
                        "id": "gkp-linked-unspecified",
                        "strength": "ClipLinked",
                        "temporalRelation": "Unspecified",
                    },
                    {
                        "id": "gkp-visual-prior",
                        "strength": "CandidateForVisualGrounding",
                        "temporalRelation": "ImmediatelyPriorContext",
                    },
                ]
            }
        }

        self.assertEqual(
            ["gkp-linked-current", "gkp-visual-current"],
            [item["id"] for item in _current_knowledge_candidates(request)],
        )

    def test_case_schema_reserves_space_for_deterministic_hashtag(self) -> None:
        canonical, _ = _metadata_schema(
            {"game": {"hashtag": "#Example.Game"}}
        )
        schema = json.loads(canonical)
        self.assertEqual(66, schema["properties"]["titleBody"]["maxLength"])
        self.assertNotIn("title", schema["properties"])
        self.assertEqual(
            60,
            schema["properties"]["tags"]["items"]["maxLength"],
        )
        self.assertEqual(
            r'^[^#"\\\r\n\t]+$',
            schema["properties"]["tags"]["items"]["pattern"],
        )

    def test_case_schema_prevents_hash_prefixed_tags_during_decoding(self) -> None:
        canonical, _ = _metadata_schema(
            {"game": {"hashtag": "#VoidlingBound"}}
        )
        grammar = xgr.Grammar.from_json_schema(
            canonical,
            any_whitespace=False,
        )
        valid = {
            "titleBody": "Crossed the ruined field",
            "description": "The character crossed a grassy field beside a ruined structure.",
            "tags": ["VoidlingBound"],
            "grounding": [],
            "temporalVoice": "RetrospectivePast",
        }
        invalid = {**valid, "tags": ["#VoidlingBound"]}

        self.assertTrue(
            _is_grammar_accept_string(
                grammar,
                json.dumps(valid, sort_keys=True),
            )
        )
        self.assertFalse(
            _is_grammar_accept_string(
                grammar,
                json.dumps(invalid, sort_keys=True),
            )
        )

    def test_synthesis_evidence_withholds_unsupported_actor_role_labels(self) -> None:
        projected = _synthesis_draft(
            {
                "environment": "A grassy clearing with palm trees",
                "environmentUncertain": False,
                "subjectsAndObjects": [
                    "A player-controlled character",
                    "A wooden ruin",
                ],
                "actions": [
                    "A player-controlled character stands beside the ruin",
                    "Wind moves the grass",
                ],
                "readableText": [],
                "uncertainties": [],
            },
            [],
        )

        self.assertEqual(["A wooden ruin"], projected["subjectsAndObjects"])
        self.assertEqual(
            ["stands beside the ruin", "Wind moves the grass"],
            projected["actions"],
        )

    def test_prompt_contains_rules_not_semantic_examples(self) -> None:
        prompt = _prompt_text()
        lowered = prompt.casefold()

        self.assertIn(
            "treat the game identity as request data",
            lowered,
        )
        self.assertIn("never expose source positions", lowered)
        self.assertIn("generic production wording is invalid", lowered)
        self.assertIn("silent bounded gameplay review", lowered)
        self.assertIn("ordered, strictly grounded visual drafts", lowered)
        self.assertIn("appends the supplied canonical hashtag", lowered)
        self.assertIn("sole authority for creator embodiment", lowered)
        self.assertIn("automaticunreviewed", lowered)
        self.assertIn("belongs to its unconfirmed source", lowered)
        self.assertIn("never copy four or more consecutive words", lowered)
        self.assertNotRegex(prompt, re.compile(r"#[A-Za-z0-9]"))
        for example_marker in (
            "for example",
            "for instance",
            "e.g.",
            "example:",
        ):
            self.assertNotIn(example_marker, lowered)

        visual_prompt = _visual_draft_prompt_text().casefold()
        self.assertIn(
            "before the change, the change itself, and the resulting state",
            visual_prompt,
        )
        self.assertIn(
            "a menu or interface backdrop does not establish",
            visual_prompt,
        )
        self.assertIn(
            "particles, fire, smoke, flashes, occlusion, a health bar",
            visual_prompt,
        )

    def test_model_context_omits_internal_identity_timing_and_scores(self) -> None:
        request = {
            "candidateId": "candidate-internal",
            "attempt": 3,
            "game": {
                "name": "Example Game",
                "hashtag": "#ExampleGame",
                "source": "UserConfirmed",
                "notes": "A user-authored note.",
            },
            "clip": {
                "startSeconds": 321.25,
                "endSeconds": 341.25,
                "sourceDurationSeconds": 7200,
                "deterministicScore": 87,
                "deterministicReason": "An internal score explanation.",
            },
            "transcripts": [
                {
                    "absoluteAudioStreamIndex": 4,
                    "role": "CreatorSpeech",
                    "authority": "HumanReviewed",
                    "text": "A reviewed transcript.",
                }
            ],
            "evidence": [
                {
                    "id": "visual-01",
                    "kind": "VisualObservation",
                    "description": "A visible door opens.",
                },
                {
                    "id": "moment-01",
                    "kind": "DeterministicMoment",
                    "description": "Internal peak at 12 seconds.",
                },
            ],
            "gameKnowledge": None,
            "profile": {
                "audienceAddress": "Chat",
                "namingGuidance": "Keep it factual.",
                "reusableDescriptionSignature": "Private signature.",
                "defaultTags": ["gaming"],
                "voicePerspective": "CreatorFirstPerson",
                "variantIntent": "DirectAction",
            },
            "_validated": {
                "videoPath": "C:/external/review.mp4",
                "videoDuration": 20,
            },
        }

        messages = _visual_draft_messages(
            request,
            _visual_draft_prompt_text(),
            (0.0, 20.0),
            1,
            1,
        )
        self.assertEqual("video", messages[1]["content"][0]["type"])
        self.assertEqual(
            "C:/external/review.mp4",
            messages[1]["content"][0]["video"],
        )
        user_context = messages[1]["content"][1]["text"]

        self.assertNotIn("A visible door opens.", user_context)
        self.assertNotIn("A reviewed transcript.", user_context)
        for internal_value in (
            "candidate-internal",
            "321.25",
            "7200",
            "87",
            "visual-01",
            "moment-01",
            "Internal peak",
            "Private signature",
            "absoluteAudioStreamIndex",
        ):
            self.assertNotIn(internal_value, user_context)

        draft = _visual_draft(
            "An interior room",
            "A hand opens the visible door.",
        )
        draft["readableText"] = ["Unreviewed visible wording"]
        later_draft = _visual_draft(
            "A guessed exact location",
            "The open door reveals a visible staircase.",
        )
        later_draft["environmentUncertain"] = True
        refinement_messages = _metadata_messages(
            request,
            _prompt_text(),
            grounded_drafts=[draft, later_draft],
        )
        self.assertEqual(1, len(refinement_messages[1]["content"]))
        self.assertEqual(
            "text",
            refinement_messages[1]["content"][0]["type"],
        )
        self.assertIn(
            "fallible visual evidence",
            refinement_messages[1]["content"][0]["text"],
        )
        self.assertIn(
            draft["actions"][0],
            refinement_messages[1]["content"][0]["text"],
        )
        self.assertIn(
            later_draft["actions"][0],
            refinement_messages[1]["content"][0]["text"],
        )
        self.assertNotIn(
            "A guessed exact location",
            refinement_messages[1]["content"][0]["text"],
        )
        contaminated = _visual_draft(
            "An alley beneath UNSTABLE LONG SIGN WORDING",
            "A figure stood beneath UNSTABLE LONG SIGN WORDING.",
        )
        contaminated["readableText"] = ["UNSTABLE LONG SIGN WORDING"]
        sanitized_messages = _metadata_messages(
            request,
            _prompt_text(),
            grounded_drafts=[contaminated],
        )
        self.assertNotIn(
            "UNSTABLE LONG SIGN WORDING",
            sanitized_messages[1]["content"][0]["text"],
        )
        embedded = _visual_draft(
            "An alley beside a banner reading 'UNSTABLE LONG SIGN WORDING'",
            "A figure stood beside a banner displaying UNSTABLE LONG SIGN WORDING.",
        )
        embedded["readableText"] = ["Different uncertain text"]
        embedded_messages = _metadata_messages(
            request,
            _prompt_text(),
            grounded_drafts=[embedded],
        )
        embedded_text = embedded_messages[1]["content"][0]["text"]
        self.assertNotIn("UNSTABLE LONG SIGN WORDING", embedded_text)
        self.assertIn("An alley beside a banner", embedded_text)
        self.assertIn(
            '"isPrimary":true',
            refinement_messages[1]["content"][0]["text"],
        )
        self.assertNotIn(
            "Unreviewed visible wording",
            refinement_messages[1]["content"][0]["text"],
        )
        self.assertIn(
            "A visible door opens.",
            refinement_messages[1]["content"][0]["text"],
        )
        self.assertIn(
            "A reviewed transcript.",
            refinement_messages[1]["content"][0]["text"],
        )

        visual_schema = json.loads(_visual_draft_schema()[0])
        self.assertEqual(
            [
                "environment",
                "environmentUncertain",
                "subjectsAndObjects",
                "actions",
                "readableText",
                "uncertainties",
            ],
            visual_schema["required"],
        )

    def test_visual_draft_is_typed_bounded_and_preserves_uncertainty(self) -> None:
        value = _strict_visual_draft(
            json.dumps(
                {
                    "environment": "A bright featureless backdrop",
                    "environmentUncertain": True,
                    "subjectsAndObjects": ["A bed", "A monitor"],
                    "actions": ["A masked person reaches toward the bed"],
                    "readableText": ["CLEAR TEXT"],
                    "uncertainties": ["The physical room boundary is not visible"],
                },
                separators=(",", ":"),
            )
        )
        self.assertTrue(value["environmentUncertain"])
        self.assertEqual(["A bed", "A monitor"], value["subjectsAndObjects"])
        self.assertEqual(["CLEAR TEXT"], value["readableText"])
        duplicate_action = dict(value)
        duplicate_action["actions"] = ["A visible action", "A visible action"]
        self.assertEqual(
            ["A visible action", "A visible action"],
            _strict_visual_draft(
                json.dumps(duplicate_action, separators=(",", ":"))
            )["actions"],
        )
        with self.assertRaises((InferenceError, UsageOrInputError)):
            _strict_visual_draft(
                '{"environment":"Room","environmentUncertain":false,'
                '"subjectsAndObjects":[],"actions":[],"readableText":[],'
                '"uncertainties":[]}'
            )

    def test_visual_draft_prompt_preserves_literal_uncertainty_without_examples(self) -> None:
        prompt = _visual_draft_prompt_text()
        lowered = prompt.casefold()
        self.assertIn("set environmentuncertain to true", lowered)
        self.assertIn("copy readabletext exactly", lowered)
        self.assertIn(
            "texture, color, reflection, lighting, or backdrop alone",
            lowered,
        )
        self.assertIn("separate screen-space interfaces", lowered)
        self.assertIn("consumer platform, storefront, launcher", lowered)
        self.assertIn("do not turn an overlay into a physical screen", lowered)
        self.assertIn("do not by themselves establish a completed outcome or explosion", lowered)
        for example_marker in (
            "for example",
            "for instance",
            "such as",
            "e.g.",
            "example:",
        ):
            self.assertNotIn(example_marker, lowered)

    def test_strict_metadata_requires_authority_for_interface_identity_and_display_source(self) -> None:
        request = {
            "game": {
                "name": "Example Game",
                "hashtag": "#ExampleGame",
                "notes": None,
                "source": "SourcePathHint",
            },
            "transcripts": [],
            "visualText": {"groundingAnchors": []},
        }
        draft = {
            "environment": "A dark interface",
            "environmentUncertain": False,
            "subjectsAndObjects": ["A grid of game tiles"],
            "actions": ["A game tile became highlighted"],
            "readableText": [],
            "uncertainties": [],
        }
        with self.assertRaises(InferenceError) as platform_error:
            _strict_metadata(
                '{"titleBody":"Steam Client showed several game covers",'
                '"description":"Steam Client opened a grid and highlighted one cover.",'
                '"tags":["game menu"],"grounding":[],'
                '"temporalVoice":"RetrospectivePast"}',
                request,
                visual_drafts=[draft],
            )
        self.assertEqual(
            "UnsupportedMentalState",
            _validation_failure_code(platform_error.exception),
        )

        request["visualText"] = {
            "groundingAnchors": [
                {"text": "VOIDLING BOUND", "occurrenceCount": 3}
            ]
        }
        text_draft = dict(draft)
        text_draft["readableText"] = ["VOIDLING BOUND"]
        with self.assertRaises(InferenceError) as display_error:
            _strict_metadata(
                '{"titleBody":"Opened the industrial chamber",'
                '"description":"VOIDLING BOUND blinked on the display.",'
                '"tags":["industrial chamber"],"grounding":[],'
                '"temporalVoice":"RetrospectivePast"}',
                request,
                visual_drafts=[text_draft],
            )
        self.assertEqual(
            "UnsupportedInterfaceAttribution",
            _validation_failure_code(display_error.exception),
        )
        _, feedback = _retry_feedback(display_error.exception)
        self.assertIn("screen-space menus", feedback)
        self.assertIn("physical objects", feedback)
        self.assertIn("marked primary visual draft", feedback)

    def test_interface_attribution_rule_allows_literal_menu_layout(self) -> None:
        request = {
            "game": {
                "name": "Voidling Bound",
                "hashtag": "#VoidlingBound",
                "notes": None,
                "source": "SourcePathHint",
            },
            "profile": {"defaultTags": []},
            "transcripts": [],
            "visualText": {
                "groundingAnchors": [
                    {"text": "VOIDLING BOUND", "occurrenceCount": 3}
                ]
            },
        }
        draft = {
            "environment": "A creature customization menu",
            "environmentUncertain": False,
            "subjectsAndObjects": [
                "A green glowing creature model",
                "A circular platform",
                "A menu on the right side",
            ],
            "actions": [
                "A green glowing creature model was displayed on a circular platform",
                "A menu appeared on the right side",
            ],
            "readableText": ["VOIDLING BOUND"],
            "uncertainties": [],
        }
        result = _strict_metadata(
            '{"titleBody":"Green creature glowed on circular platform",'
            '"description":"A green glowing creature model was displayed on a circular platform, with a menu appearing on the right side.",'
            '"tags":["Voidling Bound"],"grounding":[],'
            '"temporalVoice":"RetrospectivePast"}',
            request,
            visual_drafts=[draft],
            primary_visual_draft_ordinal=1,
        )
        self.assertEqual(
            "Green creature glowed on circular platform #VoidlingBound",
            result["title"],
        )


    def test_visual_windows_overlap_peak_bounded_cores(self) -> None:
        self.assertEqual(
            [(0.0, 11.0), (9.0, 20.0)],
            _visual_windows(20.0),
        )
        self.assertEqual(
            [(0.0, 18.0), (18.0, 31.0), (29.0, 42.0), (42.0, 60.0)],
            _visual_windows(60.0),
        )

    def test_strict_metadata_rejects_analysis_bookkeeping(self) -> None:
        request = {
            "game": {
                "hashtag": "#ExampleGame",
            },
        }
        with self.assertRaises(InferenceError):
            _strict_metadata(
                '{"titleBody":"Visual evidence observed",'
                '"description":"An observation supports the clip.",'
                '"tags":["ExampleGame"],"grounding":[],"temporalVoice":"RetrospectivePast"}',
                request,
            )

    def test_strict_metadata_rejects_non_english_language_drift(self) -> None:
        request = {
            "game": {
                "name": "Example Game",
                "hashtag": "#ExampleGame",
            },
        }
        with self.assertRaises(InferenceError):
            _strict_metadata(
                '{"titleBody":"我认为这个时刻会很有趣",'
                '"description":"这个说明完全切换成了另一种语言。",'
                '"tags":["ExampleGame"],"grounding":[],"temporalVoice":"RetrospectivePast"}',
                request,
            )

    def test_strict_metadata_rejects_concatenated_hashtag(self) -> None:
        request = {
            "game": {
                "name": "Example Game",
                "hashtag": "#ExampleGame",
            },
        }
        with self.assertRaises(InferenceError):
            _strict_metadata(
                '{"titleBody":"Opening the gate#ExampleGame",'
                '"description":"I move through as the gate opens.",'
                '"tags":["ExampleGame"],"grounding":[],"temporalVoice":"RetrospectivePast"}',
                request,
            )

    def test_strict_metadata_allows_confirmed_non_latin_game_name(self) -> None:
        request = {
            "game": {
                "name": "龍が如く",
                "hashtag": "#龍が如く",
            },
        }
        result = _strict_metadata(
            '{"titleBody":"Escaped narrowly in 龍が如く",'
            '"description":"The chase turned at the last doorway.",'
            '"tags":["龍が如く"],"grounding":[],"temporalVoice":"RetrospectivePast"}',
            request,
        )
        self.assertEqual("The chase turned at the last doorway.", result["description"])

    def test_strict_metadata_rejects_a_stray_mixed_script_token(self) -> None:
        request = {
            "game": {
                "name": "Example Game",
                "hashtag": "#ExampleGame",
            },
        }
        with self.assertRaises(InferenceError):
            _strict_metadata(
                '{"titleBody":"Found the note on the 地板",'
                '"description":"I found the note on the floor.",'
                '"tags":["note"],"grounding":[],'
                '"temporalVoice":"RetrospectivePast"}',
                request,
            )

    def test_strict_metadata_rejects_third_person_creator_framing(self) -> None:
        request = {
            "game": {
                "name": "Example Game",
                "hashtag": "#ExampleGame",
                "notes": None,
            },
            "transcripts": [],
        }
        with self.assertRaises(InferenceError):
            _strict_metadata(
                '{"titleBody":"Chose the next skill",'
                '"description":"The player opens the skill menu and confirms an upgrade.",'
                '"tags":["ExampleGame"],"grounding":[],"temporalVoice":"RetrospectivePast"}',
                request,
            )

        result = _strict_metadata(
            '{"titleBody":"I found the hidden switch",'
            '"description":"I found the switch and opened the gate.",'
            '"tags":["ExampleGame"],"grounding":[],'
            '"temporalVoice":"RetrospectivePast"}',
            request,
        )
        self.assertEqual(
            "I found the hidden switch #ExampleGame",
            result["title"],
        )

    def test_strict_metadata_rejects_generic_observer_person_framing(self) -> None:
        request = {
            "game": {
                "name": "Example Game",
                "hashtag": "#ExampleGame",
                "notes": None,
            },
            "transcripts": [],
        }
        rejected = (
            (
                "A man in a green shirt said something",
                "I heard a man in a green shirt beside the doorway.",
            ),
            (
                "Heard someone beside the doorway",
                "I heard a man in a green shirt beside the doorway.",
            ),
        )
        for title_body, description in rejected:
            with self.subTest(title=title_body), self.assertRaises(InferenceError) as raised:
                _strict_metadata(
                    '{"titleBody":' + json.dumps(title_body) + ','
                    '"description":' + json.dumps(description) + ','
                    '"tags":["ExampleGame"],"grounding":[],'
                    '"temporalVoice":"RetrospectivePast"}',
                    request,
                )
            self.assertEqual(
                "ThirdPersonCreatorFraming",
                _validation_failure_code(raised.exception),
            )

        with self.assertRaises(InferenceError) as present:
            _strict_metadata(
                '{"titleBody":"A man in a green shirt says something",'
                '"description":"I crossed the room and reached the doorway.",'
                '"tags":["ExampleGame"],"grounding":[],'
                '"temporalVoice":"RetrospectivePast"}',
                request,
            )
        self.assertEqual(
            "ThirdPersonCreatorFraming",
            _validation_failure_code(present.exception),
        )
        self.assertEqual(
            {
                "nonEvidence": True,
                "rejectedTitleBody": "A man in a green shirt says something",
                "offendingActionField": "titleBody",
                "offendingActionForm": "says",
            },
            _retry_correction_envelope(present.exception),
        )

        for title_body in (
            "Lyra confronts Rowan beside the doorway",
            "Lyra raises both arms beside the doorway",
            "Explosions erupt beside the wooden ruin",
        ):
            with self.subTest(title=title_body), self.assertRaises(
                InferenceError
            ) as named_present:
                _strict_metadata(
                    '{"titleBody":' + json.dumps(title_body) + ','
                    '"description":"Lyra crossed the room and reached the doorway.",'
                    '"tags":["doorway"],"grounding":[],'
                    '"temporalVoice":"RetrospectivePast"}',
                    request,
                )
            self.assertEqual(
                "NonRetrospectiveVoice",
                _validation_failure_code(named_present.exception),
            )

    def test_nonretrospective_retry_preserves_only_grounded_canonical_target(self) -> None:
        request = {
            "game": {
                "name": "Example Game",
                "hashtag": "#ExampleGame",
                "notes": None,
            },
            "transcripts": [],
        }
        rejected_title = "Lyra confronts Rowan beside the doorway"
        with self.assertRaises(InferenceError) as captured:
            _strict_metadata(
                '{"titleBody":' + json.dumps(rejected_title) + ','
                '"description":"Lyra crossed the room and reached the doorway.",'
                '"tags":["doorway"],"grounding":[],'
                '"temporalVoice":"RetrospectivePast"}',
                request,
            )

        code, feedback = _retry_feedback(captured.exception)
        self.assertEqual("NonRetrospectiveVoice", code)
        self.assertNotIn(json.dumps(rejected_title), feedback)
        self.assertNotIn(json.dumps("confronts"), feedback)
        self.assertIn("required grounding binding", feedback)
        self.assertNotIn("Ghostwire", feedback)
        self.assertEqual(
            {
                "nonEvidence": True,
                "rejectedTitleBody": rejected_title,
                "offendingActionField": "titleBody",
                "offendingActionForm": "confronts",
            },
            _retry_correction_envelope(captured.exception),
        )

        generic_feedback = _validation_feedback("ThirdPersonCreatorFraming")
        self.assertIn("exact canonical identity", generic_feedback)
        self.assertIn("required grounding binding", generic_feedback)
        self.assertIn("neutral retrospective", generic_feedback)
        self.assertIn("never force I or we", generic_feedback)

    def test_user_confirmed_note_identity_remains_authorized_during_retry(self) -> None:
        request = {
            "game": {
                "name": "Ghostwire: Tokyo",
                "hashtag": "#Ghostwire",
                "source": "UserConfirmed",
                "notes": "Hannya is the masked figure confronting Akito.",
            },
            "gameKnowledge": None,
            "visualText": None,
            "clip": {},
            "transcripts": [],
            "evidence": [],
            "profile": {
                "audienceAddress": "Viewers",
                "namingGuidance": None,
                "defaultTags": [],
                "voicePerspective": "CreatorFirstPerson",
                "variantIntent": "DirectAction",
            },
        }
        rejected_json = json.dumps(
            {
                "titleBody": "Hannya confronts Akito amid glowing chains",
                "description": "Hannya confronts Akito as the scene shifts.",
                "tags": ["glowing chains"],
                "grounding": [],
                "temporalVoice": "RetrospectivePast",
            },
            ensure_ascii=False,
            sort_keys=True,
            separators=(",", ":"),
        )
        messages = _metadata_messages(
            request,
            _prompt_text(),
            validation_feedback=_validation_feedback("NonRetrospectiveVoice"),
            grounded_drafts=[
                _visual_draft(
                    "A foggy open area",
                    "A masked figure confronted another person beside a blue chain.",
                )
            ],
            primary_actor_authority="Unknown",
            primary_creator_experience_relation="Unestablished",
            schema_valid_rejected_json=rejected_json,
            rejected_rule_codes=("NonRetrospectiveVoice",),
            retry_correction_envelope={
                "nonEvidence": True,
                "rejectedTitleBody":
                    "Hannya confronts Akito amid glowing chains",
                "offendingActionField": "titleBody",
                "offendingActionForm": "confronts",
            },
        )
        self.assertIn(
            "exact offendingActionForm token is forbidden",
            messages[-1]["content"][0]["text"],
        )

        base_user = messages[1]["content"][0]["text"]
        correction = messages[-1]["content"][0]["text"]
        self.assertIn('"notesAuthority":"UserConfirmed"', base_user)
        self.assertIn('"gameKnowledge":null', base_user)
        self.assertIn(
            "UserConfirmed or ReusedUserMemory game notes plus the bounded review support it",
            correction,
        )
        self.assertIn("without a game-knowledge binding", correction)
        self.assertIn(
            "selected authorized game knowledge plus its bounded clip evidence",
            correction,
        )
        self.assertIn(
            "SourcePathHint notes, automatic transcript text, and path wording cannot authorize",
            correction,
        )
        self.assertNotIn("only when the unchanged CurrentEventCandidate", correction)

    def test_typed_retry_authority_anchor_excludes_path_hint_asr_and_readable_text(self) -> None:
        request = {
            "game": {
                "name": "Example Game",
                "hashtag": "#ExampleGame",
                "source": "SourcePathHint",
                "notes": "PATH-ONLY PERSON NAME MUST NOT BECOME AUTHORITY",
            },
            "gameKnowledge": None,
            "transcripts": [{
                "role": "CreatorSpeech",
                "authority": "AutomaticUnreviewed",
                "text": "ASR-ONLY PERSON NAME MUST NOT BECOME AUTHORITY",
            }],
            "evidence": [{
                "kind": "VisualObservation",
                "description": "PATH A:/private/source-video.mp4",
            }],
            "profile": {
                "audienceAddress": "Viewers",
                "namingGuidance": None,
                "defaultTags": [],
                "voicePerspective": "CreatorFirstPerson",
                "variantIntent": "DirectAction",
            },
        }
        draft = _visual_draft(
            "A foggy room",
            "A masked figure faced another person.",
        )
        draft["subjectsAndObjects"] = [
            "A masked figure",
            "A sign reads OCR-ONLY PERSON NAME",
        ]
        draft["readableText"] = ["OCR-ONLY PERSON NAME"]
        anchor = _typed_retry_authority_anchor(
            request,
            [draft],
            1,
            "OtherPerson",
            "Unestablished",
        )
        serialized = json.dumps(anchor, ensure_ascii=False, sort_keys=True)
        self.assertNotIn("PATH-ONLY", serialized)
        self.assertNotIn("ASR-ONLY", serialized)
        self.assertNotIn("OCR-ONLY", serialized)
        self.assertNotIn("source-video", serialized)
        self.assertEqual(
            "A masked figure faced another person.",
            anchor["primaryVisual"]["actions"][0],
        )
        self.assertEqual(
            ["A masked figure", "A sign"],
            anchor["primaryVisual"]["subjectsAndObjects"],
        )
        self.assertNotIn("userGameContext", anchor)

    def test_typed_retry_authority_anchor_excludes_unsupported_mental_state(self) -> None:
        request = {
            "game": {
                "name": "Example Game",
                "hashtag": "#ExampleGame",
                "source": "UserConfirmed",
                "notes": "Lyra is the masked figure.",
            },
            "gameKnowledge": None,
        }
        draft = _visual_draft(
            "A foggy room",
            "A masked figure appears afraid beside a door.",
        )
        draft["subjectsAndObjects"] = [
            "A masked figure",
            "A visible door",
        ]
        anchor = _typed_retry_authority_anchor(
            request,
            [draft],
            1,
            "OtherPerson",
            "Unestablished",
        )
        serialized = json.dumps(anchor, ensure_ascii=False, sort_keys=True)
        self.assertNotIn("appears afraid", serialized)
        self.assertEqual([], anchor["primaryVisual"]["actions"])
        self.assertEqual(
            ["A masked figure", "A visible door"],
            anchor["primaryVisual"]["subjectsAndObjects"],
        )

    def test_typed_retry_authority_anchor_retains_only_selected_knowledge_bindings(self) -> None:
        selected_id = "selected-current"
        evidence_id = "visual-1"
        request = {
            "game": {
                "name": "Example Game",
                "hashtag": "#ExampleGame",
                "source": "UserConfirmed",
                "notes": "Lyra is the masked figure.",
            },
            "gameKnowledge": {
                "matches": [{
                    "id": selected_id,
                    "section": "Scene",
                    "text": "Lyra confronted the visitor beside a chain.",
                    "strength": "CandidateForVisualGrounding",
                    "temporalRelation": "CurrentEventCandidate",
                    "clipEvidenceIds": [evidence_id],
                }, {
                    "id": "prior-context",
                    "section": "Earlier",
                    "text": "UNSELECTED PRIOR DETAIL",
                    "strength": "CandidateForVisualGrounding",
                    "temporalRelation": "ImmediatelyPriorContext",
                    "clipEvidenceIds": ["visual-2"],
                }],
            },
        }
        anchor = _typed_retry_authority_anchor(
            request,
            [_visual_draft("A foggy room", "A masked figure faced a visitor.")],
            1,
            "OtherPerson",
            "Unestablished",
        )
        serialized = json.dumps(anchor, ensure_ascii=False, sort_keys=True)
        self.assertIn("Lyra is the masked figure", serialized)
        self.assertIn(selected_id, serialized)
        self.assertIn(
            grounding_binding_id(selected_id, evidence_id),
            anchor["selectedGameKnowledge"][0]["authorizedBindingIds"],
        )
        self.assertNotIn("UNSELECTED PRIOR DETAIL", serialized)

    def test_trace_proven_tense_forms_remain_python_csharp_parity_targets(self) -> None:
        request = {
            "game": {
                "name": "Example Game",
                "hashtag": "#ExampleGame",
                "notes": None,
            },
            "transcripts": [],
        }
        cases = (
            (
                "The scene shifts into a foggy area",
                "The blue chain tightened around the masked figure.",
                "titleBody",
                "shifts",
            ),
            (
                "The blue chain tightened around the masked figure",
                "A blue chain hangs beside the doorway.",
                "description",
                "hangs",
            ),
            (
                "Explosions erupted beside the wooden ruin",
                "A purple, green, yellow, and red glowing explosion erupts beside the ruin.",
                "description",
                "erupts",
            ),
            (
                "Purple explosion occurs in the jungle",
                "A yellow projectile crossed the jungle near wooden structures.",
                "titleBody",
                "occurs",
            ),
            (
                "Purple creature hovered in the cavern",
                "A purple creature floats above the water beneath yellow energy arcs.",
                "description",
                "floats",
            ),
        )
        for title_body, description, field, form in cases:
            with self.subTest(form=form), self.assertRaises(
                InferenceError
            ) as raised:
                _strict_metadata(
                    '{"titleBody":' + json.dumps(title_body) + ','
                    '"description":' + json.dumps(description) + ','
                    '"tags":["chain"],"grounding":[],'
                    '"temporalVoice":"RetrospectivePast"}',
                    request,
                )
            self.assertEqual(
                "NonRetrospectiveVoice",
                _validation_failure_code(raised.exception),
            )
            envelope = _retry_correction_envelope(raised.exception)
            self.assertIsNotNone(envelope)
            self.assertEqual(field, envelope["offendingActionField"])
            self.assertEqual(form, envelope["offendingActionForm"])

    def test_strict_metadata_allows_creator_voice_and_named_entities(self) -> None:
        request = {
            "game": {
                "name": "Example Game",
                "hashtag": "#ExampleGame",
                "notes": None,
            },
            "transcripts": [],
        }
        named = _strict_metadata(
            '{"titleBody":"Ellie crossed the flooded courtyard",'
            '"description":"I followed Ellie through the flooded courtyard and reached the stairwell.",'
            '"tags":["ExampleGame"],"grounding":[],'
            '"temporalVoice":"RetrospectivePast"}',
            request,
        )
        self.assertTrue(named["title"].startswith("Ellie crossed"))

        long_named = _strict_metadata(
            '{"titleBody":"Ellie at the end of the flooded courtyard crossed safely",'
            '"description":"I followed Ellie through the flooded courtyard and reached the stairwell.",'
            '"tags":["ExampleGame"],"grounding":[],'
            '"temporalVoice":"RetrospectivePast"}',
            request,
        )
        self.assertIn("crossed safely", long_named["title"])

        first_person = _strict_metadata(
            '{"titleBody":"Helped a man escape the room",'
            '"description":"I helped a man escape the room before the door closed.",'
            '"tags":["ExampleGame"],"grounding":[],'
            '"temporalVoice":"RetrospectivePast"}',
            request,
        )
        self.assertTrue(first_person["description"].startswith("I helped"))

    def test_actor_authority_rejects_other_person_embodiment(self) -> None:
        request = {
            "game": {"name": "Example Game", "hashtag": "#ExampleGame"},
            "transcripts": [],
            "profile": {"variantIntent": "DirectAction", "defaultTags": []},
        }
        draft = _visual_draft(
            "Interior",
            "A masked figure screamed while a blue chain tightened around the figure.",
        )
        with self.assertRaises(InferenceError) as raised:
            _strict_metadata(
                '{"titleBody":"I screamed as the blue chain tightened",'
                '"description":"The masked figure transformed as the chain wrapped around my neck.",'
                '"tags":["transformation","blue chain"],"grounding":[],'
                '"temporalVoice":"RetrospectivePast"}',
                request,
                [draft],
                1,
                "OtherPerson",
                "CreatorEncountered",
            )
        self.assertEqual(
            "UnsupportedCreatorEmbodiment",
            _validation_failure_code(raised.exception),
        )
        feedback = _validation_feedback("UnsupportedCreatorEmbodiment")
        self.assertIn("neutral retrospective past-action", feedback)
        self.assertNotIn("Ghostwire", feedback)

        neutral = _strict_metadata(
            '{"titleBody":"A blue chain tightened during the transformation",'
            '"description":"The masked figure transformed as the chain tightened.",'
            '"tags":["transformation","blue chain"],"grounding":[],'
            '"temporalVoice":"RetrospectivePast"}',
            request,
            [draft],
            1,
            "OtherPerson",
            "CreatorEncountered",
        )
        self.assertTrue(neutral["title"].startswith("A blue chain tightened"))

    def test_actor_authority_preserves_creator_action_effect_and_encounter(self) -> None:
        request = {
            "game": {"name": "Example Game", "hashtag": "#ExampleGame"},
            "transcripts": [],
            "profile": {"variantIntent": "DirectAction", "defaultTags": []},
        }
        controlled = _strict_metadata(
            '{"titleBody":"Upgraded my equipment at the workbench",'
            '"description":"I upgraded the equipment before leaving the room.",'
            '"tags":["equipment","workbench"],"grounding":[],'
            '"temporalVoice":"RetrospectivePast"}',
            request,
            [_visual_draft("Workshop", "The controlled avatar upgraded equipment.")],
            1,
            "CreatorControlled",
            "CreatorActed",
        )
        self.assertIn("Upgraded my equipment", controlled["title"])

        affected = _strict_metadata(
            '{"titleBody":"I got attacked beside the doorway",'
            '"description":"I got attacked before I reached the doorway.",'
            '"tags":["attack","doorway"],"grounding":[],'
            '"temporalVoice":"RetrospectivePast"}',
            request,
            [_visual_draft("Hall", "A masked figure attacked near a doorway.")],
            1,
            "OtherPerson",
            "CreatorAffected",
        )
        self.assertTrue(affected["title"].startswith("I got attacked"))
        with self.assertRaises(InferenceError):
            _strict_metadata(
                '{"titleBody":"I screamed beside the doorway",'
                '"description":"I screamed before the masked figure moved.",'
                '"tags":["doorway","masked figure"],"grounding":[],'
                '"temporalVoice":"RetrospectivePast"}',
                request,
                [_visual_draft("Hall", "A masked figure screamed near a doorway.")],
                1,
                "OtherPerson",
                "CreatorAffected",
            )

        encountered = _strict_metadata(
            '{"titleBody":"Confronted the masked figure during the transformation",'
            '"description":"I confronted the masked figure as the chain tightened.",'
            '"tags":["confrontation","transformation"],"grounding":[],'
            '"temporalVoice":"RetrospectivePast"}',
            request,
            [_visual_draft("Hall", "A masked figure transformed beside a chain.")],
            1,
            "OtherPerson",
            "CreatorEncountered",
        )
        self.assertTrue(encountered["description"].startswith("I confronted"))

    def test_unknown_actor_rejects_unestablished_first_person(self) -> None:
        request = {
            "game": {"name": "Example Game", "hashtag": "#ExampleGame"},
            "transcripts": [],
            "profile": {"variantIntent": "ConcreteDetail", "defaultTags": []},
        }
        with self.assertRaises(InferenceError) as raised:
            _strict_metadata(
                '{"titleBody":"I opened the sealed doorway",'
                '"description":"I crossed the threshold after the doorway opened.",'
                '"tags":["doorway"],"grounding":[],'
                '"temporalVoice":"RetrospectivePast"}',
                request,
                [_visual_draft("Unknown", "A sealed doorway opened.")],
                1,
                "Unknown",
                "Unestablished",
            )
        self.assertEqual(
            "UnsupportedCreatorEmbodiment",
            _validation_failure_code(raised.exception),
        )

    def test_unknown_actor_allows_neutral_past_person_and_ignores_us_tag(self) -> None:
        request = {
            "game": {"name": "The Last of Us", "hashtag": "#TheLastofUs"},
            "transcripts": [],
            "profile": {
                "variantIntent": "ConcreteDetail",
                "defaultTags": ["The Last of Us"],
            },
        }
        draft = _visual_draft(
            "Dirt path",
            "A person walked along a dirt path with a rifle and backpack.",
        )
        neutral = _strict_metadata(
            '{"titleBody":"A person walked along the dirt path",'
            '"description":"A person carried a rifle and backpack past grass and rocks.",'
            '"tags":["The Last of Us","dirt path"],"grounding":[],'
            '"temporalVoice":"RetrospectivePast"}',
            request,
            [draft],
            1,
            "Unknown",
            "Unestablished",
        )
        self.assertTrue(neutral["title"].startswith("A person walked"))

        with self.assertRaises(InferenceError) as present:
            _strict_metadata(
                '{"titleBody":"A person walks along the dirt path",'
                '"description":"A person carries a rifle and backpack past grass and rocks.",'
                '"tags":["The Last of Us","dirt path"],"grounding":[],'
                '"temporalVoice":"RetrospectivePast"}',
                request,
                [draft],
                1,
                "Unknown",
                "Unestablished",
            )
        self.assertEqual(
            "NonRetrospectiveVoice",
            _validation_failure_code(present.exception),
        )

        with self.assertRaises(InferenceError) as role_label:
            _strict_metadata(
                '{"titleBody":"The player walked along the dirt path",'
                '"description":"The player carried a rifle and backpack past grass and rocks.",'
                '"tags":["The Last of Us","dirt path"],"grounding":[],'
                '"temporalVoice":"RetrospectivePast"}',
                request,
                [draft],
                1,
                "Unknown",
                "Unestablished",
            )
        self.assertEqual(
            "ThirdPersonCreatorFraming",
            _validation_failure_code(role_label.exception),
        )

    def test_reviewed_commentary_authorizes_only_grounded_creator_wording(self) -> None:
        request = {
            "game": {"name": "Example Game", "hashtag": "#ExampleGame"},
            "transcripts": [{
                "authority": "HumanReviewed",
                "text": "I called out the masked figure",
            }],
            "profile": {"variantIntent": "CommentaryLed", "defaultTags": []},
        }
        result = _strict_metadata(
            '{"titleBody":"Called out the masked figure",'
            '"description":"I called out the masked figure as the chain tightened.",'
            '"tags":["commentary","masked figure"],"grounding":[],'
            '"temporalVoice":"RetrospectivePast"}',
            request,
            [_visual_draft("Hall", "A masked figure transformed beside a chain.")],
            1,
            "OtherPerson",
            "Unestablished",
        )
        self.assertTrue(result["description"].startswith("I called out"))

    def test_strict_metadata_rejects_generic_viewer_opening(self) -> None:
        request = {
            "game": {
                "name": "Example Game",
                "hashtag": "#ExampleGame",
                "notes": None,
            },
            "transcripts": [],
        }
        with self.assertRaises(InferenceError):
            _strict_metadata(
                '{"titleBody":"Choosing the next skill",'
                '"description":"I watch the skill menu open before choosing an upgrade.",'
                '"tags":["ExampleGame"],"grounding":[],"temporalVoice":"RetrospectivePast"}',
                request,
            )

    def test_strict_metadata_rejects_inferred_expression(self) -> None:
        request = {
            "game": {"name": "Example Game", "hashtag": "#ExampleGame"},
            "transcripts": [],
        }
        with self.assertRaises(InferenceError):
            _strict_metadata(
                '{"titleBody":"A gesture beside the van",'
                '"description":"A person turns toward the camera with a tense expression.",'
                '"tags":["ExampleGame"],"grounding":[],"temporalVoice":"RetrospectivePast"}',
                request,
            )
        with self.assertRaises(InferenceError):
            _strict_metadata(
                '{"titleBody":"A gesture beside the van",'
                '"description":"A person raises one hand during a tense moment.",'
                '"tags":["ExampleGame"],"grounding":[],"temporalVoice":"RetrospectivePast"}',
                request,
            )

    def test_strict_metadata_rejects_inferred_future_intent(self) -> None:
        request = {
            "game": {"name": "Example Game", "hashtag": "#ExampleGame"},
            "transcripts": [],
        }
        with self.assertRaises(InferenceError):
            _strict_metadata(
                '{"titleBody":"Climbing the structure",'
                '"description":"I climb the structure, preparing to take control.",'
                '"tags":["ExampleGame"],"grounding":[],"temporalVoice":"RetrospectivePast"}',
                request,
            )
        with self.assertRaises(InferenceError):
            _strict_metadata(
                '{"titleBody":"A blue figure pulsed beneath the sign",'
                '"description":"A blue figure waited for the event beneath the sign.",'
                '"tags":["figure"],"grounding":[],'
                '"temporalVoice":"RetrospectivePast"}',
                request,
            )

    def test_strict_metadata_rejects_generic_or_reaction_tag(self) -> None:
        request = {
            "game": {"name": "Example Game", "hashtag": "#ExampleGame"},
            "transcripts": [],
        }
        with self.assertRaises(InferenceError):
            _strict_metadata(
                '{"titleBody":"A gesture beside the van",'
                '"description":"A person raises one hand beside a parked van.",'
                '"tags":["ExampleGame","reaction"],"grounding":[],"temporalVoice":"RetrospectivePast"}',
                request,
            )

    def test_strict_metadata_rejects_ungrounded_release_and_platform_tags(self) -> None:
        request = {
            "game": {"name": "Example Game", "hashtag": "#ExampleGame"},
            "transcripts": [],
        }
        for unsupported_tag in (
            "new release",
            "best game 2026",
            "PC gaming",
            "PlayStation 5 gameplay",
        ):
            with self.subTest(tag=unsupported_tag), self.assertRaises(InferenceError) as raised:
                _strict_metadata(
                    '{"titleBody":"Opened the sealed gate",'
                    '"description":"I crossed the threshold and reached the courtyard.",'
                    f'"tags":["Example Game","{unsupported_tag}"],'
                    '"grounding":[],"temporalVoice":"RetrospectivePast"}',
                    request,
                )
            self.assertEqual(
                "UnsupportedTag",
                _validation_failure_code(raised.exception),
            )
        content_release = _strict_metadata(
            '{"titleBody":"Released the prisoner",'
            '"description":"I opened the cell and watched the prisoner cross the threshold.",'
            '"tags":["Example Game","release"],'
            '"grounding":[],"temporalVoice":"RetrospectivePast"}',
            request,
        )
        self.assertIn("release", content_release["tags"])
        content_switch = _strict_metadata(
            '{"titleBody":"Flipped the hidden switch",'
            '"description":"I flipped the switch and opened the gate.",'
            '"tags":["Example Game","switch"],'
            '"grounding":[],"temporalVoice":"RetrospectivePast"}',
            request,
        )
        self.assertIn("switch", content_switch["tags"])

    def test_strict_metadata_allows_year_inside_confirmed_game_identity(self) -> None:
        request = {
            "game": {"name": "F1 2026", "hashtag": "#F12026"},
            "transcripts": [],
        }
        result = _strict_metadata(
            '{"titleBody":"Passed through the final corner",'
            '"description":"I held the racing line and crossed the finish.",'
            '"tags":["F12026","gameplay"],'
            '"grounding":[],"temporalVoice":"RetrospectivePast"}',
            request,
        )
        self.assertEqual(["F12026", "gameplay"], result["tags"])

    def test_strict_metadata_preserves_explicit_user_platform_tag(self) -> None:
        request = {
            "game": {"name": "Example Game", "hashtag": "#ExampleGame"},
            "profile": {"defaultTags": ["PC gaming"]},
            "transcripts": [],
        }
        result = _strict_metadata(
            '{"titleBody":"Opened the sealed gate",'
            '"description":"I crossed the threshold and reached the courtyard.",'
            '"tags":["Example Game","PC gaming"],'
            '"grounding":[],"temporalVoice":"RetrospectivePast"}',
            request,
        )
        self.assertIn("PC gaming", result["tags"])

    def test_validation_retry_is_typed_and_contains_no_semantic_example(self) -> None:
        error = InferenceError(
            "Grounded metadata assigned an unsupported mental state."
        )
        code = _validation_failure_code(error)
        feedback = _validation_feedback(code)

        self.assertEqual("UnsupportedMentalState", code)
        self.assertIn("physical action", feedback)
        self.assertNotIn("game", feedback.casefold())
        self.assertNotIn("person stands", feedback.casefold())
        retry_code, retry_feedback = _retry_feedback(error)
        self.assertEqual(code, retry_code)
        self.assertIn("unsupported intent", retry_feedback)
        self.assertNotIn("game", retry_feedback.casefold())

    def test_strict_metadata_rejects_an_unsupported_interpretive_claim(self) -> None:
        request = {
            "game": {"name": "Voidling Bound", "hashtag": "#VoidlingBound"},
            "profile": {"defaultTags": []},
            "transcripts": [],
        }
        with self.assertRaises(InferenceError) as rejected:
            _strict_metadata(
                '{"titleBody":"10 KWIPECK was shown on screen",'
                '"description":"The screen displayed 10 KWIPECK as a stable in-game objective, indicating progression or reward.",'
                '"tags":["Voidling Bound"],"grounding":[],'
                '"temporalVoice":"RetrospectivePast"}',
                request,
            )
        self.assertEqual(
            "UnsupportedMentalState",
            _validation_failure_code(rejected.exception),
        )

    def test_uncoupled_knowledge_retry_requires_claim_or_no_citation(self) -> None:
        code = _validation_failure_code(
            InferenceError(
                "Grounded metadata knowledge claim did not use a canonical name or two distinctive cited-passage terms."
            )
        )
        self.assertEqual("UncoupledKnowledgeReference", code)
        feedback = _validation_feedback(code)
        self.assertIn("remove the grounding item entirely", feedback)
        self.assertIn("supported canonical name", feedback)
        self.assertNotIn("Ghostwire", feedback)

    def test_strict_metadata_rejects_unreviewed_transcript_reuse(self) -> None:
        request = {
            "game": {
                "name": "Example Game",
                "hashtag": "#ExampleGame",
                "notes": None,
            },
            "transcripts": [
                {
                    "authority": "AutomaticUnreviewed",
                    "text": "automatic words should never become audience copy",
                }
            ],
            "evidence": [],
            "gameKnowledge": None,
            "profile": {
                "audienceAddress": "Chat",
                "namingGuidance": None,
                "defaultTags": [],
                "voicePerspective": "CreatorFirstPerson",
                "variantIntent": "DirectAction",
            },
        }
        with self.assertRaises(InferenceError) as captured:
            _strict_metadata(
                '{"titleBody":"Chose the next skill",'
                '"description":"Automatic words should never become the description.",'
                '"tags":["ExampleGame"],"grounding":[],"temporalVoice":"RetrospectivePast"}',
                request,
            )
        self.assertIn(
            'Rejected phrase: "automatic words should never"',
            str(captured.exception),
        )
        code, feedback = _retry_feedback(captured.exception)
        self.assertEqual("UnreviewedTranscriptReuse", code)
        self.assertNotIn("automatic words should never", feedback)
        self.assertIn("withheld", feedback)

        retry_messages = _metadata_messages(
            request,
            _prompt_text(),
            validation_feedback=feedback,
            grounded_drafts=[_visual_draft("A room", "A hand opens a door")],
            withhold_unreviewed_transcripts=True,
            schema_valid_rejected_json=(
                '{"description":"Automatic words should never become the description.",'
                '"grounding":[],"tags":["ExampleGame"],'
                '"temporalVoice":"RetrospectivePast",'
                '"titleBody":"Chose the next skill"}'
            ),
            rejected_rule_codes=("UnreviewedTranscriptReuse",),
        )
        retry_text = retry_messages[1]["content"][0]["text"]
        self.assertNotIn(
            "automatic words should never become audience copy",
            retry_text,
        )
        self.assertNotIn("Example Game", retry_text)
        self.assertNotIn("#ExampleGame", retry_text)
        self.assertIn("identityWithheldForSafety", retry_text)
        self.assertIn(
            "withheld",
            retry_messages[-1]["content"][0]["text"],
        )
        self.assertIn("no spoken or readable wording is authorized", retry_text)
        self.assertIn(
            "do not quote, paraphrase, summarize",
            retry_text.casefold(),
        )
        self.assertEqual("assistant", retry_messages[-2]["role"])
        self.assertEqual("user", retry_messages[-1]["role"])
        self.assertIn("not factual evidence", retry_messages[-1]["content"][0]["text"])

    def test_strict_metadata_rejects_redundant_game_name_in_title(self) -> None:
        request = {
            "game": {"name": "Example Game", "hashtag": "#ExampleGame"},
            "transcripts": [],
        }
        with self.assertRaises(InferenceError):
            _strict_metadata(
                '{"titleBody":"Opened the gate in Example Game",'
                '"description":"I opened the gate and moved into the next corridor.",'
                '"tags":["gate"],"grounding":[],"temporalVoice":"RetrospectivePast"}',
                request,
            )

    def test_title_repetition_requires_distinct_description_detail(self) -> None:
        request = {
            "game": {
                "name": "Example Game",
                "hashtag": "#ExampleGame",
                "notes": None,
            },
            "transcripts": [],
            "evidence": [],
            "gameKnowledge": None,
            "profile": {
                "audienceAddress": "Chat",
                "namingGuidance": None,
                "defaultTags": [],
                "voicePerspective": "CreatorFirstPerson",
                "variantIntent": "DirectAction",
            },
        }
        with self.assertRaises(InferenceError) as captured:
            _strict_metadata(
                '{"titleBody":"Opened the sealed gate",'
                '"description":"I opened the sealed gate again.",'
                '"tags":["gate"],"grounding":[],"temporalVoice":"RetrospectivePast"}',
                request,
            )
        self.assertEqual(
            "Opened the sealed gate",
            captured.exception.rejected_title_body,
        )
        self.assertEqual(
            "I opened the sealed gate again.",
            captured.exception.rejected_description,
        )
        envelope = _retry_correction_envelope(captured.exception)
        self.assertEqual(
            {
                "nonEvidence": True,
                "rejectedTitleBody": "Opened the sealed gate",
                "rejectedDescription": "I opened the sealed gate again.",
            },
            envelope,
        )
        code, feedback = _retry_feedback(captured.exception)
        self.assertEqual("TitleDescriptionRepetition", code)
        self.assertIn("full title-body phrase", feedback)
        self.assertIn("at least two supported content words", feedback)
        retry_messages = _metadata_messages(
            request,
            _prompt_text(),
            validation_feedback=feedback,
            grounded_drafts=[_visual_draft("A courtyard", "A sealed gate opened")],
            schema_valid_rejected_json=(
                '{"description":"I opened the sealed gate again.",'
                '"grounding":[],"tags":["gate"],'
                '"temporalVoice":"RetrospectivePast",'
                '"titleBody":"Opened the sealed gate"}'
            ),
            retry_correction_envelope=envelope,
            rejected_rule_codes=(code,),
        )
        first_pass_text = retry_messages[1]["content"][0]["text"]
        correction_text = retry_messages[-1]["content"][0]["text"]
        self.assertIn("must not contain the complete titleBody phrase", first_pass_text)
        self.assertIn('"rejectedDescription"', correction_text)
        self.assertIn("rewrite that field completely", correction_text)
        self.assertIn("absent from rejectedTitleBody", correction_text)
        result = _strict_metadata(
            '{"titleBody":"Opened the sealed gate",'
            '"description":"I opened the sealed gate and revealed a flooded courtyard beyond it.",'
            '"tags":["gate"],"grounding":[],"temporalVoice":"RetrospectivePast"}',
            request,
        )
        self.assertIn("flooded courtyard", result["description"])

    def test_strict_metadata_allows_reviewed_creator_voice(self) -> None:
        request = {
            "game": {"name": "Example Game", "hashtag": "#ExampleGame"},
            "transcripts": [
                {
                    "authority": "UserCorrected",
                    "text": "I finally found the hidden switch",
                }
            ],
        }
        result = _strict_metadata(
            '{"titleBody":"Found the hidden switch",'
            '"description":"I finally found the hidden switch and opened the sealed door.",'
            '"tags":["ExampleGame","hidden switch"],"grounding":[],"temporalVoice":"RetrospectivePast"}',
            request,
        )
        self.assertTrue(result["description"].startswith("I finally"))

    def test_schema_constrains_grounding_to_clip_linked_identities(self) -> None:
        request = self.knowledge_request()
        canonical, _ = _metadata_schema(request)
        grounding = json.loads(canonical)["properties"]["grounding"]
        item = grounding["items"]["properties"]
        binding_id = grounding_binding_id("gkp-linked", "visual-change-1")

        self.assertEqual(2, grounding["maxItems"])
        self.assertEqual(
            [binding_id],
            item["bindingIds"]["items"]["enum"],
        )
        self.assertEqual(["Title", "Description"], item["audienceField"]["enum"])

    def test_strict_metadata_accepts_exact_clip_linked_claim(self) -> None:
        request = self.knowledge_request()
        binding_id = grounding_binding_id("gkp-linked", "visual-change-1")
        result = _strict_metadata(
            '{"titleBody":"Found the masked visitor",'
            '"description":"I found the masked visitor waiting inside the clinic.",'
            '"tags":["clinic"],'
            '"grounding":[{"audienceField":"Description","bindingIds":["'
            + binding_id
            + '"]}],"temporalVoice":"RetrospectivePast"}',
            request,
        )

        self.assertEqual("gkp-linked", result["grounding"][0]["knowledgeReferenceIds"][0])

    def test_strict_metadata_rejects_uncoupled_knowledge_reference(self) -> None:
        request = self.knowledge_request()
        with self.assertRaises(InferenceError):
            _strict_metadata(
                '{"titleBody":"Found the masked visitor",'
                '"description":"I found the masked visitor waiting inside the clinic.",'
                '"tags":["clinic"],'
                '"grounding":[{"audienceField":"Description",'
                '"bindingIds":["gkb-foreign"]}],"temporalVoice":"RetrospectivePast"}',
                request,
            )

    def test_strict_metadata_accepts_bounded_visual_grounding_candidate(self) -> None:
        request = self.knowledge_request()
        request["gameKnowledge"]["matches"].append(
            {
                "id": "gkp-review-candidate",
                "strength": "CandidateForVisualGrounding",
                "text": "A masked visitor waits inside the clinic.",
                "clipEvidenceIds": ["bounded-review-1234"],
            }
        )
        canonical, _ = _metadata_schema(request)
        identities = json.loads(canonical)["properties"]["grounding"]["items"][
            "properties"
        ]
        self.assertEqual(
            [
                grounding_binding_id("gkp-linked", "visual-change-1"),
                grounding_binding_id(
                    "gkp-review-candidate",
                    "bounded-review-1234",
                ),
            ],
            identities["bindingIds"]["items"]["enum"],
        )
        self.assertNotIn(
            grounding_binding_id("gkp-linked", "bounded-review-1234"),
            identities["bindingIds"]["items"]["enum"],
        )
        binding_id = grounding_binding_id(
            "gkp-review-candidate",
            "bounded-review-1234",
        )
        result = _strict_metadata(
            '{"titleBody":"Found the masked visitor",'
            '"description":"I found the masked visitor waiting inside the clinic.",'
            '"tags":["clinic"],'
            '"grounding":[{"audienceField":"Description","bindingIds":["'
            + binding_id
            + '"]}],"temporalVoice":"RetrospectivePast"}',
            request,
        )
        self.assertEqual(
            "gkp-review-candidate",
            result["grounding"][0]["knowledgeReferenceIds"][0],
        )

    def test_strict_metadata_rejects_generic_citation_without_specific_claim(self) -> None:
        request = self.knowledge_request()
        binding_id = grounding_binding_id("gkp-linked", "visual-change-1")
        with self.assertRaises(InferenceError):
            _strict_metadata(
                '{"titleBody":"Faced a masked figure",'
                '"description":"I faced a masked figure beside a dock.",'
                '"tags":["mask"],'
                '"grounding":[{"audienceField":"Description","bindingIds":["'
                + binding_id
                + '"]}],"temporalVoice":"RetrospectivePast"}',
                request,
            )

    @staticmethod
    def knowledge_request() -> dict[str, object]:
        return {
            "game": {"name": "Example Game", "hashtag": "#ExampleGame"},
            "transcripts": [],
            "gameKnowledge": {
                "matches": [
                    {
                        "id": "gkp-linked",
                        "strength": "ClipLinked",
                        "text": "A masked visitor waits inside the clinic.",
                        "clipEvidenceIds": ["visual-change-1"],
                    },
                    {
                        "id": "gkp-general",
                        "strength": "GeneralContext",
                        "text": "A broad fictional premise.",
                        "clipEvidenceIds": [],
                    },
                ]
            },
        }


if __name__ == "__main__":
    unittest.main()
