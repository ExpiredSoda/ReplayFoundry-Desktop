import copy
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
from replayfoundry_visual_semantic.scene_copy import VERSION, copy_key, run, validate_copy
from replayfoundry_visual_semantic.scene_cache import save_review
from replayfoundry_visual_semantic.scene_value import relevance
from replayfoundry_visual_semantic import copy_judgment


class SceneCopyTests(unittest.TestCase):
    def test_independent_retry_keeps_all_source_text_and_never_mutates_original_sequence(self):
        part = dict(centralEvent="Invented result", setupObservation="Before", outcomeObservation="Pending",
            reviewedContext=dict(categories=[{"explanation":"Invented result"}], sourceText=[{"text":"Will you proceed?"}], speech=[]))
        original = dict(centralEvent="Sequence", reviewedContext=dict(sequence=[part, copy.deepcopy(part)]))
        corrected = copy_judgment.independent_context(original)
        self.assertEqual(2, len(corrected["reviewedContext"]["sequence"]))
        for retained in corrected["reviewedContext"]["sequence"]:
            self.assertEqual("", retained["centralEvent"])
            self.assertEqual(part["reviewedContext"]["sourceText"], retained["reviewedContext"]["sourceText"])
            self.assertNotIn("categories", retained["reviewedContext"])
        self.assertEqual("Invented result", part["centralEvent"])

    def test_confirmed_conflict_withholds_summary_but_preserves_observations_and_speech(self):
        context = dict(centralEvent="The box opened.", setupObservation="A closed box.",
            outcomeObservation="Will you open the box?", reviewedContext=dict(categories=[{"explanation":"Opened"}],
            speech=[dict(text="Can anybody remember the code?",role="CreatorSpeech",roleSource="UserConfirmed")]))
        disputed = {**relevance([-8, -7]), "version":copy_judgment.VERSION}
        with patch.object(copy_judgment, "compare", return_value=disputed):
            author, _ = copy_judgment.reconcile_author_context(None, None, None, context)
        self.assertNotEqual(context["centralEvent"], author["centralEvent"])
        self.assertNotIn("categories", author["reviewedContext"])
        self.assertEqual(context["reviewedContext"]["speech"], author["reviewedContext"]["speech"])
        self.assertEqual(context["outcomeObservation"], author["outcomeObservation"])
        self.assertIn("categories", context["reviewedContext"], "Original audit evidence is immutable")

    def test_grounding_requires_agreement_in_both_answer_orders(self):
        disputed = {**relevance([8, -7]), "version":copy_judgment.VERSION}
        with patch.object(copy_judgment, "compare", return_value=disputed):
            selected, _, _ = copy_judgment.judge(None, None, None, dict(centralEvent="Ambiguous", priorTitles=[]),
                [dict(titleBody="A completed action",description="The event ended successfully.")])
        self.assertIsNone(selected)

    def test_independent_end_observation_reaches_grounding_without_prior_titles_as_facts(self):
        context = dict(centralEvent="The box opened and its contents appeared.",
            setupObservation="The box is locked.", outcomeObservation="A prompt asks: Will you open the box?",
            priorTitles=["The box opened"], reviewedContext={})
        seen = []
        def compare(model, processor, torch, prompt, evidence, options):
            seen.append((prompt, evidence))
            return {**relevance([-2, -2] if prompt == copy_judgment.GROUND else [2, 2]), "version":copy_judgment.VERSION}
        with patch.object(copy_judgment, "compare", side_effect=compare):
            selected, rows, comparisons = copy_judgment.judge(None, None, None, context,
                [dict(titleBody="The box opened", description="The contents were revealed.")])
        self.assertIsNone(selected)
        factual = next(evidence for prompt, evidence in seen if prompt == copy_judgment.GROUND)
        self.assertEqual(context["outcomeObservation"], factual["outcomeObservation"])
        self.assertNotIn("priorTitles", factual)
        self.assertFalse(any(prompt == copy_judgment.NOVELTY for prompt, _ in seen))

    def test_cosmetic_new_title_cannot_pass_only_on_grounding_and_quality(self):
        context = dict(centralEvent="The player unlocked a door.", priorTitles=["The locked door finally opened"])
        def compare(model, processor, torch, prompt, evidence, options):
            return {**relevance([-2, -2] if prompt == copy_judgment.NOVELTY else [2, 2]), "version":copy_judgment.VERSION}
        with patch.object(copy_judgment, "compare", side_effect=compare):
            selected, rows, _ = copy_judgment.judge(None, None, None, context,
                [dict(titleBody="Finally opening the locked door", description="The key fit the lock.")])
        self.assertIsNone(selected)
        self.assertLess(rows[0]["novelty"]["value"], .5)

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

    def test_ambiguous_variation_cannot_replace_saved_wording_even_with_positive_average(self):
        context = dict(centralEvent="The door closed.", priorTitles=["The Door Finally Closed"])
        def compare(model, processor, torch, prompt, evidence, options):
            return {**relevance([8, -1] if prompt == copy_judgment.NOVELTY else [3, 3]), "version":copy_judgment.VERSION}
        with patch.object(copy_judgment, "compare", side_effect=compare):
            selected, rows, _ = copy_judgment.judge(None, None, None, context,
                [dict(titleBody="Closing the Door at Last", description="The lock clicked shut.")])
        self.assertGreater(rows[0]["novelty"]["value"], .5)
        self.assertIsNone(selected)

    def test_valid_cached_copy_returns_for_current_attempt_without_loading_model(self):
        case = dict(candidateId="clip", attempt=2, reviewVideoHash="a" * 64,
                    context=dict(titleLimit=72, priorTitles=[]))
        row = dict(candidateId="clip", attempt=1, status="Succeeded", elapsedSeconds=12,
                   copy=dict(titleBody="The car reached the runway", description="Gunfire followed the chase."),
                   review=dict(grounded=True, useful=True, reason=""))
        row["neuralGrounding"] = row["neuralQuality"] = {**relevance([2,2]), "version":"copy-judgment-2"}
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
