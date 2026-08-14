"""ReplayFoundry local visual-semantic host implementation module."""
from __future__ import annotations

from .generation import *  # noqa: F401,F403

def _attempt_failure_stage(stage: str) -> str:
    allowed = {
        "VideoSampling",
        "Generation",
        "OutputSafety",
        "OutputValidation",
        "MediaRevalidation",
    }
    return stage if stage in allowed else "Unknown"


def _is_case_local_provider_failure(error: Exception) -> bool:
    if isinstance(
        error,
        (
            GenerationTokenBudgetExceededError,
            UnexpectedGenerationTerminationError,
        ),
    ):
        return True
    return (
        isinstance(error, InferenceError)
        and _FAILURE_CONTEXT["stage"]
        in {"OutputSafety", "OutputValidation"}
    )


def _provider_case_failure(
    request: dict[str, Any],
    case_ordinal: int,
    error: Exception,
    generation_case: dict[str, Any] | None = None,
    execution_timing: dict[str, Any] | None = None,
    elapsed_seconds: float | None = None,
) -> dict[str, Any]:
    partial = _FAILURE_CONTEXT.get("providerOutput", {})
    provider_echo_case_id = None
    provider_echo_candidate_id = None
    raw_hash = None
    if isinstance(partial, dict):
        provider_echo_case_id = partial.get("providerEchoCaseId")
        provider_echo_candidate_id = partial.get(
            "providerEchoCandidateId"
        )
        raw_hash = partial.get("rawGeneratedTextSha256")

    if generation_case is None:
        value = _FAILURE_CONTEXT.get("caseGeneration")
        if isinstance(value, dict):
            generation_case = copy.deepcopy(value)
    if execution_timing is None:
        value = _FAILURE_CONTEXT.get("executionTiming")
        if isinstance(value, dict):
            execution_timing = copy.deepcopy(value)

    return {
        "caseId": request["caseId"],
        "candidateId": request["candidate"]["id"],
        "caseOrdinal": case_ordinal,
        "status": "Failed",
        "stage": _attempt_failure_stage(_FAILURE_CONTEXT["stage"]),
        "observation": None,
        "identityBindingAudit": None,
        "normalizationAudit": None,
        "generation": generation_case,
        "executionTiming": execution_timing,
        "elapsedSeconds": (
            None
            if elapsed_seconds is None
            else round(max(0.0, elapsed_seconds), 6)
        ),
        "failure": {
            "errorCode": type(error).__name__,
            "message": _bounded_failure_message(error),
            "rawGeneratedTextSha256": raw_hash,
            "providerEchoCaseId": provider_echo_case_id,
            "providerEchoCandidateId": provider_echo_candidate_id,
        },
    }


def _provider_case_success(
    request: dict[str, Any],
    case_ordinal: int,
    observation: dict[str, Any],
    elapsed: float,
    generation_case: dict[str, Any],
    execution_timing: dict[str, Any],
) -> dict[str, Any]:
    identity_audit = observation.get("identityBindingAudit")
    trusted_case_id = request["caseId"]
    trusted_candidate_id = request["candidate"]["id"]
    if (
        not isinstance(identity_audit, dict)
        or observation.get("caseId") != trusted_case_id
        or observation.get("candidateId") != trusted_candidate_id
        or identity_audit.get("trustedCaseId") != trusted_case_id
        or identity_audit.get("trustedCandidateId")
            != trusted_candidate_id
        or identity_audit.get("caseOrdinal") != case_ordinal
        or not isinstance(generation_case, dict)
        or not isinstance(execution_timing, dict)
        or not math.isfinite(elapsed)
        or elapsed < 0
    ):
        _fail(
            OutputError,
            "Successful provider attempt is not bound to its trusted request.",
        )
    return {
        "caseId": trusted_case_id,
        "candidateId": trusted_candidate_id,
        "caseOrdinal": case_ordinal,
        "status": "Succeeded",
        "stage": "Completed",
        "observation": observation,
        "identityBindingAudit": identity_audit,
        "normalizationAudit": observation.get("normalizationAudit"),
        "generation": generation_case,
        "executionTiming": execution_timing,
        "elapsedSeconds": round(elapsed, 6),
        "failure": None,
    }


def _attempt_batch_payload(
    outcomes: list[dict[str, Any]],
    peak_allocated_gpu_bytes: int,
    total_elapsed_seconds: float,
) -> dict[str, Any]:
    if not outcomes:
        _fail(OutputError, "Provider-attempt batch requires at least one case.")
    ordinals = [item.get("caseOrdinal") for item in outcomes]
    if ordinals != list(range(1, len(outcomes) + 1)):
        _fail(
            OutputError,
            "Provider-attempt outcomes changed stable request order.",
        )
    if len({item.get("caseId") for item in outcomes}) != len(outcomes):
        _fail(OutputError, "Provider-attempt batch has duplicate case IDs.")
    if len({item.get("candidateId") for item in outcomes}) != len(outcomes):
        _fail(
            OutputError,
            "Provider-attempt batch has duplicate candidate IDs.",
        )
    if (
        isinstance(peak_allocated_gpu_bytes, bool)
        or not isinstance(peak_allocated_gpu_bytes, int)
        or peak_allocated_gpu_bytes < 0
        or not math.isfinite(total_elapsed_seconds)
        or total_elapsed_seconds < 0
    ):
        _fail(
            OutputError,
            "Provider-attempt batch has invalid memory or timing telemetry.",
        )

    success_count = 0
    for outcome in outcomes:
        status = outcome.get("status")
        if status == "Succeeded":
            success_count += 1
            if (
                outcome.get("stage") != "Completed"
                or not isinstance(outcome.get("observation"), dict)
                or not isinstance(
                    outcome.get("identityBindingAudit"),
                    dict,
                )
                or not isinstance(outcome.get("generation"), dict)
                or not isinstance(outcome.get("executionTiming"), dict)
                or outcome.get("elapsedSeconds") is None
                or outcome.get("failure") is not None
            ):
                _fail(
                    OutputError,
                    "Successful provider-attempt outcome is incomplete.",
                )
        elif status == "Failed":
            if (
                outcome.get("stage") == "Completed"
                or outcome.get("observation") is not None
                or outcome.get("identityBindingAudit") is not None
                or outcome.get("normalizationAudit") is not None
                or not isinstance(outcome.get("failure"), dict)
            ):
                _fail(
                    OutputError,
                    "Failed provider-attempt outcome is inconsistent.",
                )
        else:
            _fail(
                OutputError,
                "Provider-attempt outcome has unsupported status.",
            )

    payload = {
        "schemaVersion": ATTEMPT_SCHEMA,
        "hostVersion": HOST_VERSION,
        "modelRepository": MODEL_REPOSITORY,
        "modelRevision": MODEL_REVISION,
        "device": DEVICE,
        "backend": BACKEND,
        "requestCount": len(outcomes),
        "successCount": success_count,
        "failureCount": len(outcomes) - success_count,
        "peakAllocatedGpuBytes": peak_allocated_gpu_bytes,
        "totalElapsedSeconds": round(total_elapsed_seconds, 6),
        "outcomes": outcomes,
    }
    payload["canonicalAttemptSha256"] = _canonical_json_sha256(payload)
    return payload



__all__ = [name for name in globals() if not name.startswith("__")]
