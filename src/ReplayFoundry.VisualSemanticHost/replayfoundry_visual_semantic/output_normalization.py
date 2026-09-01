"""ReplayFoundry local visual-semantic host implementation module."""
from __future__ import annotations

from .output_safety import *  # noqa: F401,F403

class _ProviderCollectionCanonicalization(NamedTuple):
    raw_evidence_intervals: list[dict[str, Any]]
    canonical_evidence_intervals: list[dict[str, Any]]
    evidence_interval_order_changed: bool
    duplicate_evidence_interval_count: int
    raw_uncertainties: list[dict[str, str]]
    canonical_uncertainties: list[dict[str, str]]
    uncertainty_order_changed: bool
    duplicate_uncertainty_count: int
    raw_limitations: list[str]
    canonical_limitations: list[str]
    limitation_order_changed: bool
    duplicate_limitation_count: int


def _canonicalize_provider_collections(
    observation: dict[str, Any],
    video_duration: Decimal,
) -> _ProviderCollectionCanonicalization:
    raw_interval_values = _require_array(
        observation["evidenceIntervals"],
        "provider observation.evidenceIntervals",
        maximum=MAX_EVIDENCE_INTERVALS,
    )
    raw_evidence_intervals: list[dict[str, Any]] = []
    keyed_intervals: list[
        tuple[tuple[Decimal, Decimal, bytes], dict[str, Any]]
    ] = []
    for index, interval_value in enumerate(raw_interval_values):
        location = f"provider observation.evidenceIntervals[{index}]"
        interval = _require_object(interval_value, location)
        _require_exact_keys(
            interval,
            {"startSeconds", "endSeconds", "description"},
            location,
        )
        start = _require_finite_decimal(
            interval["startSeconds"],
            f"{location}.startSeconds",
        )
        end = _require_finite_decimal(
            interval["endSeconds"],
            f"{location}.endSeconds",
        )
        description = _require_exact_semantic_string(
            interval["description"],
            f"{location}.description",
            maximum=MAX_DETAIL_TEXT,
        )
        if start < 0 or end < start or end > video_duration:
            _fail(
                InferenceError,
                f"{location} is outside the review video.",
            )
        output = {
            "startSeconds": float(start),
            "endSeconds": float(end),
            "description": description,
        }
        raw_evidence_intervals.append(output)
        keyed_intervals.append(
            (
                (start, end, _ordinal_string_key(description)),
                output,
            )
        )

    ordered_intervals = sorted(
        keyed_intervals,
        key=lambda item: item[0],
    )
    evidence_interval_order_changed = (
        [item[0] for item in keyed_intervals]
        != [item[0] for item in ordered_intervals]
    )
    canonical_evidence_intervals: list[dict[str, Any]] = []
    evidence_interval_keys: set[
        tuple[Decimal, Decimal, bytes]
    ] = set()
    for key, interval in ordered_intervals:
        if key in evidence_interval_keys:
            continue
        evidence_interval_keys.add(key)
        canonical_evidence_intervals.append(interval)
    duplicate_evidence_interval_count = (
        len(raw_evidence_intervals)
        - len(canonical_evidence_intervals)
    )

    raw_uncertainty_values = _require_array(
        observation["uncertainties"],
        "provider observation.uncertainties",
        maximum=MAX_UNCERTAINTIES,
    )
    raw_uncertainties: list[dict[str, str]] = []
    for index, uncertainty_value in enumerate(raw_uncertainty_values):
        location = f"provider observation.uncertainties[{index}]"
        uncertainty = _require_object(uncertainty_value, location)
        _require_exact_keys(
            uncertainty,
            {"code", "description"},
            location,
        )
        raw_uncertainties.append(
            {
                "code": _require_enum(
                    uncertainty["code"],
                    UNCERTAINTY_CODES,
                    f"{location}.code",
                ),
                "description": _require_collection_output_string(
                    uncertainty["description"],
                    f"{location}.description",
                    maximum=MAX_DETAIL_TEXT,
                ),
            }
        )

    uncertainty_rank = {
        code: index for index, code in enumerate(UNCERTAINTY_CODE_ORDER)
    }
    sorted_uncertainties = sorted(
        raw_uncertainties,
        key=lambda item: (
            uncertainty_rank[item["code"]],
            _ordinal_string_key(item["description"]),
        ),
    )
    uncertainty_order_changed = (
        raw_uncertainties != sorted_uncertainties
    )
    canonical_uncertainties: list[dict[str, str]] = []
    uncertainty_keys: set[tuple[str, str]] = set()
    for item in sorted_uncertainties:
        key = (item["code"], item["description"])
        if key in uncertainty_keys:
            continue
        uncertainty_keys.add(key)
        canonical_uncertainties.append(item)
    duplicate_uncertainty_count = (
        len(raw_uncertainties) - len(canonical_uncertainties)
    )

    raw_limitations = _validate_string_array(
        observation["limitations"],
        "provider observation.limitations",
        maximum_count=MAX_LIMITATIONS,
        maximum_length=MAX_DETAIL_TEXT,
    )
    sorted_limitations = sorted(
        raw_limitations,
        key=_ordinal_string_key,
    )
    limitation_order_changed = (
        raw_limitations != sorted_limitations
    )
    canonical_limitations = sorted(
        set(raw_limitations),
        key=_ordinal_string_key,
    )
    duplicate_limitation_count = (
        len(raw_limitations) - len(canonical_limitations)
    )

    return _ProviderCollectionCanonicalization(
        raw_evidence_intervals=raw_evidence_intervals,
        canonical_evidence_intervals=canonical_evidence_intervals,
        evidence_interval_order_changed=
            evidence_interval_order_changed,
        duplicate_evidence_interval_count=
            duplicate_evidence_interval_count,
        raw_uncertainties=raw_uncertainties,
        canonical_uncertainties=canonical_uncertainties,
        uncertainty_order_changed=uncertainty_order_changed,
        duplicate_uncertainty_count=duplicate_uncertainty_count,
        raw_limitations=raw_limitations,
        canonical_limitations=canonical_limitations,
        limitation_order_changed=limitation_order_changed,
        duplicate_limitation_count=duplicate_limitation_count,
    )



__all__ = [name for name in globals() if not name.startswith("__")]
