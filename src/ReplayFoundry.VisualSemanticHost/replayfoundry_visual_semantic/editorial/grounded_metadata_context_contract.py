"""Validation for OCR and licensed game-knowledge metadata context."""
from __future__ import annotations

import hashlib
from typing import Any

from ..commands import UsageOrInputError, _fail
from ..request_validation import (
    _require_array,
    _require_exact_keys,
    _require_object,
    _require_sha256,
)
from .grounded_metadata_contract_values import (
    bounded_text,
    finite_number,
    https_uri,
    stable_id,
    utc_timestamp,
)


GAME_KNOWLEDGE_POLICY_VERSION = "1.4"


def validate_visual_text(
    value: Any,
    location: str,
    clip_start: float,
    clip_end: float,
) -> dict[str, Any] | None:
    if value is None:
        return None
    visual_text = _require_object(value, location)
    _require_exact_keys(
        visual_text,
        {
            "samplingPolicyVersion",
            "stabilityPolicyVersion",
            "provider",
            "sampledFrameCount",
            "groundingAnchors",
            "diagnosticAnchors",
        },
        location,
    )
    if bounded_text(
        visual_text["samplingPolicyVersion"],
        f"{location}.samplingPolicyVersion",
        80,
    ) != "visual-text-sampling-1.0":
        _fail(UsageOrInputError, f"{location}.samplingPolicyVersion is unsupported.")
    if bounded_text(
        visual_text["stabilityPolicyVersion"],
        f"{location}.stabilityPolicyVersion",
        80,
    ) != "visual-text-stability-1.1":
        _fail(UsageOrInputError, f"{location}.stabilityPolicyVersion is unsupported.")
    sampled_frame_count = visual_text["sampledFrameCount"]
    if (
        isinstance(sampled_frame_count, bool)
        or not isinstance(sampled_frame_count, int)
        or sampled_frame_count < 0
        or sampled_frame_count > 8
    ):
        _fail(UsageOrInputError, f"{location}.sampledFrameCount is invalid.")
    provider_value = visual_text["provider"]
    provider = None
    if provider_value is not None:
        provider_object = _require_object(provider_value, f"{location}.provider")
        _require_exact_keys(
            provider_object,
            {"name", "version", "backend", "runtimeVersion", "languageTag"},
            f"{location}.provider",
        )
        provider = {
            key: bounded_text(provider_object[key], f"{location}.provider.{key}", 256)
            for key in (
                "name", "version", "backend", "runtimeVersion", "languageTag"
            )
        }
    if sampled_frame_count > 0 and provider is None:
        _fail(UsageOrInputError, f"{location}.provider is required for sampled frames.")

    def anchors(
        name: str,
        expected_grounding: bool,
        maximum: int,
    ) -> list[dict[str, Any]]:
        values = _require_array(visual_text[name], f"{location}.{name}", maximum=maximum)
        result: list[dict[str, Any]] = []
        normalized_values: set[str] = set()
        for index, anchor_value in enumerate(values):
            anchor_location = f"{location}.{name}[{index}]"
            anchor = _require_object(anchor_value, anchor_location)
            _require_exact_keys(
                anchor,
                {"text", "sourceKind", "occurrenceCount", "sourceTimestampsSeconds"},
                anchor_location,
            )
            text = bounded_text(anchor["text"], f"{anchor_location}.text", 1000)
            source_kind = bounded_text(
                anchor["sourceKind"],
                f"{anchor_location}.sourceKind",
                20,
            )
            if source_kind not in {"Line", "Word"}:
                _fail(UsageOrInputError, f"{anchor_location}.sourceKind is invalid.")
            normalized = " ".join(text.casefold().split())
            if normalized in normalized_values:
                _fail(UsageOrInputError, f"{location} has duplicate OCR anchors.")
            normalized_values.add(normalized)
            occurrence_count = anchor["occurrenceCount"]
            timestamp_values = _require_array(
                anchor["sourceTimestampsSeconds"],
                f"{anchor_location}.sourceTimestampsSeconds",
                maximum=8,
            )
            timestamps = [
                finite_number(
                    timestamp,
                    f"{anchor_location}.sourceTimestampsSeconds",
                    clip_start,
                    clip_end,
                )
                for timestamp in timestamp_values
            ]
            if (
                isinstance(occurrence_count, bool)
                or not isinstance(occurrence_count, int)
                or occurrence_count != len(timestamps)
                or timestamps != sorted(set(timestamps))
                or occurrence_count < 1
            ):
                _fail(
                    UsageOrInputError,
                    f"{anchor_location} has invalid stability provenance.",
                )
            lexical_words = normalized.split()
            if expected_grounding and (
                occurrence_count < 2
                or source_kind != "Line"
                or len(lexical_words) < 2
                or not text[0].isalnum()
                or not text[-1].isalnum()
            ):
                _fail(UsageOrInputError, f"{anchor_location} lacks grounding authority.")
            result.append(
                {
                    "text": text,
                    "sourceKind": source_kind,
                    "occurrenceCount": occurrence_count,
                    "sourceTimestampsSeconds": timestamps,
                }
            )
        return result

    return {
        "samplingPolicyVersion": "visual-text-sampling-1.0",
        "stabilityPolicyVersion": "visual-text-stability-1.1",
        "provider": provider,
        "sampledFrameCount": sampled_frame_count,
        "groundingAnchors": anchors("groundingAnchors", True, 24),
        "diagnosticAnchors": anchors("diagnosticAnchors", False, 12),
    }


def validate_game_knowledge(
    value: Any,
    location: str,
    available_clip_evidence_ids: set[str],
) -> dict[str, Any] | None:
    if value is None:
        return None
    knowledge = _require_object(value, location)
    _require_exact_keys(
        knowledge,
        {"policyVersion", "snapshotSha256", "provider", "sources", "matches"},
        location,
    )
    if (
        bounded_text(knowledge["policyVersion"], f"{location}.policyVersion", 20)
        != GAME_KNOWLEDGE_POLICY_VERSION
    ):
        _fail(UsageOrInputError, f"{location}.policyVersion is unsupported.")
    snapshot_sha256 = _require_sha256(
        knowledge["snapshotSha256"], f"{location}.snapshotSha256"
    )
    provider = _require_object(knowledge["provider"], f"{location}.provider")
    _require_exact_keys(provider, {"name", "version"}, f"{location}.provider")
    validated_provider = {
        "name": bounded_text(provider["name"], f"{location}.provider.name", 120),
        "version": bounded_text(provider["version"], f"{location}.provider.version", 40),
    }

    source_values = _require_array(knowledge["sources"], f"{location}.sources", maximum=8)
    if not source_values:
        _fail(UsageOrInputError, f"{location}.sources cannot be empty.")
    sources: list[dict[str, Any]] = []
    source_ids: set[str] = set()
    for index, source_value in enumerate(source_values):
        source_location = f"{location}.sources[{index}]"
        source = _require_object(source_value, source_location)
        _require_exact_keys(
            source,
            {
                "id", "kind", "role", "title", "pageUri", "revisionId",
                "revisionTimestampUtc", "licenseIdentifier", "licenseUri",
                "attribution", "contentSha256",
            },
            source_location,
        )
        source_id = stable_id(source["id"], f"{source_location}.id")
        if source_id in source_ids:
            _fail(UsageOrInputError, f"{location}.sources has duplicate IDs.")
        source_ids.add(source_id)
        kind = bounded_text(source["kind"], f"{source_location}.kind", 40)
        if kind not in {"Wikipedia", "Wikidata"}:
            _fail(UsageOrInputError, f"{source_location}.kind is unsupported.")
        role = bounded_text(source["role"], f"{source_location}.role", 40)
        if role not in {"PrimaryArticle", "RelatedArticle", "StructuredIdentity"}:
            _fail(UsageOrInputError, f"{source_location}.role is unsupported.")
        sources.append(
            {
                "id": source_id,
                "kind": kind,
                "role": role,
                "title": bounded_text(source["title"], f"{source_location}.title", 240),
                "pageUri": https_uri(source["pageUri"], f"{source_location}.pageUri"),
                "revisionId": bounded_text(
                    source["revisionId"], f"{source_location}.revisionId", 120
                ),
                "revisionTimestampUtc": utc_timestamp(
                    source["revisionTimestampUtc"],
                    f"{source_location}.revisionTimestampUtc",
                ),
                "licenseIdentifier": bounded_text(
                    source["licenseIdentifier"],
                    f"{source_location}.licenseIdentifier",
                    80,
                ),
                "licenseUri": https_uri(
                    source["licenseUri"], f"{source_location}.licenseUri"
                ),
                "attribution": bounded_text(
                    source["attribution"], f"{source_location}.attribution", 500
                ),
                "contentSha256": _require_sha256(
                    source["contentSha256"], f"{source_location}.contentSha256"
                ),
            }
        )

    match_values = _require_array(knowledge["matches"], f"{location}.matches", maximum=4)
    if not match_values:
        _fail(UsageOrInputError, f"{location}.matches cannot be empty.")
    matches: list[dict[str, Any]] = []
    match_ids: set[str] = set()
    for index, match_value in enumerate(match_values):
        match_location = f"{location}.matches[{index}]"
        match = _require_object(match_value, match_location)
        _require_exact_keys(
            match,
            {
                "id", "sourceId", "section", "text", "contentSha256",
                "strength", "temporalRelation", "relevance", "matchedTerms",
                "clipEvidenceIds",
            },
            match_location,
        )
        match_id = stable_id(match["id"], f"{match_location}.id")
        source_id = stable_id(match["sourceId"], f"{match_location}.sourceId")
        if match_id in match_ids or source_id not in source_ids:
            _fail(UsageOrInputError, f"{match_location} has invalid stable identity.")
        match_ids.add(match_id)
        text = bounded_text(match["text"], f"{match_location}.text", 1800)
        content_sha256 = _require_sha256(
            match["contentSha256"], f"{match_location}.contentSha256"
        )
        if hashlib.sha256(text.encode("utf-8")).hexdigest() != content_sha256:
            _fail(UsageOrInputError, f"{match_location}.contentSha256 does not match text.")
        strength = bounded_text(match["strength"], f"{match_location}.strength", 40)
        if strength not in {
            "GeneralContext", "CandidateForVisualGrounding", "ClipLinked"
        }:
            _fail(UsageOrInputError, f"{match_location}.strength is unsupported.")
        temporal_relation = bounded_text(
            match["temporalRelation"],
            f"{match_location}.temporalRelation",
            40,
        )
        if temporal_relation not in {
            "Unspecified", "CurrentEventCandidate", "ImmediatelyPriorContext"
        }:
            _fail(UsageOrInputError, f"{match_location}.temporalRelation is unsupported.")
        if (
            temporal_relation == "ImmediatelyPriorContext"
            and strength != "CandidateForVisualGrounding"
        ):
            _fail(UsageOrInputError, f"{match_location}.temporalRelation is inconsistent.")
        terms = [
            bounded_text(term, f"{match_location}.matchedTerms", 80)
            for term in _require_array(
                match["matchedTerms"], f"{match_location}.matchedTerms", maximum=40
            )
        ]
        if (
            not terms and strength == "ClipLinked"
            or len({term.casefold() for term in terms}) != len(terms)
        ):
            _fail(UsageOrInputError, f"{match_location}.matchedTerms is invalid.")
        clip_ids = [
            stable_id(item, f"{match_location}.clipEvidenceIds")
            for item in _require_array(
                match["clipEvidenceIds"],
                f"{match_location}.clipEvidenceIds",
                maximum=24,
            )
        ]
        if (
            len(set(clip_ids)) != len(clip_ids)
            or any(item not in available_clip_evidence_ids for item in clip_ids)
            or (strength == "ClipLinked" and not clip_ids)
            or (strength == "GeneralContext" and clip_ids)
            or (strength == "CandidateForVisualGrounding" and not clip_ids)
        ):
            _fail(UsageOrInputError, f"{match_location}.clipEvidenceIds is invalid.")
        matches.append(
            {
                "id": match_id,
                "sourceId": source_id,
                "section": bounded_text(
                    match["section"], f"{match_location}.section", 160
                ),
                "text": text,
                "contentSha256": content_sha256,
                "strength": strength,
                "temporalRelation": temporal_relation,
                "relevance": finite_number(
                    match["relevance"], f"{match_location}.relevance", 0, 1
                ),
                "matchedTerms": terms,
                "clipEvidenceIds": clip_ids,
            }
        )
    return {
        "policyVersion": GAME_KNOWLEDGE_POLICY_VERSION,
        "snapshotSha256": snapshot_sha256,
        "provider": validated_provider,
        "sources": sources,
        "matches": matches,
    }
