"""ReplayFoundry local visual-semantic host implementation module."""
from __future__ import annotations

from .errors import *  # noqa: F401,F403

def _empty_failure_sampling() -> dict[str, Any]:
    return {
        "backend": VIDEO_BACKEND,
        "sourceAverageFramesPerSecond": None,
        "frameIndices": None,
        "inferredTimestampsSeconds": None,
        "actualPtsSeconds": None,
        "actualFrameDurationsSeconds": None,
        "frameCount": None,
        "candidateIntersectingFrameCount": None,
    }


def _empty_failure_identity() -> dict[str, str | None]:
    return {
        "inputBatchSha256": None,
        "inputCaseSha256": None,
        "modelManifestSha256": None,
        "environmentSha256": None,
        "promptSha256": PROMPT_SHA256,
    }


def _empty_case_audit_sections() -> dict[str, Any]:
    return {
        "qwenMetadata": None,
        "directTorchCodecMetadata": None,
        "comparison": None,
        "candidateVisibility": None,
        "reviewCoverage": None,
        "legacyValidation": None,
    }


def _new_failure_context(command: str | None = None) -> dict[str, Any]:
    return {
        "command": command,
        "stage": "ArgumentValidation",
        "case": None,
        "videoArtifact": None,
        "timing": None,
        "sampling": _empty_failure_sampling(),
        "identity": _empty_failure_identity(),
        "caseAuditSections": _empty_case_audit_sections(),
        "generation": None,
        "generationWatchdog": None,
        "groundedMemoryPolicy": None,
        "caseGeneration": None,
        "executionTiming": None,
        "structuredDecodingAudit": None,
        "providerOutput": {},
        "recoveryPoolLedger": [],
        "diagnostics": [],
        "failureOutputApproved": False,
    }


_FAILURE_CONTEXT = _new_failure_context()


def _reset_failure_context(command: str | None = None) -> None:
    global _FAILURE_CONTEXT
    _FAILURE_CONTEXT.clear()
    _FAILURE_CONTEXT.update(_new_failure_context(command))


def _set_failure_stage(stage: str) -> None:
    if stage not in FAILURE_STAGES:
        raise ValueError(f"Unsupported failure stage: {stage}")
    _FAILURE_CONTEXT["stage"] = stage


def _set_failure_identity(**values: str | None) -> None:
    identity = _FAILURE_CONTEXT["identity"]
    for key, value in values.items():
        if key not in identity:
            raise ValueError(f"Unsupported failure identity field: {key}")
        identity[key] = value


def _set_failure_case(
    request: dict[str, Any],
    case_ordinal: int,
    input_case_sha256: str,
) -> None:
    validated = request["_validated"]
    review_start = float(validated["sourceAbsoluteOffset"])
    review_duration = float(validated["videoDuration"])
    review_end = review_start + review_duration
    candidate_relative_start = float(validated["candidateStart"])
    candidate_relative_end = float(validated["candidateEnd"])
    _FAILURE_CONTEXT["case"] = {
        "caseId": request["caseId"],
        "candidateId": request["candidate"]["id"],
        "caseOrdinal": case_ordinal,
    }
    _FAILURE_CONTEXT["videoArtifact"] = {
        "sha256": validated["expectedVideoHash"],
        "byteLength": validated["expectedVideoLength"],
        "reviewDurationSeconds": review_duration,
    }
    _FAILURE_CONTEXT["timing"] = {
        "sourceAbsoluteOffsetSeconds": review_start,
        "reviewStartSeconds": review_start,
        "reviewEndSeconds": review_end,
        "candidateRelativeStartSeconds": candidate_relative_start,
        "candidateRelativeEndSeconds": candidate_relative_end,
        "candidateAbsoluteStartSeconds":
            review_start + candidate_relative_start,
        "candidateAbsoluteEndSeconds":
            review_start + candidate_relative_end,
    }
    _FAILURE_CONTEXT["sampling"] = _empty_failure_sampling()
    _FAILURE_CONTEXT["caseAuditSections"] = _empty_case_audit_sections()
    _FAILURE_CONTEXT["generation"] = None
    _FAILURE_CONTEXT["generationWatchdog"] = None
    _FAILURE_CONTEXT["caseGeneration"] = None
    _FAILURE_CONTEXT["executionTiming"] = None
    _FAILURE_CONTEXT["structuredDecodingAudit"] = None
    _FAILURE_CONTEXT["providerOutput"] = {}
    _FAILURE_CONTEXT["recoveryPoolLedger"] = []
    _set_failure_identity(inputCaseSha256=input_case_sha256)


def _set_failure_sampling(**values: Any) -> None:
    sampling = _FAILURE_CONTEXT["sampling"]
    for key, value in values.items():
        if key not in sampling:
            raise ValueError(f"Unsupported failure sampling field: {key}")
        sampling[key] = value


def _set_case_audit_section(name: str, value: dict[str, Any]) -> None:
    sections = _FAILURE_CONTEXT["caseAuditSections"]
    if name not in sections:
        raise ValueError(f"Unsupported case-audit section: {name}")
    sections[name] = copy.deepcopy(value)


def _set_failure_generation(value: dict[str, Any]) -> None:
    _FAILURE_CONTEXT["generation"] = copy.deepcopy(value)


def _set_failure_generation_watchdog(value: dict[str, Any]) -> None:
    _FAILURE_CONTEXT["generationWatchdog"] = copy.deepcopy(value)


def _set_failure_grounded_memory_policy(value: dict[str, Any]) -> None:
    _FAILURE_CONTEXT["groundedMemoryPolicy"] = copy.deepcopy(value)


def _set_case_generation(value: dict[str, Any]) -> None:
    _FAILURE_CONTEXT["caseGeneration"] = copy.deepcopy(value)


def _set_failure_execution_timing(value: dict[str, Any]) -> None:
    _FAILURE_CONTEXT["executionTiming"] = copy.deepcopy(value)


def _set_failure_structured_decoding(
    value: dict[str, Any] | None,
) -> None:
    _FAILURE_CONTEXT["structuredDecodingAudit"] = copy.deepcopy(value)


def _set_failure_provider_output(**values: Any) -> None:
    output = _FAILURE_CONTEXT["providerOutput"]
    for key, value in values.items():
        if key not in {
            "rawGeneratedTextSha256",
            "providerEchoCaseId",
            "providerEchoCandidateId",
        }:
            raise ValueError(
                f"Unsupported failure provider-output field: {key}"
            )
        output[key] = value


def _append_failure_recovery_pool_ledger(
    attestation: dict[str, Any],
) -> None:
    """Reserve complete pool witnesses outside the lossy diagnostic list."""
    ledger = _FAILURE_CONTEXT["recoveryPoolLedger"]
    if len(ledger) >= 4:
        raise ValueError("Recovery-pool failure ledger exceeded four entries.")
    exact_fields = (
        "candidateOrdinal",
        "seed",
        "sourceSelectionReason",
        "sourcePassOrdinal",
        "sourceRejectedJsonSha256",
        "canonicalMessagesSha256",
        "renderedPromptSha256",
        "renderedPromptUtf8ByteCount",
        "inputTokenIdsSha256",
        "inputTokenCount",
        "outputSha256",
        "completedJsonSha256",
        "rejectionCode",
        "accepted",
    )
    entry = {name: attestation[name] for name in exact_fields}
    expected_ordinal = len(ledger) + 1
    if entry["candidateOrdinal"] != expected_ordinal:
        raise ValueError("Recovery-pool failure ledger ordinal is invalid.")
    ledger.append(copy.deepcopy(entry))


def _add_failure_diagnostic(value: Any) -> None:
    diagnostics = _FAILURE_CONTEXT["diagnostics"]
    if len(diagnostics) >= MAX_FAILURE_DIAGNOSTICS:
        return
    text = _sanitize_failure_text(value)
    if not text:
        return
    diagnostics.append(text[:MAX_FAILURE_DIAGNOSTIC_LENGTH])


def _bounded_failure_message(value: Any) -> str:
    text = _sanitize_failure_text(value)
    return text[:MAX_FAILURE_MESSAGE_LENGTH]


def _sanitize_failure_text(value: Any) -> str:
    """Keep retained diagnostics useful without persisting secrets or paths."""
    text = re.sub(r"\s+", " ", str(value)).strip()
    text = re.sub(
        r"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+",
        "Bearer [redacted]",
        text,
    )
    text = re.sub(
        r"(?i)\b(access[_-]?token|refresh[_-]?token|client[_-]?secret|"
        r"password|authorization|api[_-]?key|cookie|session[_-]?id)\b"
        r"\s*[:=]\s*[^\s,;]+",
        r"\1=[redacted]",
        text,
    )
    text = re.sub(
        r"(?i)(?<![A-Za-z0-9])(?:[A-Z]:\\|\\\\)[^\s<>|\"']+",
        "[local-path]",
        text,
    )
    text = re.sub(
        r"(?i)\b(?:ignore|disregard|override|forget)\s+(?:all\s+)?"
        r"(?:previous|prior|above|system|developer)\s+"
        r"(?:instructions?|messages?|prompts?)\b"
        r"|<\|(?:im_start|im_end|system|assistant|user|developer|tool)[^>]*\|>",
        "[untrusted instruction-like text removed]",
        text,
    )
    return text


def _approve_failure_output() -> None:
    _FAILURE_CONTEXT["failureOutputApproved"] = True



__all__ = [name for name in globals() if not name.startswith("__")]
