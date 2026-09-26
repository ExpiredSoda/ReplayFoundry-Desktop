"""Safety-ranked reviewable candidates for bounded metadata recovery."""
from __future__ import annotations

from dataclasses import dataclass
from typing import Any

from .grounded_metadata_audience_validation import (
    contains_unsupported_mental_state,
)
from .grounded_metadata_synthesis_decoding import (
    RETRYABLE_COMPLETED_SEMANTIC_REJECTIONS,
)


_PROVENANCE_REVIEW_CODES = frozenset(
    (
        *RETRYABLE_COMPLETED_SEMANTIC_REJECTIONS,
        "EditorialFrameDrift",
        "StrictOutputValidation",
    )
)
_FATAL_REVIEW_CODES = frozenset(
    {
        "AnalysisBookkeeping",
        "CaseLocalFactRetention",
        "CrossDraftTitleContamination",
        "OutputLanguage",
        "StrictOutputValidation",
        "UncoupledKnowledgeReference",
        "UnresolvedVisualGrounding",
        "UnreviewedTranscriptReuse",
        "UnsupportedCreatorEmbodiment",
        "UnsupportedInterfaceAttribution",
        "UnsupportedKnowledgeGrounding",
        "UnstableReadableTextReuse",
    }
)
_UNSUPPORTED_INTERPRETATION_CODES = frozenset({"UnsupportedMentalState"})
_STRUCTURAL_REVIEW_CODES = frozenset(
    {
        # These packages are still schema-valid and therefore remain available
        # as continuity fallbacks, but a complete title always outranks them.
        "GameHashtag",
        "IncompleteTitle",
    }
)
_REWRITE_REVIEW_CODES = frozenset(
    {
        "EditorialFrameDrift",
        "FirstPersonTitleSubject",
        "NonRetrospectiveVoice",
        "ThirdPersonCreatorFraming",
    }
)
_REVIEW_CODE_ALIASES = {
    # The output contract has always named this public issue GameHashtag.
    "EmbeddedHashtag": "GameHashtag",
    # Literal reporting is a rewrite-quality issue, not an unknown safety issue.
    "LiteralSceneReport": "GenericOpening",
}


@dataclass(frozen=True)
class ReviewableRecoveryCandidate:
    candidate_ordinal: int
    metadata: dict[str, Any]
    trace: Any
    audit: Any
    decoded_sha256: str
    completed_json: str
    diversity_result: Any
    review_issues: tuple[str, ...]
    attestation_index: int
    pool_attestation_index: int
    rejection_rule_index: int
    raw_attestation: dict[str, Any]


def complete_review_issues(
    metadata: dict[str, Any],
    reported_issues: Any,
) -> tuple[str, ...]:
    """Complete public review codes despite strict-parser first-error masking."""
    issues: list[str] = []
    values = reported_issues if isinstance(reported_issues, list) else []
    for raw_code in values:
        code = _REVIEW_CODE_ALIASES.get(raw_code, raw_code)
        if code not in _PROVENANCE_REVIEW_CODES:
            code = "StrictOutputValidation"
        if code not in issues:
            issues.append(code)
    audience_copy = " ".join(
        value
        for value in (
            metadata.get("title"),
            metadata.get("description"),
        )
        if isinstance(value, str)
    )
    if (
        contains_unsupported_mental_state(audience_copy)
        and "UnsupportedMentalState" not in issues
    ):
        issues.append("UnsupportedMentalState")
    return tuple(issues)


def review_safety_key(
    candidate: ReviewableRecoveryCandidate,
) -> tuple[int, int, int, int, int, int]:
    """Rank complete fallbacks by risk, then preserve seed-order stability."""
    issue_set = frozenset(candidate.review_issues)
    categorized = (
        _STRUCTURAL_REVIEW_CODES
        | _FATAL_REVIEW_CODES
        | _UNSUPPORTED_INTERPRETATION_CODES
        | _REWRITE_REVIEW_CODES
    )
    return (
        len(issue_set & _STRUCTURAL_REVIEW_CODES),
        len(issue_set & _FATAL_REVIEW_CODES),
        len(issue_set & _UNSUPPORTED_INTERPRETATION_CODES),
        len(issue_set & _REWRITE_REVIEW_CODES),
        len(issue_set - categorized),
        candidate.candidate_ordinal,
    )


def primary_review_code(review_issues: tuple[str, ...]) -> str:
    """Choose the highest-risk issue as the candidate attestation code."""
    def priority(code: str) -> tuple[int, str]:
        if code in _STRUCTURAL_REVIEW_CODES:
            return 0, code
        if code in _FATAL_REVIEW_CODES:
            return 1, code
        if code in _UNSUPPORTED_INTERPRETATION_CODES:
            return 2, code
        if code in _REWRITE_REVIEW_CODES:
            return 3, code
        return 4, code

    return min(review_issues, key=priority)
