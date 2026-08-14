"""Strict label-blind Prompt 2.3 contract-pilot protocol."""
from __future__ import annotations

from typing import Any

from ..request_validation import *
from .protocol import (
    CONFIGURATION_LOCK_SHA256,
    EDITORIAL_ADAPTER_VERSION,
    EDITORIAL_HOST_VERSION,
    ENVIRONMENT_MANIFEST_SHA256,
    GENERATION_POLICY_SHA256,
    GENERATION_POLICY_VERSION,
    MODEL_MANIFEST_SHA256,
    _require_hash,
    _validate_model,
    _validate_prompt,
)

PILOT_PLAN_SCHEMA = "visual-semantic-editorial-contract-pilot-plan-1.0"
PILOT_ATTEMPT_SCHEMA = (
    "visual-semantic-editorial-contract-pilot-attempt-1.0"
)
PILOT_COMPLETED_SCHEMA = (
    "visual-semantic-editorial-contract-pilot-completed-1.0"
)
PILOT_POLICY_VERSION = "visual-semantic-prompt2-contract-pilot-1.2"
PILOT_CANARY = ("review-c4bfbdec6bc32d3f",)
PILOT_THREE = (
    "review-c4bfbdec6bc32d3f",
    "review-a80601ff85e908a1",
    "review-5530b3d1d93d03af",
)
PILOT_EXPANDED = (
    "review-3d484a8624d010ae",
    "review-5530b3d1d93d03af",
    "review-a80601ff85e908a1",
    "review-b532ac2742583709",
    "review-c4bfbdec6bc32d3f",
    "review-c96ae2839811cf7e",
    "review-f7809800d3ccaa18",
    "review-f953326213edfbbe",
)
_FORBIDDEN_KEYS = {
    "label",
    "labels",
    "score",
    "scores",
    "rank",
    "ranks",
    "outcome",
    "outcomes",
    "expectedDisposition",
    "expectedOutcome",
}


def _scan_forbidden_pilot_fields(value: Any, location: str = "$") -> None:
    if isinstance(value, dict):
        for key, item in value.items():
            if key in _FORBIDDEN_KEYS or "holdout" in key.casefold():
                _fail(
                    UsageOrInputError,
                    f"{location}.{key} is forbidden in a label-blind pilot.",
                )
            _scan_forbidden_pilot_fields(item, f"{location}.{key}")
    elif isinstance(value, list):
        for index, item in enumerate(value):
            _scan_forbidden_pilot_fields(item, f"{location}[{index}]")


def _validate_runtime(value: Any) -> None:
    runtime = _require_object(value, "$.runtime")
    _require_exact_keys(
        runtime,
        {
            "environmentManifestSha256",
            "generationPolicyVersion",
            "generationPolicySha256",
            "maximumNewTokens",
            "doSample",
            "numberOfBeams",
            "useCache",
            "audioSupplied",
        },
        "$.runtime",
    )
    if (
        runtime["generationPolicyVersion"] != GENERATION_POLICY_VERSION
        or runtime["maximumNewTokens"] != ACTIVE_POLICY_MAX_NEW_TOKENS
        or runtime["doSample"] is not False
        or runtime["numberOfBeams"] != NUMBER_OF_BEAMS
        or runtime["useCache"] is not True
        or runtime["audioSupplied"] is not False
    ):
        _fail(
            UsageOrInputError,
            "Prompt 2.3 pilot runtime differs from the frozen lock.",
        )
    _require_hash(
        runtime["environmentManifestSha256"],
        ENVIRONMENT_MANIFEST_SHA256,
        "$.runtime.environmentManifestSha256",
    )
    _require_hash(
        runtime["generationPolicySha256"],
        GENERATION_POLICY_SHA256,
        "$.runtime.generationPolicySha256",
    )


def _validate_sampling(
    value: Any,
    requests: list[dict[str, Any]],
) -> dict[str, Any]:
    baseline = _require_object(value, "$.samplingBaseline")
    _require_exact_keys(
        baseline,
        {"sourceArtifactSha256", "sourceCanonicalHash", "cases"},
        "$.samplingBaseline",
    )
    _require_sha256(
        baseline["sourceArtifactSha256"],
        "$.samplingBaseline.sourceArtifactSha256",
    )
    _require_sha256(
        baseline["sourceCanonicalHash"],
        "$.samplingBaseline.sourceCanonicalHash",
    )
    rows = _require_array(
        baseline["cases"],
        "$.samplingBaseline.cases",
        maximum=8,
    )
    if len(rows) != len(requests):
        _fail(
            UsageOrInputError,
            "Prompt 2.3 pilot sampling count differs from its cases.",
        )

    for index, (row_value, request) in enumerate(zip(rows, requests)):
        location = f"$.samplingBaseline.cases[{index}]"
        row = _require_object(row_value, location)
        _require_exact_keys(
            row,
            {
                "caseId",
                "candidateId",
                "caseOrdinal",
                "finalTensorSha256",
                "finalFrameSha256",
                "actualPtsSeconds",
                "actualFrameDurationsSeconds",
            },
            location,
        )
        if (
            row["caseId"] != request["caseId"]
            or row["candidateId"] != request["candidate"]["id"]
            or row["caseOrdinal"] != index + 1
        ):
            _fail(
                UsageOrInputError,
                f"{location} identity differs from the pilot request.",
            )
        _require_sha256(
            row["finalTensorSha256"],
            f"{location}.finalTensorSha256",
        )
        frames = _require_array(
            row["finalFrameSha256"],
            f"{location}.finalFrameSha256",
            maximum=VIDEO_MAX_FRAMES,
        )
        pts = _require_array(
            row["actualPtsSeconds"],
            f"{location}.actualPtsSeconds",
            maximum=VIDEO_MAX_FRAMES,
        )
        durations = _require_array(
            row["actualFrameDurationsSeconds"],
            f"{location}.actualFrameDurationsSeconds",
            maximum=VIDEO_MAX_FRAMES,
        )
        if not (len(frames) == len(pts) == len(durations)):
            _fail(
                UsageOrInputError,
                f"{location} sampling cardinality differs.",
            )
        for frame in frames:
            _require_sha256(frame, f"{location}.finalFrameSha256")
        for number in [*pts, *durations]:
            if _require_finite_decimal(number, location) < 0:
                _fail(
                    UsageOrInputError,
                    f"{location} sampling values must be non-negative.",
                )

    return baseline


def validate_pilot_plan(value: Any) -> dict[str, Any]:
    plan = _require_object(value, "$")
    _require_exact_keys(
        plan,
        {
            "schemaVersion",
            "hostVersion",
            "adapterVersion",
            "policyVersion",
            "phase",
            "selectionBasis",
            "labelsPermitted",
            "metricsPermitted",
            "futureHoldoutPermitted",
            "maximumRealCases",
            "maximumRealModelInvocations",
            "configurationLockCanonicalHash",
            "prompt",
            "model",
            "videoPolicy",
            "runtime",
            "samplingBaseline",
            "requests",
            "canonicalHash",
        },
        "$",
    )
    if (
        plan["schemaVersion"] != PILOT_PLAN_SCHEMA
        or plan["hostVersion"] != EDITORIAL_HOST_VERSION
        or plan["adapterVersion"] != EDITORIAL_ADAPTER_VERSION
        or plan["policyVersion"] != PILOT_POLICY_VERSION
        or plan["selectionBasis"]
        != "HistoricalRuntimeAndBoundedNonLabelStructuralCoverage"
        or plan["labelsPermitted"] is not False
        or plan["metricsPermitted"] is not False
        or plan["futureHoldoutPermitted"] is not False
        or plan["maximumRealCases"] != 8
        or plan["maximumRealModelInvocations"] != 12
    ):
        _fail(
            UsageOrInputError,
            "Prompt 2.3 pilot policy identity changed.",
        )
    expected = (
        PILOT_CANARY if plan["phase"] == "Canary"
        else PILOT_THREE if plan["phase"] == "Pilot"
        else PILOT_EXPANDED if plan["phase"] == "Expanded"
        else None
    )
    if expected is None:
        _fail(UsageOrInputError, "Prompt 2.3 pilot phase is invalid.")
    _require_hash(
        plan["configurationLockCanonicalHash"],
        CONFIGURATION_LOCK_SHA256,
        "$.configurationLockCanonicalHash",
    )
    supplied = _require_sha256(plan["canonicalHash"], "$.canonicalHash")
    identity = copy.deepcopy(plan)
    identity.pop("canonicalHash")
    if supplied != _canonical_json_sha256(identity):
        _fail(UsageOrInputError, "Prompt 2.3 pilot hash is invalid.")
    _scan_forbidden_pilot_fields(plan["requests"], "$.requests")
    prompt_text = _validate_prompt(plan["prompt"])
    _validate_model(plan["model"])
    video_policy = _validate_video_policy(plan["videoPolicy"])
    _validate_runtime(plan["runtime"])
    request_values = _require_array(
        plan["requests"],
        "$.requests",
        maximum=8,
    )
    if tuple(row.get("caseId") for row in request_values) != expected:
        _fail(
            UsageOrInputError,
            "Prompt 2.3 pilot case identities or order changed.",
        )
    media_hash_cache: dict[Path, str] = {}
    requests: list[dict[str, Any]] = []
    for index, request_value in enumerate(request_values):
        request = _validate_request(
            request_value,
            index,
            media_hash_cache,
        )
        request["_validated"]["videoPolicy"] = video_policy
        requests.append(request)
    baseline = _validate_sampling(plan["samplingBaseline"], requests)
    plan["_validated"] = {
        "promptText": prompt_text,
        "requests": requests,
        "samplingBaseline": baseline,
    }
    return plan


__all__ = [name for name in globals() if not name.startswith("__")]
