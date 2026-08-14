"""Frozen CUDA-memory bounds for grounded editorial metadata."""
from __future__ import annotations

from dataclasses import dataclass
from contextlib import contextmanager
import hashlib
import math
from pathlib import Path
from typing import Any

from .errors import InitializationError


POLICY_VERSION = "grounded-editorial-cuda-memory-1.5"
POLICY_FILE_NAME = (
    "replayfoundry-grounded-editorial-cuda-memory-policy-1.5.txt"
)
# SHA-256 of the normalized policy text beside the host entry point.
POLICY_SHA256 = (
    "732b33e80cb0e8a50c44f75b1f84e16aefe8044ae3cd88189b18a19e01e4220b"
)
CUDA_DEVICE_INDEX = 0
RESERVED_ALLOCATOR_HEADROOM_BYTES = 3 * 1024 * 1024 * 1024
QUALIFICATION_REFERENCE_ARTIFACT_NAME = "real-qwen-metadata-v1.6.json"
QUALIFICATION_REFERENCE_ARTIFACT_SCHEMA = (
    "replayfoundry-editorial-metadata-real-quality-1.0"
)
QUALIFICATION_REFERENCE_ARTIFACT_SHA256 = (
    "0EC7F4BE4DD3664091D6808176B2FEA36B7FE016B277422DACA00C4C9D28EC70"
)
QUALIFICATION_REFERENCE_PEAK_ALLOCATED_BYTES = 11_705_485_312
MINIMUM_VIABLE_ALLOCATOR_LIMIT_BYTES = (
    QUALIFICATION_REFERENCE_PEAK_ALLOCATED_BYTES + 1
)
CACHE_IMPLEMENTATION = "offloaded"
ATTENTION_IMPLEMENTATION = "sdpa"
SDPA_BACKEND = "CudnnAttention"
SDPA_BACKEND_FORCED = True
ATTENTION_FALLBACK_PERMITTED = False
ALLOCATOR_SCOPE = "PyTorchNativeCudaCachingAllocator"
STARTUP_GATE = "FreeMemoryMinusReserveExceedsQualificationPeak"
GLOBAL_FREE_MEMORY_GUARANTEED = False
CPU_MODEL_OFFLOAD_PERMITTED = True
QUANTIZATION_PERMITTED = False
AUTOMATIC_FALLBACK_PERMITTED = False
_FRACTION_TOLERANCE = 1e-12

GROUNDED_MODEL_LOAD_DEVICE_MAP = {
    "model.visual": "cpu",
    "model.language_model": CUDA_DEVICE_INDEX,
    "lm_head": CUDA_DEVICE_INDEX,
}
GROUNDED_VISION_MODULE = "model.visual"
GROUNDED_VISION_PRELOAD_MODULE_CLASS = "Qwen3VLVisionModel"
GROUNDED_MODEL_DEVICE_MAP = dict(GROUNDED_MODEL_LOAD_DEVICE_MAP)
VISION_PARAMETER_PREFIX = "model.visual."


PRE_GENERATION_GATE = "CurrentFreeMemoryAtLeastFixedReserve"
RUNTIME_OUTCOME_CONFIGURED = "Configured"
RUNTIME_OUTCOME_GENERATION_ADMITTED = "GenerationAdmitted"
RUNTIME_OUTCOME_COMPLETED = "Completed"
RUNTIME_OUTCOME_STARTUP_REJECTED = "StartupAdmissionRejected"
RUNTIME_OUTCOME_PRE_GENERATION_REJECTED = (
    "PreGenerationAdmissionRejected"
)
RUNTIME_OUTCOME_CUDA_OOM = "CudaAllocatorOutOfMemory"
REASON_INSUFFICIENT_STARTUP_FREE_MEMORY = (
    "InsufficientStartupFreeMemory"
)
REASON_INSUFFICIENT_PRE_GENERATION_FREE_MEMORY = (
    "InsufficientPreGenerationFreeMemory"
)
REASON_ALLOCATOR_LIMIT_EXCEEDED = "AllocatorLimitExceeded"
REASON_CUDA_ALLOCATOR_OOM = "CudaAllocatorOutOfMemory"


@dataclass
class GroundedCudaMemoryPolicyApplication:
    total_device_memory_bytes: int
    startup_free_memory_bytes: int
    allocator_limit_bytes: int
    allocator_fraction: float
    observed_allocator_fraction: float | None
    pre_generation_admission_count: int = 0
    minimum_pre_generation_free_device_memory_bytes: int | None = None
    last_pre_generation_free_device_memory_bytes: int | None = None
    peak_allocated_gpu_bytes: int | None = None
    peak_reserved_gpu_bytes: int | None = None
    end_allocated_gpu_bytes: int | None = None
    end_reserved_gpu_bytes: int | None = None
    end_free_device_memory_bytes: int | None = None
    runtime_outcome: str = RUNTIME_OUTCOME_CONFIGURED
    failure_reason: str | None = None

    def payload(self) -> dict[str, Any]:
        return {
            "policyVersion": POLICY_VERSION,
            "policySha256": POLICY_SHA256,
            "cudaDeviceIndex": CUDA_DEVICE_INDEX,
            "cacheImplementation": CACHE_IMPLEMENTATION,
            "attentionImplementation": ATTENTION_IMPLEMENTATION,
            "sdpaBackend": SDPA_BACKEND,
            "sdpaBackendForced": SDPA_BACKEND_FORCED,
            "attentionFallbackPermitted": ATTENTION_FALLBACK_PERMITTED,
            "allocatorScope": ALLOCATOR_SCOPE,
            "startupGate": STARTUP_GATE,
            "preGenerationGate": PRE_GENERATION_GATE,
            "totalDeviceMemoryBytes": self.total_device_memory_bytes,
            "startupFreeMemoryBytes": self.startup_free_memory_bytes,
            "startupExternallyOccupiedMemoryBytes":
                self.total_device_memory_bytes
                - self.startup_free_memory_bytes,
            "requiredStartupFreeMemoryBytes":
                RESERVED_ALLOCATOR_HEADROOM_BYTES
                + MINIMUM_VIABLE_ALLOCATOR_LIMIT_BYTES,
            "reservedAllocatorHeadroomBytes":
                RESERVED_ALLOCATOR_HEADROOM_BYTES,
            "allocatorLimitBytes": self.allocator_limit_bytes,
            "minimumViableAllocatorLimitBytes":
                MINIMUM_VIABLE_ALLOCATOR_LIMIT_BYTES,
            "allocatorFraction": self.allocator_fraction,
            "observedAllocatorFraction":
                self.observed_allocator_fraction,
            "qualificationReferencePeakAllocatedBytes":
                QUALIFICATION_REFERENCE_PEAK_ALLOCATED_BYTES,
            "qualificationReferenceArtifactName":
                QUALIFICATION_REFERENCE_ARTIFACT_NAME,
            "qualificationReferenceArtifactSchema":
                QUALIFICATION_REFERENCE_ARTIFACT_SCHEMA,
            "qualificationReferenceArtifactSha256":
                QUALIFICATION_REFERENCE_ARTIFACT_SHA256,
            "preGenerationAdmissionCount":
                self.pre_generation_admission_count,
            "minimumPreGenerationFreeDeviceMemoryBytes":
                self.minimum_pre_generation_free_device_memory_bytes,
            "lastPreGenerationFreeDeviceMemoryBytes":
                self.last_pre_generation_free_device_memory_bytes,
            "peakAllocatedGpuBytes": self.peak_allocated_gpu_bytes,
            "peakReservedGpuBytes": self.peak_reserved_gpu_bytes,
            "endAllocatedGpuBytes": self.end_allocated_gpu_bytes,
            "endReservedGpuBytes": self.end_reserved_gpu_bytes,
            "endFreeDeviceMemoryBytes":
                self.end_free_device_memory_bytes,
            "runtimeOutcome": self.runtime_outcome,
            "failureReason": self.failure_reason,
            "globalFreeMemoryGuaranteed":
                GLOBAL_FREE_MEMORY_GUARANTEED,
            "cpuModelOffloadPermitted": CPU_MODEL_OFFLOAD_PERMITTED,
            "quantizationPermitted": QUANTIZATION_PERMITTED,
            "automaticFallbackPermitted":
                AUTOMATIC_FALLBACK_PERMITTED,
        }


_ACTIVE_APPLICATION: GroundedCudaMemoryPolicyApplication | None = None


@contextmanager
def grounded_sdpa_context(torch: Any):
    """Force the qualified fused attention backend for one generation."""
    try:
        if (
            not torch.backends.cudnn.is_available()
            or not torch.backends.cuda.cudnn_sdp_enabled()
        ):
            raise RuntimeError("cuDNN attention is unavailable")
        attention = torch.nn.attention
        backend = attention.SDPBackend.CUDNN_ATTENTION
        context = attention.sdpa_kernel(backend)
    except Exception as error:
        raise InitializationError(
            "The pinned runtime cannot force the qualified cuDNN scaled "
            "dot product attention backend."
        ) from error
    try:
        with context:
            yield
    except RuntimeError as error:
        if "no available kernel" not in str(error).casefold():
            raise
        raise InitializationError(
            "The pinned runtime could not execute the qualified cuDNN "
            "scaled dot product attention backend."
        ) from error


def _publish(application: GroundedCudaMemoryPolicyApplication) -> None:
    from .failure_state import _set_failure_grounded_memory_policy

    _set_failure_grounded_memory_policy(application.payload())


def _memory_snapshot(torch: Any) -> tuple[int, int, int, int]:
    free, total = torch.cuda.mem_get_info(CUDA_DEVICE_INDEX)
    return (
        int(free),
        int(total),
        int(torch.cuda.memory_allocated(CUDA_DEVICE_INDEX)),
        int(torch.cuda.memory_reserved(CUDA_DEVICE_INDEX)),
    )


def _set_runtime_snapshot(
    application: GroundedCudaMemoryPolicyApplication,
    torch: Any,
) -> None:
    free, total, allocated, reserved = _memory_snapshot(torch)
    if total != application.total_device_memory_bytes:
        raise InitializationError(
            "CUDA total memory changed during grounded metadata execution."
        )
    application.end_free_device_memory_bytes = free
    application.end_allocated_gpu_bytes = allocated
    application.end_reserved_gpu_bytes = reserved
    application.peak_allocated_gpu_bytes = int(
        torch.cuda.max_memory_allocated(CUDA_DEVICE_INDEX)
    )
    application.peak_reserved_gpu_bytes = int(
        torch.cuda.max_memory_reserved(CUDA_DEVICE_INDEX)
    )


def _normalized_policy_sha256() -> str:
    path = Path(__file__).resolve().parent.parent / POLICY_FILE_NAME
    text = path.read_text(encoding="utf-8").replace(
        "\r\n", "\n"
    ).replace("\r", "\n").strip()
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def _accelerate_hooks(hook: Any) -> tuple[Any, ...]:
    """Flatten Accelerate's optional SequentialHook without importing it."""
    nested = getattr(hook, "hooks", None)
    if not isinstance(nested, (tuple, list)):
        return (hook,)
    return tuple(
        child
        for item in nested
        for child in _accelerate_hooks(item)
    )


def _is_cuda_zero(device: Any) -> bool:
    if isinstance(device, int):
        return device == CUDA_DEVICE_INDEX
    device_type = getattr(device, "type", None)
    device_index = getattr(device, "index", None)
    return (
        device_type == "cuda"
        and device_index in {None, CUDA_DEVICE_INDEX}
    ) or str(device) in {"cuda", "cuda:0"}


def _offloaded_visual_tensor_backing(
    module: Any,
    tensor_name: str,
    torch: Any,
) -> Any | None:
    """Validate Accelerate's CPU backing store for one dormant meta tensor.

    ``dispatch_model`` intentionally parks CPU-offloaded parameters on the
    meta device.  Its AlignDevicesHook materializes each tensor on CUDA 0 for
    the vision forward, then returns it to meta while the authoritative BF16
    value remains in the hook's CPU ``weights_map``.  Inspecting only
    ``parameter.device`` therefore rejects the exact qualified layout.
    """
    hook = getattr(module, "_hf_hook", None)
    for candidate in _accelerate_hooks(hook) if hook is not None else ():
        weights_map = getattr(candidate, "weights_map", None)
        if (
            getattr(candidate, "offload", False) is not True
            or not _is_cuda_zero(
                getattr(candidate, "execution_device", None)
            )
            or weights_map is None
        ):
            continue
        try:
            stored = weights_map[tensor_name]
        except (KeyError, TypeError, AttributeError):
            continue
        if (
            getattr(getattr(stored, "device", None), "type", None) == "cpu"
            and (
                not stored.is_floating_point()
                or stored.dtype == torch.bfloat16
            )
        ):
            return stored
    return None


def _remove_accelerate_hook(module: Any, *, recurse: bool = False) -> None:
    try:
        from accelerate.hooks import remove_hook_from_module
    except Exception as error:
        raise InitializationError(
            "Pinned Accelerate hook removal is unavailable."
        ) from error
    remove_hook_from_module(module, recurse=recurse)


def _restore_accelerate_tensor(
    module: Any,
    tensor_name: str,
    value: Any,
) -> None:
    try:
        from accelerate.utils.modeling import set_module_tensor_to_device
    except Exception as error:
        raise InitializationError(
            "Pinned Accelerate tensor restoration is unavailable."
        ) from error
    set_module_tensor_to_device(
        module,
        tensor_name,
        "cpu",
        value=value,
        clear_cache=False,
    )


def _install_root_visual_offload(module: Any, torch: Any) -> None:
    try:
        from accelerate.big_modeling import cpu_offload
    except Exception as error:
        raise InitializationError(
            "Pinned Accelerate root-preload CPU offload is unavailable."
        ) from error
    cpu_offload(
        module,
        execution_device=torch.device("cuda", CUDA_DEVICE_INDEX),
        offload_buffers=False,
        preload_module_classes=[GROUNDED_VISION_PRELOAD_MODULE_CLASS],
    )


def _root_visual_offload_hook(module: Any) -> Any | None:
    hook = getattr(module, "_hf_hook", None)
    for candidate in _accelerate_hooks(hook) if hook is not None else ():
        if (
            getattr(candidate, "offload", False) is True
            and getattr(candidate, "place_submodules", False) is True
            and _is_cuda_zero(
                getattr(candidate, "execution_device", None)
            )
            and getattr(candidate, "weights_map", None) is not None
        ):
            return candidate
    return None


def finalize_grounded_model_placement(model: Any, torch: Any) -> None:
    """Replace fragile leaf offload with one root-preloaded vision stage."""
    device_map = getattr(model, "hf_device_map", None)
    if not isinstance(device_map, dict) or device_map != (
        GROUNDED_MODEL_LOAD_DEVICE_MAP
    ):
        raise InitializationError(
            "Grounded model did not retain its exact load placement map."
        )
    try:
        visual = model.get_submodule(GROUNDED_VISION_MODULE)
    except (AttributeError, KeyError) as error:
        raise InitializationError(
            "Grounded model has no qualified visual encoder."
        ) from error
    if type(visual).__name__ != GROUNDED_VISION_PRELOAD_MODULE_CLASS:
        raise InitializationError(
            "Grounded visual encoder class is not qualified for root preload."
        )
    visual_backings: list[tuple[Any, str, Any]] = []
    for module in visual.modules():
        for tensor_name, parameter in module.named_parameters(recurse=False):
            backing = _offloaded_visual_tensor_backing(
                module, tensor_name, torch
            )
            if (
                parameter.device.type != "meta"
                or backing is None
            ):
                raise InitializationError(
                    "Grounded visual encoder has no complete qualified CPU "
                    "offload backing."
                )
            visual_backings.append((module, tensor_name, backing))
    if not visual_backings:
        raise InitializationError(
            "Grounded visual encoder contained no parameters."
        )

    _remove_accelerate_hook(visual, recurse=True)
    try:
        for module, tensor_name, backing in visual_backings:
            _restore_accelerate_tensor(module, tensor_name, backing)
        visual.to(torch.device("cpu"))
    except Exception as error:
        raise InitializationError(
            "Grounded visual encoder could not be restored to concrete CPU "
            "parameters."
        ) from error
    for parameter in visual.parameters():
        if (
            parameter.device.type != "cpu"
            or parameter.is_floating_point()
            and parameter.dtype != torch.bfloat16
        ):
            raise InitializationError(
                "Grounded visual encoder did not restore exact CPU BF16 "
                "parameters."
            )

    _install_root_visual_offload(visual, torch)
    model.hf_device_map = dict(GROUNDED_MODEL_DEVICE_MAP)


def validate_grounded_model_placement(model: Any, torch: Any) -> None:
    """Require exact BF16 CUDA-language/Accelerate vision offload."""
    if bool(getattr(model, "is_quantized", False)) or getattr(
        model, "quantization_method", None
    ) is not None:
        raise InitializationError(
            "Quantized grounded model execution is prohibited."
        )
    device_map = getattr(model, "hf_device_map", None)
    if not isinstance(device_map, dict) or device_map != (
        GROUNDED_MODEL_DEVICE_MAP
    ):
        raise InitializationError(
            "Grounded model did not retain its exact vision-only CPU "
            "placement map."
        )

    saw_vision = False
    saw_language = False
    try:
        visual = model.get_submodule(GROUNDED_VISION_MODULE)
    except (AttributeError, KeyError) as error:
        raise InitializationError(
            "Grounded model has no qualified visual encoder."
        ) from error
    if type(visual).__name__ != GROUNDED_VISION_PRELOAD_MODULE_CLASS:
        raise InitializationError(
            "Grounded visual encoder class changed after root preload."
        )
    root_hook = _root_visual_offload_hook(visual)
    if root_hook is None:
        raise InitializationError(
            "Grounded visual encoder has no root-preload CPU offload hook."
        )
    root_weights = root_hook.weights_map
    for module_name, module in model.named_modules():
        for tensor_name, parameter in module.named_parameters(recurse=False):
            name = f"{module_name}.{tensor_name}" if module_name else tensor_name
            vision = name.startswith(VISION_PARAMETER_PREFIX)
            saw_vision = saw_vision or vision
            saw_language = saw_language or not vision
            if vision:
                relative_name = name[len(VISION_PARAMETER_PREFIX):]
                try:
                    stored = root_weights[relative_name]
                except (KeyError, TypeError, AttributeError):
                    stored = None
                placement_valid = parameter.device.type == "meta" and (
                    stored is not None
                    and stored.device.type == "cpu"
                    and (
                        not stored.is_floating_point()
                        or stored.dtype == torch.bfloat16
                    )
                ) and (
                    module is visual or not hasattr(module, "_hf_hook")
                )
            else:
                placement_valid = (
                    parameter.device.type == "cuda"
                    and parameter.device.index in {None, CUDA_DEVICE_INDEX}
                )
            if not placement_valid:
                raise InitializationError(
                    f"Grounded model parameter '{name}' changed its "
                    "qualified device placement."
                )
            if parameter.is_floating_point() and parameter.dtype != (
                torch.bfloat16
            ):
                raise InitializationError(
                    f"Grounded model parameter '{name}' is not BF16."
                )
    if not saw_vision or not saw_language:
        raise InitializationError(
            "Grounded model placement did not cover both model branches."
        )
    for name, buffer in model.named_buffers():
        # Accelerate's exact dispatch uses offload_buffers=False: visual
        # buffers remain on the CUDA execution device while visual parameters
        # are backed by CPU tensors and parked on meta between forwards.
        if buffer.device.type != "cuda" or buffer.device.index not in {
            None,
            CUDA_DEVICE_INDEX,
        }:
            raise InitializationError(
                f"Grounded model buffer '{name}' changed its qualified "
                "device placement."
            )


def configure_grounded_cuda_memory(
    torch: Any,
) -> GroundedCudaMemoryPolicyApplication:
    """Apply the fixed allocator ceiling before grounded model loading."""
    global _ACTIVE_APPLICATION
    _ACTIVE_APPLICATION = None
    actual_policy_sha256 = _normalized_policy_sha256()
    if actual_policy_sha256 != POLICY_SHA256:
        raise InitializationError(
            "Grounded CUDA memory policy source changed."
        )

    try:
        properties = torch.cuda.get_device_properties(CUDA_DEVICE_INDEX)
        property_total = int(properties.total_memory)
        startup_free, reported_total = torch.cuda.mem_get_info(
            CUDA_DEVICE_INDEX
        )
        startup_free = int(startup_free)
        reported_total = int(reported_total)
    except Exception as error:
        raise InitializationError(
            "Could not inspect CUDA memory for the grounded metadata "
            f"startup gate: {type(error).__name__}: {error}"
        ) from error

    if (
        property_total <= 0
        or reported_total <= 0
        or property_total != reported_total
        or startup_free < 0
        or startup_free > reported_total
    ):
        raise InitializationError(
            "CUDA reported inconsistent memory totals for the grounded "
            "metadata startup gate."
        )

    allocator_limit = startup_free - RESERVED_ALLOCATOR_HEADROOM_BYTES
    allocator_fraction = allocator_limit / reported_total
    application = GroundedCudaMemoryPolicyApplication(
        total_device_memory_bytes=reported_total,
        startup_free_memory_bytes=startup_free,
        allocator_limit_bytes=allocator_limit,
        allocator_fraction=allocator_fraction,
        observed_allocator_fraction=None,
    )
    _publish(application)
    if allocator_limit < MINIMUM_VIABLE_ALLOCATOR_LIMIT_BYTES:
        application.runtime_outcome = RUNTIME_OUTCOME_STARTUP_REJECTED
        application.failure_reason = (
            REASON_INSUFFICIENT_STARTUP_FREE_MEMORY
        )
        _publish(application)
        raise InitializationError(
            "Grounded metadata did not start because current free GPU "
            f"memory ({startup_free} bytes) cannot preserve the fixed 3 "
            "GiB reserve while retaining its minimum viable allocator "
            f"limit ({MINIMUM_VIABLE_ALLOCATOR_LIMIT_BYTES} bytes). Close "
            "another GPU workload and retry; the policy will not relax "
            "automatically."
        )

    if (
        not math.isfinite(allocator_fraction)
        or allocator_fraction <= 0.0
        or allocator_fraction >= 1.0
    ):
        raise InitializationError(
            "Grounded metadata computed an invalid CUDA allocator fraction."
        )

    try:
        torch.cuda.memory.set_per_process_memory_fraction(
            allocator_fraction,
            CUDA_DEVICE_INDEX,
        )
        observed_fraction = float(
            torch.cuda.memory.get_per_process_memory_fraction(
                CUDA_DEVICE_INDEX
            )
        )
        torch.cuda.reset_peak_memory_stats(CUDA_DEVICE_INDEX)
    except Exception as error:
        raise InitializationError(
            "Could not apply the fixed grounded metadata CUDA allocator "
            f"limit: {type(error).__name__}: {error}"
        ) from error

    if (
        not math.isfinite(observed_fraction)
        or not math.isclose(
            observed_fraction,
            allocator_fraction,
            rel_tol=0.0,
            abs_tol=_FRACTION_TOLERANCE,
        )
    ):
        raise InitializationError(
            "PyTorch did not retain the exact grounded metadata CUDA "
            "allocator fraction."
        )

    try:
        allocated = int(torch.cuda.memory_allocated(CUDA_DEVICE_INDEX))
        reserved = int(torch.cuda.memory_reserved(CUDA_DEVICE_INDEX))
    except Exception as error:
        raise InitializationError(
            "Could not verify existing CUDA allocator use after applying "
            f"the grounded metadata limit: {type(error).__name__}: {error}"
        ) from error
    if (
        allocated < 0
        or reserved < 0
        or allocated > allocator_limit
        or reserved > allocator_limit
    ):
        raise InitializationError(
            "Existing PyTorch CUDA allocations already exceed the fixed "
            "grounded metadata allocator limit."
        )

    application.observed_allocator_fraction = observed_fraction
    _ACTIVE_APPLICATION = application
    _publish(application)
    return application


def admit_grounded_generation(torch: Any) -> None:
    """Release idle cache and fail before generation if headroom is gone."""
    application = _ACTIVE_APPLICATION
    if application is None:
        raise InitializationError(
            "Grounded CUDA memory policy was not configured before generation."
        )
    try:
        torch.cuda.empty_cache()
        free, total, allocated, reserved = _memory_snapshot(torch)
    except Exception as error:
        raise InitializationError(
            "Could not evaluate grounded CUDA pre-generation admission: "
            f"{type(error).__name__}: {error}"
        ) from error
    if total != application.total_device_memory_bytes:
        application.runtime_outcome = (
            RUNTIME_OUTCOME_PRE_GENERATION_REJECTED
        )
        application.failure_reason = REASON_ALLOCATOR_LIMIT_EXCEEDED
        _publish(application)
        raise InitializationError(
            "CUDA total memory changed before grounded model generation."
        )

    application.last_pre_generation_free_device_memory_bytes = free
    previous_minimum = (
        application.minimum_pre_generation_free_device_memory_bytes
    )
    application.minimum_pre_generation_free_device_memory_bytes = (
        free if previous_minimum is None else min(previous_minimum, free)
    )
    if allocated > application.allocator_limit_bytes or reserved > (
        application.allocator_limit_bytes
    ):
        application.runtime_outcome = (
            RUNTIME_OUTCOME_PRE_GENERATION_REJECTED
        )
        application.failure_reason = REASON_ALLOCATOR_LIMIT_EXCEEDED
        _publish(application)
        raise InitializationError(
            "Grounded model generation was rejected because current "
            "PyTorch CUDA use exceeded its fixed allocator limit."
        )
    if free < RESERVED_ALLOCATOR_HEADROOM_BYTES:
        application.runtime_outcome = (
            RUNTIME_OUTCOME_PRE_GENERATION_REJECTED
        )
        application.failure_reason = (
            REASON_INSUFFICIENT_PRE_GENERATION_FREE_MEMORY
        )
        _publish(application)
        raise InitializationError(
            "Grounded model generation was rejected because current free "
            f"GPU memory ({free} bytes) is below the fixed 3 GiB admission "
            "floor. Close another GPU workload and retry; the policy will "
            "not relax automatically."
        )
    application.pre_generation_admission_count += 1
    application.runtime_outcome = RUNTIME_OUTCOME_GENERATION_ADMITTED
    application.failure_reason = None
    _publish(application)


def is_cuda_out_of_memory(error: BaseException, torch: Any) -> bool:
    """Recognize the pinned runtime's typed CUDA OOM through wrapper errors."""
    oom_type = getattr(torch, "OutOfMemoryError", None)
    if not isinstance(oom_type, type):
        return False
    seen: set[int] = set()
    current: BaseException | None = error
    while current is not None and id(current) not in seen:
        seen.add(id(current))
        if isinstance(current, oom_type):
            return True
        current = current.__cause__ or current.__context__
    return False


def record_grounded_cuda_out_of_memory(torch: Any) -> None:
    application = _ACTIVE_APPLICATION
    if application is None:
        return
    application.runtime_outcome = RUNTIME_OUTCOME_CUDA_OOM
    application.failure_reason = REASON_CUDA_ALLOCATOR_OOM
    try:
        _set_runtime_snapshot(application, torch)
    except Exception:
        pass
    _publish(application)


def complete_grounded_cuda_memory(torch: Any) -> dict[str, Any]:
    application = _ACTIVE_APPLICATION
    if application is None:
        raise InitializationError(
            "Grounded CUDA memory policy was not configured at completion."
        )
    _set_runtime_snapshot(application, torch)
    if (
        application.peak_allocated_gpu_bytes is None
        or application.peak_reserved_gpu_bytes is None
        or application.peak_allocated_gpu_bytes
            > application.peak_reserved_gpu_bytes
        or application.peak_reserved_gpu_bytes
            > application.allocator_limit_bytes
    ):
        raise InitializationError(
            "Grounded CUDA peak telemetry exceeded its fixed allocator "
            "relationships."
        )
    application.runtime_outcome = RUNTIME_OUTCOME_COMPLETED
    application.failure_reason = None
    _publish(application)
    return application.payload()


__all__ = [name for name in globals() if not name.startswith("__")]
