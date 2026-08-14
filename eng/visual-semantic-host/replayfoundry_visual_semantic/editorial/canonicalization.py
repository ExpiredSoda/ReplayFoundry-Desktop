"""Frozen syntactic-only Prompt 2.3 canonicalization."""
from __future__ import annotations

from typing import Any

from .constants import (
    CANONICALIZATION_POLICY_VERSION,
    UNCERTAINTY_ORDER,
)
from .primitives import ordinal


def canonicalize(
    changes: list[dict[str, Any]],
    intervals: list[dict[str, Any]],
    uncertainties: list[dict[str, Any]],
    nested_change_count: int,
) -> tuple[
    list[dict[str, Any]],
    list[dict[str, Any]],
    list[dict[str, Any]],
    dict[str, Any],
]:
    interval_key = lambda item: (
        item["startSeconds"],
        item["endSeconds"],
        ordinal(item["description"]),
        ordinal(item["evidenceBasis"]),
        ordinal(item["id"]),
    )
    change_key = lambda item: (
        ordinal(item["description"]),
        ordinal(item["evidenceBasis"]),
        tuple(ordinal(value) for value in item["evidenceIntervalIds"]),
    )
    uncertainty_key = lambda item: (
        UNCERTAINTY_ORDER.index(item["code"]),
        ordinal(item["description"]),
    )

    def canonical(
        values: list[dict[str, Any]],
        key: Any,
    ) -> tuple[list[dict[str, Any]], dict[str, Any]]:
        unique: dict[Any, dict[str, Any]] = {}
        for item in values:
            unique.setdefault(key(item), item)
        result = [unique[item] for item in sorted(unique)]
        raw_unique_keys = list(unique)
        return result, {
            "rawCount": len(values),
            "canonicalCount": len(result),
            "exactDuplicateCount": len(values) - len(unique),
            "orderChanged": raw_unique_keys != sorted(raw_unique_keys),
        }

    canonical_changes, change_audit = canonical(changes, change_key)
    canonical_intervals, interval_audit = canonical(
        intervals,
        interval_key,
    )
    canonical_uncertainties, uncertainty_audit = canonical(
        uncertainties,
        uncertainty_key,
    )
    audits = (change_audit, interval_audit, uncertainty_audit)
    syntactic_count = nested_change_count + sum(
        1
        for audit in audits
        if audit["exactDuplicateCount"] > 0 or audit["orderChanged"]
    )
    return (
        canonical_changes,
        canonical_intervals,
        canonical_uncertainties,
        {
            "policyVersion": CANONICALIZATION_POLICY_VERSION,
            "observedChanges": change_audit,
            "evidenceIntervals": interval_audit,
            "uncertaintyReasons": uncertainty_audit,
            "syntacticCanonicalizationCount": syntactic_count,
            "schemaShapeCanonicalizationCount": 0,
            "wireRepresentationVersion": None,
            "semanticRepairCount": 0,
        },
    )
