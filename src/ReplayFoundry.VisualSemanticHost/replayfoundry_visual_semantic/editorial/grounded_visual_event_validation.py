"""Deterministic quality and premise checks for grounded visual events."""
from __future__ import annotations

import re
from typing import Any

from .grounded_metadata_lexical import (
    contains_non_latin_letter,
    contains_readable_fragment,
    normalize_lexical,
)

_OBSERVER_PREMISE = re.compile(
    r"^\s*(?:a|an|the)?\s*(?:man|woman|person|figure|guy|player|character)\b|"
    r"^\s*(?:a|an|the)?\s*(?:screen|display|interface|image)\s+"
    r"(?:shows?|showed|displays?|displayed|contains?|contained)\b|"
    r"\b(?:is visible|was visible|can be seen|was displayed|"
    r"on[- ]screen text)\b",
    re.IGNORECASE,
)
_PREMISE_INTERPRETATION = re.compile(
    r"\b(?:indicat(?:e|es|ed|ing)|suggest(?:s|ed|ing)|"
    r"impl(?:y|ies|ied|ying))\b",
    re.IGNORECASE,
)
_PREMISE_INSTRUCTION_LANGUAGE = re.compile(
    r"\b(?:ignore\s+(?:all\s+)?previous|system\s+prompt|developer\s+message|"
    r"follow\s+(?:these|the)\s+instructions|assistant\s+must|user\s+said)\b",
    re.IGNORECASE,
)
_DANGLING_PREMISE_ENDINGS = frozenset({
    "a", "an", "and", "as", "at", "by", "for", "from", "in", "into",
    "of", "on", "or", "the", "to", "with",
})
_PREMISE_STOP_WORDS = frozenset({
    "about", "after", "again", "against", "along", "also", "another",
    "around", "before", "being", "between", "could", "during", "from",
    "into", "itself", "moment", "more", "over", "scene", "sequence",
    "that", "their", "there", "these", "they", "this", "through", "under",
    "video", "what", "when", "where", "which", "while", "with", "within",
})
_PREMISE_SHAPE_TOKENS = frozenset({
    "action", "beat", "centered", "choice", "complication", "decision",
    "discovery", "encounter", "exposition", "focused", "introduced",
    "introduction", "outcome", "progress", "result", "reveal", "revealed",
    "review", "reviewed", "transition",
})


def _visual_action_quality_penalty(draft: dict[str, Any]) -> int:
    penalty = 0
    for raw_action in draft.get("actions", []):
        if not isinstance(raw_action, str):
            continue
        action = " ".join(raw_action.split()).strip()
        words = normalize_lexical(action).split()
        structural_single_quotes = re.sub(
            r"(?<=\w)'(?=\w)",
            "",
            action,
        ).count("'")
        if contains_non_latin_letter(action):
            penalty += 2
        if (
            not action
            or action.endswith((",", ":", ";", "/", "-", "–", "—"))
            or (words and words[-1] in _DANGLING_PREMISE_ENDINGS)
            or structural_single_quotes % 2 == 1
            or action.count('"') % 2 == 1
        ):
            penalty += 1
    return penalty


def _safe_editorial_premise(
    value: Any,
    supporting_drafts: list[dict[str, Any]],
    supporting_presentation_kinds: list[str],
) -> str | None:
    if not isinstance(value, str):
        return None
    premise = " ".join(value.split()).strip()
    words = normalize_lexical(premise).split()
    if (
        not 1 <= len(premise) <= 180
        or len(words) < 4
        or words[-1] in _DANGLING_PREMISE_ENDINGS
        or premise.endswith((",", ":", ";", "/", "-", "–", "—"))
        or ";" in premise
        or "|" in premise
        or "•" in premise
        or premise.count(",") > 1
        or re.search(r"[.!?]\s+\S", premise)
        or _OBSERVER_PREMISE.search(premise)
        or _PREMISE_INTERPRETATION.search(premise)
        or _PREMISE_INSTRUCTION_LANGUAGE.search(premise)
        or contains_non_latin_letter(premise)
    ):
        return None
    readable_values = [
        str(item)
        for draft in supporting_drafts
        for item in draft.get("readableText", [])
        if isinstance(item, str)
    ]
    evidence_text = " ".join(
        text
        for draft in supporting_drafts
        for field in ("environment", "subjectsAndObjects", "actions")
        for item in (
            draft.get(field, [])
            if isinstance(draft.get(field, []), list)
            else [draft.get(field, "")]
        )
        for text in [str(item)]
        if not contains_readable_fragment(text, readable_values)
    )
    evidence_tokens = {
        token for token in normalize_lexical(evidence_text).split()
        if len(token) >= 4 and token not in _PREMISE_STOP_WORDS
    }
    presentation_evidence = {
        "CinematicSequence": "cinematic sequence cutscene",
        "InWorldRecording": "in world recording recorded",
        "DocumentOrLore": "document lore review",
        "MenuOrLoadout": "menu loadout",
        "ObjectiveOrInterface": "objective interface",
        "MixedOrTransition": "mixed transition",
    }
    evidence_tokens.update(
        token
        for kind in supporting_presentation_kinds
        for token in presentation_evidence.get(kind, "").split()
    )
    premise_tokens = {
        token for token in normalize_lexical(premise).split()
        if len(token) >= 4 and token not in _PREMISE_STOP_WORDS
    }
    return (
        premise
        if len(evidence_tokens.intersection(premise_tokens)) >= 2
        and not premise_tokens.difference(
            evidence_tokens,
            _PREMISE_SHAPE_TOKENS,
        )
        else None
    )
