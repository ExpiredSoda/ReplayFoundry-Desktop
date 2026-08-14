"""Constrained visual-event assessment and deterministic primary selection."""
from __future__ import annotations

import hashlib
import json
from typing import Any

from ..commands import HOST_DIRECTORY, InferenceError, UsageOrInputError, _fail
from ..errors import NoDistinctPrimaryVisualEventError
from ..request_validation import _require_array, _require_exact_keys, _require_object

VISUAL_EVENT_SELECTION_SCHEMA_VERSION = (
    "grounded-editorial-visual-event-selection-json-schema-1.2"
)
VISUAL_EVENT_SELECTION_PROMPT_VERSION = "1.1"
VISUAL_EVENT_SELECTION_PROMPT_SHA256 = (
    "26a6529193c9093dea13001ab9f4b5b2051e3b2aaad1328c78ea80444a39df30"
)


def _visual_event_selection_prompt_text() -> str:
    path = HOST_DIRECTORY / "replayfoundry-editorial-event-selection-prompt-1.1.txt"
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
        },
        "required": ["assessments"],
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
) -> dict[str, Any]:
    draft_count = len(visual_drafts)
    try:
        value = json.loads(text)
    except json.JSONDecodeError as error:
        _fail(InferenceError, f"Visual-event selection is not strict JSON: {error}")
    result = _require_object(value, "visual-event selection output")
    _require_exact_keys(result, {"assessments"}, "visual-event selection output")
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
    }
    actor_authorities = {"CreatorControlled", "OtherPerson", "Unknown"}
    creator_relations = {
        "CreatorActed",
        "CreatorAffected",
        "CreatorEncountered",
        "Unestablished",
    }
    typed: list[dict[str, Any]] = []
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
            }
        ):
            _fail(InferenceError, "Visual-event assessments must be ordered and Boolean.")
        if (
            assessment["actorAuthority"] not in actor_authorities
            or assessment["creatorExperienceRelation"] not in creator_relations
            or (
                assessment["creatorExperienceRelation"] == "CreatorActed"
                and assessment["actorAuthority"] != "CreatorControlled"
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
        typed.append(typed_assessment)

    def priority(item: dict[str, Any]) -> tuple[int, int, int, int, int, int, int]:
        score = (
            3 * int(item["distinctAction"])
            + 2 * int(item["objectInteraction"])
            + 2 * int(item["visibleOutcome"])
            + int(item["readableInterfaceChange"])
            - 3 * int(item["routineOnly"])
            - int(item["uncertain"])
        )
        return (
            score,
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
    if not eligible and require_distinct_primary:
        _fail(
            NoDistinctPrimaryVisualEventError,
            "No assessed visual draft established a distinct primary event.",
        )

    return {
        "primaryVisualDraftOrdinal": (
            max(eligible, key=priority)["ordinal"] if eligible else 1
        ),
        "assessments": typed,
    }
