"""One-load schema-constrained Prompt 2.3 contract pilot."""
from __future__ import annotations

import sys
from pathlib import Path

from ..commands import *
from .attempts import attempt_editorial_set
from .development_command import _completed_set
from .pilot_protocol import validate_pilot_plan
from .protocol import EDITORIAL_ADAPTER_VERSION, EDITORIAL_HOST_VERSION
from .sampling_authorization import authorize_sampling
from .structured_decoding import StructuredDecodingSession, model_vocab_size
from .structured_decoding_capability import QUALIFICATION_LOCK_SCHEMA
from .structured_decoding_policy import (
    POLICY_VERSION,
    StructuredDecodingUnavailableError,
    require_frozen_lock,
    require_frozen_packages,
)

CONSTRAINED_ATTEMPT_SCHEMA = (
    "visual-semantic-editorial-constrained-pilot-attempt-1.0"
)
CONSTRAINED_COMPLETED_SCHEMA = (
    "visual-semantic-editorial-constrained-pilot-completed-1.0"
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


def run_editorial_constrained_contract_pilot(
    model_path: Path,
    input_path: Path,
    output_path: Path,
    attempt_output_path: Path,
    qualification_lock_path: Path,
    ffmpeg_directory: Path,
    failure_output_path: Path | None,
) -> None:
    started = time.perf_counter()
    _set_failure_stage("InputLoading")
    plan = validate_pilot_plan(_load_strict_json(input_path))
    lock = validate_qualification_lock(
        _load_strict_json(qualification_lock_path)
    )
    require_frozen_packages()
    prompt_text = plan["_validated"]["promptText"]
    requests = plan["_validated"]["requests"]
    _validate_failure_output_against_media(
        failure_output_path,
        requests,
    )

    _set_failure_stage("RuntimeInitialization")
    torch, torchcodec, transformers, process_vision_info = _load_runtime(
        ffmpeg_directory
    )
    _validate_model_directory(model_path)
    sampling = authorize_sampling(
        requests,
        plan["_validated"]["samplingBaseline"],
        prompt_text,
        torch,
        torchcodec,
        process_vision_info,
    )
    model, processor = _load_model_and_processor(
        model_path,
        torch,
        transformers,
    )
    session = StructuredDecodingSession(
        processor.tokenizer,
        model_vocab_size(model),
    )
    primary = attempt_editorial_set(
        "Primary",
        requests,
        prompt_text,
        model,
        processor,
        torch,
        torchcodec,
        process_vision_info,
        session,
    )
    _set_failure_stage("MediaRevalidation")
    _revalidate_media_inputs(requests)
    peak_gpu = (
        int(torch.cuda.max_memory_allocated(0))
        if torch.cuda.is_available()
        else None
    )
    attempt = {
        "schemaVersion": CONSTRAINED_ATTEMPT_SCHEMA,
        "hostVersion": EDITORIAL_HOST_VERSION,
        "adapterVersion": EDITORIAL_ADAPTER_VERSION,
        "policyVersion": POLICY_VERSION,
        "phase": plan["phase"],
        "configurationLockCanonicalHash":
            plan["configurationLockCanonicalHash"],
        "qualificationLockCanonicalHash": lock["canonicalHash"],
        "samplingAuthorization": sampling,
        "primary": primary,
        "peakAllocatedGpuBytes": peak_gpu,
        "totalElapsedSeconds":
            round(max(0.0, time.perf_counter() - started), 6),
    }
    attempt["canonicalHash"] = _canonical_json_sha256(attempt)
    _set_failure_stage("OutputWrite")
    _write_json_atomic(attempt_output_path, attempt)

    if primary["failedCount"] > 0:
        _set_failure_stage("AttemptCompletedWithCaseFailures")
        _fail(
            ProviderCaseFailuresDetected,
            "One or more constrained Prompt 2.3 cases failed; every "
            "case outcome was retained. No unconstrained retry occurred.",
        )

    completed = {
        "schemaVersion": CONSTRAINED_COMPLETED_SCHEMA,
        "hostVersion": EDITORIAL_HOST_VERSION,
        "adapterVersion": EDITORIAL_ADAPTER_VERSION,
        "policyVersion": POLICY_VERSION,
        "phase": plan["phase"],
        "configurationLockCanonicalHash":
            plan["configurationLockCanonicalHash"],
        "qualificationLockCanonicalHash": lock["canonicalHash"],
        "attemptCanonicalHash": attempt["canonicalHash"],
        "primary": _completed_set(primary),
    }
    completed["canonicalHash"] = _canonical_json_sha256(completed)
    _set_failure_stage("OutputWrite")
    _write_json_atomic(output_path, completed)


__all__ = [name for name in globals() if not name.startswith("__")]
