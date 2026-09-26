"""Validation for the production structured-decoding qualification lock."""
from __future__ import annotations

import copy
import sys
from pathlib import Path
from typing import Any

from ..canonical_json import (
    _canonical_json_sha256,
    _require_exact_keys,
    _require_sha256,
    _require_utc_timestamp,
    _sha256_file,
)
from .structured_decoding_policy import (
    StructuredDecodingUnavailableError,
    require_frozen_lock,
)

QUALIFICATION_LOCK_SCHEMA = (
    "visual-semantic-editorial-structured-decoding-lock-1.0"
)


def validate_qualification_lock(value: Any) -> dict[str, Any]:
    lock = require_frozen_lock(value)
    _require_exact_keys(
        lock,
        {
            "schemaVersion",
            "policyVersion",
            "backendName",
            "backendVersion",
            "representation",
            "cudaMaskBackend",
            "constraintSchemaVersion",
            "constraintSchemaSha256",
            "environmentCanonicalHash",
            "pythonExecutableSha256",
            "capabilityCanonicalHash",
            "configurationLockCanonicalHash",
            "promptSha256",
            "promptFileSha256",
            "modelManifestSha256",
            "unconstrainedFallbackPermitted",
            "semanticRepairPermitted",
            "capabilitySucceeded",
            "lockedAtUtc",
            "canonicalHash",
        },
        "$",
    )
    if (
        lock["schemaVersion"] != QUALIFICATION_LOCK_SCHEMA
        or lock["capabilitySucceeded"] is not True
        or lock["pythonExecutableSha256"]
        != _sha256_file(Path(sys.executable))
    ):
        raise StructuredDecodingUnavailableError(
            "Structured-decoding qualification lock does not authorize "
            "this exact Python environment."
        )
    supplied = _require_sha256(lock["canonicalHash"], "$.canonicalHash")
    identity = copy.deepcopy(lock)
    identity.pop("canonicalHash")
    if supplied != _canonical_json_sha256(identity):
        raise StructuredDecodingUnavailableError(
            "Structured-decoding qualification lock hash is invalid."
        )
    _require_utc_timestamp(lock["lockedAtUtc"], "$.lockedAtUtc")
    return lock


__all__ = ["QUALIFICATION_LOCK_SCHEMA", "validate_qualification_lock"]
