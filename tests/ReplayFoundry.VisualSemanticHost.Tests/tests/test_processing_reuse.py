import copy
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

from replayfoundry_visual_semantic.frame_extraction import missing_ranges, extract_index_frames, extract_review_frames
from replayfoundry_visual_semantic.scene_facts import validate_fact_check, validate_copy_numbers
from replayfoundry_visual_semantic.vision_reuse import VisionFeatureReuse


class ProcessingReuseTests(unittest.TestCase):
    def test_dialogue_check_keeps_source_excerpts_separate_from_unverified_claims(self):
        from replayfoundry_visual_semantic.scene_facts import text_detail_evidence
        claims = {"event":"A voice message from Casey played."}
        check = {"claims":{"event":{"verbatimSourceText":"Hey Casey, thanks for calling."}}}
        evidence = text_detail_evidence(claims, check, [])
        self.assertEqual(claims["event"], evidence["visuallyReviewedEvent"])
        self.assertEqual("Hey Casey, thanks for calling.", evidence["sourceExcerpts"][0]["sourceText"])
        self.assertIsNone(text_detail_evidence({}, {"claims":{}}, []))
        self.assertIsNotNone(text_detail_evidence({"event":"A message."}, {"claims":{}}, [{"text":"A message."}]))

    def test_closing_vision_reuse_releases_the_model_without_a_bound_method_cycle(self):
        import weakref
        class Owner:
            training = False
            def get_image_features(self, pixels, grid=None):
                return pixels
        owner = Owner()
        reference = weakref.ref(owner)
        reuse = VisionFeatureReuse(SimpleNamespace(model=owner), SimpleNamespace())
        reuse.close()
        reuse.close()
        self.assertNotIn("get_image_features", vars(owner))
        del owner
        self.assertIsNone(reference())

    def test_long_recording_reduction_preserves_speaker_and_category_evidence(self):
        from replayfoundry_visual_semantic.recording_comparison import compact_regions
        rows = [dict(ordinal=129, summary="A message played.", labels=["Lore"], speechSource="GameDialogue"),
                dict(ordinal=130, summary="The creator reacted.", labels=["Commentary"], speechSource="CreatorSpeech")]
        result = compact_regions([dict(firstWindow=129, lastWindow=130)], rows)[0]
        self.assertEqual((129, 130), (result["firstSourceWindow"], result["lastSourceWindow"]))
        self.assertEqual(["GameDialogue", "CreatorSpeech"], [row["speechSource"] for row in result["evidence"]])
        self.assertEqual(["Lore"], result["evidence"][0]["labels"])

    def test_partial_resume_decodes_only_missing_contiguous_sections(self):
        self.assertEqual([(154, 155, 6930, 6938)], list(missing_ranges(155, set(range(154)), 6938)))
        self.assertEqual([(1, 3, 45, 135), (4, 5, 180, 192)], list(missing_ranges(5, {0, 3}, 192)))
        self.assertEqual([], list(missing_ranges(2, {0, 1}, 90)))
        commands = []
        def decode(command, **kwargs):
            commands.append(command)
            folder = Path(command[-1]).parent
            for prefix, count in (("game", 2), ("context", 1)):
                for index in range(count):
                    (folder/f"{prefix}-{index+1:06d}.jpg").write_bytes(b"frame")
        with tempfile.TemporaryDirectory() as temporary, patch("replayfoundry_visual_semantic.frame_extraction.subprocess.run", decode):
            games, people, seconds = extract_index_frames("ffmpeg", "recording.mkv", temporary,
                155, set(range(154)), 6938, [0,0,1,1], None)
        self.assertEqual(8, seconds)
        self.assertEqual([154], list(games))
        self.assertEqual(2, len(games[154]))
        self.assertEqual(1, len(people[154]))
        self.assertEqual("6930", commands[0][commands[0].index("-ss")+1])
        self.assertIn("trim=duration=8", commands[0][commands[0].index("-filter_complex")+1])

    def test_review_reuses_identical_sample_frames_without_shifting_later_samples(self):
        commands = []
        def decode(command, **kwargs):
            commands.append(command)
            for index in range(3):
                (Path(command[-1]).parent/f"review-{index+1:03d}.jpg").write_bytes(b"frame")
        with tempfile.TemporaryDirectory() as temporary, patch("replayfoundry_visual_semantic.frame_extraction.subprocess.run", decode):
            frames = extract_review_frames("ffmpeg", "review.mp4", temporary, [0,.01,.0999,1.01])
            self.assertEqual(frames[1], frames[2])
            self.assertNotEqual(frames[2], frames[3])
        self.assertEqual(1, len(commands))
        self.assertIn("eq(n,11)", commands[0][commands[0].index("-vf")+1])

    def test_invented_number_fails_even_when_model_says_grounded(self):
        claims = dict(setup="A phone is visible.", event="The character dialed 911.", outcome="A voice message played.")
        check = dict(grounded=True, reason="Supported", claims={name:dict(supported=True,
            evidenceIds=["frame-0"], verbatimSourceText="") for name in claims})
        self.assertFalse(validate_fact_check(copy.deepcopy(check), claims, 12, [])["grounded"])
        check["claims"]["event"]["verbatimSourceText"] = "911"
        self.assertTrue(validate_fact_check(check, claims, 12, [])["grounded"])
        repeated = copy.deepcopy(check)
        repeated["claims"]["event"]["verbatimSourceText"] = claims["event"]
        self.assertFalse(validate_fact_check(repeated, claims, 12, [])["grounded"])
        check["claims"]["event"]["evidenceIds"] = ["frame-99"]
        with self.assertRaises(ValueError): validate_fact_check(check, claims, 12, [])

    def test_timestamp_or_stream_id_is_not_evidence_for_a_number_in_copy(self):
        draft = dict(titleBody="Dialed 911", description="A voice answered.")
        context = dict(centralEvent="A voice message played.", reviewedContext=dict(speech=[dict(id="911", start=911,
            end=912, text="Let's try 100.")]))
        with self.assertRaises(ValueError): validate_copy_numbers(draft, context)
        draft["titleBody"] = "Tried 100"
        self.assertEqual(draft, validate_copy_numbers(draft, context))

    def test_visual_reuse_is_bounded_case_local_and_disabled_for_training(self):
        tensor = SimpleNamespace(numel=lambda:4, element_size=lambda:2)
        calls = []
        def original(pixels, grid=None, **kwargs):
            calls.append(pixels)
            return SimpleNamespace(pooler_output=(tensor,), deepstack_features=[tensor])
        owner = SimpleNamespace(get_image_features=original, training=False)
        torch = SimpleNamespace(is_grad_enabled=lambda:False)
        reuse = VisionFeatureReuse(SimpleNamespace(model=owner), torch, maximum_bytes=16)
        reuse.key = lambda pixels, grid, options: (pixels, grid, tuple(options.items()))
        first = owner.get_image_features("same", "grid")
        self.assertIs(first, owner.get_image_features("same", "grid"))
        owner.get_image_features("different", "grid")
        owner.get_image_features("different", "grid")
        self.assertEqual(3, len(calls))
        self.assertEqual(16, reuse.bytes)
        reuse.clear()
        owner.get_image_features("same", "grid")
        owner.training = True
        owner.get_image_features("same", "grid")
        self.assertEqual(5, len(calls))
        reuse.close()
        self.assertIs(original, owner.get_image_features)
