"""Validated story-frame adherence for optional editorial rephrasing."""
from __future__ import annotations

import re
from typing import Any

from ..errors import InferenceError
from .grounded_metadata_editorial_framing import _GENERIC_PERSON_SUBJECT_OPENING


_MEANINGFUL_FRAME_MOMENTS = frozenset({
    "Action", "Progress", "Complication", "Discovery", "Exposition",
    "Decision", "Outcome",
})
_NARRATIVE_PRESENTATIONS = frozenset({
    "CinematicSequence", "InWorldRecording", "DocumentOrLore",
    "MenuOrLoadout", "ObjectiveOrInterface",
})
_EVIDENCE_REPORTING = re.compile(
    r"\b(?:is|was|are|were)\s+visible\b|\bcan\s+be\s+seen\b|"
    r"\bon[- ]screen\s+text\b|\b(?:the|a|an)\s+"
    r"(?:[\w'’-]+\s+){0,3}(?:screen|display|interface)\s+"
    r"(?:shows?|showed|displays?|displayed|contains?|contained)\b",
    re.IGNORECASE,
)
_CAMERA_INVENTORY = re.compile(
    r"\bfilm(?:ed|ing)\b|"
    r"\bfootage\s+(?:captured|showed|displayed)\s+(?:a|an|the)\b|"
    r"\b(?:beside|before|facing|near|toward)\s+(?:the\s+)?camera\b|"
    r"\b(?:speaks?|spoke|speaking)\s+to\s+(?:the\s+)?camera\b|"
    r"\b(?:film(?:ed|ing)|record(?:ed|ing))\s+"
    r"(?:him|her|them|the\s+scene)\b|\bclose[- ]?up\b",
    re.IGNORECASE,
)
_NARRATIVE_OBSERVER_OPENING = re.compile(
    r"^\s*(?:a|an|the)\s+"
    r"(?!(?:briefing|cutscene|presentation|recording|sequence)\b)"
    r"(?:(?!(?:as|while|when|after|before|because)\b)[\w'’-]+\s+){1,10}"
    r"(?:stands?|stood|sits?|sat|holds?|held|carries|carried|"
    r"remains?|remained|speaks?|spoke|talks?|talked|looks?|looked|"
    r"turns?|turned|wears?|wore|walks?|walked|faces?|faced|"
    r"raises?|raised|moves?|moved|points?|pointed)\b",
    re.IGNORECASE,
)
_DISPLAY_INVENTORY = re.compile(
    r"\b(?:image|notice|document|menu|text)\b[^.!?]{0,80}\b"
    r"(?:appears?|appeared|was\s+displayed|was\s+shown)\b|"
    r"\b(?:appears?|appeared|was\s+displayed|was\s+shown)\s+"
    r"(?:on|in)\s+(?:a|the)\s+(?:screen|display|interface)\b|"
    r"\b(?:a|an|the)\s+(?:[\w'’-]+\s+){0,4}"
    r"(?:document|image|notice|menu|text)\s+"
    r"(?:filled|occupied|covered)\b[^.!?]{0,50}\b"
    r"(?:screen|display|interface)\b",
    re.IGNORECASE,
)
_BARE_DISPLAY_EVENT = re.compile(
    r"^\s*(?:(?:a|an|the)\s+)?(?:[\w'’-]+\s+){0,8}"
    r"(?:appears?|appeared|displays?|displayed|shows?|showed|shown)\b",
    re.IGNORECASE,
)

def _editorial_frame(value: dict[str, Any]) -> dict[str, Any] | None:
    frame = value.get("_editorialFraming")
    return (
        frame
        if isinstance(frame, dict)
        and frame.get("authorityKind") == "StoryShapeOnly"
        else None
    )


def _presentation_kinds(frame: dict[str, Any]) -> set[str]:
    values = [
        frame.get("primaryPresentationKind"),
        *frame.get("supportingPresentationKinds", []),
    ]
    return {value for value in values if isinstance(value, str)}


def _frame_has_useful_shape(frame: dict[str, Any]) -> bool:
    return (
        frame.get("momentKind") in _MEANINGFUL_FRAME_MOMENTS
        or bool(_presentation_kinds(frame).intersection(_NARRATIVE_PRESENTATIONS))
    )


def _editorial_frame_drift(
    metadata: dict[str, Any],
    request: dict[str, Any],
) -> bool:
    frame = _editorial_frame(request)
    if frame is None:
        return False
    title = metadata["title"]
    description = metadata["description"]
    if (
        _GENERIC_PERSON_SUBJECT_OPENING.search(title)
        or _GENERIC_PERSON_SUBJECT_OPENING.search(description)
        or _EVIDENCE_REPORTING.search(title + "\n" + description)
    ):
        return True
    if not _frame_has_useful_shape(frame):
        return False
    presentations = _presentation_kinds(frame)
    combined = title + "\n" + description
    if presentations.intersection({"CinematicSequence", "InWorldRecording"}) \
            and (
                _CAMERA_INVENTORY.search(combined)
                or _NARRATIVE_OBSERVER_OPENING.search(title)
                or _NARRATIVE_OBSERVER_OPENING.search(description)
            ):
        return True
    if presentations.intersection({
        "DocumentOrLore", "MenuOrLoadout", "ObjectiveOrInterface",
    }) and (
        _DISPLAY_INVENTORY.search(combined)
        or _BARE_DISPLAY_EVENT.search(title)
        or _BARE_DISPLAY_EVENT.search(description)
    ):
        return True
    return False


def _require_editorial_frame_adherence(
    metadata: dict[str, Any],
    request: dict[str, Any],
) -> None:
    if _editorial_frame_drift(metadata, request):
        raise InferenceError(
            "Grounded editorial rephrase did not preserve its validated "
            "editorial frame and retained literal observer framing."
        )


