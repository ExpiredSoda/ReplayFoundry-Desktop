"""ReplayFoundry local visual-semantic host implementation module."""
from __future__ import annotations

from .request_validation import *  # noqa: F401,F403

def _assert_cuda_only_model(model: Any, torch: Any) -> None:
    if bool(getattr(model, "is_quantized", False)) or getattr(
        model,
        "quantization_method",
        None,
    ) is not None:
        _fail(
            InitializationError,
            "Quantized model execution is prohibited by the frozen BF16 policy.",
        )

    device_map = getattr(model, "hf_device_map", None)
    if isinstance(device_map, dict):
        for module_name, raw_device in device_map.items():
            device_text = str(raw_device).casefold()
            if device_text not in {"0", "cuda", "cuda:0"}:
                _fail(
                    InitializationError,
                    f"Model module '{module_name}' was placed on prohibited "
                    f"device '{raw_device}'.",
                )

    seen_parameter = False
    for name, parameter in model.named_parameters():
        seen_parameter = True
        if (
            parameter.device.type != "cuda"
            or parameter.device.index not in {None, 0}
        ):
            _fail(
                InitializationError,
                f"Model parameter '{name}' is not on CUDA device 0.",
            )
        if (
            parameter.is_floating_point()
            and parameter.dtype != torch.bfloat16
        ):
            _fail(
                InitializationError,
                f"Model parameter '{name}' is not BF16.",
            )
    if not seen_parameter:
        _fail(InitializationError, "Loaded model contains no parameters.")
    for name, buffer in model.named_buffers():
        if (
            buffer.device.type != "cuda"
            or buffer.device.index not in {None, 0}
        ):
            _fail(
                InitializationError,
                f"Model buffer '{name}' is not on CUDA device 0.",
            )

def _install_network_prohibition() -> None:
    def audit_hook(event: str, args: tuple[Any, ...]) -> None:
        if event in {
            "socket.__new__",
            "socket.bind",
            "socket.connect",
            "socket.getaddrinfo",
            "socket.gethostbyaddr",
            "socket.gethostbyname",
            "socket.gethostbyname_ex",
            "socket.sendto",
        }:
            raise NetworkProhibitedError(
                f"Network operation '{event}' is prohibited by the offline host."
            )

    sys.addaudithook(audit_hook)


def _configure_ffmpeg_shared_library_directory(
    ffmpeg_directory: Path,
) -> tuple[Any, str | None]:
    if sys.platform != "win32" or sys.maxsize <= 2**32:
        _fail(
            InitializationError,
            "The frozen research host requires 64-bit Windows.",
        )

    dll_names = [
        path.name.casefold()
        for path in ffmpeg_directory.glob("*.dll")
        if path.is_file()
    ]
    for prefix in REQUIRED_FFMPEG_DLL_PREFIXES:
        if not any(name.startswith(prefix) for name in dll_names):
            _fail(
                InitializationError,
                "The explicit shared-FFmpeg directory is missing required "
                f"library '{prefix}*.dll'.",
            )

    original_path = os.environ.get("PATH")
    os.environ["PATH"] = (
        str(ffmpeg_directory)
        if not original_path
        else str(ffmpeg_directory) + os.pathsep + original_path
    )
    try:
        dll_cookie = os.add_dll_directory(str(ffmpeg_directory))
    except (AttributeError, OSError) as error:
        if original_path is None:
            os.environ.pop("PATH", None)
        else:
            os.environ["PATH"] = original_path
        _fail(
            InitializationError,
            "Could not register the explicit shared-FFmpeg directory: "
            f"{type(error).__name__}: {error}",
        )
    return dll_cookie, original_path


def _restore_process_library_path(
    dll_cookie: Any,
    original_path: str | None,
) -> None:
    try:
        dll_cookie.close()
    finally:
        if original_path is None:
            os.environ.pop("PATH", None)
        else:
            os.environ["PATH"] = original_path


def _loaded_ffmpeg_libraries(
    ffmpeg_directory: Path,
) -> list[dict[str, Any]]:
    try:
        import psutil
    except Exception as error:
        _fail(
            InitializationError,
            "Could not import the pinned process-inspection runtime: "
            f"{type(error).__name__}: {error}",
        )

    root = ffmpeg_directory.resolve()
    modules: dict[str, Path] = {}
    try:
        memory_maps = psutil.Process().memory_maps()
    except Exception as error:
        _fail(
            InitializationError,
            "Could not inspect loaded shared libraries: "
            f"{type(error).__name__}: {error}",
        )
    for mapping in memory_maps:
        raw_path = getattr(mapping, "path", "")
        if not raw_path:
            continue
        path = Path(raw_path)
        try:
            resolved = path.resolve(strict=True)
            relative = resolved.relative_to(root)
        except (OSError, ValueError):
            continue
        if resolved.is_file() and resolved.suffix.casefold() == ".dll":
            modules[str(relative).casefold()] = resolved

    result: list[dict[str, Any]] = []
    for relative_key in sorted(modules):
        path = modules[relative_key]
        result.append(
            {
                "relativePath": path.relative_to(root).as_posix(),
                "byteLength": path.stat().st_size,
                "sha256": _sha256_file(
                    path,
                    error_type=InitializationError,
                ),
            }
        )

    loaded_names = {
        Path(item["relativePath"]).name.casefold()
        for item in result
    }
    for prefix in REQUIRED_FFMPEG_DLL_PREFIXES:
        if not any(name.startswith(prefix) for name in loaded_names):
            _fail(
                InitializationError,
                "TorchCodec did not load required shared-FFmpeg library "
                f"'{prefix}*.dll' from the explicit directory.",
            )
    return result


def _loaded_ffmpeg_libraries_canonical_sha256(
    libraries: list[dict[str, Any]],
) -> str:
    canonical = "".join(
        f"{Path(item['relativePath']).name}\t"
        f"{item['byteLength']}\t{item['sha256'].upper()}\n"
        for item in sorted(
            libraries,
            key=lambda value: Path(value["relativePath"]).name,
        )
    )
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest().upper()


def _ffmpeg_version(torchcodec: Any) -> str:
    try:
        versions = torchcodec._core.get_ffmpeg_library_versions()
    except Exception as error:
        _fail(
            InitializationError,
            "TorchCodec did not report its loaded FFmpeg version: "
            f"{type(error).__name__}: {error}",
        )
    if not isinstance(versions, dict):
        _fail(
            InitializationError,
            "TorchCodec returned malformed FFmpeg version metadata.",
        )
    value = versions.get("ffmpeg_version")
    if (
        not isinstance(value, str)
        or not value.strip()
        or len(value.strip()) > 256
    ):
        _fail(
            InitializationError,
            "TorchCodec returned malformed FFmpeg version text.",
        )
    return value.strip()


def _load_runtime(ffmpeg_directory: Path) -> tuple[Any, Any, Any, Any]:
    _install_network_prohibition()
    if sys.platform != "win32" or sys.maxsize <= 2**32:
        _fail(
            InitializationError,
            "The frozen research host requires 64-bit Windows.",
        )
    if sys.version_info[:3] != (3, 11, 9):
        _fail(
            InitializationError,
            "The frozen research host requires CPython 3.11.9.",
        )
    try:
        import torch
        import torchcodec
        import transformers
        from qwen_vl_utils import process_vision_info
        from qwen_vl_utils.vision_process import get_video_reader_backend
    except HostError:
        raise
    except Exception as error:
        _fail(
            InitializationError,
            f"Could not import the pinned Qwen runtime: "
            f"{type(error).__name__}: {error}",
        )

    actual_versions = {
        name: _package_version(name)
        for name in EXPECTED_PACKAGE_VERSIONS
    }
    for name, expected in EXPECTED_PACKAGE_VERSIONS.items():
        if actual_versions[name] != expected:
            _fail(
                InitializationError,
                f"Python package '{name}' is {actual_versions[name]}; "
                f"the frozen host requires {expected}.",
            )

    try:
        selected_backend = get_video_reader_backend()
    except Exception as error:
        _fail(
            InitializationError,
            "Qwen video-backend selection failed: "
            f"{type(error).__name__}: {error}",
        )
    if selected_backend != VIDEO_BACKEND:
        _fail(
            InitializationError,
            f"Qwen selected video backend '{selected_backend}'; "
            f"the frozen host requires '{VIDEO_BACKEND}' and prohibits fallback.",
        )

    # Importing TorchCodec loads its native library and the FFmpeg dependency
    # family. Verify that each required FFmpeg DLL came from the explicit
    # external directory before any model checkpoint is loaded.
    _ffmpeg_version(torchcodec)
    _loaded_ffmpeg_libraries(ffmpeg_directory)

    if not torch.cuda.is_available():
        _fail(
            InitializationError,
            "CUDA is unavailable; CPU fallback is prohibited.",
        )
    if torch.cuda.device_count() < 1:
        _fail(InitializationError, "CUDA device 0 is unavailable.")
    if not torch.cuda.is_bf16_supported():
        _fail(
            InitializationError,
            "CUDA device 0 does not report BF16 support.",
        )

    torch.cuda.set_device(0)
    return torch, torchcodec, transformers, process_vision_info


def _runtime_package_manifest(
    torch: Any,
    torchcodec: Any,
    transformers: Any,
    ffmpeg_directory: Path,
) -> dict[str, Any]:
    properties = torch.cuda.get_device_properties(0)
    loaded_libraries = _loaded_ffmpeg_libraries(ffmpeg_directory)
    return {
        "python": sys.version.split()[0],
        "torch": _package_version("torch"),
        "torchvision": _package_version("torchvision"),
        "transformers": _package_version("transformers"),
        "qwenVlUtils": _package_version("qwen-vl-utils"),
        "accelerate": _package_version("accelerate"),
        "torchcodec": _package_version("torchcodec"),
        "psutil": _package_version("psutil"),
        "ffmpegVersion": _ffmpeg_version(torchcodec),
        "ffmpegSharedLibraryDirectory": str(ffmpeg_directory),
        "ffmpegLoadedLibrariesCanonicalSha256":
            _loaded_ffmpeg_libraries_canonical_sha256(loaded_libraries),
        "videoDecodeDevice": VIDEO_DECODE_DEVICE,
        "cudaRuntime": str(torch.version.cuda),
        "cudnn": str(torch.backends.cudnn.version()),
        "gpuName": str(properties.name),
        "gpuTotalMemoryBytes": str(int(properties.total_memory)),
        "computeCapability": f"{properties.major}.{properties.minor}",
        "bfloat16Supported": str(bool(torch.cuda.is_bf16_supported())).lower(),
        "hostVersion": HOST_VERSION,
        "normalizationPolicyVersion": NORMALIZATION_POLICY_VERSION,
        "normalizationPolicySha256": NORMALIZATION_POLICY_SHA256,
        "generationPolicyVersion": GENERATION_POLICY_VERSION,
        "generationPolicySha256": GENERATION_POLICY_SHA256,
        "trustedIdentityBindingPolicyVersion":
            IDENTITY_BINDING_POLICY_VERSION,
        "trustedIdentityBindingPolicySha256":
            IDENTITY_BINDING_POLICY_SHA256,
        "generationPolicyMaximumNewTokens":
            str(ACTIVE_POLICY_MAX_NEW_TOKENS),
        "generationRuntimeMaximumNewTokens":
            str(MAX_NEW_TOKENS),
        "generationDoSample": "false",
        "generationNumberOfBeams": str(NUMBER_OF_BEAMS),
        "generationUseCache": "true",
        "generationPhaseADiagnosticGateActive": str(
            MAX_NEW_TOKENS != ACTIVE_POLICY_MAX_NEW_TOKENS
        ).lower(),
    }


def _raw_audit_identity(
    batch: dict[str, Any],
    input_path: Path,
    runtime_packages: dict[str, Any],
) -> dict[str, Any]:
    prompt = batch["prompt"]
    model = batch["model"]
    return {
        "host": {
            "version": HOST_VERSION,
            "scriptSha256": _sha256_file(
                HOST_ENTRY_PATH.resolve(),
                error_type=OutputError,
            ),
        },
        "model": {
            "repository": MODEL_REPOSITORY,
            "revision": MODEL_REVISION,
            "manifestSha256": model["manifestSha256"],
        },
        "prompt": {
            "name": PROMPT_NAME,
            "version": PROMPT_VERSION,
            "sha256": prompt["sha256"],
        },
        "outputNormalizationPolicy": {
            "version": NORMALIZATION_POLICY_VERSION,
            "sha256": NORMALIZATION_POLICY_SHA256,
            "applied": False,
        },
        "input": {
            "schemaVersion": INPUT_SCHEMA,
            "batchSha256": _sha256_file(
                input_path,
                error_type=OutputError,
            ),
            "caseSha256": _canonical_json_sha256(batch["requests"][0]),
        },
        "environment": {
            "backend": BACKEND,
            "videoBackend": VIDEO_BACKEND,
            "identityKind":
                "host-observed-runtime-package-manifest-1.0",
            "hostObservedEnvironmentSha256":
                _canonical_json_sha256(runtime_packages),
            "ffmpegLoadedLibrariesCanonicalSha256":
                runtime_packages[
                    "ffmpegLoadedLibrariesCanonicalSha256"
                ],
        },
        "generation": {
            "seed": 0,
            "policyVersion": GENERATION_POLICY_VERSION,
            "policySha256": GENERATION_POLICY_SHA256,
            "maxNewTokens": MAX_NEW_TOKENS,
            "activePolicyMaxNewTokens":
                ACTIVE_POLICY_MAX_NEW_TOKENS,
            "doSample": False,
            "numberOfBeams": NUMBER_OF_BEAMS,
            "useCache": True,
            "forcedEndOfSequencePermitted": False,
            "stopStringsPermitted": False,
            "automaticRetryPermitted": False,
            "phaseADiagnosticGateActive":
                MAX_NEW_TOKENS !=
                ACTIVE_POLICY_MAX_NEW_TOKENS,
            "skipSpecialTokens": True,
            "cleanUpTokenizationSpaces": False,
        },
    }


def _load_model_and_processor(
    model_path: Path,
    torch: Any,
    transformers: Any,
    *,
    device_map: dict[str, Any] | None = None,
    placement_finalizer: Any | None = None,
    placement_validator: Any | None = None,
) -> tuple[Any, Any]:
    try:
        model = transformers.AutoModelForImageTextToText.from_pretrained(
            str(model_path),
            local_files_only=True,
            trust_remote_code=False,
            dtype=torch.bfloat16,
            device_map={"": DEVICE} if device_map is None else device_map,
            low_cpu_mem_usage=True,
            attn_implementation="sdpa",
        )
        processor = transformers.AutoProcessor.from_pretrained(
            str(model_path),
            local_files_only=True,
            trust_remote_code=False,
        )
        model.eval()
        if placement_finalizer is not None:
            placement_finalizer(model, torch)
    except HostError:
        raise
    except Exception as error:
        raise InitializationError(
            f"Pinned model initialization failed: "
            f"{type(error).__name__}: {error}"
        ) from error
    if placement_validator is None:
        _assert_cuda_only_model(model, torch)
    else:
        placement_validator(model, torch)
    return model, processor



__all__ = [name for name in globals() if not name.startswith("__")]
