"""Bounded knowledge-candidate assessment for grounded metadata."""
from __future__ import annotations

import hashlib
import json
from typing import Any

from ..commands import HOST_DIRECTORY, InferenceError, UsageOrInputError, _fail
from ..request_validation import _require_exact_keys, _require_object

KNOWLEDGE_SELECTION_SCHEMA_VERSION = (
    "grounded-editorial-knowledge-selection-json-schema-1.1"
)
KNOWLEDGE_SELECTION_PROMPT_VERSION = "1.4"
KNOWLEDGE_SELECTION_PROMPT_SHA256 = (
    "723d0892c5e74d75671bc61854f2717b19f852133c8b994ceed4bd595ec0001a"
)


def _knowledge_selection_prompt_text() -> str:
    path = HOST_DIRECTORY / "replayfoundry-editorial-knowledge-selection-prompt-1.4.txt"
    text = path.read_text(encoding="utf-8").replace("\r\n", "\n").replace("\r", "\n").strip()
    if hashlib.sha256(text.encode("utf-8")).hexdigest() != KNOWLEDGE_SELECTION_PROMPT_SHA256:
        _fail(UsageOrInputError, "Editorial knowledge-selection prompt source changed.")
    return text


def _current_knowledge_candidates(request: dict[str, Any]) -> list[dict[str, Any]]:
    return [
        item
        for item in (request.get("gameKnowledge") or {}).get("matches", [])
        if item["strength"] in {"ClipLinked", "CandidateForVisualGrounding"}
        and item["temporalRelation"] == "CurrentEventCandidate"
    ]


def _knowledge_selection_schema(
    candidates: list[dict[str, Any]],
) -> tuple[str, str]:
    assessment_schema = {
        "type": "object",
        "properties": {
            "setting": {"type": "boolean"},
            "entity": {"type": "boolean"},
            "object": {"type": "boolean"},
            "action": {"type": "boolean"},
            "order": {"type": "boolean"},
            "conflict": {"type": "boolean"},
        },
        "required": ["setting", "entity", "object", "action", "order", "conflict"],
        "additionalProperties": False,
    }
    identities = [item["id"] for item in candidates]
    schema = {
        "type": "object",
        "properties": {
            "assessments": {
                "type": "object",
                "properties": {identity: assessment_schema for identity in identities},
                "required": identities,
                "additionalProperties": False,
            }
        },
        "required": ["assessments"],
        "additionalProperties": False,
    }
    canonical = json.dumps(schema, sort_keys=True, separators=(",", ":"))
    return canonical, hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def _knowledge_selection_messages(
    request: dict[str, Any],
    prompt_text: str,
    visual_drafts: list[dict[str, Any]],
    candidates: list[dict[str, Any]],
) -> list[dict[str, Any]]:
    context = {
        "chronologicalVisualDrafts": [
            {"ordinal": index + 1, **draft}
            for index, draft in enumerate(visual_drafts)
        ],
        "currentEventCandidates": [
            {"id": item["id"], "section": item["section"], "text": item["text"]}
            for item in candidates
        ],
        "stableVisualTextAnchors": [
            item["text"]
            for item in (request.get("visualText") or {}).get(
                "groundingAnchors", []
            )
        ],
    }
    return [
        {"role": "system", "content": [{"type": "text", "text": prompt_text}]},
        {
            "role": "user",
            "content": [
                {
                    "type": "video",
                    "video": str(request["_validated"]["videoPath"]),
                    "max_pixels": 131_072,
                    "total_pixels": 16 * 131_072,
                    "fps": 0.2,
                    "min_frames": 4,
                    "max_frames": 16,
                    "video_start": 0.0,
                    "video_end": float(request["_validated"]["videoDuration"]),
                },
                {
                    "type": "text",
                    "text": "Assess every current-event candidate:\n" + json.dumps(
                        context,
                        ensure_ascii=False,
                        sort_keys=True,
                        separators=(",", ":"),
                    ),
                },
            ],
        },
    ]


def _strict_knowledge_selection(
    text: str,
    candidates: list[dict[str, Any]],
) -> dict[str, Any]:
    try:
        value = json.loads(text)
    except json.JSONDecodeError as error:
        _fail(InferenceError, f"Knowledge selection is not strict JSON: {error}")
    result = _require_object(value, "knowledge selection output")
    _require_exact_keys(result, {"assessments"}, "knowledge selection output")
    assessments = _require_object(result["assessments"], "knowledge selection assessments")
    identities = [item["id"] for item in candidates]
    _require_exact_keys(assessments, set(identities), "knowledge selection assessments")
    fields = ("setting", "entity", "object", "action", "order", "conflict")
    expanded = {
        "setting": "settingSupport",
        "entity": "entityIdentitySupport",
        "object": "distinctiveObjectSupport",
        "action": "centralActionSupport",
        "order": "chronologySupport",
        "conflict": "materialContradiction",
    }
    typed: list[dict[str, Any]] = []
    eligible: list[tuple[int, str]] = []
    for identity in identities:
        assessment = _require_object(assessments[identity], f"knowledge assessment {identity}")
        _require_exact_keys(assessment, set(fields), f"knowledge assessment {identity}")
        if any(not isinstance(assessment[field], bool) for field in fields):
            _fail(InferenceError, "Knowledge selection assessments must be Boolean.")
        support_count = sum(int(assessment[field]) for field in fields[:-1])
        typed.append({"passageId": identity, **{expanded[field]: assessment[field] for field in fields}})
        if support_count >= 2 and not assessment["conflict"]:
            eligible.append((support_count, identity))
    selected_identity = "None"
    if eligible:
        maximum = max(score for score, _ in eligible)
        winners = [identity for score, identity in eligible if score == maximum]
        if len(winners) == 1:
            selected_identity = winners[0]
    return {"currentPassageId": selected_identity, "assessments": typed}


def _request_with_selected_knowledge(
    request: dict[str, Any],
    selected_identity: str,
) -> dict[str, Any]:
    result = dict(request)
    knowledge = request.get("gameKnowledge")
    if knowledge is None:
        result["gameKnowledge"] = None
        return result
    matches = knowledge["matches"]
    general_context = [
        item for item in matches
        if item["strength"] == "GeneralContext"
        and item["temporalRelation"] == "Unspecified"
    ]
    if selected_identity == "None":
        if not general_context:
            result["gameKnowledge"] = None
            return result
        filtered = dict(knowledge)
        filtered["matches"] = general_context
        result["gameKnowledge"] = filtered
        return result
    selected_index = next(
        index for index, item in enumerate(matches) if item["id"] == selected_identity
    )
    retained = [matches[selected_index]]
    if (
        selected_index + 1 < len(matches)
        and matches[selected_index + 1]["temporalRelation"] == "ImmediatelyPriorContext"
    ):
        retained.append(matches[selected_index + 1])
    retained.extend(
        item for item in general_context
        if all(existing["id"] != item["id"] for existing in retained)
    )
    filtered = dict(knowledge)
    filtered["matches"] = retained
    result["gameKnowledge"] = filtered
    return result
