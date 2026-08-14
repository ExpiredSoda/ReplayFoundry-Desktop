"""Source and pass attestations for grounded metadata synthesis."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
from typing import Any

from ..commands import _add_failure_diagnostic
from .grounded_metadata_pipeline_contract import (
    GROUNDED_METADATA_MODULE_FILES,
    _anchor_sha256,
)

_UNCHANGED_ATTESTATION_VALUE = object()


def _grounded_metadata_module_identities() -> list[dict[str, str]]:
    """Bind the exact current Python modules without copying source content."""
    module_directory = Path(__file__).resolve().parent
    identities: list[dict[str, str]] = []
    local_diagnostic: list[dict[str, str]] = []
    for module_name, file_name in GROUNDED_METADATA_MODULE_FILES:
        module_path = module_directory / file_name
        sha256 = hashlib.sha256(module_path.read_bytes()).hexdigest()
        identities.append(
            {
                "moduleName": module_name,
                "fileName": file_name,
                "sha256": sha256,
            }
        )
        local_diagnostic.append(
            {
                "moduleName": module_name,
                "localPath": str(module_path),
                "sha256": sha256,
            }
        )
    _add_failure_diagnostic(
        "Grounded metadata module identities "
        + json.dumps(local_diagnostic, sort_keys=True, separators=(",", ":"))
    )
    return identities


def _synthesis_attestation_context(
    logical_pass_ordinal: int,
    candidate_ordinal: int | None,
    decoding: str,
    seed: int,
    source_pass_ordinal: int | None,
    source_rejected_json_sha256: str | None,
    source_selection_reason: str | None,
    retry_anchor_applied: bool,
    retry_anchor_disabled_reason: str | None,
    retry_anchor_envelope_sha256: str | None,
    retry_anchor_authority_sha256: str | None,
) -> dict[str, Any]:
    return {
        "logicalPassOrdinal": logical_pass_ordinal,
        "candidateOrdinal": candidate_ordinal,
        "decoding": decoding,
        "seed": seed,
        "sourcePassOrdinal": source_pass_ordinal,
        "sourceRejectedJsonSha256": source_rejected_json_sha256,
        "sourceSelectionReason": source_selection_reason,
        "retryAnchorCaptured": False,
        "retryAnchorApplied": retry_anchor_applied,
        "retryAnchorDisabledReason": retry_anchor_disabled_reason,
        "retryAnchorEnvelopeSha256": retry_anchor_envelope_sha256,
        "retryAnchorAuthoritySha256": retry_anchor_authority_sha256,
    }


def _require_synthesis_attestation(
    value: Any,
    context: dict[str, Any],
) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise AssertionError("Grounded synthesis generation omitted its attestation.")
    for name, expected in context.items():
        if value.get(name) != expected:
            raise AssertionError(
                f"Grounded synthesis attestation changed {name}."
            )
    return dict(value)


def _is_sha256(value: Any) -> bool:
    return (
        isinstance(value, str)
        and len(value) == 64
        and all(character in "0123456789abcdef" for character in value.lower())
    )


def _require_complete_pool_candidate_attestation(
    attestation: dict[str, Any],
    completed_json: Any,
) -> str:
    """Require every witness before a semantic rejection may advance."""
    if not isinstance(completed_json, str) or not completed_json:
        raise AssertionError(
            "Recovery-pool semantic continuation omitted canonical JSON."
        )
    completed_sha256 = hashlib.sha256(
        completed_json.encode("utf-8")
    ).hexdigest()
    for name in (
        "canonicalMessagesSha256",
        "renderedPromptSha256",
        "inputTokenIdsSha256",
        "outputSha256",
        "completedJsonSha256",
    ):
        if not _is_sha256(attestation.get(name)):
            raise AssertionError(
                f"Recovery-pool semantic continuation omitted {name}."
            )
    if attestation["completedJsonSha256"] != completed_sha256:
        raise AssertionError(
            "Recovery-pool canonical JSON did not match its attestation."
        )
    if (
        not isinstance(attestation.get("renderedPromptUtf8ByteCount"), int)
        or attestation["renderedPromptUtf8ByteCount"] <= 0
        or not isinstance(attestation.get("inputTokenCount"), int)
        or attestation["inputTokenCount"] <= 0
    ):
        raise AssertionError(
            "Recovery-pool semantic continuation had incomplete prompt or token counts."
        )
    return completed_sha256


def _finish_synthesis_attestation(
    attestation: dict[str, Any],
    *,
    rejection_code: str | None,
    accepted: bool,
    retry_anchor_captured: bool = False,
    retry_anchor_disabled_reason: str | None | object =
        _UNCHANGED_ATTESTATION_VALUE,
    retry_anchor_envelope_sha256: str | None = None,
    retry_anchor_authority_sha256: str | None = None,
) -> dict[str, Any]:
    finished = dict(attestation)
    finished["rejectionCode"] = rejection_code
    finished["accepted"] = accepted
    finished["retryAnchorCaptured"] = retry_anchor_captured
    if retry_anchor_disabled_reason is not _UNCHANGED_ATTESTATION_VALUE:
        finished["retryAnchorDisabledReason"] = retry_anchor_disabled_reason
    if retry_anchor_envelope_sha256 is not None:
        finished["retryAnchorEnvelopeSha256"] = retry_anchor_envelope_sha256
    if retry_anchor_authority_sha256 is not None:
        finished["retryAnchorAuthoritySha256"] = retry_anchor_authority_sha256
    _add_failure_diagnostic(
        "Grounded synthesis pass attestation "
        + json.dumps(finished, sort_keys=True, separators=(",", ":"))
    )
    return finished


def _requires_primary_only_synthesis_evidence(code: str) -> bool:
    """Return the sole validation failure that narrows retry evidence."""
    return code == "CrossDraftTitleContamination"
