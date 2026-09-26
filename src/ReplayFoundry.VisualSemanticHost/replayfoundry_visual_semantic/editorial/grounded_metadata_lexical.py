"""Canonical lexical operations for grounded audience metadata."""
from __future__ import annotations

import re
import unicodedata
from typing import Any


_CENTER_TOKEN_STOP_WORDS = frozenset({
    "and", "for", "from", "into", "near", "the", "through", "with",
})
_CASE_FACT_STOP_WORDS = frozenset({
    "about", "across", "after", "again", "against", "along", "among",
    "another", "around", "before", "briefing", "camera", "center", "clip",
    "cutscene", "display", "during", "figure", "focus", "footage", "game",
    "inside", "interface", "item", "moment", "object", "other", "people",
    "person", "presentation", "recording", "scene", "screen", "sequence",
    "someone", "speaker", "thing", "throughout", "unfold", "video", "woman",
    "man", *_CENTER_TOKEN_STOP_WORDS,
})
_CASE_FACT_IRREGULAR = {
    "came": "come", "fell": "fall", "found": "find", "held": "hold",
    "left": "leave", "made": "make", "read": "read", "saw": "see",
    "showed": "show", "shown": "show", "spoke": "speak", "stood": "stand",
    "took": "take", "went": "go", "wore": "wear",
}


def normalize_lexical(value: str) -> str:
    return " ".join(re.sub(r"[^\w'’]+", " ", value.casefold()).split())


def _case_fact_stem(value: str) -> str:
    word = _CASE_FACT_IRREGULAR.get(value, value)
    if word != value:
        return word
    if len(word) > 5 and word.endswith("ing"):
        word = word[:-3]
    if len(word) > 4 and word.endswith("ied"):
        return word[:-3] + "y"
    elif len(word) > 4 and word.endswith("ed"):
        word = word[:-2]
    elif len(word) > 4 and word.endswith("ies"):
        word = word[:-3] + "y"
    elif len(word) > 4 and word.endswith("es"):
        word = word[:-2]
    elif len(word) > 3 and word.endswith("s"):
        word = word[:-1]
    return word[:-1] if len(word) > 3 and word[-1] == word[-2] else word


def _case_fact_terms(values: list[str]) -> set[str]:
    return {
        stem
        for value in values
        for token in normalize_lexical(value).split()
        for stem in [_case_fact_stem(token)]
        if len(stem) >= 3 and stem not in _CASE_FACT_STOP_WORDS
    }


def shares_token_window(audience_copy: str, transcript: str, size: int) -> bool:
    return bool(shared_token_windows(audience_copy, transcript, size))


def shared_token_windows(
    audience_copy: str,
    transcript: str,
    size: int,
) -> list[str]:
    copy = " " + normalize_lexical(audience_copy) + " "
    tokens = normalize_lexical(transcript).split()
    if size < 1:
        raise ValueError("Transcript overlap size must be positive.")
    return list(
        dict.fromkeys(
            " ".join(tokens[index:index + size])
            for index in range(0, len(tokens) - size + 1)
            if " " + " ".join(tokens[index:index + size]) + " " in copy
        )
    )[:3]


def readable_text_fragments(value: str) -> tuple[str, ...]:
    """Return bounded phrases whose wording requires readable-text authority."""
    words = normalize_lexical(value).split()
    alphabetic_count = sum(
        any(character.isalpha() for character in word)
        for word in words
    )
    window_size = min(4, alphabetic_count)
    if window_size < 3:
        return ()
    return tuple(
        dict.fromkeys(
            " ".join(window)
            for index in range(len(words) - window_size + 1)
            for window in [words[index:index + window_size]]
            if all(any(character.isalpha() for character in word) for word in window)
        )
    )


def readable_text_key(value: str) -> tuple[str, str] | None:
    normalized = " ".join(
        unicodedata.normalize("NFKC", value).split()
    ).strip()
    if len(normalized) < 4 or not any(character.isalpha() for character in normalized):
        return None
    return normalized.lower(), normalized


def contains_readable_fragment(value: str, readable_values: list[str]) -> bool:
    normalized_value = " " + normalize_lexical(value) + " "
    return any(
        " " + fragment + " " in normalized_value
        for readable in readable_values
        for fragment in readable_text_fragments(readable)
    )


def contains_non_latin_letter(value: str) -> bool:
    return any(
        character.isalpha()
        and not unicodedata.name(character, "").startswith("LATIN ")
        for character in value
    )


def contains_unapproved_non_latin(value: str, request: dict[str, Any]) -> bool:
    audience_copy = value
    for retained in (request["game"]["name"], request["game"]["hashtag"]):
        audience_copy = audience_copy.replace(retained, "")
    return contains_non_latin_letter(audience_copy)
