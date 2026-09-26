"""ReplayFoundry local visual-semantic host implementation module."""
from __future__ import annotations

from .model_runtime import *  # noqa: F401,F403

def _revalidate_media_inputs(
    requests: list[dict[str, Any]],
    input_case_hashes: list[str] | None = None,
) -> None:
    if (
        input_case_hashes is not None
        and len(input_case_hashes) != len(requests)
    ):
        raise ValueError(
            "Media revalidation case hashes must match request cardinality."
        )
    verified_paths: set[Path] = set()
    for index, request in enumerate(requests):
        if input_case_hashes is not None:
            _set_failure_case(
                request,
                index + 1,
                input_case_hashes[index],
            )
        validated = request["_validated"]
        video_path: Path = validated["videoPath"]
        if video_path in verified_paths:
            continue
        try:
            stat = video_path.stat()
        except OSError as error:
            _fail(
                InferenceError,
                f"Review video disappeared during inference: '{video_path}': {error}",
            )
        actual_last_write = datetime.fromtimestamp(
            stat.st_mtime,
            tz=timezone.utc,
        )
        if (
            stat.st_size != validated["expectedVideoLength"]
            or abs(
                (
                    actual_last_write
                    - validated["expectedLastWriteUtc"]
                ).total_seconds()
            )
            > 0.001
            or _sha256_file(
                video_path,
                error_type=InferenceError,
            )
            != validated["expectedVideoHash"]
        ):
            _fail(
                InferenceError,
                f"Review video changed during inference: '{video_path}'.",
            )
        verified_paths.add(video_path)


def _input_case_hashes(batch_value: Any) -> list[str]:
    if not isinstance(batch_value, dict):
        return []
    requests = batch_value.get("requests")
    if not isinstance(requests, list):
        return []
    return [
        _canonical_json_sha256(request)
        for request in requests
    ]


def _record_input_failure_identity(
    input_path: Path,
    batch_value: Any,
) -> None:
    values: dict[str, str | None] = {
        "inputBatchSha256": _sha256_file(
            input_path,
            error_type=UsageOrInputError,
        ),
    }
    if isinstance(batch_value, dict):
        model = batch_value.get("model")
        if isinstance(model, dict):
            manifest_hash = model.get("manifestSha256")
            if (
                isinstance(manifest_hash, str)
                and SHA256_PATTERN.fullmatch(manifest_hash)
            ):
                values["modelManifestSha256"] = manifest_hash.lower()
        prompt = batch_value.get("prompt")
        if isinstance(prompt, dict):
            prompt_hash = prompt.get("sha256")
            if (
                isinstance(prompt_hash, str)
                and SHA256_PATTERN.fullmatch(prompt_hash)
            ):
                values["promptSha256"] = prompt_hash.lower()
    _set_failure_identity(**values)


def _clear_failure_case() -> None:
    _FAILURE_CONTEXT["case"] = None
    _FAILURE_CONTEXT["videoArtifact"] = None
    _FAILURE_CONTEXT["timing"] = None
    _FAILURE_CONTEXT["sampling"] = _empty_failure_sampling()
    _FAILURE_CONTEXT["caseAuditSections"] = _empty_case_audit_sections()
    _FAILURE_CONTEXT["generation"] = None
    _FAILURE_CONTEXT["generationWatchdog"] = None
    _FAILURE_CONTEXT["caseGeneration"] = None
    _FAILURE_CONTEXT["executionTiming"] = None
    _FAILURE_CONTEXT["structuredDecodingAudit"] = None
    _FAILURE_CONTEXT["providerOutput"] = {}
    _FAILURE_CONTEXT["recoveryPoolLedger"] = []
    _set_failure_identity(inputCaseSha256=None)


def _validate_failure_output_against_media(
    failure_output_path: Path | None,
    requests: list[dict[str, Any]],
) -> None:
    if failure_output_path is None:
        return
    roots: dict[str, Path] = {}
    for request in requests:
        media_root = request["_validated"]["videoPath"].parent
        roots[str(media_root).casefold()] = media_root
    _require_path_outside_roots(
        failure_output_path,
        [
            ("source-media directory", root)
            for root in roots.values()
        ],
        "--failure-output",
    )
    _approve_failure_output()



__all__ = [name for name in globals() if not name.startswith("__")]
