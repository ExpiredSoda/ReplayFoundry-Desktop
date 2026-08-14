"""Frozen Prompt 2.3 evidence and editorial truth table."""
from __future__ import annotations

from decimal import Decimal
from typing import Any

from .constants import MENU_TERMS
from .primitives import EditorialContractError


def validate_references(
    root: dict[str, Any],
) -> dict[str, dict[str, Any]]:
    by_id: dict[str, dict[str, Any]] = {}
    for interval in root["evidenceIntervals"]:
        identifier = interval["id"]
        if identifier in by_id:
            raise EditorialContractError(
                f"Evidence ID '{identifier}' is duplicated with "
                "distinct content."
            )
        by_id[identifier] = interval

    if root["transcriptContextSupport"] == "NotSupplied" and any(
        interval["evidenceBasis"] in {"TranscriptContext", "Both"}
        for interval in root["evidenceIntervals"]
    ):
        raise EditorialContractError(
            "Transcript evidence cannot exist when transcript context "
            "was not supplied."
        )

    for change in root["observedChanges"]:
        try:
            referenced = [
                by_id[value]
                for value in change["evidenceIntervalIds"]
            ]
        except KeyError as error:
            raise EditorialContractError(
                "Observed change references unknown evidence interval "
                f"'{error.args[0]}'."
            ) from error
        has_visual = any(
            item["evidenceBasis"] in {"Visual", "Both"}
            for item in referenced
        )
        has_transcript = any(
            item["evidenceBasis"] in {"TranscriptContext", "Both"}
            for item in referenced
        )
        basis = change["evidenceBasis"]
        if (
            basis == "Visual"
            and not has_visual
            or basis == "TranscriptContext"
            and not has_transcript
            or basis == "Both"
            and not (has_visual and has_transcript)
        ):
            raise EditorialContractError(
                "Observed-change evidence basis is unsupported by "
                "cited intervals."
            )
    return by_id


def highest_reject(root: dict[str, Any]) -> str | None:
    menu = (
        root["routineTraversalOrMenuOnly"] == "Yes"
        and root["observableContentType"] == "MenuOrTraversal"
        and any(
            change["evidenceBasis"] in {"Visual", "Both"}
            and any(
                term in change["description"].casefold()
                for term in MENU_TERMS
            )
            for change in root["observedChanges"]
        )
    )
    if menu:
        return "MenuOrInventoryOnly"
    if root["routineTraversalOrMenuOnly"] == "Yes":
        return "RoutineTraversal"
    if root["candidateContainsOnlyAmbientChange"] == "Yes":
        return "AmbientChangeOnly"
    if root["hasDistinctEvent"] == "No":
        return "NoDistinctEvent"
    if root["hasObservablePayoff"] == "No":
        return "NoObservablePayoff"
    if root["candidateRequiresMissingContext"] == "Yes":
        return "MissingRequiredContext"
    return None


def _grounded_overlap(
    root: dict[str, Any],
    by_id: dict[str, dict[str, Any]],
    candidate_start: Decimal,
    candidate_end: Decimal,
) -> bool:
    return any(
        change["evidenceBasis"] in {"Visual", "Both"}
        and any(
            by_id[identifier]["startSeconds"] <= candidate_end
            and by_id[identifier]["endSeconds"] >= candidate_start
            for identifier in change["evidenceIntervalIds"]
        )
        for change in root["observedChanges"]
    )


def validate_truth_table(
    root: dict[str, Any],
    by_id: dict[str, dict[str, Any]],
    candidate_start: Decimal,
    candidate_end: Decimal,
) -> None:
    established = highest_reject(root)
    disposition = root["editorialDisposition"]
    reason = root["rejectReason"]
    qualifying = _grounded_overlap(
        root,
        by_id,
        candidate_start,
        candidate_end,
    )

    if disposition == "Keep":
        if not (
            root["hasDistinctEvent"] == "Yes"
            and root["hasObservablePayoff"] == "Yes"
            and root["routineTraversalOrMenuOnly"] == "No"
            and root["candidateContainsOnlyAmbientChange"] == "No"
            and root["candidateRequiresMissingContext"] == "No"
            and established is None
            and reason == "None"
            and qualifying
        ):
            raise EditorialContractError(
                "Keep violates the frozen Prompt 2.3 truth table."
            )
        return

    if disposition == "Reject":
        if established is None or reason != established or reason in {
            "None",
            "InsufficientEvidence",
        }:
            raise EditorialContractError(
                "Reject must use the highest-priority established reason."
            )
        return

    ambiguous = (
        any(
            root[name] == "Unsure"
            for name in (
                "hasDistinctEvent",
                "hasObservablePayoff",
                "routineTraversalOrMenuOnly",
                "candidateRequiresMissingContext",
                "candidateContainsOnlyAmbientChange",
            )
        )
        or root["transcriptContextSupport"] == "UnreliableOrAmbiguous"
    )
    asserts_keep = (
        root["hasDistinctEvent"] == "Yes"
        and root["hasObservablePayoff"] == "Yes"
        and root["routineTraversalOrMenuOnly"] == "No"
        and root["candidateContainsOnlyAmbientChange"] == "No"
        and root["candidateRequiresMissingContext"] == "No"
        and qualifying
    )
    if not (
        ambiguous
        and not asserts_keep
        and established is None
        and reason == "InsufficientEvidence"
        and root["uncertaintyReasons"]
    ):
        raise EditorialContractError(
            "Unsure requires typed ambiguity and no established "
            "Reject reason."
        )
