"""Model-free Prompt 2.3 semantic validation."""
from __future__ import annotations

import json
from decimal import Decimal
from typing import Any

from .canonicalization import canonicalize
from .constants import *
from .primitives import *
from .truth_table import validate_references, validate_truth_table


_WIRE_CONTENT_TYPES = {
    "A": "Action", "D": "Dialogue", "X": "Discovery",
    "F": "Failure", "H": "Humor", "S": "Story",
    "M": "MenuOrTraversal", "C": "Cinematic", "O": "Other",
    "U": "Unknown",
}
_WIRE_TERNARY = {"Y": "Yes", "N": "No", "U": "Unsure"}
_WIRE_TRANSCRIPT = {
    "S": "Supports", "D": "DoesNotSupport", "N": "NotSupplied",
    "U": "UnreliableOrAmbiguous",
}
_WIRE_BASIS = {"V": "Visual", "T": "TranscriptContext", "B": "Both"}
_REASON_RATIONALES = {
    "RoutineTraversal": "The observed activity is routine traversal.",
    "MenuOrInventoryOnly": "The observed activity is limited to an inventory menu.",
    "NoObservablePayoff": "No observable payoff is established in the candidate.",
    "AmbientChangeOnly": "Only ambient change is established in the candidate.",
    "MissingRequiredContext": "Required context falls outside the candidate.",
    "NoDistinctEvent": "No distinct event is established in the candidate.",
    "InsufficientEvidence": "The bounded evidence is insufficient for a definitive disposition.",
    "None": "A distinct observable event and payoff are grounded inside the candidate.",
}


def _wire_enum(value: Any, values: dict[str, str], location: str) -> str:
    if not isinstance(value, str) or value not in values:
        raise EditorialContractError(f"{location} contains an unknown compact code.")
    return values[value]


def _expand_wire(root: dict[str, Any]) -> dict[str, Any]:
    """Expand the frozen compact wire record without semantic inference."""
    wire = exact(root, WIRE_ROOT_KEYS, "$")
    flags = array(wire["v"], "$.v", 5)
    if len(flags) != 5:
        raise EditorialContractError("$.v must contain exactly five flags.")
    expanded_flags = [
        _wire_enum(value, _WIRE_TERNARY, f"$.v[{index}]")
        for index, value in enumerate(flags)
    ]
    evidence_rows = array(wire["e"], "$.e", 4)
    intervals: list[dict[str, Any]] = []
    changes: list[dict[str, Any]] = []
    for index, row_value in enumerate(evidence_rows):
        row = array(row_value, f"$.e[{index}]", 3)
        if len(row) != 3:
            raise EditorialContractError(
                f"$.e[{index}] must contain exactly three values."
            )
        identifier = text(row[0], f"$.e[{index}][0]", 32)
        basis = _wire_enum(row[2], _WIRE_BASIS, f"$.e[{index}][2]")
        description = {
            "Visual": "A visual evidence point supports the observation.",
            "TranscriptContext": "A transcript-context evidence point supports the observation.",
            "Both": "A combined visual and transcript-context evidence point supports the observation.",
        }[basis]
        intervals.append({
            "id": identifier,
            "atSeconds": row[1],
            "description": description,
            "evidenceBasis": basis,
        })
        changes.append({
            "description": description,
            "evidenceBasis": basis,
            "evidenceIntervalIds": [identifier],
        })
    transcript = _wire_enum(wire["x"], _WIRE_TRANSCRIPT, "$.x")
    reason = (
        "RoutineTraversal" if expanded_flags[2] == "Yes" else
        "AmbientChangeOnly" if expanded_flags[4] == "Yes" else
        "NoDistinctEvent" if expanded_flags[0] == "No" else
        "NoObservablePayoff" if expanded_flags[1] == "No" else
        "MissingRequiredContext" if expanded_flags[3] == "Yes" else
        "None" if expanded_flags == ["Yes", "Yes", "No", "No", "No"] else
        "InsufficientEvidence"
    )
    disposition = (
        "Keep" if reason == "None" else
        "Unsure" if reason == "InsufficientEvidence" else
        "Reject"
    )
    uncertainties: list[dict[str, str]] = []
    if disposition == "Unsure":
        uncertainty_code = (
            "TranscriptMayBeInaccurate"
            if transcript == "UnreliableOrAmbiguous"
            else "AmbiguousEventBoundary"
        )
        uncertainties.append({
            "code": uncertainty_code,
            "description": (
                "Transcript context may be inaccurate."
                if uncertainty_code == "TranscriptMayBeInaccurate"
                else "The observable event boundary remains ambiguous."
            ),
        })
    return {
        "observableContentType": _wire_enum(wire["t"], _WIRE_CONTENT_TYPES, "$.t"),
        "hasDistinctEvent": expanded_flags[0],
        "hasObservablePayoff": expanded_flags[1],
        "routineTraversalOrMenuOnly": expanded_flags[2],
        "candidateRequiresMissingContext": expanded_flags[3],
        "candidateContainsOnlyAmbientChange": expanded_flags[4],
        "transcriptContextSupport": transcript,
        "observedChanges": changes,
        "evidenceIntervals": intervals,
        "uncertaintyReasons": uncertainties,
        "editorialDisposition": disposition,
        "rejectReason": reason,
        "dispositionRationale": _REASON_RATIONALES[reason],
    }


def _parse_interval(
    value: Any,
    location: str,
    review_duration: Decimal,
) -> dict[str, Any]:
    if isinstance(value, dict) and "atSeconds" in value:
        item = exact(
            value,
            {"id", "atSeconds", "description", "evidenceBasis"},
            location,
        )
        start = end = decimal_seconds(
            item["atSeconds"],
            f"{location}.atSeconds",
        )
    else:
        item = exact(
            value,
            {"id", "startSeconds", "endSeconds", "description", "evidenceBasis"},
            location,
        )
        start = decimal_seconds(
            item["startSeconds"],
            f"{location}.startSeconds",
        )
        end = decimal_seconds(
            item["endSeconds"],
            f"{location}.endSeconds",
        )
    identifier = text(item["id"], f"{location}.id", 32)
    if ID_PATTERN.fullmatch(identifier) is None:
        raise EditorialContractError(
            f"{location}.id is not a stable evidence ID."
        )
    if end < start or end > review_duration:
        raise EditorialContractError(
            f"{location} must be ordered and inside the bounded review."
        )
    return {
        "id": identifier,
        "startSeconds": start,
        "endSeconds": end,
        "description": text(
            item["description"],
            f"{location}.description",
            MAX_DETAIL,
        ),
        "evidenceBasis": enum(
            item["evidenceBasis"],
            EVIDENCE_BASIS,
            f"{location}.evidenceBasis",
        ),
    }


def _parse_change(
    value: Any,
    location: str,
) -> tuple[dict[str, Any], bool]:
    item = exact(
        value,
        {"description", "evidenceBasis", "evidenceIntervalIds"},
        location,
    )
    raw_ids = array(
        item["evidenceIntervalIds"],
        f"{location}.evidenceIntervalIds",
        MAX_INTERVALS,
    )
    if not raw_ids:
        raise EditorialContractError(
            f"{location}.evidenceIntervalIds must not be empty."
        )
    identifiers = [
        text(child, f"{location}.evidenceIntervalIds[{index}]", 32)
        for index, child in enumerate(raw_ids)
    ]
    if any(ID_PATTERN.fullmatch(value) is None for value in identifiers):
        raise EditorialContractError(
            f"{location}.evidenceIntervalIds contains an invalid ID."
        )
    canonical_ids = sorted(set(identifiers), key=ordinal)
    return (
        {
            "description": text(
                item["description"],
                f"{location}.description",
                MAX_DETAIL,
            ),
            "evidenceBasis": enum(
                item["evidenceBasis"],
                EVIDENCE_BASIS,
                f"{location}.evidenceBasis",
            ),
            "evidenceIntervalIds": canonical_ids,
        },
        identifiers != canonical_ids,
    )


def _parse_uncertainty(
    value: Any,
    location: str,
) -> dict[str, Any]:
    item = exact(value, {"code", "description"}, location)
    return {
        "code": enum(
            item["code"],
            UNCERTAINTY_CODES,
            f"{location}.code",
        ),
        "description": text(
            item["description"],
            f"{location}.description",
            MAX_DETAIL,
        ),
    }


def parse_and_canonicalize_editorial_output(
    value: str,
    *,
    review_duration_seconds: Decimal | int | str,
    candidate_start_seconds: Decimal | int | str,
    candidate_end_seconds: Decimal | int | str,
) -> tuple[dict[str, Any], dict[str, Any]]:
    """Parse one bare Prompt 2.3 semantic object without model execution."""
    if not isinstance(value, str) or not value:
        raise EditorialContractError(
            "Prompt 2.3 output must be one bare JSON object "
            "without wrappers."
        )
    normalized = value.strip(" \t\r\n")
    outer_whitespace_trimmed = normalized != value
    if not normalized or not normalized.startswith("{") or not normalized.endswith("}"):
        raise EditorialContractError(
            "Prompt 2.3 output must be one bare JSON object "
            "without wrappers."
        )
    try:
        parsed = json.loads(
            normalized,
            parse_float=Decimal,
            parse_int=Decimal,
            parse_constant=reject_constant,
            object_pairs_hook=object_pairs,
        )
    except (json.JSONDecodeError, TypeError) as error:
        raise EditorialContractError(
            "Prompt 2.3 output is malformed JSON."
        ) from error
    is_wire = isinstance(parsed, dict) and set(parsed) == WIRE_ROOT_KEYS
    root = _expand_wire(parsed) if is_wire else exact(parsed, ROOT_KEYS, "$")
    review_duration = decimal_seconds(
        Decimal(str(review_duration_seconds)),
        "review_duration_seconds",
    )
    candidate_start = decimal_seconds(
        Decimal(str(candidate_start_seconds)),
        "candidate_start_seconds",
    )
    candidate_end = decimal_seconds(
        Decimal(str(candidate_end_seconds)),
        "candidate_end_seconds",
    )
    if candidate_end <= candidate_start or candidate_end > review_duration:
        raise EditorialContractError(
            "Candidate interval is outside the bounded review."
        )

    intervals = [
        _parse_interval(item, f"$.evidenceIntervals[{index}]", review_duration)
        for index, item in enumerate(
            array(
                root["evidenceIntervals"],
                "$.evidenceIntervals",
                MAX_INTERVALS,
            )
        )
    ]
    changes: list[dict[str, Any]] = []
    nested_count = 0
    for index, item in enumerate(
        array(root["observedChanges"], "$.observedChanges", MAX_CHANGES)
    ):
        parsed_change, changed = _parse_change(
            item,
            f"$.observedChanges[{index}]",
        )
        changes.append(parsed_change)
        nested_count += int(changed)
    uncertainties = [
        _parse_uncertainty(item, f"$.uncertaintyReasons[{index}]")
        for index, item in enumerate(
            array(
                root["uncertaintyReasons"],
                "$.uncertaintyReasons",
                MAX_UNCERTAINTIES,
            )
        )
    ]
    changes, intervals, uncertainties, audit = canonicalize(
        changes,
        intervals,
        uncertainties,
        nested_count,
    )
    canonical = {
        "observableContentType": enum(
            root["observableContentType"],
            CONTENT_TYPES,
            "$.observableContentType",
        ),
        "hasDistinctEvent": enum(
            root["hasDistinctEvent"],
            TERNARY,
            "$.hasDistinctEvent",
        ),
        "hasObservablePayoff": enum(
            root["hasObservablePayoff"],
            TERNARY,
            "$.hasObservablePayoff",
        ),
        "routineTraversalOrMenuOnly": enum(
            root["routineTraversalOrMenuOnly"],
            TERNARY,
            "$.routineTraversalOrMenuOnly",
        ),
        "candidateRequiresMissingContext": enum(
            root["candidateRequiresMissingContext"],
            TERNARY,
            "$.candidateRequiresMissingContext",
        ),
        "candidateContainsOnlyAmbientChange": enum(
            root["candidateContainsOnlyAmbientChange"],
            TERNARY,
            "$.candidateContainsOnlyAmbientChange",
        ),
        "transcriptContextSupport": enum(
            root["transcriptContextSupport"],
            TRANSCRIPT_SUPPORT,
            "$.transcriptContextSupport",
        ),
        "observedChanges": changes,
        "evidenceIntervals": intervals,
        "uncertaintyReasons": uncertainties,
        "editorialDisposition": enum(
            root["editorialDisposition"],
            DISPOSITIONS,
            "$.editorialDisposition",
        ),
        "rejectReason": enum(
            root["rejectReason"],
            REJECT_REASONS,
            "$.rejectReason",
        ),
        "dispositionRationale": text(
            root["dispositionRationale"],
            "$.dispositionRationale",
            MAX_RATIONALE,
        ),
    }
    by_id = validate_references(canonical)
    validate_truth_table(
        canonical,
        by_id,
        candidate_start,
        candidate_end,
    )
    audit["outerWhitespaceTrimmed"] = outer_whitespace_trimmed
    if is_wire:
        audit["wireRepresentationVersion"] = WIRE_REPRESENTATION_VERSION
        audit["schemaShapeCanonicalizationCount"] = 1
    if outer_whitespace_trimmed:
        audit["syntacticCanonicalizationCount"] += 1
    return canonical, audit


__all__ = [
    "CANONICALIZATION_POLICY_VERSION",
    "EditorialContractError",
    "SCHEMA_VERSION",
    "parse_and_canonicalize_editorial_output",
]
