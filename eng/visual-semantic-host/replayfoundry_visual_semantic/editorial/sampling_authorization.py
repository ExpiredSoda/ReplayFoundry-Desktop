"""Shared actual-PTS sampling authorization for Prompt 2.3 runs."""
from __future__ import annotations

from typing import Any

from ..commands import *


def _sampling_projection(case: dict[str, Any]) -> dict[str, Any]:
    qwen = case["qwenMetadata"]
    direct = case["directTorchCodecMetadata"]
    return {
        "caseId": case["caseId"],
        "candidateId": case["candidateId"],
        "caseOrdinal": case["caseOrdinal"],
        "finalTensorSha256": qwen["finalTensorSha256"],
        "finalFrameSha256": qwen["finalFrameSha256"],
        "actualPtsSeconds": direct["actualPtsSeconds"],
        "actualFrameDurationsSeconds":
            direct["actualFrameDurationsSeconds"],
    }


def authorize_sampling(
    requests: list[dict[str, Any]],
    baseline: dict[str, Any],
    prompt_text: str,
    torch: Any,
    torchcodec: Any,
    process_vision_info: Any,
) -> dict[str, Any]:
    """Re-run the exact sampling audit for the selected frozen requests."""
    refreshed: list[dict[str, Any]] = []

    for index, request in enumerate(requests):
        ordinal = index + 1
        _set_failure_case(request, ordinal, request["caseHash"])
        result = _audit_sampling_case(
            request,
            ordinal,
            request["caseHash"],
            prompt_text,
            torch,
            torchcodec,
            process_vision_info,
        )
        if (
            result["status"] != "Succeeded"
            or not result["correctedPolicyValidation"]["passed"]
        ):
            _fail(
                InitializationError,
                "Current-host Prompt 2.3 sampling authorization failed "
                f"for case {ordinal}.",
            )
        refreshed.append(_sampling_projection(result))

    expected = baseline["cases"]
    if refreshed != expected:
        _fail(
            InitializationError,
            "Current-host Prompt 2.3 sampling differs from the frozen "
            f"{len(requests)}-case actual-PTS baseline.",
        )

    parity = {
        "sourceArtifactSha256": baseline["sourceArtifactSha256"],
        "parityCaseCount": len(refreshed),
        "cases": refreshed,
    }
    parity_hash = _canonical_json_sha256(parity)
    _clear_failure_case()
    return {
        "sourceArtifactSha256": baseline["sourceArtifactSha256"],
        "parityCaseCount": len(refreshed),
        "parityCanonicalHash": parity_hash,
    }


__all__ = [name for name in globals() if not name.startswith("__")]
