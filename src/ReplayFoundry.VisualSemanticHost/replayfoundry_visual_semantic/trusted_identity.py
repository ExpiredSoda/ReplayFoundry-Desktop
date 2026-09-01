"""ReplayFoundry local visual-semantic host implementation module."""
from __future__ import annotations

from .output_normalization import *  # noqa: F401,F403

def _bind_trusted_identity(
    provider_payload: dict[str, Any],
    request: dict[str, Any],
    case_ordinal: int,
    *,
    canonical_provider_payload: dict[str, Any] | None = None,
) -> tuple[dict[str, Any], dict[str, Any]]:
    if (
        isinstance(case_ordinal, bool)
        or not isinstance(case_ordinal, int)
        or case_ordinal <= 0
    ):
        _fail(
            OutputError,
            "Trusted identity binding requires a positive stable case ordinal.",
        )

    provider_case_id = _require_id(
        provider_payload["caseId"],
        "provider observation.caseId",
    )
    provider_candidate_id = _require_id(
        provider_payload["candidateId"],
        "provider observation.candidateId",
    )
    trusted_case_id = request["caseId"]
    trusted_candidate_id = request["candidate"]["id"]

    bound_payload = dict(provider_payload)
    bound_payload["caseId"] = trusted_case_id
    bound_payload["candidateId"] = trusted_candidate_id

    # Identity binding remains the first mutation of the validated provider
    # payload. The audit hashes use the canonical collection projection so
    # the .NET boundary can reconstruct both hashes from ordinary output even
    # when the separate normalization audit records a raw collection form.
    audit_provider_payload = (
        provider_payload
        if canonical_provider_payload is None
        else canonical_provider_payload
    )
    if (
        audit_provider_payload.get("caseId") != provider_case_id
        or audit_provider_payload.get("candidateId")
        != provider_candidate_id
    ):
        _fail(
            OutputError,
            "Trusted identity-binding audit projection changed provider "
            "identity.",
        )
    provider_hash = _canonical_json_sha256(audit_provider_payload)
    audit_bound_payload = dict(audit_provider_payload)
    audit_bound_payload["caseId"] = trusted_case_id
    audit_bound_payload["candidateId"] = trusted_candidate_id
    bound_hash = _canonical_json_sha256(audit_bound_payload)

    case_match = provider_case_id == trusted_case_id
    candidate_match = provider_candidate_id == trusted_candidate_id
    if (case_match and candidate_match) != (provider_hash == bound_hash):
        _fail(
            OutputError,
            "Trusted identity-binding hashes are inconsistent with echo "
            "equality.",
        )

    audit = {
        "policyVersion": IDENTITY_BINDING_POLICY_VERSION,
        "policySha256": IDENTITY_BINDING_POLICY_SHA256,
        "source": "HostRequest",
        "caseOrdinal": case_ordinal,
        "trustedCaseId": trusted_case_id,
        "trustedCandidateId": trusted_candidate_id,
        "providerEchoCaseId": provider_case_id,
        "providerEchoCandidateId": provider_candidate_id,
        "caseEchoMatched": case_match,
        "candidateEchoMatched": candidate_match,
        "providerPayloadSha256": provider_hash,
        "trustedBoundPayloadSha256": bound_hash,
        "boundAtUtc": (
            datetime.now(timezone.utc)
            .isoformat(timespec="microseconds")
            .replace("+00:00", "Z")
        ),
    }
    return bound_payload, audit



__all__ = [name for name in globals() if not name.startswith("__")]
