"""One-load label-blind Prompt 2.3 contract pilot."""
from __future__ import annotations

from ..commands import *
from .attempts import attempt_editorial_set
from .development_command import _completed_set
from .pilot_protocol import (
    PILOT_ATTEMPT_SCHEMA,
    PILOT_COMPLETED_SCHEMA,
    PILOT_POLICY_VERSION,
    validate_pilot_plan,
)
from .protocol import EDITORIAL_ADAPTER_VERSION, EDITORIAL_HOST_VERSION
from .sampling_authorization import authorize_sampling


def run_editorial_contract_pilot(
    model_path: Path,
    input_path: Path,
    output_path: Path,
    attempt_output_path: Path,
    ffmpeg_directory: Path,
    failure_output_path: Path | None,
) -> None:
    started = time.perf_counter()
    _set_failure_stage("InputLoading")
    plan = validate_pilot_plan(_load_strict_json(input_path))
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
    primary = attempt_editorial_set(
        "Primary",
        requests,
        prompt_text,
        model,
        processor,
        torch,
        torchcodec,
        process_vision_info,
    )
    _set_failure_stage("MediaRevalidation")
    _revalidate_media_inputs(requests)
    peak_gpu = (
        int(torch.cuda.max_memory_allocated(0))
        if torch.cuda.is_available()
        else None
    )
    attempt = {
        "schemaVersion": PILOT_ATTEMPT_SCHEMA,
        "hostVersion": EDITORIAL_HOST_VERSION,
        "adapterVersion": EDITORIAL_ADAPTER_VERSION,
        "policyVersion": PILOT_POLICY_VERSION,
        "phase": plan["phase"],
        "configurationLockCanonicalHash":
            plan["configurationLockCanonicalHash"],
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
            "One or more Prompt 2.3 pilot cases failed; every pilot "
            "outcome was retained.",
        )

    completed = {
        "schemaVersion": PILOT_COMPLETED_SCHEMA,
        "hostVersion": EDITORIAL_HOST_VERSION,
        "adapterVersion": EDITORIAL_ADAPTER_VERSION,
        "policyVersion": PILOT_POLICY_VERSION,
        "phase": plan["phase"],
        "configurationLockCanonicalHash":
            plan["configurationLockCanonicalHash"],
        "attemptCanonicalHash": attempt["canonicalHash"],
        "primary": _completed_set(primary),
    }
    completed["canonicalHash"] = _canonical_json_sha256(completed)
    _set_failure_stage("OutputWrite")
    _write_json_atomic(output_path, completed)


__all__ = [name for name in globals() if not name.startswith("__")]
