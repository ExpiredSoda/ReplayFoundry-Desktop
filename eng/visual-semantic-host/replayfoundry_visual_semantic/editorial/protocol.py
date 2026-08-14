"""Strict frozen Prompt 2.3 development-plan protocol."""
from __future__ import annotations

from typing import Any

from ..request_validation import *

EDITORIAL_PLAN_SCHEMA = "visual-semantic-editorial-development-plan-1.0"
EDITORIAL_ATTEMPT_SET_SCHEMA = "visual-semantic-editorial-attempt-set-1.0"
EDITORIAL_ATTEMPT_PLAN_SCHEMA = (
    "visual-semantic-editorial-development-attempt-1.0"
)
EDITORIAL_COMPLETED_BATCH_SCHEMA = (
    "visual-semantic-editorial-observation-batch-2.0"
)
EDITORIAL_COMPLETED_EXECUTION_SCHEMA = (
    "visual-semantic-editorial-development-execution-1.0"
)
EDITORIAL_HOST_VERSION = "0.7B.6"
EDITORIAL_ADAPTER_VERSION = "0.7.5-research"
CONFIGURATION_LOCK_SHA256 = (
    "52d94c5ed65903ae61c2f61ab2657426a941ad65ec981d51701a0f1887b503e7"
)
PROMPT_SCHEMA = "visual-semantic-prompt-manifest-2.0"
PROMPT_NAME = "ReplayFoundry Visual Semantic Editorial Observation Prompt"
PROMPT_VERSION = "2.7"
PROMPT_SHA256 = (
    "2fba68a0cb64a5f9911e898f10efc516ea087786b07ddeb05a917730dbf197bb"
)
PROMPT_FILE_SHA256 = (
    "8cebc1e886e72d387df459fca07c2e488e1cdde24aa74721b0741bc5c3989198"
)
SEMANTIC_SCHEMA_SHA256 = (
    "9e603f1168e5ac77359337e3b205d263d196ae6714abc693c8702eafed5629a8"
)
CANONICALIZATION_SHA256 = (
    "0ac5df8cbe5882640472196b5b7de4302e8dcae01a892cf1e7f58156b659bd63"
)
GATE_SHA256 = (
    "bf6f66e8d9a5d8bd383f5bf8ec7992c37a9b9c0559da7cb66fd1c73e3865a410"
)
MODEL_MANIFEST_SHA256 = (
    "2018ffabe5257d8045bd565a232d82da382679c9e71c388f6880bff01acf17b4"
)
ENVIRONMENT_MANIFEST_SHA256 = (
    "0ec121b9403becd30aa63d2b63154078fed2993076b2fde9bbe084a0497a289f"
)
RUN_KINDS = ("Primary", "Repeat", "VisualOnly")
RUN_COUNTS = {"Primary": 30, "Repeat": 6, "VisualOnly": 12}
SOURCE_RUN_KINDS = {
    "Primary": "FullContextV1",
    "Repeat": "FullContextRepeatV1",
    "VisualOnly": "VisualOnlyV1",
}


def _require_hash(value: Any, expected: str, location: str) -> str:
    actual = _require_sha256(value, location)
    if actual.casefold() != expected.casefold():
        _fail(
            UsageOrInputError,
            f"{location} differs from the frozen Prompt 2.3 identity.",
        )
    return actual


def _validate_prompt(value: Any) -> str:
    prompt = _require_object(value, "$.prompt")
    _require_exact_keys(
        prompt,
        {
            "schemaVersion",
            "name",
            "version",
            "text",
            "sha256",
            "fileSha256",
            "semanticSchemaVersion",
            "semanticSchemaSha256",
            "canonicalizationPolicyVersion",
            "canonicalizationPolicySha256",
            "gatePolicyVersion",
            "gatePolicySha256",
            "frozenAtUtc",
        },
        "$.prompt",
    )
    if (
        prompt["schemaVersion"] != PROMPT_SCHEMA
        or prompt["name"] != PROMPT_NAME
        or prompt["version"] != PROMPT_VERSION
        or prompt["semanticSchemaVersion"]
        != "visual-semantic-editorial-observation-2.0"
        or prompt["canonicalizationPolicyVersion"]
        != "visual-semantic-editorial-canonicalization-1.3"
        or prompt["gatePolicyVersion"]
        != "visual-semantic-editorial-development-gates-1.0"
    ):
        _fail(
            UsageOrInputError,
            "Prompt 2.3 plan contains a foreign prompt or policy identity.",
        )
    prompt_text = _require_string(
        prompt["text"],
        "$.prompt.text",
        maximum=32 * 1024,
    )
    _require_hash(prompt["sha256"], PROMPT_SHA256, "$.prompt.sha256")
    if hashlib.sha256(prompt_text.encode("utf-8")).hexdigest() != PROMPT_SHA256:
        _fail(
            UsageOrInputError,
            "Prompt 2.3 text differs from its frozen hash.",
        )
    _require_hash(
        prompt["fileSha256"],
        PROMPT_FILE_SHA256,
        "$.prompt.fileSha256",
    )
    _require_hash(
        prompt["semanticSchemaSha256"],
        SEMANTIC_SCHEMA_SHA256,
        "$.prompt.semanticSchemaSha256",
    )
    _require_hash(
        prompt["canonicalizationPolicySha256"],
        CANONICALIZATION_SHA256,
        "$.prompt.canonicalizationPolicySha256",
    )
    _require_hash(
        prompt["gatePolicySha256"],
        GATE_SHA256,
        "$.prompt.gatePolicySha256",
    )
    _require_utc_timestamp(prompt["frozenAtUtc"], "$.prompt.frozenAtUtc")
    return prompt_text


def _validate_model(value: Any) -> None:
    model = _require_object(value, "$.model")
    _require_exact_keys(
        model,
        {"schemaVersion", "repositoryId", "revision", "manifestSha256"},
        "$.model",
    )
    if (
        model["schemaVersion"] != MODEL_MANIFEST_SCHEMA
        or model["repositoryId"] != MODEL_REPOSITORY
        or model["revision"] != MODEL_REVISION
    ):
        _fail(
            UsageOrInputError,
            "Prompt 2.3 model identity differs from the pinned model.",
        )
    _require_hash(
        model["manifestSha256"],
        MODEL_MANIFEST_SHA256,
        "$.model.manifestSha256",
    )


def _validate_sampling_baseline(
    value: Any,
    primary: list[dict[str, Any]],
) -> dict[str, Any]:
    baseline = _require_object(value, "$.samplingBaseline")
    _require_exact_keys(
        baseline,
        {
            "sourceArtifactSha256",
            "sourceCanonicalHash",
            "cases",
        },
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
        maximum=30,
    )
    if len(rows) != 30:
        _fail(
            UsageOrInputError,
            "Prompt 2.3 sampling baseline requires exactly 30 cases.",
        )
    for index, (row_value, request) in enumerate(zip(rows, primary)):
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
                f"{location} identity differs from the primary plan.",
            )
        _require_sha256(
            row["finalTensorSha256"],
            f"{location}.finalTensorSha256",
        )
        frame_hashes = _require_array(
            row["finalFrameSha256"],
            f"{location}.finalFrameSha256",
            maximum=VIDEO_MAX_FRAMES,
        )
        for frame_index, frame_hash in enumerate(frame_hashes):
            _require_sha256(
                frame_hash,
                f"{location}.finalFrameSha256[{frame_index}]",
            )
        for field in (
            "actualPtsSeconds",
            "actualFrameDurationsSeconds",
        ):
            numbers = _require_array(
                row[field],
                f"{location}.{field}",
                maximum=VIDEO_MAX_FRAMES,
            )
            for number_index, number in enumerate(numbers):
                if _require_finite_decimal(
                    number,
                    f"{location}.{field}[{number_index}]",
                ) < 0:
                    _fail(
                        UsageOrInputError,
                        f"{location}.{field} must be non-negative.",
                    )
        if not (
            len(frame_hashes)
            == len(row["actualPtsSeconds"])
            == len(row["actualFrameDurationsSeconds"])
        ):
            _fail(
                UsageOrInputError,
                f"{location} sampling cardinality differs.",
            )
    return baseline


def validate_editorial_plan(
    value: Any,
) -> dict[str, Any]:
    """Validate the immutable 30/6/12 plan before model loading."""
    plan = _require_object(value, "$")
    _require_exact_keys(
        plan,
        {
            "schemaVersion",
            "hostVersion",
            "adapterVersion",
            "configurationLockCanonicalHash",
            "prompt",
            "model",
            "videoPolicy",
            "runtime",
            "samplingBaseline",
            "sets",
            "canonicalHash",
        },
        "$",
    )
    if (
        plan["schemaVersion"] != EDITORIAL_PLAN_SCHEMA
        or plan["hostVersion"] != EDITORIAL_HOST_VERSION
        or plan["adapterVersion"] != EDITORIAL_ADAPTER_VERSION
    ):
        _fail(
            UsageOrInputError,
            "Prompt 2.3 plan protocol identity is unsupported.",
        )
    _require_hash(
        plan["configurationLockCanonicalHash"],
        CONFIGURATION_LOCK_SHA256,
        "$.configurationLockCanonicalHash",
    )
    supplied_hash = _require_sha256(plan["canonicalHash"], "$.canonicalHash")
    identity = copy.deepcopy(plan)
    identity.pop("canonicalHash")
    if supplied_hash != _canonical_json_sha256(identity):
        _fail(UsageOrInputError, "Prompt 2.3 plan canonical hash is invalid.")
    _scan_forbidden_input_keys(plan)
    prompt_text = _validate_prompt(plan["prompt"])
    _validate_model(plan["model"])
    video_policy = _validate_video_policy(plan["videoPolicy"])
    runtime = _require_object(plan["runtime"], "$.runtime")
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
            "Prompt 2.3 generation/runtime policy differs from the lock.",
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
    sets = _require_array(plan["sets"], "$.sets", maximum=3)
    if len(sets) != 3:
        _fail(UsageOrInputError, "Prompt 2.3 plan requires three sets.")
    media_hash_cache: dict[Path, str] = {}
    validated_sets: dict[str, list[dict[str, Any]]] = {}

    for set_index, set_value in enumerate(sets):
        location = f"$.sets[{set_index}]"
        item = _require_object(set_value, location)
        _require_exact_keys(
            item,
            {
                "runKind",
                "sourceRunKind",
                "sourceCanonicalHash",
                "preparedCanonicalHash",
                "orderedCaseIdentitySha256",
                "requests",
            },
            location,
        )
        expected_kind = RUN_KINDS[set_index]
        if (
            item["runKind"] != expected_kind
            or item["sourceRunKind"] != SOURCE_RUN_KINDS[expected_kind]
        ):
            _fail(
                UsageOrInputError,
                f"{location} changed frozen set order or kind.",
            )
        for field in (
            "sourceCanonicalHash",
            "preparedCanonicalHash",
            "orderedCaseIdentitySha256",
        ):
            _require_sha256(item[field], f"{location}.{field}")
        request_values = _require_array(
            item["requests"],
            f"{location}.requests",
            maximum=RUN_COUNTS[expected_kind],
        )
        if len(request_values) != RUN_COUNTS[expected_kind]:
            _fail(
                UsageOrInputError,
                f"{location} has the wrong frozen case count.",
            )
        requests: list[dict[str, Any]] = []
        for request_index, request_value in enumerate(request_values):
            request = _validate_request(
                request_value,
                request_index,
                media_hash_cache,
            )
            request["_validated"]["videoPolicy"] = video_policy
            requests.append(request)
        if len({row["caseId"] for row in requests}) != len(requests):
            _fail(UsageOrInputError, f"{location} has duplicate cases.")
        validated_sets[expected_kind] = requests

    baseline = _validate_sampling_baseline(
        plan["samplingBaseline"],
        validated_sets["Primary"],
    )
    plan["_validated"] = {
        "promptText": prompt_text,
        "sets": validated_sets,
        "samplingBaseline": baseline,
    }
    return plan
