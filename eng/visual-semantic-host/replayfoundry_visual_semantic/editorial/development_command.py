"""One-load exhaustive Prompt 2.3 Development execution."""
from __future__ import annotations

from typing import Any

from ..commands import *
from .attempts import attempt_editorial_set, not_run_editorial_set
from .protocol import (
    EDITORIAL_ADAPTER_VERSION,
    EDITORIAL_ATTEMPT_PLAN_SCHEMA,
    EDITORIAL_COMPLETED_BATCH_SCHEMA,
    EDITORIAL_COMPLETED_EXECUTION_SCHEMA,
    EDITORIAL_HOST_VERSION,
    validate_editorial_plan,
)
from .sampling_authorization import authorize_sampling


def _authorize_sampling(
    plan: dict[str, Any],
    prompt_text: str,
    torch: Any,
    torchcodec: Any,
    process_vision_info: Any,
) -> dict[str, Any]:
    primary = plan["_validated"]["sets"]["Primary"]
    baseline = plan["_validated"]["samplingBaseline"]
    return authorize_sampling(
        primary,
        baseline,
        prompt_text,
        torch,
        torchcodec,
        process_vision_info,
    )


def _attempt_payload(
    plan: dict[str, Any],
    sampling: dict[str, Any],
    primary: dict[str, Any],
    repeat: dict[str, Any],
    visual_only: dict[str, Any],
    peak_gpu_bytes: int | None,
    elapsed_seconds: float,
) -> dict[str, Any]:
    result = {
        "schemaVersion": EDITORIAL_ATTEMPT_PLAN_SCHEMA,
        "hostVersion": EDITORIAL_HOST_VERSION,
        "adapterVersion": EDITORIAL_ADAPTER_VERSION,
        "configurationLockCanonicalHash":
            plan["configurationLockCanonicalHash"],
        "samplingAuthorization": sampling,
        "primary": primary,
        "repeat": repeat,
        "visualOnly": visual_only,
        "peakAllocatedGpuBytes": peak_gpu_bytes,
        "totalElapsedSeconds": round(max(0.0, elapsed_seconds), 6),
    }
    result["canonicalHash"] = _canonical_json_sha256(result)
    return result


def _completed_set(attempt_set: dict[str, Any]) -> dict[str, Any]:
    rows = [
        {
            "caseId": row["caseId"],
            "candidateId": row["candidateId"],
            "caseOrdinal": row["caseOrdinal"],
            "runKind": row["runKind"],
            "observation": row["observation"],
            "canonicalizationAudit": row["canonicalizationAudit"],
            "requestBinding": row["requestBinding"],
            "generation": row["generation"],
            "executionTiming": row["executionTiming"],
            "sampling": row["sampling"],
            "elapsedSeconds": row["elapsedSeconds"],
            **(
                {
                    "structuredDecodingAudit":
                        row["structuredDecodingAudit"]
                }
                if "structuredDecodingAudit" in row
                else {}
            ),
        }
        for row in attempt_set["outcomes"]
        if row["status"] == "Succeeded"
    ]
    result = {
        "schemaVersion": (
            "visual-semantic-editorial-constrained-observation-batch-1.0"
            if any(
                "structuredDecodingAudit" in row
                for row in attempt_set["outcomes"]
            )
            else EDITORIAL_COMPLETED_BATCH_SCHEMA
        ),
        "runKind": attempt_set["runKind"],
        "caseCount": len(rows),
        "cases": rows,
    }
    result["canonicalHash"] = _canonical_json_sha256(result)
    return result


def _completed_payload(
    plan: dict[str, Any],
    attempt: dict[str, Any],
) -> dict[str, Any]:
    result = {
        "schemaVersion": EDITORIAL_COMPLETED_EXECUTION_SCHEMA,
        "hostVersion": EDITORIAL_HOST_VERSION,
        "adapterVersion": EDITORIAL_ADAPTER_VERSION,
        "configurationLockCanonicalHash":
            plan["configurationLockCanonicalHash"],
        "attemptCanonicalHash": attempt["canonicalHash"],
        "primary": _completed_set(attempt["primary"]),
        "repeat": _completed_set(attempt["repeat"]),
        "visualOnly": _completed_set(attempt["visualOnly"]),
    }
    result["canonicalHash"] = _canonical_json_sha256(result)
    return result


def run_editorial_development(
    model_path: Path,
    input_path: Path,
    output_path: Path,
    attempt_output_path: Path,
    ffmpeg_directory: Path,
    failure_output_path: Path | None,
) -> None:
    started = time.perf_counter()
    _set_failure_stage("InputLoading")
    plan = validate_editorial_plan(_load_strict_json(input_path))
    prompt_text = plan["_validated"]["promptText"]
    all_requests = [
        row
        for kind in ("Primary", "Repeat", "VisualOnly")
        for row in plan["_validated"]["sets"][kind]
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

    primary = attempt_editorial_set(
        "Primary",
        plan["_validated"]["sets"]["Primary"],
        prompt_text,
        model,
        processor,
        torch,
        torchcodec,
        process_vision_info,
    )
    if primary["failedCount"] == 0:
        repeat = attempt_editorial_set(
            "Repeat",
            plan["_validated"]["sets"]["Repeat"],
            prompt_text,
            model,
            processor,
            torch,
            torchcodec,
            process_vision_info,
        )
        visual_only = attempt_editorial_set(
            "VisualOnly",
            plan["_validated"]["sets"]["VisualOnly"],
            prompt_text,
            model,
            processor,
            torch,
            torchcodec,
            process_vision_info,
        )
    else:
        repeat = not_run_editorial_set(
            "Repeat",
            plan["_validated"]["sets"]["Repeat"],
            "NotRunPrimaryIncomplete",
        )
        visual_only = not_run_editorial_set(
            "VisualOnly",
            plan["_validated"]["sets"]["VisualOnly"],
            "NotRunPrimaryIncomplete",
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
        sampling,
        primary,
        repeat,
        visual_only,
        peak_gpu,
        time.perf_counter() - started,
    )
    _set_failure_stage("OutputWrite")
    _write_json_atomic(attempt_output_path, attempt)

    if primary["failedCount"] > 0:
        _set_failure_stage("AttemptCompletedWithCaseFailures")
        _fail(
            ProviderCaseFailuresDetected,
            "One or more Prompt 2.3 primary cases failed; all primary "
            "outcomes were retained.",
        )

    _write_json_atomic(output_path, _completed_payload(plan, attempt))


__all__ = [name for name in globals() if not name.startswith("__")]
