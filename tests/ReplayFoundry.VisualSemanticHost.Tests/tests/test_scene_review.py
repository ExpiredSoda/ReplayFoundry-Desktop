import unittest
from replayfoundry_visual_semantic.scene_review import validate, combine_assessment
from replayfoundry_visual_semantic.recording_index import window_speech, validate_speech_ownership


class SceneReviewTests(unittest.TestCase):
    def case(self):
        return dict(setup="A small vehicle approaches an aircraft.", event="A vehicle races down the runway as gunfire and explosions occur.", outcome="The vehicle reaches the aircraft and an explosion follows.",
            firstFrame=1, lastFrame=10, kind="Action", hasDistinctEvent="Yes", hasPayoff="Yes", onlyRoutineMovementOrMenus="No", needsEarlierContext="No",
            onlyLightingOrCameraChanges="No", transcriptSupport="NotSupplied", editorialValue=85,recommendation="Keep")

    def test_temporal_evidence_requires_available_ordered_frames(self):
        self.assertEqual("Action", validate(self.case(), 12, False)["kind"])
        for change in ({"editorialValue":101}, {"firstFrame":12}, {"lastFrame":-1}, {"firstFrame":True}, {"firstFrame":11,"lastFrame":1}):
            with self.subTest(change=change), self.assertRaises(ValueError):
                validate({**self.case(), **change}, 12, False)

    def test_no_invented_transcript_or_labels(self):
        for change in ({"transcriptSupport":"Supports"}, {"kind":"Perfect"}, {"hasPayoff":True}, {"setup":""}):
            with self.subTest(change=change), self.assertRaises(ValueError):
                validate({**self.case(), **change}, 12, False)

    def test_judgment_cannot_rewrite_independent_states_or_cite_other_frames(self):
        states = dict(setup="A person holds another person's collar.", outcome="The person is still held by the collar.")
        judgment = {key:value for key,value in self.case().items() if key not in states and key != "editorialValue"}
        self.assertEqual(states["outcome"], combine_assessment(states, judgment, False, 85)["outcome"])
        for change in ({"outcome":"The person jumps."}, {"lastFrame":5}, {"firstFrame":5}, {"editorialValue":75}):
            with self.subTest(change=change), self.assertRaises(ValueError):
                combine_assessment(states, {**judgment, **change}, False, 85)

    def test_spoken_nominations_belong_to_the_supplied_window(self):
        words = window_speech([dict(start=0,end=3,text="old"),dict(start=47,end=51,text="reaction")],45,90)
        self.assertEqual([1],[row["id"] for row in words])
        validate_speech_ownership(dict(speechMomentIds=[1],commentary=True),words)
        with self.assertRaises(ValueError):
            validate_speech_ownership(dict(speechMomentIds=[0],commentary=True),words)
        with self.assertRaises(ValueError):
            validate_speech_ownership(dict(speechMomentIds=[],commentary=True,speechSource="creator"),[])
