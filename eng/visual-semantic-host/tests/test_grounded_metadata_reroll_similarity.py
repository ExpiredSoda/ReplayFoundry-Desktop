from __future__ import annotations

from decimal import Decimal
from fractions import Fraction
import unittest

from replayfoundry_visual_semantic.editorial.grounded_metadata_reroll_similarity import (
    RerollTitleReference,
    RerollTitleScope,
    RerollTitleSimilarityCode,
    canonical_title_tokens,
    evaluate_reroll_title,
    normalize_terminal_single_period_title_body,
)


HASHTAG = "#TheLastofUs"
SCOPE = RerollTitleScope("last-of-us-ladder-house", Decimal(0), Decimal(40))


def _reference(title: str, scope: RerollTitleScope = SCOPE) -> RerollTitleReference:
    return RerollTitleReference(scope, title, HASHTAG)


class GroundedMetadataRerollSimilarityTests(unittest.TestCase):
    def test_real_attempts_zero_one_two_form_one_cluster(self) -> None:
        original = _reference(
            "We climbed a ladder and entered a house together. #TheLastofUs"
        )
        exact_reroll = _reference(
            "We climbed a ladder and entered a house together. #TheLastofUs"
        )
        article_reroll = _reference(
            "We climbed the ladder and entered the house together. #TheLastofUs"
        )

        exact = evaluate_reroll_title(exact_reroll, [original])
        article = evaluate_reroll_title(article_reroll, [original, exact_reroll])

        self.assertEqual(
            RerollTitleSimilarityCode.EXACT_CANONICAL_TITLE,
            exact.code,
        )
        self.assertEqual(
            RerollTitleSimilarityCode.EXACT_CANONICAL_TITLE,
            article.code,
        )
        self.assertFalse(exact.is_materially_distinct)
        self.assertFalse(article.is_materially_distinct)

    def test_real_attempt_three_is_a_materially_distinct_supported_detail(self) -> None:
        prior = [
            _reference(
                "We climbed a ladder and entered a house together. #TheLastofUs"
            ),
            _reference(
                "We climbed the ladder and entered the house together. #TheLastofUs"
            ),
        ]
        concrete = _reference(
            "We climbed a ladder into a house with Bill inside. #TheLastofUs"
        )

        result = evaluate_reroll_title(concrete, prior)

        self.assertEqual(RerollTitleSimilarityCode.MATERIALLY_DISTINCT, result.code)
        self.assertTrue(result.is_materially_distinct)
        self.assertLess(result.token_jaccard, Fraction(85, 100))

    def test_nfkc_case_and_punctuation_do_not_manufacture_diversity(self) -> None:
        original = _reference(
            "We climbed a ladder and entered a house together. #TheLastofUs"
        )
        cosmetic = _reference(
            "ＷＥ CLIMBED—THE LADDER; AND ENTERED THE HOUSE TOGETHER! #TheLastofUs"
        )

        result = evaluate_reroll_title(cosmetic, [original])

        self.assertEqual(
            canonical_title_tokens(original.title, HASHTAG),
            canonical_title_tokens(cosmetic.title, HASHTAG),
        )
        self.assertEqual(
            RerollTitleSimilarityCode.EXACT_CANONICAL_TITLE,
            result.code,
        )

    def test_high_jaccard_overlap_rejects_trivial_token_reordering(self) -> None:
        original = _reference(
            "Climbed the ladder then entered the wooden house together #TheLastofUs"
        )
        reordered = _reference(
            "Together entered the wooden house then climbed the ladder #TheLastofUs"
        )

        result = evaluate_reroll_title(reordered, [original])

        self.assertEqual(RerollTitleSimilarityCode.HIGH_TOKEN_OVERLAP, result.code)
        self.assertEqual(Fraction(1, 1), result.token_jaccard)

    def test_only_same_candidate_and_exact_cut_are_comparable(self) -> None:
        title = "Found Bill inside the house #TheLastofUs"
        other_candidate = RerollTitleScope("another-candidate", 0, 40)
        other_cut = RerollTitleScope("last-of-us-ladder-house", 1, 40)

        result = evaluate_reroll_title(
            _reference(title),
            [_reference(title, other_candidate), _reference(title, other_cut)],
        )

        self.assertEqual(RerollTitleSimilarityCode.NO_COMPARABLE_PRIOR, result.code)
        self.assertEqual(0, result.comparable_prior_count)
        self.assertTrue(result.is_materially_distinct)

    def test_every_comparable_prior_is_considered(self) -> None:
        first = _reference("Found Bill inside the house #TheLastofUs")
        later = _reference("Climbed onto the wooden roof #TheLastofUs")
        candidate = _reference("Found Bill inside the house! #TheLastofUs")

        result = evaluate_reroll_title(candidate, [first, later])

        self.assertEqual(
            RerollTitleSimilarityCode.EXACT_CANONICAL_TITLE,
            result.code,
        )
        self.assertEqual(first.title, result.matched_prior_title)
        self.assertEqual(2, result.comparable_prior_count)

    def test_single_terminal_period_normalization_changes_no_words(self) -> None:
        self.assertEqual(
            "Found Bill inside",
            normalize_terminal_single_period_title_body("Found Bill inside."),
        )
        self.assertEqual(
            "Then Bill appeared...",
            normalize_terminal_single_period_title_body("Then Bill appeared..."),
        )
        self.assertEqual(
            "Did Bill make it inside?",
            normalize_terminal_single_period_title_body("Did Bill make it inside?"),
        )
        self.assertEqual(
            ".",
            normalize_terminal_single_period_title_body("."),
        )

    def test_invalid_scope_title_and_hashtag_fail_closed(self) -> None:
        for start, end in ((0, 0), (-1, 1), (0, "NaN")):
            with self.subTest(start=start, end=end), self.assertRaises(ValueError):
                RerollTitleScope("candidate", start, end)
        with self.assertRaises(ValueError):
            _reference("Found Bill inside")
        with self.assertRaises(ValueError):
            RerollTitleReference(SCOPE, "Found Bill #Wrong", HASHTAG)
        with self.assertRaises(ValueError):
            _reference("We and the #TheLastofUs")


if __name__ == "__main__":
    unittest.main()
