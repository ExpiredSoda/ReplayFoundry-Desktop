"""Qualified bounded production editorial-observation batch."""
from __future__ import annotations

import hashlib
import time
from pathlib import Path
from typing import Any

from ..commands import *
from ..request_validation import (
    _require_array,
    _require_exact_keys,
    _require_object,
    _require_sha256,
    _require_utc_timestamp,
    _scan_forbidden_input_keys,
    _validate_request,
    _validate_video_policy,
)
from .attempts import attempt_editorial_set
from .constrained_pilot_command import validate_qualification_lock
from .protocol import (
    MODEL_MANIFEST_SHA256,
    MODEL_REPOSITORY,
    MODEL_REVISION,
    PROMPT_NAME,
    PROMPT_SHA256,
    PROMPT_VERSION,
)
from .qualified_cuda_attention import (
    CACHE_IMPLEMENTATION as QUALIFIED_CACHE_IMPLEMENTATION,
    policy_payload as qualified_cuda_attention_payload,
    qualified_cuda_attention_context,
    require_policy_source as require_qualified_cuda_attention_policy,
)
from .structured_decoding import StructuredDecodingSession, model_vocab_size
from .structured_decoding_policy import POLICY_VERSION, require_frozen_packages

QUALIFIED_INPUT_SCHEMA = "visual-semantic-qualified-input-batch-1.0"
QUALIFIED_ATTEMPT_SCHEMA = "visual-semantic-qualified-attempt-batch-1.1"
QUALIFIED_OUTPUT_SCHEMA = "visual-semantic-qualified-observation-batch-1.1"
MAXIMUM_PRODUCTION_CASES = 8


def _prompt_text() -> str:
    path = HOST_DIRECTORY / "replayfoundry-visual-semantic-editorial-prompt-2.7.txt"
    text = path.read_text(encoding="utf-8").replace("\r\n", "\n").replace("\r", "\n").strip()
    if hashlib.sha256(text.encode("utf-8")).hexdigest() != PROMPT_SHA256:
        _fail(UsageOrInputError, "Qualified editorial prompt source changed.")
    return text


def _validate_batch(value: Any) -> tuple[list[dict[str, Any]], str]:
    batch = _require_object(value, "$")
    _require_exact_keys(
        batch,
        {"schemaVersion", "prompt", "model", "videoPolicy", "requests"},
        "$",
    )
    if batch["schemaVersion"] != QUALIFIED_INPUT_SCHEMA:
        _fail(UsageOrInputError, "Qualified editorial input schema is unsupported.")
    _scan_forbidden_input_keys(batch)
    prompt = _require_object(batch["prompt"], "$.prompt")
    _require_exact_keys(
        prompt,
        {"schemaVersion", "name", "version", "text", "sha256", "frozenAtUtc"},
        "$.prompt",
    )
    prompt_text = _prompt_text()
    if (
        prompt["schemaVersion"] != "visual-semantic-prompt-manifest-2.0"
        or prompt["name"] != PROMPT_NAME
        or prompt["version"] != PROMPT_VERSION
        or prompt["text"] != prompt_text
        or _require_sha256(prompt["sha256"], "$.prompt.sha256") != PROMPT_SHA256
    ):
        _fail(UsageOrInputError, "Qualified editorial prompt identity changed.")
    _require_utc_timestamp(prompt["frozenAtUtc"], "$.prompt.frozenAtUtc")
    model = _require_object(batch["model"], "$.model")
    _require_exact_keys(
        model,
        {"schemaVersion", "repositoryId", "revision", "manifestSha256"},
        "$.model",
    )
    if (
        model["schemaVersion"] != "visual-semantic-model-manifest-1.0"
        or model["repositoryId"] != MODEL_REPOSITORY
        or model["revision"] != MODEL_REVISION
        or _require_sha256(model["manifestSha256"], "$.model.manifestSha256")
        != MODEL_MANIFEST_SHA256
    ):
        _fail(UsageOrInputError, "Qualified editorial model identity changed.")
    video_policy = _validate_video_policy(batch["videoPolicy"])
    values = _require_array(
        batch["requests"],
        "$.requests",
        maximum=MAXIMUM_PRODUCTION_CASES,
    )
    if not values:
        _fail(UsageOrInputError, "Qualified editorial batch is empty.")
    media_hash_cache: dict[Path, str] = {}
    requests: list[dict[str, Any]] = []
    identities: set[tuple[str, str]] = set()
    for index, value in enumerate(values):
        request = _validate_request(value, index, media_hash_cache)
        identity = (request["caseId"], request["candidate"]["id"])
        if identity in identities:
            _fail(UsageOrInputError, "Qualified editorial batch has duplicate identity.")
        identities.add(identity)
        request["_validated"]["videoPolicy"] = video_policy
        requests.append(request)
    return requests, prompt_text


def run_qualified_editorial_batch(
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
    requests, prompt_text = _validate_batch(_load_strict_json(input_path))
    lock = validate_qualification_lock(_load_strict_json(qualification_lock_path))
    require_frozen_packages()
    require_qualified_cuda_attention_policy()
    _validate_failure_output_against_media(failure_output_path, requests)

    _set_failure_stage("RuntimeInitialization")
    torch, torchcodec, transformers, process_vision_info = _load_runtime(ffmpeg_directory)
    _validate_model_directory(model_path)
    model, processor = _load_model_and_processor(model_path, torch, transformers)
    session = StructuredDecodingSession(processor.tokenizer, model_vocab_size(model))
    try:
        with qualified_cuda_attention_context(torch):
            attempted = attempt_editorial_set(
                "Primary",
                requests,
                prompt_text,
                model,
                processor,
                torch,
                torchcodec,
                process_vision_info,
                session,
                QUALIFIED_CACHE_IMPLEMENTATION,
            )
        cuda_attention_policy = qualified_cuda_attention_payload()
        _set_failure_stage("MediaRevalidation")
        _revalidate_media_inputs(requests)
        attempt = {
            "schemaVersion": QUALIFIED_ATTEMPT_SCHEMA,
            "policyVersion": POLICY_VERSION,
            "qualificationLockCanonicalHash": lock["canonicalHash"],
            "cudaAttentionPolicy": cuda_attention_policy,
            "cases": attempted,
            "peakAllocatedGpuBytes": int(torch.cuda.max_memory_allocated(0)),
            "totalElapsedSeconds": round(time.perf_counter() - started, 6),
        }
        attempt["canonicalHash"] = _canonical_json_sha256(attempt)
        _set_failure_stage("OutputWrite")
        _write_json_atomic(attempt_output_path, attempt)
        if attempted["failedCount"]:
            _set_failure_stage("AttemptCompletedWithCaseFailures")
            _fail(
                ProviderCaseFailuresDetected,
                "One or more qualified Qwen observation cases failed; all outcomes were retained.",
            )
        result = {
            "schemaVersion": QUALIFIED_OUTPUT_SCHEMA,
            "policyVersion": POLICY_VERSION,
            "qualificationLockCanonicalHash": lock["canonicalHash"],
            "cudaAttentionPolicy": cuda_attention_policy,
            "attemptCanonicalHash": attempt["canonicalHash"],
            "results": attempted["outcomes"],
            "peakAllocatedGpuBytes": attempt["peakAllocatedGpuBytes"],
            "totalElapsedSeconds": attempt["totalElapsedSeconds"],
        }
        result["canonicalHash"] = _canonical_json_sha256(result)
        _write_json_atomic(output_path, result)
    finally:
        del processor
        del model
        torch.cuda.empty_cache()


__all__ = [name for name in globals() if not name.startswith("__")]
