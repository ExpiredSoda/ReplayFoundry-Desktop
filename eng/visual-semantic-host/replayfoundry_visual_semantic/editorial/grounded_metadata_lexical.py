"""Canonical lexical operations for grounded audience metadata."""
from __future__ import annotations

import re
import unicodedata
from typing import Any


def normalize_lexical(value: str) -> str:
    return " ".join(re.sub(r"[^\w'’]+", " ", value.casefold()).split())


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


def contains_unapproved_non_latin(value: str, request: dict[str, Any]) -> bool:
    audience_copy = value
    for retained in (request["game"]["name"], request["game"]["hashtag"]):
        audience_copy = audience_copy.replace(retained, "")
    letters = [character for character in audience_copy if character.isalpha()]
    return any(
        not unicodedata.name(character, "").startswith("LATIN ")
        for character in letters
    )
