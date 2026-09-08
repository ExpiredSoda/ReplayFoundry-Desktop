import tempfile
import hashlib
import json
import unittest
from pathlib import Path
from types import SimpleNamespace
from replayfoundry_visual_semantic.recording_index import PROMPT, VERSION, run, validate_prediction, fingerprint


class RecordingIndexTests(unittest.TestCase):
    def test_complete_saved_map_needs_no_model_or_video_decoder(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "source.mkv"
            source.write_bytes(b"unchanged recording identity")
            request = dict(schemaVersion=VERSION, sourcePath=str(source), durationSeconds=30,
                           modelHash="model", region=[0, 0, 1, 1], transcript=[], preferences={})
            identity = dict(version=VERSION, prompt=hashlib.sha256(PROMPT.encode()).hexdigest(), model="model",
                            source=fingerprint(source), duration=30.0, region=[0, 0, 1, 1], contextRegion=None, transcript=[], preferences={})
            key = hashlib.sha256(json.dumps(identity, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
            prediction = dict(gameplay=True, funny=False, commentary=False, menu=False, summary="A vehicle chase.",
                              visibleGameTitle="", speechMomentIds=[], editorialValue=85, lore=False, speechSource="unknown")
            row = dict(ordinal=0, start=0, end=30, prediction=prediction, elapsedSeconds=12)
            cache = root / "cache"
            cache.mkdir()
            (cache / (key + ".json")).write_text(json.dumps(dict(key=key, windows=[row])))
            (root / "input.json").write_text(json.dumps(request))
            run(SimpleNamespace(input=root / "input.json", output=root / "output.json", cache=cache,
                                model=root / "missing-model", ffmpeg=root / "missing-ffmpeg"))
            result = json.loads((root / "output.json").read_text())
            self.assertEqual(1, result["cacheHits"])
            self.assertEqual([row], result["windows"])

    def prediction(self):
        return dict(gameplay=True, funny=True, commentary=True, menu=False,
                    summary="A vehicle chase with a spoken reaction.", visibleGameTitle="", speechMomentIds=[3, 4], editorialValue=85, lore=False, speechSource="creator")

    def test_overlapping_labels_are_preserved(self):
        value = validate_prediction(self.prediction())
        self.assertTrue(value["gameplay"] and value["funny"] and value["commentary"])

    def test_missing_or_invented_fields_are_not_accepted(self):
        for change in ({"confidence":1}, {"gameplay":"yes"}, {"speechMomentIds":[True]},
                       {"speechMomentIds":[1, 2, 3, 4]}, {"summary":"a"*301}):
            with self.subTest(change=change), self.assertRaises(ValueError):
                validate_prediction({**self.prediction(), **change})

    def test_content_change_invalidates_identity_even_at_same_size(self):
        with tempfile.TemporaryDirectory() as temporary:
            source = Path(temporary)/"source.mkv"
            source.write_bytes(b"first recording")
            original = fingerprint(source)
            source.write_bytes(b"other recording")
            self.assertNotEqual(original, fingerprint(source))

    def test_story_and_original_commentary_can_overlap(self):
        result = validate_prediction({**self.prediction(), "lore": True})
        self.assertTrue(result["lore"] and result["commentary"])

    def test_game_dialogue_does_not_establish_creator_commentary(self):
        with self.assertRaises(ValueError):
            validate_prediction({**self.prediction(), "speechSource": "game"})

    def test_missing_speech_is_unknown_not_negative_training_evidence(self):
        from replayfoundry_visual_semantic.recording_index import validate_speech_ownership
        prediction = {**self.prediction(), "speechMomentIds": [], "commentary": False, "speechSource": "unknown"}
        validate_speech_ownership(prediction, [])
        with self.assertRaises(ValueError):
            validate_speech_ownership({**prediction, "speechSource": "none"}, [])
