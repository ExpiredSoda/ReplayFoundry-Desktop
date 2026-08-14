"""One-load schema-constrained Prompt 2.3 Development execution."""
from __future__ import annotations

from typing import Any

from ..commands import *
from .attempts import attempt_editorial_set, not_run_editorial_set
from .constrained_pilot_command import validate_qualification_lock
from .development_command import _authorize_sampling, _completed_set
from .protocol import validate_editorial_plan
from .structured_decoding import StructuredDecodingSession, model_vocab_size
from .structured_decoding_policy import (
    ATTEMPT_SET_SCHEMA_VERSION,
    POLICY_VERSION,
    require_frozen_packages,
)

CONSTRAINED_DEVELOPMENT_ATTEMPT_SCHEMA = (
    "visual-semantic-editorial-constrained-development-attempt-1.0"
)
CONSTRAINED_DEVELOPMENT_COMPLETED_SCHEMA = (
    "visual-semantic-editorial-constrained-development-execution-1.0"
)
CONSTRAINED_DEVELOPMENT_HOST_VERSION = "0.8B.0"
CONSTRAINED_DEVELOPMENT_ADAPTER_VERSION = "0.8.0-research"


def _gpu_total_memory(torch: Any) -> int | None:
    if not torch.cuda.is_available():
        return None
    properties = torch.cuda.get_device_properties(0)
    value = getattr(properties, "total_memory", None)
    return int(value) if isinstance(value, int) and value >= 0 else None


def _constraint_schema_bindings(
    *sets: dict[str, Any],
) -> list[dict[str, Any]]:
    return [
        {
            "runKind": row["runKind"],
            "caseOrdinal": row["caseOrdinal"],
            "caseId": row["caseId"],
            "candidateId": row["candidateId"],
            "schemaSha256":
                row["structuredDecodingAudit"]["schemaSha256"],
        }
        for attempted_set in sets
        for row in attempted_set["outcomes"]
        if row["status"] != "NotRun"
    ]


def _attempt_payload(
    plan: dict[str, Any],
    lock: dict[str, Any],
    sampling: dict[str, Any],
    primary: dict[str, Any],
    repeat: dict[str, Any],
    visual_only: dict[str, Any],
    primary_elapsed: float,
    repeat_elapsed: float | None,
    visual_only_elapsed: float | None,
    peak_gpu_bytes: int | None,
    total_gpu_bytes: int | None,
    total_elapsed: float,
) -> dict[str, Any]:
    result = {
        "schemaVersion": CONSTRAINED_DEVELOPMENT_ATTEMPT_SCHEMA,
        "hostVersion": CONSTRAINED_DEVELOPMENT_HOST_VERSION,
        "adapterVersion": CONSTRAINED_DEVELOPMENT_ADAPTER_VERSION,
        "policyVersion": POLICY_VERSION,
        "configurationLockCanonicalHash":
            plan["configurationLockCanonicalHash"],
        "qualificationLockCanonicalHash": lock["canonicalHash"],
        "samplingAuthorization": sampling,
        "constraintSchemaBindings": _constraint_schema_bindings(
            primary,
            repeat,
            visual_only,
        ),
        "primary": primary,
        "repeat": repeat,
        "visualOnly": visual_only,
        "primaryElapsedSeconds": round(max(0.0, primary_elapsed), 6),
        "repeatElapsedSeconds": (
            None
            if repeat_elapsed is None
            else round(max(0.0, repeat_elapsed), 6)
        ),
        "visualOnlyElapsedSeconds": (
            None
            if visual_only_elapsed is None
            else round(max(0.0, visual_only_elapsed), 6)
        ),
        "peakAllocatedGpuBytes": peak_gpu_bytes,
        "gpuTotalMemoryBytes": total_gpu_bytes,
        "totalElapsedSeconds": round(max(0.0, total_elapsed), 6),
    }
    result["canonicalHash"] = _canonical_json_sha256(result)
    return result


def _completed_payload(
    plan: dict[str, Any],
    attempt: dict[str, Any],
) -> dict[str, Any]:
    result = {
        "schemaVersion": CONSTRAINED_DEVELOPMENT_COMPLETED_SCHEMA,
        "hostVersion": CONSTRAINED_DEVELOPMENT_HOST_VERSION,
        "adapterVersion": CONSTRAINED_DEVELOPMENT_ADAPTER_VERSION,
        "policyVersion": POLICY_VERSION,
        "configurationLockCanonicalHash":
            plan["configurationLockCanonicalHash"],
        "qualificationLockCanonicalHash":
            attempt["qualificationLockCanonicalHash"],
        "attemptCanonicalHash": attempt["canonicalHash"],
        "primary": _completed_set(attempt["primary"]),
        "repeat": _completed_set(attempt["repeat"]),
        "visualOnly": _completed_set(attempt["visualOnly"]),
    }
    result["canonicalHash"] = _canonical_json_sha256(result)
    return result


def run_editorial_constrained_development(
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
    plan = validate_editorial_plan(_load_strict_json(input_path))
    lock = validate_qualification_lock(
        _load_strict_json(qualification_lock_path)
    )
    require_frozen_packages()
    prompt_text = plan["_validated"]["promptText"]
    sets = plan["_validated"]["sets"]
    all_requests = [
        row
        for kind in ("Primary", "Repeat", "VisualOnly")
        for row in sets[kind]
    ]
    _validate_failure_output_against_media(
        failure_output_path,
        all_requests,
    )

    _set_failure_stage("RuntimeInitialization")
    torch, torchcodec, transformers, process_vision_info = _load_runtime(
        ffmpeg_directory
    )
    _validate_model_directory(model_path)
    sampling = _authorize_sampling(
        plan,
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

    primary_started = time.perf_counter()
    primary = attempt_editorial_set(
        "Primary",
        sets["Primary"],
        prompt_text,
        model,
        processor,
        torch,
        torchcodec,
        process_vision_info,
        session,
    )
    primary_elapsed = time.perf_counter() - primary_started
    repeat_elapsed = None
    visual_only_elapsed = None

    if primary["failedCount"] == 0:
        repeat_started = time.perf_counter()
        repeat = attempt_editorial_set(
            "Repeat",
            sets["Repeat"],
            prompt_text,
            model,
            processor,
            torch,
            torchcodec,
            process_vision_info,
            session,
        )
        repeat_elapsed = time.perf_counter() - repeat_started
        visual_only_started = time.perf_counter()
        visual_only = attempt_editorial_set(
            "VisualOnly",
            sets["VisualOnly"],
            prompt_text,
            model,
            processor,
            torch,
            torchcodec,
            process_vision_info,
            session,
        )
        visual_only_elapsed = time.perf_counter() - visual_only_started
    else:
        repeat = not_run_editorial_set(
            "Repeat",
            sets["Repeat"],
            "NotRunPrimaryIncomplete",
            ATTEMPT_SET_SCHEMA_VERSION,
        )
        visual_only = not_run_editorial_set(
            "VisualOnly",
            sets["VisualOnly"],
            "NotRunPrimaryIncomplete",
            ATTEMPT_SET_SCHEMA_VERSION,
        )

    _set_failure_stage("MediaRevalidation")
    _revalidate_media_inputs(all_requests)
    peak_gpu = (
        int(torch.cuda.max_memory_allocated(0))
        if torch.cuda.is_available()
        else None
    )
    attempt = _attempt_payload(
        plan,
        lock,
        sampling,
        primary,
        repeat,
        visual_only,
        primary_elapsed,
        repeat_elapsed,
        visual_only_elapsed,
        peak_gpu,
        _gpu_total_memory(torch),
        time.perf_counter() - started,
    )
    _set_failure_stage("OutputWrite")
    _write_json_atomic(attempt_output_path, attempt)

    if primary["failedCount"] > 0:
        _set_failure_stage("AttemptCompletedWithCaseFailures")
        _fail(
            ProviderCaseFailuresDetected,
            "One or more constrained Prompt 2.3 Primary cases failed; all "
            "30 outcomes were retained and no unconstrained retry occurred.",
        )

    _write_json_atomic(output_path, _completed_payload(plan, attempt))


__all__ = [name for name in globals() if not name.startswith("__")]
