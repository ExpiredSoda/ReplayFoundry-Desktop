"""Deterministic, model-free diversity checks for editorial title rerolls."""
from __future__ import annotations

import re
import unicodedata
from dataclasses import dataclass
from decimal import Decimal, InvalidOperation
from enum import Enum
from fractions import Fraction
from typing import Iterable


MAXIMUM_TITLE_LENGTH = 100
MAXIMUM_RETAINED_TITLES = 8
SIMILARITY_THRESHOLD = Fraction(85, 100)
REROLL_DIVERSITY_POLICY_VERSION = "grounded-editorial-reroll-diversity-1.0"

# This deliberately small closed class removes grammatical glue, not content.
# Do not add game vocabulary, actions, objects, locations, or creator-specific
# wording here: those terms carry the distinction this policy must preserve.
_CLOSED_CLASS_WORDS = frozenset(
    {
        "a",
        "an",
        "and",
        "at",
        "but",
        "by",
        "for",
        "from",
        "i",
        "in",
        "into",
        "me",
        "my",
        "of",
        "on",
        "or",
        "our",
        "the",
        "to",
        "us",
        "we",
        "with",
    }
)
_WORD = re.compile(r"[^\W_]+", re.UNICODE)


class RerollTitleSimilarityCode(str, Enum):
    NO_COMPARABLE_PRIOR = "NoComparablePrior"
    EXACT_CANONICAL_TITLE = "ExactCanonicalTitle"
    HIGH_TOKEN_OVERLAP = "HighTokenOverlap"
    MATERIALLY_DISTINCT = "MateriallyDistinct"


@dataclass(frozen=True)
class RerollTitleScope:
    """Identity within which reroll diversity may be compared."""

    candidate_id: str
    source_start_seconds: Decimal
    source_end_seconds: Decimal

    def __post_init__(self) -> None:
        candidate_id = self.candidate_id.strip()
        if not candidate_id or len(candidate_id) > 256:
            raise ValueError("Reroll title scope requires a bounded candidate ID.")
        start = _decimal(self.source_start_seconds, "source start")
        end = _decimal(self.source_end_seconds, "source end")
        if start < 0 or end <= start:
            raise ValueError("Reroll title scope requires a positive exact source cut.")
        object.__setattr__(self, "candidate_id", candidate_id)
        object.__setattr__(self, "source_start_seconds", start)
        object.__setattr__(self, "source_end_seconds", end)


@dataclass(frozen=True)
class RerollTitleReference:
    scope: RerollTitleScope
    title: str
    game_hashtag: str

    def __post_init__(self) -> None:
        if not isinstance(self.scope, RerollTitleScope):
            raise TypeError("Reroll title reference requires a validated scope.")
        title = self.title.strip()
        hashtag = self.game_hashtag.strip()
        if not title or len(title) > MAXIMUM_TITLE_LENGTH:
            raise ValueError("Reroll title must use the bounded product title length.")
        if (
            not hashtag.startswith("#")
            or len(hashtag) < 2
            or any(character.isspace() for character in hashtag)
        ):
            raise ValueError("Reroll title requires one canonical game hashtag.")
        _title_body(title, hashtag)
        canonical_title_tokens(title, hashtag)
        object.__setattr__(self, "title", title)
        object.__setattr__(self, "game_hashtag", hashtag)


@dataclass(frozen=True)
class RerollTitleSimilarityResult:
    code: RerollTitleSimilarityCode
    canonical_tokens: tuple[str, ...]
    comparable_prior_count: int
    matched_prior_title: str | None
    matched_prior_tokens: tuple[str, ...]
    token_jaccard: Fraction

    @property
    def is_materially_distinct(self) -> bool:
        return self.code in {
            RerollTitleSimilarityCode.NO_COMPARABLE_PRIOR,
            RerollTitleSimilarityCode.MATERIALLY_DISTINCT,
        }


def canonical_title_tokens(title: str, game_hashtag: str) -> tuple[str, ...]:
    """Return content-bearing tokens after bounded grammatical normalization."""

    body = _title_body(title.strip(), game_hashtag.strip())
    normalized = unicodedata.normalize("NFKC", body).casefold()
    tokens = tuple(
        token
        for token in _WORD.findall(normalized)
        if token not in _CLOSED_CLASS_WORDS
        and (len(token) > 1 or token.isdecimal())
    )
    if not tokens:
        raise ValueError("Reroll title retains no content-bearing title tokens.")
    return tokens


def normalize_terminal_single_period_title_body(title_body: str) -> str:
    """Remove only a cosmetic final full stop from a non-empty title body."""

    body = title_body.strip()
    if not body.endswith(".") or body.endswith("..."):
        return body
    normalized = body[:-1].rstrip()
    return normalized if normalized else body


def evaluate_reroll_title(
    candidate: RerollTitleReference,
    prior_titles: Iterable[RerollTitleReference],
) -> RerollTitleSimilarityResult:
    """Compare one title only with prior titles from its exact retained cut."""

    if not isinstance(candidate, RerollTitleReference):
        raise TypeError("Reroll title evaluation requires a validated candidate.")
    candidate_tokens = canonical_title_tokens(
        candidate.title,
        candidate.game_hashtag,
    )
    comparable: list[tuple[RerollTitleReference, tuple[str, ...]]] = []
    for prior in tuple(prior_titles):
        if not isinstance(prior, RerollTitleReference):
            raise TypeError("Prior reroll titles must be validated references.")
        if prior.scope != candidate.scope:
            continue
        if prior.game_hashtag != candidate.game_hashtag:
            raise ValueError(
                "Titles from one candidate cut must retain one exact game hashtag."
            )
        comparable.append(
            (
                prior,
                canonical_title_tokens(prior.title, prior.game_hashtag),
            )
        )

    if not comparable:
        return RerollTitleSimilarityResult(
            RerollTitleSimilarityCode.NO_COMPARABLE_PRIOR,
            candidate_tokens,
            0,
            None,
            (),
            Fraction(0, 1),
        )

    for prior, prior_tokens in comparable:
        if candidate_tokens == prior_tokens:
            return RerollTitleSimilarityResult(
                RerollTitleSimilarityCode.EXACT_CANONICAL_TITLE,
                candidate_tokens,
                len(comparable),
                prior.title,
                prior_tokens,
                Fraction(1, 1),
            )

    similarities = [
        (_jaccard(candidate_tokens, prior_tokens), prior, prior_tokens)
        for prior, prior_tokens in comparable
    ]
    similarity, matched, matched_tokens = max(
        similarities,
        key=lambda item: item[0],
    )
    code = (
        RerollTitleSimilarityCode.HIGH_TOKEN_OVERLAP
        if similarity >= SIMILARITY_THRESHOLD
        else RerollTitleSimilarityCode.MATERIALLY_DISTINCT
    )
    return RerollTitleSimilarityResult(
        code,
        candidate_tokens,
        len(comparable),
        matched.title,
        matched_tokens,
        similarity,
    )


def _decimal(value: object, label: str) -> Decimal:
    if isinstance(value, bool):
        raise ValueError(f"Reroll title {label} must be a finite decimal.")
    try:
        decimal = value if isinstance(value, Decimal) else Decimal(str(value))
    except (InvalidOperation, ValueError):
        raise ValueError(
            f"Reroll title {label} must be a finite decimal."
        ) from None
    if not decimal.is_finite():
        raise ValueError(f"Reroll title {label} must be a finite decimal.")
    return decimal


def _title_body(title: str, game_hashtag: str) -> str:
    suffix = " " + game_hashtag
    if not title.endswith(suffix):
        raise ValueError(
            "Reroll title must end with one space and the exact game hashtag."
        )
    body = title[: -len(suffix)].rstrip()
    if not body:
        raise ValueError("Reroll title requires audience copy before its hashtag.")
    return body


def _jaccard(a: tuple[str, ...], b: tuple[str, ...]) -> Fraction:
    left = frozenset(a)
    right = frozenset(b)
    return Fraction(len(left & right), len(left | right))


__all__ = [
    "MAXIMUM_TITLE_LENGTH",
    "REROLL_DIVERSITY_POLICY_VERSION",
    "SIMILARITY_THRESHOLD",
    "RerollTitleReference",
    "RerollTitleScope",
    "RerollTitleSimilarityCode",
    "RerollTitleSimilarityResult",
    "canonical_title_tokens",
    "evaluate_reroll_title",
    "normalize_terminal_single_period_title_body",
]
