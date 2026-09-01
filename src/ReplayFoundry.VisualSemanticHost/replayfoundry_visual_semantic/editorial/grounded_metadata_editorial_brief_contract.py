"""Validation for host-authored grounded editorial briefs."""
from __future__ import annotations

from typing import Any

from ..commands import UsageOrInputError, _fail
from ..request_validation import (
    _require_array,
    _require_exact_keys,
    _require_object,
    _require_sha256,
)
from .grounded_metadata_contract_values import bounded_text, optional_text


EDITORIAL_BRIEF_POLICY_VERSION = "grounded-editorial-brief-1.0"
EDITORIAL_BRIEF_COPY_GOAL = (
    "Describe one broad playthrough beat across the whole clip. Prefer the "
    "supported objective, complication, outcome, or next step over a list "
    "of visible objects or a frame-by-frame recap."
)


def validate_editorial_brief(
    value: Any,
    location: str,
) -> dict[str, Any] | None:
    if value is None:
        return None
    brief = _require_object(value, location)
    _require_exact_keys(
        brief,
        {
            "policyVersion", "copyGoal", "fingerprint", "revisionKind",
            "canonicalIdentity", "primaryGameplayBeat", "leadIn",
            "visibleFollowThrough", "safeCommentaryAngle",
            "creatorControlRelation", "qualityFlags", "claims",
            "sourceBindings",
        },
        location,
    )
    if bounded_text(brief["policyVersion"], f"{location}.policyVersion", 80) != (
        EDITORIAL_BRIEF_POLICY_VERSION
    ):
        _fail(UsageOrInputError, f"{location}.policyVersion is unsupported.")
    if bounded_text(brief["copyGoal"], f"{location}.copyGoal", 400) != (
        EDITORIAL_BRIEF_COPY_GOAL
    ):
        _fail(UsageOrInputError, f"{location}.copyGoal is not host-authored.")
    revision_kind = bounded_text(
        brief["revisionKind"], f"{location}.revisionKind", 40
    )
    if revision_kind not in {"InitialDraft", "StructuralReroll"}:
        _fail(UsageOrInputError, f"{location}.revisionKind is unsupported.")
    claim_kinds = {
        "GameIdentity", "Edition", "Developer", "Series", "MissionOrChapter",
        "Location", "CanonicalEntity", "NarrativeContext",
        "CreatorCommentaryCue", "DialogueCue", "StableReadableText",
    }
    claim_authorities = {
        "UserConfirmedGameContext", "ConfirmedWikidataIdentity",
        "LicensedGeneralKnowledge", "LicensedCurrentEventCandidate",
        "ClipLinkedLicensedKnowledge", "HumanReviewedTranscript",
        "AutomaticTranscriptCue", "StableLocalOcr",
    }
    claim_states = {"Confirmed", "Supported", "Ambiguous", "Rejected"}
    editorial_fields = {"Title", "Description", "Tags"}
    claims: list[dict[str, Any]] = []
    claim_ids: set[str] = set()
    for index, claim_value in enumerate(
        _require_array(brief["claims"], f"{location}.claims", maximum=24)
    ):
        claim_location = f"{location}.claims[{index}]"
        claim = _require_object(claim_value, claim_location)
        _require_exact_keys(
            claim,
            {
                "id", "kind", "value", "authority", "state",
                "publicSourceIds", "localEvidenceIds", "fieldAuthorizations",
            },
            claim_location,
        )
        claim_id = bounded_text(claim["id"], f"{claim_location}.id", 160)
        if claim_id in claim_ids:
            _fail(UsageOrInputError, f"{location}.claims has duplicate IDs.")
        claim_ids.add(claim_id)
        kind = bounded_text(claim["kind"], f"{claim_location}.kind", 40)
        authority = bounded_text(
            claim["authority"], f"{claim_location}.authority", 50
        )
        state = bounded_text(
            claim["state"], f"{claim_location}.state", 40
        )
        if kind not in claim_kinds or authority not in claim_authorities:
            _fail(UsageOrInputError, f"{claim_location} has unsupported authority.")
        if state not in claim_states:
            _fail(UsageOrInputError, f"{claim_location}.state is unsupported.")
        public_source_ids = [
            bounded_text(item, f"{claim_location}.publicSourceIds", 160)
            for item in _require_array(
                claim["publicSourceIds"],
                f"{claim_location}.publicSourceIds",
                maximum=24,
            )
        ]
        local_evidence_ids = [
            bounded_text(item, f"{claim_location}.localEvidenceIds", 160)
            for item in _require_array(
                claim["localEvidenceIds"],
                f"{claim_location}.localEvidenceIds",
                maximum=24,
            )
        ]
        fields = [
            bounded_text(item, f"{claim_location}.fieldAuthorizations", 40)
            for item in _require_array(
                claim["fieldAuthorizations"],
                f"{claim_location}.fieldAuthorizations",
                maximum=3,
            )
        ]
        if (
            len(set(public_source_ids)) != len(public_source_ids)
            or len(set(local_evidence_ids)) != len(local_evidence_ids)
            or len(set(fields)) != len(fields)
            or any(field not in editorial_fields for field in fields)
            or state in {"Ambiguous", "Rejected"} and fields
        ):
            _fail(UsageOrInputError, f"{claim_location} has invalid source authority.")
        claims.append({
            "id": claim_id,
            "kind": kind,
            "value": bounded_text(claim["value"], f"{claim_location}.value", 600),
            "authority": authority,
            "state": state,
            "publicSourceIds": public_source_ids,
            "localEvidenceIds": local_evidence_ids,
            "fieldAuthorizations": fields,
        })
    binding_values = _require_array(
        brief["sourceBindings"], f"{location}.sourceBindings", maximum=24
    )
    bindings: list[dict[str, Any]] = []
    binding_ids: set[str] = set()
    for index, binding_value in enumerate(binding_values):
        binding_location = f"{location}.sourceBindings[{index}]"
        binding = _require_object(binding_value, binding_location)
        _require_exact_keys(
            binding,
            {
                "claimId", "publicSourceIds", "localEvidenceIds",
                "fieldAuthorizations",
            },
            binding_location,
        )
        claim_id = bounded_text(
            binding["claimId"], f"{binding_location}.claimId", 160
        )
        if claim_id not in claim_ids or claim_id in binding_ids:
            _fail(UsageOrInputError, f"{binding_location}.claimId is invalid.")
        binding_ids.add(claim_id)
        corresponding = next(item for item in claims if item["id"] == claim_id)
        public_ids = [
            bounded_text(item, f"{binding_location}.publicSourceIds", 160)
            for item in _require_array(
                binding["publicSourceIds"],
                f"{binding_location}.publicSourceIds",
                maximum=24,
            )
        ]
        local_ids = [
            bounded_text(item, f"{binding_location}.localEvidenceIds", 160)
            for item in _require_array(
                binding["localEvidenceIds"],
                f"{binding_location}.localEvidenceIds",
                maximum=24,
            )
        ]
        fields = [
            bounded_text(item, f"{binding_location}.fieldAuthorizations", 40)
            for item in _require_array(
                binding["fieldAuthorizations"],
                f"{binding_location}.fieldAuthorizations",
                maximum=3,
            )
        ]
        if (
            public_ids != corresponding["publicSourceIds"]
            or local_ids != corresponding["localEvidenceIds"]
            or fields != corresponding["fieldAuthorizations"]
        ):
            _fail(UsageOrInputError, f"{binding_location} changed claim authority.")
        bindings.append({
            "claimId": claim_id,
            "publicSourceIds": public_ids,
            "localEvidenceIds": local_ids,
            "fieldAuthorizations": fields,
        })
    creator_relation = bounded_text(
        brief["creatorControlRelation"],
        f"{location}.creatorControlRelation",
        40,
    )
    if creator_relation not in {
        "Unestablished", "CreatorControlled", "CreatorAffected",
        "CreatorEncountered",
    }:
        _fail(UsageOrInputError, f"{location}.creatorControlRelation is unsupported.")
    quality_flags = [
        bounded_text(item, f"{location}.qualityFlags", 80)
        for item in _require_array(
            brief["qualityFlags"], f"{location}.qualityFlags", maximum=16
        )
    ]
    return {
        "policyVersion": EDITORIAL_BRIEF_POLICY_VERSION,
        "copyGoal": EDITORIAL_BRIEF_COPY_GOAL,
        "fingerprint": _require_sha256(
            brief["fingerprint"], f"{location}.fingerprint"
        ),
        "revisionKind": revision_kind,
        "canonicalIdentity": optional_text(
            brief["canonicalIdentity"], f"{location}.canonicalIdentity", 160
        ),
        "primaryGameplayBeat": optional_text(
            brief["primaryGameplayBeat"], f"{location}.primaryGameplayBeat", 600
        ),
        "leadIn": optional_text(brief["leadIn"], f"{location}.leadIn", 600),
        "visibleFollowThrough": optional_text(
            brief["visibleFollowThrough"],
            f"{location}.visibleFollowThrough",
            600,
        ),
        "safeCommentaryAngle": optional_text(
            brief["safeCommentaryAngle"],
            f"{location}.safeCommentaryAngle",
            600,
        ),
        "creatorControlRelation": creator_relation,
        "qualityFlags": quality_flags,
        "claims": claims,
        "sourceBindings": bindings,
    }
