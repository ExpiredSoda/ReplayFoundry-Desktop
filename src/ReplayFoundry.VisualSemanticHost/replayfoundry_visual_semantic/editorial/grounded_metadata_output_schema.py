"""Canonical constrained-output schema for grounded audience metadata."""
from __future__ import annotations

import hashlib
import json
from typing import Any

from .grounded_metadata_grounding_validation import grounding_binding_id


def title_body_maximum(hashtag: str) -> int:
    # The desktop app appends one ASCII space plus this exact hashtag after
    # provider validation. Count UTF-16 code units to match C# string.Length
    # so constrained decoding can never emit a body that exceeds the app's
    # effective preferred finalized-title limit (normally 80 characters, with
    # bounded extra room only for an unusually long exact hashtag).
    hashtag_code_units = len(hashtag.encode("utf-16-le")) // 2
    preferred_final_maximum = min(
        100,
        max(80, hashtag_code_units + 12),
    )
    remaining = preferred_final_maximum - hashtag_code_units - 1
    if remaining < 1:
        raise ValueError(
            "The canonical game hashtag leaves no room for a publishing title."
        )
    return min(80, remaining)


def metadata_schema(request: dict[str, Any]) -> tuple[str, str]:
    # Creator authority uses title_body_maximum for its typed profile. Resolve
    # these runtime policy helpers here without a module-initialization cycle.
    from .grounded_metadata_creator_authority import (
        _requires_balanced_copy, balanced_copy_field_plan,
    )

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
    if _requires_balanced_copy(request):
        plan = balanced_copy_field_plan(
            request["profile"]["variantIntent"], title_body_maximum(request["game"]["hashtag"]),
        )
        field = plan["attributedThoughtField"]
        maximum = schema["properties"][field]["maxLength"]
        # XGrammar 0.2.2 uses pattern instead of separate min/maxLength.
        # Bound every alternative itself; no lookaround or semantic assertions.
        branches = [
            opening + " " + r'[^"\\\r\n]' + "{1," + str(maximum - len(opening) - 1) + "}"
            for opening in plan["allowedAttributionOpenings"]
        ]
        schema["properties"][field]["pattern"] = "^(" + "|".join(branches) + ")$"
    canonical = json.dumps(
        schema,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    )
    return canonical, hashlib.sha256(canonical.encode("utf-8")).hexdigest()
