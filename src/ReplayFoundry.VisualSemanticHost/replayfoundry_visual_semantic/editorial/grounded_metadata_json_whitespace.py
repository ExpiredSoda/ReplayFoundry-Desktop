"""Frozen JSON-whitespace policy for grounded editorial generation."""
from __future__ import annotations

import hashlib

from ..commands import HOST_DIRECTORY, UsageOrInputError, _fail


POLICY_VERSION = "grounded-editorial-json-whitespace-1.0"
POLICY_SHA256 = "d62e42426c262f8979d0a956072b81db0e273f20c2ab7e4ed54f594a2c6fd555"
ANY_WHITESPACE = False


def require_policy() -> None:
    path = (
        HOST_DIRECTORY
        / "replayfoundry-grounded-editorial-json-whitespace-policy-1.0.txt"
    )
    text = (
        path.read_text(encoding="utf-8")
        .replace("\r\n", "\n")
        .replace("\r", "\n")
        .strip()
    )
    if hashlib.sha256(text.encode("utf-8")).hexdigest() != POLICY_SHA256:
        _fail(UsageOrInputError, "Grounded JSON-whitespace policy source changed.")


__all__ = [name for name in globals() if not name.startswith("__")]
