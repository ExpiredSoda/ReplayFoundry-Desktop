"""Primitive validators shared by grounded metadata request contracts."""
from __future__ import annotations

import math
import re
from datetime import datetime, timezone
from typing import Any
from urllib.parse import urlparse

from ..commands import UsageOrInputError, _fail


def bounded_text(
    value: Any,
    location: str,
    maximum: int,
    *,
    blank: bool = False,
) -> str:
    if (
        not isinstance(value, str)
        or len(value) > maximum
        or (not blank and not value.strip())
    ):
        _fail(UsageOrInputError, f"{location} must be bounded text.")
    return value.strip()


def optional_text(value: Any, location: str, maximum: int) -> str | None:
    if value is None:
        return None
    return bounded_text(value, location, maximum)


def finite_number(
    value: Any,
    location: str,
    minimum: float,
    maximum: float,
) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        _fail(UsageOrInputError, f"{location} must be numeric.")
    number = float(value)
    if not math.isfinite(number) or number < minimum or number > maximum:
        _fail(UsageOrInputError, f"{location} is outside its bounded range.")
    return number


def stable_id(value: Any, location: str) -> str:
    result = bounded_text(value, location, 160)
    if re.fullmatch(r"[A-Za-z0-9._-]+", result) is None:
        _fail(UsageOrInputError, f"{location} is not a stable identity.")
    return result


def https_uri(value: Any, location: str) -> str:
    uri = bounded_text(value, location, 1000)
    parsed = urlparse(uri)
    if parsed.scheme.casefold() != "https" or not parsed.netloc:
        _fail(UsageOrInputError, f"{location} must be an absolute HTTPS URI.")
    return uri


def utc_timestamp(value: Any, location: str) -> str:
    text = bounded_text(value, location, 80)
    try:
        parsed = datetime.fromisoformat(text.replace("Z", "+00:00"))
    except ValueError:
        _fail(UsageOrInputError, f"{location} must be an ISO UTC timestamp.")
    if parsed.utcoffset() != timezone.utc.utcoffset(parsed):
        _fail(UsageOrInputError, f"{location} must be UTC.")
    return text
