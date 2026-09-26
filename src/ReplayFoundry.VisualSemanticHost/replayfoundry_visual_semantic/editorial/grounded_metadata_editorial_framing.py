"""Recorded visual chronology and audience framing from typed facts."""
from __future__ import annotations

import math
import re
from typing import Any

from ..errors import InferenceError, _fail
from .grounded_metadata_lexical import (
    _CENTER_TOKEN_STOP_WORDS,
    _case_fact_terms,
    normalize_lexical,
)
from .grounded_metadata_automatic_commentary import (
    _automatic_commentary_authorizes_creator_voice,
    _requires_balanced_copy,
)


_GENERIC_PERSON_SUBJECT_OPENING = re.compile(
    r"^\s*(?:(?:a|an|the|this|that)\s+"
    r"(?:[\w'’-]+\s+){0,4})?"
    r"(?:man|woman|person|guy|player|character)\b",
    re.IGNORECASE,
)
_INVENTORY_CENTER_SUBJECT = re.compile(
    r"\b(?:camera|display|interface|monitor|screen|text|title card)\b",
    re.IGNORECASE,
)
_LEADING_SUBJECT_ARTICLE = re.compile(
    r"^\s*(?:a|an|the|this|that)\s+",
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
def _timed_synthesis_drafts(
    drafts: list[dict[str, Any]],
    records: list[dict[str, Any]],
    clip: dict[str, Any],
) -> list[dict[str, Any]]:
    """Project recorded clocks for copy only; never change visual selection facts."""
    if len(drafts) != len(records):
        raise ValueError("Synthesis draft timing must cover every owned draft.")
    clip_start = float(clip["startSeconds"])
    clip_end = float(clip["endSeconds"])
    duration = clip_end - clip_start
    if not all(math.isfinite(value) for value in (clip_start, clip_end)) or (
        clip_start < 0 or duration <= 0
    ):
        raise ValueError("Synthesis timing requires a valid source cut.")
    result: list[dict[str, Any]] = []
    previous_start = -1.0
    for ordinal, (draft, record) in enumerate(zip(drafts, records), start=1):
        if record.get("ordinal") != ordinal:
            raise ValueError("Synthesis draft timing has a foreign ordinal.")
        start = record.get("startSeconds")
        end = record.get("endSeconds")
        if isinstance(start, bool) or isinstance(end, bool) or not isinstance(
            start, (int, float)
        ) or not isinstance(end, (int, float)):
            raise ValueError("Synthesis draft bounds must be recorded numbers.")
        if not math.isfinite(start) or not math.isfinite(end) or not (
            0 <= start < end <= duration + 0.000001
        ) or start < previous_start:
            raise ValueError("Synthesis draft bounds must belong to the source cut.")
        # Facts in these two arrays came from the same frozen grounding packet.
        # Decline a mismatched association rather than assigning clocks to other facts.
        if any(record.get(key) != value for key, value in draft.items()):
            raise ValueError("Synthesis timing does not own these visual facts.")
        previous_start = start
        result.append({
            **draft,
            "reviewStartSeconds": start,
            "reviewEndSeconds": end,
            "sourceStartSeconds": clip_start + start,
            "sourceEndSeconds": clip_start + end,
        })
    return result


def _draft_temporal_role(
    draft: dict[str, Any], primary: dict[str, Any], ordinal: int, primary_ordinal: int,
) -> dict[str, Any]:
    clocks = {
        key: draft[key]
        for key in (
            "reviewStartSeconds", "reviewEndSeconds",
            "sourceStartSeconds", "sourceEndSeconds",
        )
        if key in draft
    }
    if ordinal == primary_ordinal:
        return {"phase": "Primary", **clocks}
    if "reviewStartSeconds" in draft and "reviewStartSeconds" in primary:
        phase = (
            "LeadIn" if draft["reviewEndSeconds"] <= primary["reviewStartSeconds"]
            else "FollowThrough" if draft["reviewStartSeconds"] >= primary["reviewEndSeconds"]
            else "OverlapsPrimary"
        )
    else:
        # Older or model-free fixtures may have ordered drafts but no measured clocks.
        # Do not turn draft order into a claimed non-overlapping event sequence.
        phase = "EarlierDraft" if ordinal < primary_ordinal else "LaterDraft"
    return {"phase": phase, **clocks}


def _editorial_excerpt(value: str, maximum: int = 600) -> str:
    normalized = " ".join(value.split())
    if len(normalized) <= maximum:
        return normalized
    return normalized[:maximum].rsplit(" ", 1)[0].rstrip(" ,;:-")


def _action_linked_candidate_subjects(primary: dict[str, Any]) -> list[str]:
    action_tokens = {
        token
        for action in primary.get("actions", [])
        if isinstance(action, str)
        for token in normalize_lexical(action).split()
        if len(token) >= 3 and token not in _CENTER_TOKEN_STOP_WORDS
    }
    candidates: list[str] = []
    seen: set[str] = set()
    for value in primary.get("subjectsAndObjects", []):
        if not isinstance(value, str) or (
            _GENERIC_PERSON_SUBJECT_OPENING.search(value)
            or _INVENTORY_CENTER_SUBJECT.search(value)
        ):
            continue
        normalized = " ".join(value.split()).strip(" ,;:-")
        articleless = _LEADING_SUBJECT_ARTICLE.sub("", normalized).strip()
        subject_tokens = {
            token for token in normalize_lexical(articleless).split()
            if len(token) >= 3 and token not in _CENTER_TOKEN_STOP_WORDS
        }
        key = articleless.casefold()
        if (
            articleless
            and key not in seen
            and subject_tokens.intersection(action_tokens)
        ):
            candidates.append(articleless)
            seen.add(key)
    return candidates[:4]


def _audience_frame(
    editorial_frame: dict[str, Any],
    primary: dict[str, Any],
    actor_authority: str,
    creator_relation: str,
) -> dict[str, Any]:
    presentation = editorial_frame.get("primaryPresentationKind", "Unclear")
    creator_controlled = (
        actor_authority == "CreatorControlled"
        and creator_relation == "CreatorActed"
    )
    center_kind = (
        "NarrativePresentation"
        if presentation in {"CinematicSequence", "InWorldRecording"}
        else "DocumentPresentation"
        if presentation == "DocumentOrLore"
        else "InterfaceProgression"
        if presentation in {"MenuOrLoadout", "ObjectiveOrInterface"}
        else "CreatorAction"
        if presentation == "InteractiveGameplay" and creator_controlled
        else "NeutralGameplayEvent"
    )
    return {
        "authorityKind": "GrammaticalShapeOnly",
        "centerKind": center_kind,
        "presentationKind": presentation,
        "momentKind": editorial_frame.get("momentKind", "Unclear"),
        "candidateSubjects": _action_linked_candidate_subjects(primary),
        "primaryActionRole": (
            "SupportingDetailOnly"
            if center_kind == "NarrativePresentation"
            else "FactualSupportOnly"
        ),
    }


def _narrative_case_fact_terms(authority: dict[str, Any]) -> set[str]:
    if authority.get("audienceFrame", {}).get("centerKind") != \
            "NarrativePresentation":
        return set()
    primary = authority.get("primaryVisual", {})
    values = [
        *primary.get("actions", []),
        *authority.get("audienceFrame", {}).get("candidateSubjects", []),
        *authority.get("stableReadableText", []),
    ]
    facts = _case_fact_terms([value for value in values if isinstance(value, str)])
    identity = authority.get("confirmedGameIdentity", {})
    return facts.difference(_case_fact_terms([identity.get("name", "")]))


def narrative_presentation_has_distinct_fact(authority: dict[str, Any]) -> bool:
    return authority.get("audienceFrame", {}).get("centerKind") != \
        "NarrativePresentation" or bool(_narrative_case_fact_terms(authority))


def validate_narrative_case_fact_retention(
    title: str,
    description: str,
    authority: dict[str, Any],
    request: dict[str, Any] | None = None,
) -> None:
    facts = _narrative_case_fact_terms(authority)
    if authority.get("audienceFrame", {}).get("centerKind") != \
            "NarrativePresentation":
        return
    fields = (title, description)
    if request is not None and _requires_balanced_copy(request):
        fields = tuple(field for field in fields
                       if not _automatic_commentary_authorizes_creator_voice(request, field))
    if not facts or not fields or any(
        not facts.intersection(_case_fact_terms([field]))
        for field in fields
    ):
        _fail(
            InferenceError,
            "Grounded editorial rephrase did not retain a case-local primary "
            "fact in both audience fields.",
        )


