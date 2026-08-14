"""Strict grounded title, description, and tag batch command facade."""
from __future__ import annotations

import time
from pathlib import Path
from typing import Any

from ..commands import (
    InitializationError,
    UsageOrInputError,
    _canonical_json_sha256,
    _clear_failure_case,
    _fail,
    _input_case_hashes,
    _load_strict_json,
    _record_input_failure_identity,
    _set_failure_case,
    _set_failure_stage,
    _validate_failure_output_against_media,
    _validate_model_directory,
    _write_json_atomic,
)
from ..model_runtime import _load_model_and_processor, _load_runtime
from ..generation_watchdog import (
    generation_watchdog_policy_payload,
    grounded_case_watchdog,
    grounded_case_watchdog_success_payload,
)
from ..grounded_cuda_memory import (
    complete_grounded_cuda_memory,
    configure_grounded_cuda_memory,
    finalize_grounded_model_placement,
    GROUNDED_MODEL_LOAD_DEVICE_MAP,
    is_cuda_out_of_memory,
    record_grounded_cuda_out_of_memory,
    validate_grounded_model_placement,
)
from ..request_validation import (
    _require_array,
    _require_exact_keys,
    _require_object,
    _require_sha256,
)
from .constrained_pilot_command import validate_qualification_lock
from .grounded_knowledge_selection import (
    _knowledge_selection_messages,
    _knowledge_selection_prompt_text,
    _knowledge_selection_schema,
    _strict_knowledge_selection,
)
from .grounded_metadata_contract import validate_request as _validate_request
from .grounded_metadata_json_whitespace import (
    require_policy as require_json_whitespace_policy,
)
from .grounded_metadata_reroll_similarity import (
    MAXIMUM_RETAINED_TITLES,
    RerollTitleReference,
    RerollTitleScope,
)
from .grounded_metadata_pipeline import (
    _build_grounding_packet,
    _duplicates_prior_synthesis,
    _grounding_reuse_identity,
    _infer_case,
    _reroll_title_reference,
    _reroll_title_scope,
    _requires_primary_only_synthesis_evidence,
    _retry_correction_envelope,
    _retry_feedback,
    _synthesize_case,
    _visual_windows,
)
from .grounded_metadata_synthesis import (
    PROMPT_NAME,
    PROMPT_SHA256,
    PROMPT_VERSION,
    _metadata_messages,
    _prompt_text,
)
from .grounded_metadata_validation import (
    metadata_schema as _metadata_schema,
    strict_metadata as _strict_metadata,
    validation_failure_code as _validation_failure_code,
    validation_feedback as _validation_feedback,
)
from .grounded_visual_drafts import (
    _strict_visual_draft,
    _visual_draft_messages,
    _visual_draft_prompt_text,
    _visual_draft_schema,
)
from .grounded_visual_event_selection import (
    _strict_visual_event_selection,
    _visual_event_selection_messages,
    _visual_event_selection_prompt_text,
    _visual_event_selection_schema,
)
from .protocol import MODEL_MANIFEST_SHA256, MODEL_REPOSITORY, MODEL_REVISION
from .structured_decoding import StructuredDecodingSession, model_vocab_size
from .structured_decoding_policy import POLICY_VERSION, require_frozen_packages

INPUT_SCHEMA = "grounded-editorial-metadata-input-batch-1.8"
OUTPUT_SCHEMA = "grounded-editorial-metadata-output-batch-1.50"
MAXIMUM_CASES = 30


def _validate_batch(value: Any) -> tuple[list[dict[str, Any]], str]:
    require_json_whitespace_policy()
    batch = _require_object(value, "$")
    _require_exact_keys(batch, {"schemaVersion", "prompt", "model", "requests"}, "$")
    if batch["schemaVersion"] != INPUT_SCHEMA:
        _fail(UsageOrInputError, "Grounded metadata input schema is unsupported.")
    prompt_text = _prompt_text()
    prompt = _require_object(batch["prompt"], "$.prompt")
    _require_exact_keys(prompt, {"name", "version", "sha256", "text"}, "$.prompt")
    if (
        prompt["name"] != PROMPT_NAME
        or prompt["version"] != PROMPT_VERSION
        or _require_sha256(prompt["sha256"], "$.prompt.sha256") != PROMPT_SHA256
        or prompt["text"] != prompt_text
    ):
        _fail(UsageOrInputError, "Grounded metadata prompt identity changed.")
    model = _require_object(batch["model"], "$.model")
    _require_exact_keys(model, {"repositoryId", "revision", "manifestSha256"}, "$.model")
    if (
        model["repositoryId"] != MODEL_REPOSITORY
        or model["revision"] != MODEL_REVISION
        or _require_sha256(model["manifestSha256"], "$.model.manifestSha256")
        != MODEL_MANIFEST_SHA256
    ):
        _fail(UsageOrInputError, "Grounded metadata model identity changed.")
    values = _require_array(batch["requests"], "$.requests", maximum=MAXIMUM_CASES)
    if not values:
        _fail(UsageOrInputError, "Grounded metadata batch is empty.")
    media_hash_cache: dict[Path, str] = {}
    requests = [
        _validate_request(item, index, media_hash_cache)
        for index, item in enumerate(values)
    ]
    identities = [(item["candidateId"], item["attempt"]) for item in requests]
    if len(set(identities)) != len(identities):
        _fail(UsageOrInputError, "Grounded metadata candidate attempts must be unique.")
    for request in requests:
        request["caseId"] = request["candidateId"]
        request["candidate"] = {"id": request["candidateId"]}
    return requests, prompt_text


def _infer_grouped_requests(
    requests: list[dict[str, Any]],
    case_hashes: list[str],
    prompt_text: str,
    model: Any,
    processor: Any,
    torch: Any,
    torchcodec: Any,
    process_vision_info: Any,
    session: StructuredDecodingSession,
) -> list[dict[str, Any]]:
    """Reuse in-memory grounding only across exactly compatible attempt inputs."""
    packet_cache: dict[str, tuple[str, Any]] = {}
    accepted_titles: dict[RerollTitleScope, list[RerollTitleReference]] = {}
    results: list[dict[str, Any]] = []
    for case_ordinal, request in enumerate(requests, start=1):
        _set_failure_case(
            request,
            case_ordinal,
            case_hashes[case_ordinal - 1],
        )
        _set_failure_stage("Inference")
        with grounded_case_watchdog(
            request.get("caseId", request["candidateId"]),
            request["candidateId"],
            case_ordinal,
        ) as watchdog:
            identity_sha256, canonical_identity = _grounding_reuse_identity(request)
            cached = packet_cache.get(identity_sha256)
            if cached is None:
                packet = _build_grounding_packet(
                    request,
                    case_ordinal,
                    model,
                    processor,
                    torch,
                    torchcodec,
                    process_vision_info,
                    session,
                )
                packet_cache[identity_sha256] = (canonical_identity, packet)
                reused = False
            else:
                cached_identity, packet = cached
                if cached_identity != canonical_identity:
                    _fail(
                        UsageOrInputError,
                        "Grounding reuse identity collision was rejected.",
                    )
                reused = True
            title_scope = _reroll_title_scope(request)
            prior_accepted_titles = tuple(accepted_titles.get(title_scope, ()))
            result = _synthesize_case(
                request,
                case_ordinal,
                prompt_text,
                packet,
                reused,
                model,
                processor,
                torch,
                torchcodec,
                process_vision_info,
                session,
                prior_accepted_titles,
            )
            result["generationWatchdog"] = (
                grounded_case_watchdog_success_payload(watchdog)
            )
            accepted_title = _reroll_title_reference(
                request,
                result["metadata"]["title"],
            )
            results.append(result)
        title_history = accepted_titles.setdefault(title_scope, [])
        title_history.append(accepted_title)
        if len(title_history) > MAXIMUM_RETAINED_TITLES:
            del title_history[:-MAXIMUM_RETAINED_TITLES]
    return results


def run_grounded_editorial_metadata_batch(
    model_path: Path,
    input_path: Path,
    output_path: Path,
    qualification_lock_path: Path,
    ffmpeg_directory: Path,
    failure_output_path: Path | None,
) -> None:
    started = time.perf_counter()
    _set_failure_stage("InputLoading")
    batch_value = _load_strict_json(input_path)
    _record_input_failure_identity(input_path, batch_value)
    case_hashes = _input_case_hashes(batch_value)
    requests, prompt_text = _validate_batch(batch_value)
    _validate_failure_output_against_media(failure_output_path, requests)
    lock = validate_qualification_lock(_load_strict_json(qualification_lock_path))
    require_frozen_packages()
    _set_failure_stage("RuntimeInitialization")
    torch, torchcodec, transformers, process_vision_info = _load_runtime(ffmpeg_directory)
    configure_grounded_cuda_memory(torch)
    _validate_model_directory(model_path)
    try:
        model, processor = _load_model_and_processor(
            model_path,
            torch,
            transformers,
            device_map=GROUNDED_MODEL_LOAD_DEVICE_MAP,
            placement_finalizer=finalize_grounded_model_placement,
            placement_validator=validate_grounded_model_placement,
        )
    except InitializationError as error:
        if is_cuda_out_of_memory(error, torch):
            record_grounded_cuda_out_of_memory(torch)
        raise
    session = StructuredDecodingSession(processor.tokenizer, model_vocab_size(model))
    try:
        results = _infer_grouped_requests(
            requests,
            case_hashes,
            prompt_text,
            model,
            processor,
            torch,
            torchcodec,
            process_vision_info,
            session,
        )
        grounded_memory_policy = complete_grounded_cuda_memory(torch)
        output = {
            "schemaVersion": OUTPUT_SCHEMA,
            "policyVersion": POLICY_VERSION,
            "promptSha256": PROMPT_SHA256,
            "generationWatchdogPolicy":
                generation_watchdog_policy_payload(),
            "groundedMemoryPolicy": grounded_memory_policy,
            "qualificationLockCanonicalHash": lock["canonicalHash"],
            "results": results,
            "peakAllocatedGpuBytes":
                grounded_memory_policy["peakAllocatedGpuBytes"],
            "totalElapsedSeconds": round(time.perf_counter() - started, 6),
        }
        output["canonicalHash"] = _canonical_json_sha256(output)
        _clear_failure_case()
        _set_failure_stage("OutputWrite")
        _write_json_atomic(output_path, output)
    finally:
        del processor
        del model
        torch.cuda.empty_cache()


__all__ = [name for name in globals() if not name.startswith("__")]
