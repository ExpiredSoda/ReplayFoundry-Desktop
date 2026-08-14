"""Typed visual-draft generation for grounded editorial metadata."""
from __future__ import annotations

import hashlib
import json
from typing import Any

from ..commands import HOST_DIRECTORY, InferenceError, UsageOrInputError, _fail
from ..request_validation import _require_array, _require_exact_keys, _require_object
from .grounded_metadata_sampling import (
    CORE_TIER,
    GroundedMetadataSamplingWindow,
    _window,
)

VISUAL_DRAFT_SCHEMA_VERSION = "grounded-editorial-visual-draft-json-schema-1.1"
VISUAL_DRAFT_PROMPT_VERSION = "1.4"
VISUAL_DRAFT_PROMPT_SHA256 = "e07bb76961c9764c12fdbf13b60963928d319af15f5da55cca76bd660754f77b"


def _visual_draft_prompt_text() -> str:
    path = HOST_DIRECTORY / "replayfoundry-editorial-visual-draft-prompt-1.4.txt"
    text = path.read_text(encoding="utf-8").replace("\r\n", "\n").replace("\r", "\n").strip()
    if hashlib.sha256(text.encode("utf-8")).hexdigest() != VISUAL_DRAFT_PROMPT_SHA256:
        _fail(UsageOrInputError, "Editorial visual-draft prompt source changed.")
    return text


def _visual_draft_messages(
    request: dict[str, Any],
    prompt_text: str,
    video_window: tuple[float, float],
    chunk_ordinal: int,
    chunk_count: int,
    sampling_window: GroundedMetadataSamplingWindow | None = None,
) -> list[dict[str, Any]]:
    start, end = video_window
    sampling = sampling_window or _window(start, end, CORE_TIER)
    if (
        abs(sampling.start_seconds - start) > 1e-9
        or abs(sampling.end_seconds - end) > 1e-9
    ):
        raise ValueError("Visual-draft sampling must match its chronological window.")
    return [
        {"role": "system", "content": [{"type": "text", "text": prompt_text}]},
        {
            "role": "user",
            "content": [
                {
                    "type": "video",
                    "video": str(request["_validated"]["videoPath"]),
                    **sampling.video_options(),
                },
                {
                    "type": "text",
                    "text": (
                        "Describe only this chronological chunk of the bounded review. "
                        f"Chunk {chunk_ordinal} of {chunk_count}; relative seconds "
                        f"{start:.3f} through {end:.3f}. Return only the JSON schema."
                    ),
                },
            ],
        },
    ]


def _visual_draft_schema() -> tuple[str, str]:
    compact_text = {"type": "string", "minLength": 1, "maxLength": 100}
    schema = {
        "type": "object",
        "properties": {
            "environment": {"type": "string", "minLength": 1, "maxLength": 120},
            "environmentUncertain": {"type": "boolean"},
            "subjectsAndObjects": {
                "type": "array",
                "items": compact_text,
                "minItems": 1,
                "maxItems": 6,
            },
            "actions": {
                "type": "array",
                "items": compact_text,
                "minItems": 1,
                "maxItems": 4,
            },
            "readableText": {
                "type": "array",
                "items": {"type": "string", "minLength": 1, "maxLength": 80},
                "maxItems": 4,
            },
            "uncertainties": {
                "type": "array",
                "items": compact_text,
                "maxItems": 3,
            },
        },
        "required": [
            "environment",
            "environmentUncertain",
            "subjectsAndObjects",
            "actions",
            "readableText",
            "uncertainties",
        ],
        "additionalProperties": False,
    }
    canonical = json.dumps(schema, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return canonical, hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def _strict_visual_draft(text: str) -> dict[str, Any]:
    try:
        value = json.loads(
            text,
            parse_constant=lambda token: (_ for _ in ()).throw(
                ValueError(f"non-finite JSON token {token}")
            ),
        )
    except (json.JSONDecodeError, ValueError) as error:
        _fail(InferenceError, f"Visual draft output is not strict JSON: {error}")
    draft = _require_object(value, "visual draft output")
    fields = {
        "environment",
        "environmentUncertain",
        "subjectsAndObjects",
        "actions",
        "readableText",
        "uncertainties",
    }
    _require_exact_keys(draft, fields, "visual draft output")
    environment = draft["environment"]
    if not isinstance(environment, str) or not environment.strip() or len(environment) > 120:
        _fail(InferenceError, "Visual draft environment must contain at most 120 characters.")
    if not isinstance(draft["environmentUncertain"], bool):
        _fail(InferenceError, "Visual draft environmentUncertain must be Boolean.")

    def require_text_array(
        name: str,
        maximum_items: int,
        maximum_length: int,
        minimum_items: int = 0,
    ) -> list[str]:
        values = _require_array(draft[name], f"visual draft output.{name}", maximum=maximum_items)
        if len(values) < minimum_items:
            _fail(InferenceError, f"Visual draft {name} is incomplete.")
        if any(
            not isinstance(item, str) or not item.strip() or len(item) > maximum_length
            for item in values
        ):
            _fail(InferenceError, f"Visual draft {name} contains invalid text.")
        return [item.strip() for item in values]

    return {
        "environment": environment.strip(),
        "environmentUncertain": draft["environmentUncertain"],
        "subjectsAndObjects": require_text_array("subjectsAndObjects", 6, 100, 1),
        "actions": require_text_array("actions", 4, 100, 1),
        "readableText": require_text_array("readableText", 4, 80),
        "uncertainties": require_text_array("uncertainties", 3, 100),
    }
