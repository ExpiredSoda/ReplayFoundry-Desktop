import copy
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from replayfoundry_visual_semantic.scene_copy import VERSION, copy_key, run, validate_copy
from replayfoundry_visual_semantic.scene_cache import save_review
from replayfoundry_visual_semantic.scene_value import relevance


class SceneCopyTests(unittest.TestCase):
    def test_cache_reuses_retry_but_binds_video_model_and_writing_context(self):
        case = dict(candidateId="clip", attempt=1, reviewVideoHash="a" * 64,
                    context=dict(titleLimit=72, priorTitles=[], voice="Plain", facts=["A car reached the runway."]))
        original = copy_key("model-one", case)
        retry = copy.deepcopy(case)
        retry["attempt"] = 2
        self.assertEqual(original, copy_key("model-one", retry))
        self.assertNotEqual(original, copy_key("model-two", case))
        for field, value in (("priorTitles", ["A car reached the runway"]), ("voice", "Playful"),
                             ("facts", ["A car stopped before the runway."]), ("candidateMode", "MontageSegment")):
            changed = copy.deepcopy(case)
            changed["context"][field] = value
            self.assertNotEqual(original, copy_key("model-one", changed))
        changed = copy.deepcopy(case)
        changed["reviewVideoHash"] = "b" * 64
        self.assertNotEqual(original, copy_key("model-one", changed))

    def test_valid_cached_copy_returns_for_current_attempt_without_loading_model(self):
        case = dict(candidateId="clip", attempt=2, reviewVideoHash="a" * 64,
                    context=dict(titleLimit=72, priorTitles=[]))
        row = dict(candidateId="clip", attempt=1, status="Succeeded", elapsedSeconds=12,
                   copy=dict(titleBody="The car reached the runway", description="Gunfire followed the chase."),
                   review=dict(grounded=True, useful=True, reason=""))
        row["neuralGrounding"] = row["neuralQuality"] = {**relevance([2,2]), "version":"copy-judgment-1"}
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            save_review(root / "cache", copy_key("model-one", case), row)
            (root / "input.json").write_text(json.dumps(dict(schemaVersion=VERSION, modelHash="model-one", cases=[case])))
            run(SimpleNamespace(input=root / "input.json", output=root / "output.json", cache=root / "cache",
                                model=root / "model-does-not-exist"))
            result = json.loads((root / "output.json").read_text())
            self.assertEqual(1, result["cacheHits"])
            self.assertEqual(2, result["cases"][0]["attempt"])
            self.assertEqual(row["copy"], result["cases"][0]["copy"])
            self.assertEqual(12, result["cases"][0]["cachedInferenceSeconds"])

    def test_repeat_or_overlong_copy_is_rejected(self):
        for value in (dict(titleBody="Same",description="Same."),dict(titleBody="Old title",description="A different result."),
                      dict(titleBody="a"*81,description="The car reached the runway."),dict(titleBody="A win #fake",description="Something.")):
            with self.subTest(value=value), self.assertRaises(ValueError):
                validate_copy(value,80,["Old title"])

    def test_complementary_bounded_copy_is_preserved(self):
        value=dict(titleBody="The RC car reached the plane",description="Gunfire followed the chase along the runway.")
        self.assertEqual(value,validate_copy(value.copy(),72,[]))
