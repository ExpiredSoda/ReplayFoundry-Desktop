"""Bounded evidence sanitization for grounded metadata synthesis."""
from __future__ import annotations

import re

from .grounded_metadata_audience_validation import (
    contains_unsupported_mental_state,
)
from .grounded_metadata_lexical import (
    contains_readable_fragment as _contains_readable_fragment,
    normalize_lexical as _normalize_lexical,
    readable_text_key as _readable_text_key,
)


_QUOTED_READABLE_TEXT = re.compile(
    r"(?:(?:titled|named|labelled|labeled|reading|reads|displaying|displays)\s*)?"
    r"(?:(?<![\w])'(?P<single>[^'\r\n]{1,160})'(?![\w])|"
    r'\"(?P<double>[^\"\r\n]{1,160})\"|'
    r"(?<![\w])‘(?P<curly_single>[^’\r\n]{1,160})’(?![\w])|"
    r"“(?P<curly_double>[^”\r\n]{1,160})”)",
    re.IGNORECASE,
)
_DISPLAY_DERIVED_FACT = re.compile(
    r"\b(?:screen|display|interface|document|letter|notice|message|text|"
    r"title\s+card|list)\b[^.!?]{0,120}\b"
    r"(?:reads?|displays?|displayed|shows?|showed|shown|appears?|appeared|"
    r"contains?|contained|includes?|included|transitions?)\b|"
    r"\b(?:reads?|displays?|displayed|shows?|showed|shown|appears?|appeared|"
    r"contains?|contained|includes?|included|transitions?)\b[^.!?]{0,120}\b"
    r"(?:screen|display|interface|document|letter|notice|message|text|"
    r"title\s+card|list)\b",
    re.IGNORECASE,
)
_CAPTURE_INVENTORY_FACT = re.compile(
    r"\bcamera\b|\bclose[- ]?up\b|\bfootage\b|"
    r"\b(?:films?|filmed|filming)\b|"
    r"\b(?:is|was|are|were)\s+(?:partially\s+)?(?:visible|centered)\b",
    re.IGNORECASE,
)
_CLAUSE_BOUNDARY = re.compile(
    r"\s*(?:[.;]|,\s+(?:and\s+)?(?=(?:a|an|the|another|someone)\b)|"
    r"\b(?:while|whereas|as|before|after|then)\b)\s*",
    re.IGNORECASE,
)
_CONJOINED_CLAUSE = re.compile(r"\s+and(?:\s+then)?\s+", re.IGNORECASE)
_DISPLAY_MODIFIER_TAIL = re.compile(
    r"\s+\b(?:reading|displaying|showing|containing|including)\b.*$",
    re.IGNORECASE,
)
_CAMERA_MODIFIER_TAIL = re.compile(
    r"\s+\b(?:to|toward|towards|at|for|before|near|beside|facing)\s+"
    r"(?:the\s+)?camera\b.*$",
    re.IGNORECASE,
)
_INVENTORY_ONLY_FRAGMENT = re.compile(
    r"^\s*(?:(?:a|an|the)\s+)?(?:[\w'’-]+\s+){0,3}"
    r"(?:camera|display|document|footage|interface|letter|list|message|"
    r"notice|screen|text|title\s+card)\s*$",
    re.IGNORECASE,
)
_INCIDENTAL_CLOTHING_DETAIL = re.compile(
    r"\b(?:in|wearing|wore)\s+(?:(?:a|an|the)\s+)?"
    r"(?:[\w'’-]+\s+){0,3}"
    r"(?:coat|coats|dress|dresses|hat|hats|jacket|jackets|shirt|shirts|"
    r"suit|suits|uniform|uniforms)\b|"
    r"\b(?:coat|dress|hat|jacket|shirt|suit|uniform)[- ]clad\b|"
    r"\b(?:lab|uniform)[- ]coated\b",
    re.IGNORECASE,
)
_GENERIC_HUMAN_OBJECT = re.compile(
    r"^\s*(?:(?:a|an|the|two|three|several|other)\s+)*"
    r"(?:man|woman|person|people|guy|player|character|figure)\b",
    re.IGNORECASE,
)
_UNSUPPORTED_ACTOR_ROLE = re.compile(
    r"\b(?:player|character|streamer|creator|camera\s+wearer)\b",
    re.IGNORECASE,
)
_UNSUPPORTED_ACTOR_ROLE_PREFIX = re.compile(
    r"^\s*(?:(?:a|an|the)\s+)?"
    r"(?:player(?:[-\s]+controlled)?(?:[-\s]+character)?|"
    r"character|streamer|creator|camera\s+wearer)\b"
    r"[\s,:;-]*",
    re.IGNORECASE,
)


def _redact_unstable_quoted_text(
    value: str,
    stable_readable_text: list[str],
) -> str:
    stable_keys = {
        keyed[0]
        for item in stable_readable_text
        for keyed in [_readable_text_key(item)]
        if keyed is not None
    }

    def replace(match: re.Match[str]) -> str:
        content = next(
            part for part in match.groupdict().values() if part is not None
        )
        keyed = _readable_text_key(content)
        return (
            match.group(0)
            if keyed is not None and keyed[0] in stable_keys
            else ""
        )

    redacted = _QUOTED_READABLE_TEXT.sub(replace, value)
    redacted = re.sub(r"\s+([,.;:])", r"\1", redacted)
    return re.sub(r"\s{2,}", " ", redacted).strip()


def _quoted_readable_values(value: str) -> list[str]:
    return [
        next(part for part in match.groupdict().values() if part is not None)
        for match in _QUOTED_READABLE_TEXT.finditer(value)
    ]


def _contains_exact_readable_text(
    value: str,
    readable_values: list[str],
) -> bool:
    normalized = " " + _normalize_lexical(value).replace("'", " ") + " "
    normalized = re.sub(r"\s+", " ", normalized)
    return any(
        " " + candidate + " " in normalized
        for item in readable_values
        for candidate in [_normalize_lexical(item)]
        if candidate
    )


def _redact_embedded_readable_text(
    value: str,
    stable_readable_text: list[str] | None = None,
) -> str:
    stable = stable_readable_text or []
    redacted = _redact_unstable_quoted_text(value, stable)
    if not _contains_readable_fragment(redacted, stable):
        redacted = re.sub(
            r"(?i)\b(?:reading|reads|labelled|labeled|displaying|displays)\b.*$",
            "",
            redacted,
        )
    return redacted.rstrip(" ,;:-").strip()


def _strip_unsupported_actor_role(value: str) -> str:
    projected = _UNSUPPORTED_ACTOR_ROLE_PREFIX.sub("", value).strip()
    return "" if _UNSUPPORTED_ACTOR_ROLE.search(projected) else projected


def _clean_synthesis_fragment(value: str) -> str:
    cleaned = _INCIDENTAL_CLOTHING_DETAIL.sub("", value)
    cleaned = re.sub(r"\s{2,}", " ", cleaned).strip(" ,;:-")
    return re.sub(r"\s+([,.;:])", r"\1", cleaned)


def _project_safe_clauses(value: str) -> str:
    parts = [part for part in _CLAUSE_BOUNDARY.split(value) if part.strip()]
    if len(parts) == 1:
        parts = [part for part in _CONJOINED_CLAUSE.split(value) if part.strip()]
    retained: list[str] = []
    for part in parts:
        projected = _DISPLAY_MODIFIER_TAIL.sub("", part)
        projected = _CAMERA_MODIFIER_TAIL.sub("", projected)
        projected = _clean_synthesis_fragment(projected)
        if len(projected.split()) >= 2 and not (
            _DISPLAY_DERIVED_FACT.search(projected)
            or _CAPTURE_INVENTORY_FACT.search(projected)
        ):
            retained.append(projected)
    return " and ".join(retained)


def _safe_synthesis_text(
    value: str,
    stable_readable_text: list[str] | None = None,
) -> str:
    redacted = _redact_embedded_readable_text(value, stable_readable_text)
    redacted = _clean_synthesis_fragment(redacted)
    if _INVENTORY_ONLY_FRAGMENT.search(redacted) and not _contains_exact_readable_text(
        redacted, stable_readable_text or [],
    ):
        return ""
    if (
        _DISPLAY_DERIVED_FACT.search(redacted)
        or _CAPTURE_INVENTORY_FACT.search(redacted)
    ):
        redacted = _project_safe_clauses(redacted)
    sanitized = _strip_unsupported_actor_role(redacted)
    return "" if contains_unsupported_mental_state(sanitized) else sanitized
