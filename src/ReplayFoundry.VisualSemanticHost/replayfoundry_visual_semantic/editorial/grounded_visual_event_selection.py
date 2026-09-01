"""Constrained visual-event assessment and deterministic primary selection."""
from __future__ import annotations

import hashlib
import json
from typing import Any

from ..commands import HOST_DIRECTORY, InferenceError, UsageOrInputError, _fail
from ..errors import NoDistinctPrimaryVisualEventError
from ..request_validation import _require_array, _require_exact_keys, _require_object
from .grounded_visual_event_validation import (
    _safe_editorial_premise,
    _visual_action_quality_penalty,
)

VISUAL_EVENT_SELECTION_SCHEMA_VERSION = (
    "grounded-editorial-visual-event-selection-json-schema-1.3"
)
VISUAL_EVENT_SELECTION_PROMPT_VERSION = "1.2"
VISUAL_EVENT_SELECTION_PROMPT_SHA256 = (
    "4d717bd549a6cdac1ab739aa129bb2628037666e876ffda7df68a6abc98aee35"
)
EDITORIAL_FRAME_POLICY_VERSION = "grounded-editorial-frame-1.0"
PRESENTATION_KINDS = (
    "InteractiveGameplay",
    "CinematicSequence",
    "InWorldRecording",
    "DocumentOrLore",
    "MenuOrLoadout",
    "ObjectiveOrInterface",
    "MixedOrTransition",
    "Unclear",
)
MOMENT_KINDS = (
    "Action",
    "Progress",
    "Complication",
    "Discovery",
    "Exposition",
    "Decision",
    "Outcome",
    "Routine",
    "Unclear",
)
_NON_INTERACTIVE_PRESENTATIONS = frozenset({
    "CinematicSequence",
    "InWorldRecording",
})
def _visual_event_selection_prompt_text() -> str:
    path = HOST_DIRECTORY / "replayfoundry-editorial-event-selection-prompt-1.2.txt"
    text = path.read_text(encoding="utf-8").replace("\r\n", "\n").replace("\r", "\n").strip()
    if hashlib.sha256(text.encode("utf-8")).hexdigest() != VISUAL_EVENT_SELECTION_PROMPT_SHA256:
        _fail(UsageOrInputError, "Editorial visual-event selection prompt source changed.")
    return text


def _visual_event_selection_schema(draft_count: int) -> tuple[str, str]:
    if draft_count < 1 or draft_count > 4:
        raise ValueError("Visual-event selection requires one through four drafts.")
    assessment = {
        "type": "object",
        "properties": {
            "ordinal": {"type": "integer", "minimum": 1, "maximum": draft_count},
            "distinctAction": {"type": "boolean"},
            "objectInteraction": {"type": "boolean"},
            "visibleOutcome": {"type": "boolean"},
            "readableInterfaceChange": {"type": "boolean"},
            "routineOnly": {"type": "boolean"},
            "uncertain": {"type": "boolean"},
            "actorAuthority": {
                "type": "string",
                "enum": ["CreatorControlled", "OtherPerson", "Unknown"],
            },
            "creatorExperienceRelation": {
                "type": "string",
                "enum": [
                    "CreatorActed",
                    "CreatorAffected",
                    "CreatorEncountered",
                    "Unestablished",
                ],
            },
            "presentationKind": {
                "type": "string",
                "enum": list(PRESENTATION_KINDS),
            },
        },
        "required": [
            "ordinal",
            "distinctAction",
            "objectInteraction",
            "visibleOutcome",
            "readableInterfaceChange",
            "routineOnly",
            "uncertain",
            "actorAuthority",
            "creatorExperienceRelation",
            "presentationKind",
        ],
        "additionalProperties": False,
    }
    schema = {
        "type": "object",
        "properties": {
            "assessments": {
                "type": "array",
                "items": assessment,
                "minItems": draft_count,
                "maxItems": draft_count,
            },
            "editorialFrame": {
                "type": "object",
                "properties": {
                    "momentKind": {
                        "type": "string",
                        "enum": list(MOMENT_KINDS),
                    },
                    "premise": {
                        "type": "string",
                        "minLength": 1,
                        "maxLength": 180,
                    },
                    "supportingDraftOrdinals": {
                        "type": "array",
                        "items": {
                            "type": "integer",
                            "minimum": 1,
                            "maximum": draft_count,
                        },
                        "minItems": 1,
                        "maxItems": draft_count,
                    },
                },
                "required": [
                    "momentKind",
                    "premise",
                    "supportingDraftOrdinals",
                ],
                "additionalProperties": False,
            },
        },
        "required": ["assessments", "editorialFrame"],
        "additionalProperties": False,
    }
    canonical = json.dumps(schema, sort_keys=True, separators=(",", ":"))
    return canonical, hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def _visual_event_selection_messages(
    prompt_text: str,
    visual_drafts: list[dict[str, Any]],
) -> list[dict[str, Any]]:
    context = {
        "chronologicalVisualDrafts": [
            {"ordinal": index + 1, **draft}
            for index, draft in enumerate(visual_drafts)
        ]
    }
    return [
        {"role": "system", "content": [{"type": "text", "text": prompt_text}]},
        {
            "role": "user",
            "content": [{
                "type": "text",
                "text": "Assess every visual draft:\n" + json.dumps(
                    context,
                    ensure_ascii=False,
                    sort_keys=True,
                    separators=(",", ":"),
                ),
            }],
        },
    ]


def _strict_visual_event_selection(
    text: str,
    visual_drafts: list[dict[str, Any]],
    require_distinct_primary: bool = True,
    allow_grounded_primary_without_distinct_support: bool = False,
) -> dict[str, Any]:
    draft_count = len(visual_drafts)
    try:
        value = json.loads(text)
    except json.JSONDecodeError as error:
        _fail(InferenceError, f"Visual-event selection is not strict JSON: {error}")
    result = _require_object(value, "visual-event selection output")
    _require_exact_keys(
        result,
        {"assessments", "editorialFrame"},
        "visual-event selection output",
    )
    assessments = _require_array(
        result["assessments"],
        "visual-event selection assessments",
        maximum=draft_count,
    )
    if len(assessments) != draft_count:
        _fail(InferenceError, "Visual-event selection assessment count is invalid.")
    fields = {
        "ordinal",
        "distinctAction",
        "objectInteraction",
        "visibleOutcome",
        "readableInterfaceChange",
        "routineOnly",
        "uncertain",
        "actorAuthority",
        "creatorExperienceRelation",
        "presentationKind",
    }
    actor_authorities = {"CreatorControlled", "OtherPerson", "Unknown"}
    creator_relations = {
        "CreatorActed",
        "CreatorAffected",
        "CreatorEncountered",
        "Unestablished",
    }
    presentation_kinds = set(PRESENTATION_KINDS)
    typed: list[dict[str, Any]] = []
    authority_adjusted_ordinals: set[int] = set()
    for expected_ordinal, item in enumerate(assessments, start=1):
        assessment = _require_object(item, f"visual-event assessment {expected_ordinal}")
        _require_exact_keys(assessment, fields, f"visual-event assessment {expected_ordinal}")
        if (
            not isinstance(assessment["ordinal"], int)
            or isinstance(assessment["ordinal"], bool)
            or assessment["ordinal"] != expected_ordinal
        ) or any(
            not isinstance(assessment[field], bool)
            for field in fields
            if field not in {
                "ordinal",
                "actorAuthority",
                "creatorExperienceRelation",
                "presentationKind",
            }
        ):
            _fail(InferenceError, "Visual-event assessments must be ordered and Boolean.")
        if (
            assessment["actorAuthority"] not in actor_authorities
            or assessment["creatorExperienceRelation"] not in creator_relations
            or assessment["presentationKind"] not in presentation_kinds
            or (
                assessment["creatorExperienceRelation"] == "CreatorActed"
                and assessment["actorAuthority"] != "CreatorControlled"
                and assessment["presentationKind"] not in
                    _NON_INTERACTIVE_PRESENTATIONS
            )
        ):
            _fail(
                InferenceError,
                "Visual-event actor authority and creator-experience relation are invalid.",
            )
        typed_assessment = dict(assessment)
        draft = visual_drafts[expected_ordinal - 1]
        typed_assessment["uncertain"] = bool(
            assessment["uncertain"]
            or draft["environmentUncertain"]
            or draft["uncertainties"]
        )
        non_interactive = (
            typed_assessment["presentationKind"] in
            _NON_INTERACTIVE_PRESENTATIONS
        )
        if non_interactive and (
            typed_assessment["actorAuthority"] == "CreatorControlled"
            or typed_assessment["creatorExperienceRelation"] != "Unestablished"
        ):
            typed_assessment["actorAuthority"] = "OtherPerson"
            typed_assessment["creatorExperienceRelation"] = "Unestablished"
            authority_adjusted_ordinals.add(expected_ordinal)
        typed.append(typed_assessment)

    def priority(item: dict[str, Any]) -> tuple[int, int, int, int, int, int, int, int]:
        quality_penalty = _visual_action_quality_penalty(
            visual_drafts[item["ordinal"] - 1]
        )
        score = (
            3 * int(item["distinctAction"])
            + 2 * int(item["objectInteraction"])
            + 2 * int(item["visibleOutcome"])
            + int(item["readableInterfaceChange"])
            - 3 * int(item["routineOnly"])
            - int(item["uncertain"])
            - 2 * quality_penalty
        )
        return (
            score,
            -quality_penalty,
            int(item["distinctAction"]),
            int(item["visibleOutcome"]),
            int(item["objectInteraction"]),
            int(item["readableInterfaceChange"]),
            -int(item["routineOnly"] or item["uncertain"]),
            item["ordinal"],
        )

    eligible = [
        item
        for item in typed
        if any(
            item[field]
            for field in (
                "distinctAction",
                "objectInteraction",
                "visibleOutcome",
                "readableInterfaceChange",
            )
        )
    ]
    if (
        not eligible
        and require_distinct_primary
        and not allow_grounded_primary_without_distinct_support
    ):
        _fail(
            NoDistinctPrimaryVisualEventError,
            "No assessed visual draft established a distinct primary event.",
        )

    candidates = eligible if eligible else typed
    primary_ordinal = max(candidates, key=priority)["ordinal"]
    frame = _strict_editorial_frame(
        result["editorialFrame"],
        visual_drafts,
        primary_ordinal,
        typed,
    )
    if not eligible:
        primary_assessment = typed[primary_ordinal - 1]
        frame["momentKind"] = (
            "Routine" if primary_assessment["routineOnly"] else "Unclear"
        )
        frame["premise"] = None
        frame["supportingDraftOrdinals"] = [primary_ordinal]
    authority_adjusted = primary_ordinal in authority_adjusted_ordinals
    frame["creatorAuthorityAdjusted"] = authority_adjusted
    return {
        "primaryVisualDraftOrdinal": primary_ordinal,
        "assessments": typed,
        "editorialFrame": frame,
    }


def _strict_editorial_frame(
    value: Any,
    visual_drafts: list[dict[str, Any]],
    primary_ordinal: int,
    assessments: list[dict[str, Any]],
) -> dict[str, Any]:
    frame = _require_object(value, "visual-event editorial frame")
    _require_exact_keys(
        frame,
        {"momentKind", "premise", "supportingDraftOrdinals"},
        "visual-event editorial frame",
    )
    moment_kind = frame["momentKind"]
    if moment_kind not in set(MOMENT_KINDS):
        _fail(InferenceError, "Visual-event editorial moment kind is invalid.")
    raw_ordinals = _require_array(
        frame["supportingDraftOrdinals"],
        "visual-event editorial supporting ordinals",
        maximum=len(visual_drafts),
    )
    ordinals = sorted({
        ordinal
        for ordinal in raw_ordinals
        if isinstance(ordinal, int)
        and not isinstance(ordinal, bool)
        and 1 <= ordinal <= len(visual_drafts)
    })
    if primary_ordinal not in ordinals:
        ordinals.append(primary_ordinal)
        ordinals.sort()
    premise = _safe_editorial_premise(
        frame["premise"],
        [visual_drafts[ordinal - 1] for ordinal in ordinals],
        [assessments[ordinal - 1]["presentationKind"] for ordinal in ordinals],
    )
    return {
        "policyVersion": EDITORIAL_FRAME_POLICY_VERSION,
        "authorityKind": "StoryShapeOnly",
        "momentKind": moment_kind,
        "premise": premise,
        "supportingDraftOrdinals": ordinals,
    }
