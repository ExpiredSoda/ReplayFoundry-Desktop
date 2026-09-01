"""Exhaustive Prompt 2.3 case-attempt construction."""
from __future__ import annotations

from typing import Any

from ..generation import *
from .inference import infer_editorial_case
from .protocol import EDITORIAL_ATTEMPT_SET_SCHEMA
from .structured_decoding import StructuredDecodingSession
from .structured_decoding_policy import ATTEMPT_SET_SCHEMA_VERSION


def _case_identity(
    request: dict[str, Any],
    ordinal: int,
    run_kind: str,
) -> dict[str, Any]:
    return {
        "caseId": request["caseId"],
        "candidateId": request["candidate"]["id"],
        "caseOrdinal": ordinal,
        "runKind": run_kind,
    }


def _success_outcome(
    request: dict[str, Any],
    ordinal: int,
    run_kind: str,
    result: dict[str, Any],
) -> dict[str, Any]:
    outcome = {
        **_case_identity(request, ordinal, run_kind),
        "status": "Succeeded",
        "stage": "Completed",
        "observation": result["observation"],
        "canonicalizationAudit": result["canonicalizationAudit"],
        "requestBinding": result["requestBinding"],
        "generation": result["generation"],
        "executionTiming": result["executionTiming"],
        "sampling": result["sampling"],
        "elapsedSeconds": result["elapsedSeconds"],
        "failure": None,
        "notRunReason": None,
    }
    structured = result.get("structuredDecodingAudit")
    if structured is not None:
        outcome["structuredDecodingAudit"] = structured
    return outcome


def _failure_outcome(
    request: dict[str, Any],
    ordinal: int,
    run_kind: str,
    error: HostError,
    elapsed_seconds: float,
) -> dict[str, Any]:
    context = copy.deepcopy(_FAILURE_CONTEXT)
    generation = context.get("caseGeneration")
    raw_hash = context.get("providerOutput", {}).get(
        "rawGeneratedTextSha256"
    )
    outcome = {
        **_case_identity(request, ordinal, run_kind),
        "status": "Failed",
        "stage": context.get("stage") or "Inference",
        "observation": None,
        "canonicalizationAudit": None,
        "requestBinding": None,
        "generation": generation,
        "executionTiming": context.get("executionTiming"),
        "sampling": context.get("sampling"),
        "elapsedSeconds": round(max(0.0, elapsed_seconds), 6),
        "failure": {
            "errorCode": type(error).__name__,
            "stage": context.get("stage") or "Inference",
            "message": _bounded_failure_message(error),
            "rawGeneratedTextSha256": raw_hash,
        },
        "notRunReason": None,
    }
    structured = context.get("structuredDecodingAudit")
    if structured is not None:
        outcome["structuredDecodingAudit"] = structured
    return outcome


def _not_run_outcome(
    request: dict[str, Any],
    ordinal: int,
    run_kind: str,
    reason: str,
) -> dict[str, Any]:
    return {
        **_case_identity(request, ordinal, run_kind),
        "status": "NotRun",
        "stage": "NotStarted",
        "observation": None,
        "canonicalizationAudit": None,
        "requestBinding": None,
        "generation": None,
        "executionTiming": None,
        "sampling": None,
        "elapsedSeconds": None,
        "failure": None,
        "notRunReason": reason,
    }


def _attempt_set(
    run_kind: str,
    outcomes: list[dict[str, Any]],
    schema_version: str = EDITORIAL_ATTEMPT_SET_SCHEMA,
) -> dict[str, Any]:
    result = {
        "schemaVersion": schema_version,
        "runKind": run_kind,
        "requestedCount": len(outcomes),
        "succeededCount": sum(
            row["status"] == "Succeeded" for row in outcomes
        ),
        "failedCount": sum(row["status"] == "Failed" for row in outcomes),
        "notRunCount": sum(row["status"] == "NotRun" for row in outcomes),
        "outcomes": outcomes,
    }
    result["canonicalHash"] = _canonical_json_sha256(result)
    return result


def attempt_editorial_set(
    run_kind: str,
    requests: list[dict[str, Any]],
    prompt_text: str,
    model: Any,
    processor: Any,
    torch: Any,
    torchcodec: Any,
    process_vision_info: Any,
    structured_decoding_session: StructuredDecodingSession | None = None,
    cache_implementation: str | None = None,
) -> dict[str, Any]:
    """Attempt every case-local failure while preserving stable ordering."""
    outcomes: list[dict[str, Any]] = []

    for index, request in enumerate(requests):
        ordinal = index + 1
        _set_failure_case(
            request,
            ordinal,
            request["caseHash"],
        )
        started = time.perf_counter()
        try:
            result = infer_editorial_case(
                request,
                ordinal,
                run_kind,
                prompt_text,
                model,
                processor,
                torch,
                torchcodec,
                process_vision_info,
                structured_decoding_session,
                cache_implementation,
            )
            outcomes.append(
                _success_outcome(request, ordinal, run_kind, result)
            )
        except (
            InferenceError,
            GenerationTokenBudgetExceededError,
            UnexpectedGenerationTerminationError,
        ) as error:
            outcomes.append(
                _failure_outcome(
                    request,
                    ordinal,
                    run_kind,
                    error,
                    time.perf_counter() - started,
                )
            )
        finally:
            gc.collect()
            try:
                torch.cuda.empty_cache()
            except Exception:
                pass

    _clear_failure_case()
    return _attempt_set(
        run_kind,
        outcomes,
        ATTEMPT_SET_SCHEMA_VERSION
        if structured_decoding_session is not None
        else EDITORIAL_ATTEMPT_SET_SCHEMA,
    )


def not_run_editorial_set(
    run_kind: str,
    requests: list[dict[str, Any]],
    reason: str,
    schema_version: str = EDITORIAL_ATTEMPT_SET_SCHEMA,
) -> dict[str, Any]:
    return _attempt_set(
        run_kind,
        [
            _not_run_outcome(request, index + 1, run_kind, reason)
            for index, request in enumerate(requests)
        ],
        schema_version,
    )


__all__ = [name for name in globals() if not name.startswith("__")]
