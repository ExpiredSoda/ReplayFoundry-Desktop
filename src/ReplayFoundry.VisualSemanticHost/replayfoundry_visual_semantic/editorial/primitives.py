"""Strict Prompt 2.3 JSON primitives."""
from __future__ import annotations

from decimal import Decimal, InvalidOperation
from typing import Any

from .constants import PROHIBITED_REASONING


class EditorialContractError(ValueError):
    """Strict Prompt 2.3 contract violation."""


def reject_constant(value: str) -> None:
    raise EditorialContractError(
        f"Non-finite JSON number '{value}' is forbidden."
    )


def object_pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for name, value in pairs:
        if name in result:
            raise EditorialContractError(
                f"Duplicate JSON property '{name}' is forbidden."
            )
        result[name] = value
    return result


def text(value: Any, location: str, maximum: int) -> str:
    if (
        not isinstance(value, str)
        or not value
        or value != value.strip()
        or len(value.encode("utf-16-le", errors="surrogatepass")) // 2
        > maximum
    ):
        raise EditorialContractError(
            f"{location} must be nonblank trimmed text of at most "
            f"{maximum} characters."
        )
    lowered = value.casefold()
    if any(fragment in lowered for fragment in PROHIBITED_REASONING):
        raise EditorialContractError(
            f"{location} contains prohibited hidden-reasoning content."
        )
    return value


def enum(value: Any, allowed: set[str], location: str) -> str:
    result = text(value, location, 128)
    if result not in allowed:
        raise EditorialContractError(
            f"{location} contains an undefined enum."
        )
    return result


def ordinal(value: str) -> bytes:
    return value.encode("utf-16-be", errors="surrogatepass")


def decimal_seconds(value: Any, location: str) -> Decimal:
    if isinstance(value, bool) or not isinstance(value, (int, Decimal)):
        raise EditorialContractError(
            f"{location} must be a finite JSON number."
        )
    try:
        result = Decimal(value)
    except InvalidOperation as error:
        raise EditorialContractError(
            f"{location} must be finite."
        ) from error
    if (
        not result.is_finite()
        or result < 0
        or -result.as_tuple().exponent > 3
    ):
        raise EditorialContractError(
            f"{location} must be non-negative seconds with at most "
            "three decimals."
        )
    return result


def array(value: Any, location: str, maximum: int) -> list[Any]:
    if not isinstance(value, list) or len(value) > maximum:
        raise EditorialContractError(
            f"{location} must be an array with no more than "
            f"{maximum} entries."
        )
    return value


def exact(
    value: Any,
    keys: set[str],
    location: str,
) -> dict[str, Any]:
    if not isinstance(value, dict) or set(value) != keys:
        raise EditorialContractError(
            f"{location} contains missing, extra, or unsupported properties."
        )
    return value
