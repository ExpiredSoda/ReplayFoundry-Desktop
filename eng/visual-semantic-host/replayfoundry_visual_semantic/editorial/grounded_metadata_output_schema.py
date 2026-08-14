"""Canonical constrained-output schema for grounded audience metadata."""
from __future__ import annotations

import hashlib
import json
from typing import Any

from .grounded_metadata_grounding_validation import grounding_binding_id


def title_body_maximum(hashtag: str) -> int:
    preferred_final_maximum = min(100, max(80, len(hashtag) + 12))
    return preferred_final_maximum - len(hashtag) - 1


def metadata_schema(request: dict[str, Any]) -> tuple[str, str]:
    linked_matches = [
        item
        for item in (request.get("gameKnowledge") or {}).get("matches", [])
        if item["strength"] in {"ClipLinked", "CandidateForVisualGrounding"}
    ]
    binding_ids = [
        grounding_binding_id(item["id"], evidence_id)
        for item in linked_matches
        for evidence_id in item["clipEvidenceIds"]
    ]
    grounding_schema = (
        {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "audienceField": {
                        "type": "string",
                        "enum": ["Title", "Description"],
                    },
                    "bindingIds": {
                        "type": "array",
                        "items": {"type": "string", "enum": binding_ids},
                        "minItems": 1,
                        "maxItems": 4,
                    },
                },
                "required": ["audienceField", "bindingIds"],
                "additionalProperties": False,
            },
            "maxItems": 2,
        }
        if binding_ids
        else {"type": "array", "maxItems": 0}
    )
    schema = {
        "type": "object",
        "properties": {
            "titleBody": {
                "type": "string",
                "minLength": 1,
                "maxLength": title_body_maximum(request["game"]["hashtag"]),
            },
            "description": {"type": "string", "minLength": 1, "maxLength": 420},
            "tags": {
                "type": "array",
                "items": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 60,
                    "pattern": r'^[^#"\\\r\n\t]+$',
                },
                "minItems": 1,
                "maxItems": 8,
            },
            "grounding": grounding_schema,
            "temporalVoice": {
                "type": "string",
                "enum": ["RetrospectivePast"],
            },
        },
        "required": [
            "titleBody",
            "description",
            "tags",
            "grounding",
            "temporalVoice",
        ],
        "additionalProperties": False,
    }
    canonical = json.dumps(
        schema,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    )
    return canonical, hashlib.sha256(canonical.encode("utf-8")).hexdigest()
