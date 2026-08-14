"""Validation for licensed knowledge citations in audience metadata."""
from __future__ import annotations

import hashlib
import re
from typing import Any

from ..errors import InferenceError, _fail
from ..request_validation import _require_array, _require_exact_keys, _require_object
from .grounded_metadata_contract_values import bounded_text
from .grounded_metadata_lexical import normalize_lexical


def grounding_binding_id(knowledge_id: str, clip_evidence_id: str) -> str:
    payload = (knowledge_id + "\n" + clip_evidence_id).encode("utf-8")
    return "gkb-" + hashlib.sha256(payload).hexdigest()


def strict_grounding(
    value: Any,
    request: dict[str, Any],
    title: str,
    description: str,
) -> list[dict[str, Any]]:
    values = _require_array(value, "provider output.grounding", maximum=2)
    knowledge = request.get("gameKnowledge")
    if knowledge is None:
        if values:
            _fail(InferenceError, "Grounded metadata cited unavailable game knowledge.")
        return []
    bindings = {
        grounding_binding_id(item["id"], clip_id): (item, clip_id)
        for item in knowledge["matches"]
        if item["strength"] in {"ClipLinked", "CandidateForVisualGrounding"}
        for clip_id in item["clipEvidenceIds"]
    }
    result: list[dict[str, Any]] = []
    seen_fields: set[str] = set()
    for index, item_value in enumerate(values):
        location = f"provider output.grounding[{index}]"
        item = _require_object(item_value, location)
        _require_exact_keys(item, {"audienceField", "bindingIds"}, location)
        audience_field = bounded_text(
            item["audienceField"], f"{location}.audienceField", 20
        )
        if audience_field not in {"Title", "Description"} or audience_field in seen_fields:
            _fail(InferenceError, "Grounded metadata audience-field binding is invalid.")
        seen_fields.add(audience_field)
        binding_ids = [
            bounded_text(identity, f"{location}.bindingIds", 160)
            for identity in _require_array(
                item["bindingIds"], f"{location}.bindingIds", maximum=4
            )
        ]
        if (
            not binding_ids
            or len(set(binding_ids)) != len(binding_ids)
            or any(identity not in bindings for identity in binding_ids)
        ):
            _fail(InferenceError, "Grounded metadata used invalid game-knowledge references.")
        bound_matches = [bindings[identity] for identity in binding_ids]
        knowledge_ids = list(dict.fromkeys(match["id"] for match, _ in bound_matches))
        clip_ids = list(dict.fromkeys(clip_id for _, clip_id in bound_matches))
        audience_copy = title if audience_field == "Title" else description
        if not knowledge_claim_is_specific(
            audience_copy,
            list(dict.fromkeys(match["text"] for match, _ in bound_matches)),
        ):
            _fail(
                InferenceError,
                "Grounded metadata knowledge claim did not use a canonical name "
                "or two distinctive cited-passage terms.",
            )
        result.append(
            {
                "audienceField": audience_field,
                "knowledgeReferenceIds": knowledge_ids,
                "clipEvidenceReferenceIds": clip_ids,
            }
        )
    return result


_KNOWLEDGE_STOP_WORDS = {
    "about", "after", "again", "also", "another", "before", "being",
    "during", "following", "from", "into", "only", "other", "their",
    "there", "these", "they", "the", "this", "through", "under", "when",
    "where", "which", "while", "with", "would",
}


def knowledge_claim_is_specific(audience_copy: str, passages: list[str]) -> bool:
    audience_tokens = set(normalize_lexical(audience_copy).split())
    passage_tokens = {
        token
        for passage in passages
        for token in normalize_lexical(passage).split()
        if len(token) >= 5 and token not in _KNOWLEDGE_STOP_WORDS
    }
    if len(audience_tokens.intersection(passage_tokens)) >= 2:
        return True
    proper_names = {
        match.group(0).casefold()
        for passage in passages
        for match in re.finditer(r"\b[A-Z][A-Za-z0-9'’_-]{2,}\b", passage)
        if match.group(0).casefold() not in _KNOWLEDGE_STOP_WORDS
    }
    return bool(audience_tokens.intersection(proper_names))
