"""Deterministic per-case compact editorial wire JSON Schema."""
from __future__ import annotations

import hashlib
import json
import math
from decimal import Decimal
from typing import Any

from .structured_decoding_policy import SCHEMA_VERSION

CONTENT_TYPES = list("ADXFHSMCOU")
TERNARY = list("YNU")
EVIDENCE_BASIS = list("VB")
TRANSCRIPT_SUPPORT = list("SDNU")
ID_PATTERN = r"^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$"


def _enum(values: list[str]) -> dict[str, Any]:
    return {"type": "string", "enum": values}


def _base_properties(
    candidate_start_seconds: Decimal,
    candidate_end_seconds: Decimal,
) -> dict[str, Any]:
    interval = {
        "type": "array",
        "minItems": 3,
        "maxItems": 3,
        "prefixItems": [
            {
                "type": "string",
                "pattern": ID_PATTERN,
                "minLength": 1,
                "maxLength": 32,
            },
            {
                "type": "number",
                "minimum": candidate_start_seconds,
                "maximum": candidate_end_seconds,
            },
            _enum(EVIDENCE_BASIS),
        ],
        "items": False,
    }
    return {
        "t": _enum(CONTENT_TYPES),
        "v": {
            "type": "array",
            "minItems": 5,
            "maxItems": 5,
            "prefixItems": [_enum(TERNARY) for _ in range(5)],
            "items": False,
        },
        "x": _enum(TRANSCRIPT_SUPPORT),
        "e": {
            "type": "array",
            "minItems": 1,
            "maxItems": 4,
            "uniqueItems": True,
            "items": interval,
        },
    }


def build_editorial_schema(
    review_duration_seconds: Decimal,
    candidate_start_seconds: Decimal,
    candidate_end_seconds: Decimal,
) -> dict[str, Any]:
    if (
        not isinstance(review_duration_seconds, Decimal)
        or not review_duration_seconds.is_finite()
        or review_duration_seconds <= 0
        or not isinstance(candidate_start_seconds, Decimal)
        or not candidate_start_seconds.is_finite()
        or candidate_start_seconds < 0
        or not isinstance(candidate_end_seconds, Decimal)
        or not candidate_end_seconds.is_finite()
        or candidate_end_seconds <= candidate_start_seconds
        or candidate_end_seconds > review_duration_seconds
    ):
        raise ValueError("Review duration must be a positive finite Decimal.")

    properties = _base_properties(
        candidate_start_seconds,
        candidate_end_seconds,
    )
    return {
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "title": "ReplayFoundry Prompt 2.7 compact constrained observation",
        "type": "object",
        "additionalProperties": False,
        "required": list(properties),
        "properties": properties,
    }


def _decimal_json(value: Decimal) -> str:
    if not value.is_finite():
        raise ValueError("Canonical schema JSON requires finite decimals.")
    normalized = format(value, "f")
    if "." in normalized:
        normalized = normalized.rstrip("0").rstrip(".")
    return "0" if normalized in {"", "-0"} else normalized


def canonical_schema_json(value: Any) -> str:
    if value is None:
        return "null"
    if value is True:
        return "true"
    if value is False:
        return "false"
    if isinstance(value, Decimal):
        return _decimal_json(value)
    if isinstance(value, int) and not isinstance(value, bool):
        return str(value)
    if isinstance(value, float):
        if not math.isfinite(value):
            raise ValueError("Canonical schema JSON requires finite numbers.")
        raise TypeError("Schema numbers must not use binary floating point.")
    if isinstance(value, str):
        return json.dumps(value, ensure_ascii=False)
    if isinstance(value, list):
        return "[" + ",".join(
            canonical_schema_json(item) for item in value
        ) + "]"
    if isinstance(value, dict):
        if any(not isinstance(key, str) for key in value):
            raise TypeError("Canonical schema JSON requires string keys.")
        return "{" + ",".join(
            json.dumps(key, ensure_ascii=False)
            + ":"
            + canonical_schema_json(value[key])
            for key in sorted(value)
        ) + "}"
    raise TypeError(
        f"Unsupported canonical schema value: {type(value).__name__}."
    )


def build_editorial_schema_artifact(
    review_duration_seconds: Decimal,
    candidate_start_seconds: Decimal,
    candidate_end_seconds: Decimal,
) -> tuple[dict[str, Any], str, str]:
    schema = build_editorial_schema(
        review_duration_seconds,
        candidate_start_seconds,
        candidate_end_seconds,
    )
    canonical = canonical_schema_json(schema)
    sha256 = hashlib.sha256(canonical.encode("utf-8")).hexdigest()
    return schema, canonical, sha256


__all__ = [name for name in globals() if not name.startswith("__")]
