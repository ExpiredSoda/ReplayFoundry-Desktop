"""Typed creator-embodiment authority for grounded audience copy."""
from __future__ import annotations

import re
from typing import Any

from ..errors import InferenceError, _fail
from .grounded_metadata_lexical import normalize_lexical, shared_token_windows


_GENERIC_PERSON_SUBJECT_OPENING = re.compile(
    r"^\s*(?:(?:a|an|the|this|that)\s+"
    r"(?:[\w'’-]+\s+){0,4})?"
    r"(?:man|woman|person|guy|player|character)\b",
    re.IGNORECASE,
)
_FIRST_PERSON_GENERIC_OBSERVER_OPENING = re.compile(
    r"^\s*(?:i|we)\s+"
    r"(?:heard|noticed|observed|saw|spotted|watched)\s+"
    r"(?:(?:a|an|the|this|that)\s+)?"
    r"(?:[\w'’-]+\s+){0,4}"
    r"(?:man|woman|person|guy|player|character)\b",
    re.IGNORECASE,
)
_FIRST_PERSON_REFERENCE = re.compile(
    r"\b(?:i|me|my|mine|we|us|our|ours)\b",
    re.IGNORECASE,
)
_FIRST_PERSON_POSSESSIVE = re.compile(
    r"\b(?:my|mine|our|ours)\b",
    re.IGNORECASE,
)
_FIRST_PERSON_SUBJECT_ACTION = re.compile(
    r"\b(?:i|we)\s+(?:had\s+)?([a-z]+)\b",
    re.IGNORECASE,
)
_CREATOR_ENCOUNTER_ACTIONS = {
    "approached",
    "arrived",
    "confronted",
    "discovered",
    "encountered",
    "entered",
    "escaped",
    "faced",
    "followed",
    "found",
    "met",
    "noticed",
    "observed",
    "reached",
    "saw",
    "spotted",
    "watched",
    "witnessed",
}
_CREATOR_AFFECTED_ACTIONS = {
    "became",
    "died",
    "dropped",
    "escaped",
    "fell",
    "got",
    "lost",
    "received",
    "stumbled",
    "suffered",
    "survived",
    "took",
    "was",
    "were",
}


def _action_stem(value: str) -> str:
    word = value.casefold()
    if len(word) > 5 and word.endswith("ing"):
        word = word[:-3]
    elif len(word) > 4 and word.endswith("ed"):
        word = word[:-2]
    elif len(word) > 4 and word.endswith("es"):
        word = word[:-2]
    elif len(word) > 3 and word.endswith("s"):
        word = word[:-1]
    if len(word) > 3 and word[-1:] == word[-2:-1]:
        word = word[:-1]
    return word


def _reviewed_commentary_authorizes_creator_voice(
    request: dict[str, Any],
    audience_copy: str,
) -> bool:
    return (
        request.get("profile", {}).get("variantIntent") == "CommentaryLed"
        and any(
            transcript.get("authority") in {"UserCorrected", "HumanReviewed"}
            and bool(shared_token_windows(audience_copy, transcript.get("text", ""), 3))
            for transcript in request.get("transcripts", [])
        )
    )


def validate_creator_actor_authority(
    title_body: str,
    description: str,
    tags: list[str],
    request: dict[str, Any],
    primary_visual_draft: dict[str, Any],
    actor_authority: str,
    creator_experience_relation: str,
) -> None:
    """Reject creator embodiment that the typed primary evidence did not authorize."""
    # Tags are descriptive metadata, not grammatical audience narration. A game
    # tag such as "The Last of Us" must not turn otherwise neutral prose into a
    # first-person creator claim merely because it contains the standalone word
    # "Us".
    audience_copy = "\n".join([title_body, description])
    if not _FIRST_PERSON_REFERENCE.search(audience_copy):
        return
    if _reviewed_commentary_authorizes_creator_voice(request, audience_copy):
        return
    if creator_experience_relation == "Unestablished":
        _fail(
            InferenceError,
            "Grounded metadata used unsupported creator embodiment without an "
            "established creator-experience relation.",
        )
    if actor_authority == "CreatorControlled":
        return

    direct_actions = [
        match.group(1).casefold()
        for match in _FIRST_PERSON_SUBJECT_ACTION.finditer(audience_copy)
    ]
    if creator_experience_relation == "CreatorEncountered":
        if (
            _FIRST_PERSON_POSSESSIVE.search(audience_copy)
            or any(action not in _CREATOR_ENCOUNTER_ACTIONS for action in direct_actions)
        ):
            _fail(
                InferenceError,
                "Grounded metadata used unsupported creator embodiment for another "
                "person's primary action; use neutral past-action or grounded "
                "creator-encounter wording.",
            )
        return

    if creator_experience_relation == "CreatorAffected":
        primary_action_stems = {
            _action_stem(token)
            for action in primary_visual_draft.get("actions", [])
            for token in normalize_lexical(action).split()
            if len(token) >= 3
        }
        if (
            actor_authority == "OtherPerson"
            and _FIRST_PERSON_POSSESSIVE.search(audience_copy)
        ) or any(
            action not in _CREATOR_AFFECTED_ACTIONS
            and action not in _CREATOR_ENCOUNTER_ACTIONS
            or (
                _action_stem(action) in primary_action_stems
                and action not in {"got", "was", "were"}
            )
            for action in direct_actions
        ):
            _fail(
                InferenceError,
                "Grounded metadata used unsupported creator embodiment for another "
                "person's body or primary action; describe only the grounded effect "
                "on the creator experience.",
            )
        return

    _fail(
        InferenceError,
        "Grounded metadata used unsupported creator embodiment without "
        "creator-controlled primary-action authority.",
    )
