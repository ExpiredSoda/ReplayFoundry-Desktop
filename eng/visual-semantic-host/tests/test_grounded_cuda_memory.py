from __future__ import annotations

from pathlib import Path
from contextlib import contextmanager
from types import SimpleNamespace
import sys
import unittest


HOST_ROOT = Path(__file__).resolve().parents[1]
if str(HOST_ROOT) not in sys.path:
    sys.path.insert(0, str(HOST_ROOT))

from replayfoundry_visual_semantic.errors import InitializationError
from replayfoundry_visual_semantic import grounded_cuda_memory as policy
from replayfoundry_visual_semantic import generation, model_runtime
from replayfoundry_visual_semantic.errors import InferenceError


class _FakeCudaMemory:
    def __init__(self) -> None:
        self.fraction = 1.0
        self.calls: list[tuple[float, int]] = []

    def set_per_process_memory_fraction(
        self,
        fraction: float,
        device: int,
    ) -> None:
        self.fraction = fraction
        self.calls.append((fraction, device))

    def get_per_process_memory_fraction(self, device: int) -> float:
        if device != 0:
            raise ValueError("unexpected device")
        return self.fraction


class _FakeCuda:
    def __init__(
        self,
        total: int,
        free: int,
        allocated: int = 0,
        reserved: int = 0,
    ) -> None:
        self.total = total
        self.free = free
        self.allocated = allocated
        self.reserved = reserved
        self.peak_allocated = allocated
        self.peak_reserved = reserved
        self.empty_cache_calls = 0
        self.reset_peak_calls: list[int] = []
        self.memory = _FakeCudaMemory()

    def get_device_properties(self, device: int) -> SimpleNamespace:
        if device != 0:
            raise ValueError("unexpected device")
        return SimpleNamespace(total_memory=self.total)

    def mem_get_info(self, device: int) -> tuple[int, int]:
        if device != 0:
            raise ValueError("unexpected device")
        return self.free, self.total

    def memory_allocated(self, device: int) -> int:
        if device != 0:
            raise ValueError("unexpected device")
        return self.allocated

    def memory_reserved(self, device: int) -> int:
        if device != 0:
            raise ValueError("unexpected device")
        return self.reserved

    def max_memory_allocated(self, device: int) -> int:
        if device != 0:
            raise ValueError("unexpected device")
        return self.peak_allocated

    def max_memory_reserved(self, device: int) -> int:
        if device != 0:
            raise ValueError("unexpected device")
        return self.peak_reserved

    def empty_cache(self) -> None:
        self.empty_cache_calls += 1

    def reset_peak_memory_stats(self, device: int) -> None:
        if device != 0:
            raise ValueError("unexpected device")
        self.reset_peak_calls.append(device)


class GroundedCudaMemoryTests(unittest.TestCase):
    TOTAL = 16_311 * 1024 * 1024

    def test_policy_source_hash_and_fixed_constraints_are_exact(self) -> None:
        self.assertEqual(policy.POLICY_SHA256, policy._normalized_policy_sha256())
        self.assertEqual(3 * 1024 * 1024 * 1024, policy.RESERVED_ALLOCATOR_HEADROOM_BYTES)
        self.assertEqual(11_705_485_312, policy.QUALIFICATION_REFERENCE_PEAK_ALLOCATED_BYTES)
        self.assertEqual(
            "real-qwen-metadata-v1.6.json",
            policy.QUALIFICATION_REFERENCE_ARTIFACT_NAME,
        )
        self.assertEqual(
            "replayfoundry-editorial-metadata-real-quality-1.0",
            policy.QUALIFICATION_REFERENCE_ARTIFACT_SCHEMA,
        )
        self.assertEqual(
            "0EC7F4BE4DD3664091D6808176B2FEA36B7FE016B277422DACA00C4C9D28EC70",
            policy.QUALIFICATION_REFERENCE_ARTIFACT_SHA256,
        )
        self.assertEqual(
            11_705_485_313,
            policy.MINIMUM_VIABLE_ALLOCATOR_LIMIT_BYTES,
        )
        self.assertEqual("offloaded", policy.CACHE_IMPLEMENTATION)
        self.assertEqual("sdpa", policy.ATTENTION_IMPLEMENTATION)
        self.assertEqual("CudnnAttention", policy.SDPA_BACKEND)
        self.assertTrue(policy.SDPA_BACKEND_FORCED)
        self.assertFalse(policy.ATTENTION_FALLBACK_PERMITTED)
        self.assertEqual(
            {
                "model.visual": "cpu",
                "model.language_model": 0,
                "lm_head": 0,
            },
            policy.GROUNDED_MODEL_LOAD_DEVICE_MAP,
        )
        self.assertEqual(
            {
                "model.visual": "cpu",
                "model.language_model": 0,
                "lm_head": 0,
            },
            policy.GROUNDED_MODEL_DEVICE_MAP,
        )
        self.assertEqual(
            "Qwen3VLVisionModel",
            policy.GROUNDED_VISION_PRELOAD_MODULE_CLASS,
        )
        self.assertFalse(policy.GLOBAL_FREE_MEMORY_GUARANTEED)
        self.assertTrue(policy.CPU_MODEL_OFFLOAD_PERMITTED)
        self.assertFalse(policy.QUANTIZATION_PERMITTED)
        self.assertFalse(policy.AUTOMATIC_FALLBACK_PERMITTED)

    def test_applies_exact_fraction_and_retains_complete_provenance(self) -> None:
        cuda = _FakeCuda(self.TOTAL, self.TOTAL - 512 * 1024 * 1024)
        applied = policy.configure_grounded_cuda_memory(
            SimpleNamespace(cuda=cuda)
        )
        startup_free = self.TOTAL - 512 * 1024 * 1024
        expected_limit = startup_free - 3 * 1024 * 1024 * 1024
        expected_fraction = expected_limit / self.TOTAL
        self.assertEqual([(expected_fraction, 0)], cuda.memory.calls)
        self.assertEqual([0], cuda.reset_peak_calls)
        payload = applied.payload()
        self.assertEqual(expected_limit, payload["allocatorLimitBytes"])
        self.assertEqual(
            policy.MINIMUM_VIABLE_ALLOCATOR_LIMIT_BYTES
            + policy.RESERVED_ALLOCATOR_HEADROOM_BYTES,
            payload["requiredStartupFreeMemoryBytes"],
        )
        self.assertEqual(self.TOTAL, payload["totalDeviceMemoryBytes"])
        self.assertEqual(self.TOTAL - 512 * 1024 * 1024, payload["startupFreeMemoryBytes"])
        self.assertEqual(512 * 1024 * 1024, payload["startupExternallyOccupiedMemoryBytes"])
        self.assertEqual(
            policy.MINIMUM_VIABLE_ALLOCATOR_LIMIT_BYTES,
            payload["minimumViableAllocatorLimitBytes"],
        )
        self.assertEqual(expected_fraction, payload["allocatorFraction"])
        self.assertEqual(expected_fraction, payload["observedAllocatorFraction"])
        self.assertEqual("offloaded", payload["cacheImplementation"])
        self.assertEqual("sdpa", payload["attentionImplementation"])
        self.assertEqual("CudnnAttention", payload["sdpaBackend"])
        self.assertTrue(payload["sdpaBackendForced"])
        self.assertFalse(payload["attentionFallbackPermitted"])
        self.assertFalse(payload["globalFreeMemoryGuaranteed"])
        self.assertTrue(payload["cpuModelOffloadPermitted"])
        self.assertFalse(payload["quantizationPermitted"])
        self.assertFalse(payload["automaticFallbackPermitted"])
        self.assertEqual("Configured", payload["runtimeOutcome"])
        self.assertIsNone(payload["peakReservedGpuBytes"])

    def test_grounded_attention_forces_only_cudnn_backend(self) -> None:
        events: list[tuple[str, object]] = []
        cudnn_backend = object()

        @contextmanager
        def sdpa_kernel(backend):
            events.append(("enter", backend))
            yield
            events.append(("exit", backend))

        torch = SimpleNamespace(
            backends=SimpleNamespace(
                cudnn=SimpleNamespace(is_available=lambda: True),
                cuda=SimpleNamespace(cudnn_sdp_enabled=lambda: True),
            ),
            nn=SimpleNamespace(
                attention=SimpleNamespace(
                    SDPBackend=SimpleNamespace(
                        CUDNN_ATTENTION=cudnn_backend,
                    ),
                    sdpa_kernel=sdpa_kernel,
                )
            ),
        )
        with policy.grounded_sdpa_context(torch):
            events.append(("body", cudnn_backend))
        self.assertEqual(
            [
                ("enter", cudnn_backend),
                ("body", cudnn_backend),
                ("exit", cudnn_backend),
            ],
            events,
        )

    def test_grounded_attention_has_no_unqualified_fallback(self) -> None:
        torch = SimpleNamespace(
            backends=SimpleNamespace(
                cudnn=SimpleNamespace(is_available=lambda: False),
                cuda=SimpleNamespace(cudnn_sdp_enabled=lambda: True),
            )
        )
        with self.assertRaisesRegex(
            InitializationError,
            "cannot force the qualified cuDNN",
        ):
            with policy.grounded_sdpa_context(torch):
                self.fail("Unavailable cuDNN attention cannot enter generation.")

    def test_startup_contention_fails_before_allocator_is_changed(self) -> None:
        required_free = (
            policy.MINIMUM_VIABLE_ALLOCATOR_LIMIT_BYTES
            + policy.RESERVED_ALLOCATOR_HEADROOM_BYTES
        )
        cuda = _FakeCuda(self.TOTAL, required_free - 1)
        with self.assertRaisesRegex(
            InitializationError,
            "cannot preserve",
        ):
            policy.configure_grounded_cuda_memory(SimpleNamespace(cuda=cuda))
        self.assertEqual([], cuda.memory.calls)

    def test_device_too_small_for_reference_peak_fails_closed(self) -> None:
        total = policy.RESERVED_ALLOCATOR_HEADROOM_BYTES
        cuda = _FakeCuda(total, total)
        with self.assertRaisesRegex(
            InitializationError,
            "cannot preserve",
        ):
            policy.configure_grounded_cuda_memory(SimpleNamespace(cuda=cuda))
        self.assertEqual([], cuda.memory.calls)

    def test_existing_allocator_use_above_limit_fails_closed(self) -> None:
        free = self.TOTAL - 256 * 1024 * 1024
        limit = free - policy.RESERVED_ALLOCATOR_HEADROOM_BYTES
        cuda = _FakeCuda(
            self.TOTAL,
            free,
            allocated=limit + 1,
            reserved=limit + 1,
        )
        with self.assertRaisesRegex(
            InitializationError,
            "already exceed",
        ):
            policy.configure_grounded_cuda_memory(SimpleNamespace(cuda=cuda))
        self.assertEqual(1, len(cuda.memory.calls))

    def test_pre_generation_gate_and_completion_reconcile_peaks(self) -> None:
        cuda = _FakeCuda(self.TOTAL, self.TOTAL - 512 * 1024 * 1024)
        torch = SimpleNamespace(cuda=cuda)
        applied = policy.configure_grounded_cuda_memory(torch)
        cuda.allocated = 10_500_000_000
        cuda.reserved = 10_900_000_000
        cuda.peak_allocated = 11_000_000_000
        cuda.peak_reserved = 11_500_000_000
        policy.admit_grounded_generation(torch)
        payload = policy.complete_grounded_cuda_memory(torch)
        self.assertEqual(1, cuda.empty_cache_calls)
        self.assertEqual(1, payload["preGenerationAdmissionCount"])
        self.assertEqual(cuda.free, payload["minimumPreGenerationFreeDeviceMemoryBytes"])
        self.assertEqual(11_000_000_000, payload["peakAllocatedGpuBytes"])
        self.assertEqual(11_500_000_000, payload["peakReservedGpuBytes"])
        self.assertEqual(10_500_000_000, payload["endAllocatedGpuBytes"])
        self.assertEqual(10_900_000_000, payload["endReservedGpuBytes"])
        self.assertEqual(cuda.free, payload["endFreeDeviceMemoryBytes"])
        self.assertEqual("Completed", payload["runtimeOutcome"])
        self.assertIsNone(payload["failureReason"])

    def test_pre_generation_contention_is_typed_and_fails_closed(self) -> None:
        cuda = _FakeCuda(self.TOTAL, self.TOTAL - 512 * 1024 * 1024)
        torch = SimpleNamespace(cuda=cuda)
        applied = policy.configure_grounded_cuda_memory(torch)
        cuda.free = policy.RESERVED_ALLOCATOR_HEADROOM_BYTES - 1
        with self.assertRaisesRegex(
            InitializationError,
            "fixed 3 GiB admission floor",
        ):
            policy.admit_grounded_generation(torch)
        payload = applied.payload()
        self.assertEqual("PreGenerationAdmissionRejected", payload["runtimeOutcome"])
        self.assertEqual("InsufficientPreGenerationFreeMemory", payload["failureReason"])
        self.assertEqual(0, payload["preGenerationAdmissionCount"])

    def test_input_transfer_preserves_typed_cuda_oom_cause(self) -> None:
        class FakeCudaOutOfMemoryError(RuntimeError):
            pass

        original = FakeCudaOutOfMemoryError("allocator ceiling")

        class Inputs:
            def to(self, _device: str):
                raise original

        with self.assertRaises(InferenceError) as caught:
            generation._move_inputs_to_cuda(Inputs())
        self.assertIs(original, caught.exception.__cause__)
        torch = SimpleNamespace(OutOfMemoryError=FakeCudaOutOfMemoryError)
        self.assertTrue(policy.is_cuda_out_of_memory(caught.exception, torch))

    def test_model_load_preserves_typed_cuda_oom_cause(self) -> None:
        class FakeCudaOutOfMemoryError(RuntimeError):
            pass

        original = FakeCudaOutOfMemoryError("allocator ceiling")

        class AutoModel:
            @staticmethod
            def from_pretrained(*_args, **_kwargs):
                raise original

        transformers = SimpleNamespace(
            AutoModelForImageTextToText=AutoModel,
        )
        torch = SimpleNamespace(
            bfloat16=object(),
            OutOfMemoryError=FakeCudaOutOfMemoryError,
        )
        with self.assertRaises(InitializationError) as caught:
            model_runtime._load_model_and_processor(
                Path("A:/model"),
                torch,
                transformers,
            )
        self.assertIs(original, caught.exception.__cause__)
        self.assertTrue(policy.is_cuda_out_of_memory(caught.exception, torch))

    def test_grounded_loader_uses_and_validates_exact_split_placement(self) -> None:
        class Device:
            def __init__(self, device_type: str, index: int | None = None) -> None:
                self.type = device_type
                self.index = index

        class Tensor:
            def __init__(self, device_type: str, index: int | None = None) -> None:
                self.device = Device(device_type, index)
                self.dtype = "bf16"

            @staticmethod
            def is_floating_point() -> bool:
                return True

        visual_parameter = Tensor("meta")
        visual_backing = Tensor("cpu")
        position_parameter = Tensor("meta")
        position_backing = Tensor("cpu")
        language_parameter = Tensor("cuda", 0)
        head_parameter = Tensor("cuda", 0)

        class Module:
            def __init__(self, parameters=(), hook=None) -> None:
                self.parameters = list(parameters)
                if hook is not None:
                    self._hf_hook = hook

            def named_parameters(self, recurse=False):
                self.assert_direct(recurse)
                return iter(self.parameters)

            @staticmethod
            def assert_direct(recurse: bool) -> None:
                if recurse:
                    raise AssertionError("validator must inspect direct tensors")

        class Qwen3VLVisionModel(Module):
            def __init__(self) -> None:
                super().__init__(
                    hook=SimpleNamespace(
                        offload=True,
                        place_submodules=True,
                        execution_device=Device("cuda", 0),
                        weights_map={
                            "patch_embed.weight": visual_backing,
                            "pos_embed.weight": position_backing,
                        },
                    )
                )

        class Model:
            is_quantized = False
            quantization_method = None
            hf_device_map = dict(policy.GROUNDED_MODEL_DEVICE_MAP)

            def __init__(self) -> None:
                self.visual = Qwen3VLVisionModel()
                self.modules = [
                    ("", Module()),
                    ("model.visual", self.visual),
                    (
                        "model.visual.patch_embed",
                        Module((("weight", visual_parameter),)),
                    ),
                    (
                        "model.visual.pos_embed",
                        Module((("weight", position_parameter),)),
                    ),
                    (
                        "model.language_model.layers.0",
                        Module((("weight", language_parameter),)),
                    ),
                    ("lm_head", Module((("weight", head_parameter),))),
                ]

            def named_modules(self):
                return iter(self.modules)

            def get_submodule(self, name):
                if name != policy.GROUNDED_VISION_MODULE:
                    raise KeyError(name)
                return self.visual

            @staticmethod
            def named_parameters():
                return iter(
                    [
                        ("model.visual.patch_embed.weight", visual_parameter),
                        (
                            "model.visual.pos_embed.weight",
                            position_parameter,
                        ),
                        (
                            "model.language_model.layers.0.weight",
                            language_parameter,
                        ),
                        ("lm_head.weight", head_parameter),
                    ]
                )

            @staticmethod
            def named_buffers():
                return iter(
                    [
                        ("model.visual.position_ids", Tensor("cuda", 0)),
                        (
                            "model.language_model.rotary.inv_freq",
                            Tensor("cuda", 0),
                        ),
                    ]
                )

            @staticmethod
            def eval() -> None:
                return None

        captured: dict[str, object] = {}

        class AutoModel:
            @staticmethod
            def from_pretrained(*_args, **kwargs):
                captured.update(kwargs)
                return Model()

        class AutoProcessor:
            @staticmethod
            def from_pretrained(*_args, **_kwargs):
                return object()

        torch = SimpleNamespace(bfloat16="bf16")
        transformers = SimpleNamespace(
            AutoModelForImageTextToText=AutoModel,
            AutoProcessor=AutoProcessor,
        )
        model_runtime._load_model_and_processor(
            Path("A:/model"),
            torch,
            transformers,
            device_map=policy.GROUNDED_MODEL_LOAD_DEVICE_MAP,
            placement_validator=policy.validate_grounded_model_placement,
        )
        self.assertEqual(
            policy.GROUNDED_MODEL_LOAD_DEVICE_MAP,
            captured["device_map"],
        )
        self.assertEqual("bf16", captured["dtype"])

    def test_finalizer_replaces_leaf_hooks_with_root_preload(self) -> None:
        class Device:
            def __init__(self, device_type: str, index: int | None = None) -> None:
                self.type = device_type
                self.index = index

            def __str__(self) -> str:
                return self.type if self.index is None else f"{self.type}:{self.index}"

        class Tensor:
            def __init__(self, device_type: str, index: int | None = None) -> None:
                self.device = Device(device_type, index)
                self.dtype = "bf16"

            @staticmethod
            def is_floating_point() -> bool:
                return True

        class Leaf:
            def __init__(self, name: str) -> None:
                self.name = name
                self.weight = Tensor("meta")
                self.backing = Tensor("cpu")
                self._hf_hook = SimpleNamespace(
                    offload=True,
                    execution_device=Device("cuda", 0),
                    weights_map={"weight": self.backing},
                )

            def named_parameters(self, recurse=False):
                if recurse:
                    raise AssertionError("direct parameters only")
                return iter([("weight", self.weight)])

        class Qwen3VLVisionModel:
            def __init__(self) -> None:
                self.patch = Leaf("patch_embed")
                self.position = Leaf("pos_embed")

            def modules(self):
                return iter([self, self.patch, self.position])

            @staticmethod
            def named_parameters(recurse=False):
                if recurse:
                    return iter([])
                return iter([])

            def parameters(self):
                return iter([self.patch.weight, self.position.weight])

            def to(self, device: Device):
                for parameter in self.parameters():
                    parameter.device = device
                return self

        visual = Qwen3VLVisionModel()

        class Model:
            hf_device_map = dict(policy.GROUNDED_MODEL_LOAD_DEVICE_MAP)

            @staticmethod
            def get_submodule(name):
                if name != policy.GROUNDED_VISION_MODULE:
                    raise KeyError(name)
                return visual

        model = Model()

        original_remove = policy._remove_accelerate_hook
        original_restore = policy._restore_accelerate_tensor
        original_install = policy._install_root_visual_offload
        restored: list[str] = []
        try:
            def remove_hook(module, *, recurse=False) -> None:
                self.assertIs(visual, module)
                self.assertTrue(recurse)
                for leaf in (visual.patch, visual.position):
                    del leaf._hf_hook

            def restore_tensor(module, tensor_name, value) -> None:
                self.assertEqual("weight", tensor_name)
                self.assertIs(module.backing, value)
                self.assertEqual("meta", module.weight.device.type)
                module.weight = value
                restored.append(module.name)

            def install_root(module, torch) -> None:
                self.assertIs(visual, module)
                for leaf in (visual.patch, visual.position):
                    leaf.weight.device = Device("meta")
                module._hf_hook = SimpleNamespace(
                    offload=True,
                    place_submodules=True,
                    execution_device=Device("cuda", 0),
                    weights_map={
                        "patch.weight": visual.patch.backing,
                        "position.weight": visual.position.backing,
                    },
                )

            policy._remove_accelerate_hook = remove_hook
            policy._restore_accelerate_tensor = restore_tensor
            policy._install_root_visual_offload = install_root
            torch = SimpleNamespace(
                bfloat16="bf16",
                device=lambda device_type, index=None: Device(
                    device_type, index
                ),
            )
            policy.finalize_grounded_model_placement(model, torch)
        finally:
            policy._remove_accelerate_hook = original_remove
            policy._restore_accelerate_tensor = original_restore
            policy._install_root_visual_offload = original_install

        self.assertEqual(["patch_embed", "pos_embed"], restored)
        self.assertEqual("meta", visual.patch.weight.device.type)
        self.assertEqual("meta", visual.position.weight.device.type)
        self.assertTrue(hasattr(visual, "_hf_hook"))
        self.assertFalse(hasattr(visual.patch, "_hf_hook"))
        self.assertFalse(hasattr(visual.position, "_hf_hook"))
        self.assertEqual(policy.GROUNDED_MODEL_DEVICE_MAP, model.hf_device_map)

    def test_accelerate_restore_materializes_meta_parameter(self) -> None:
        import torch
        from accelerate.hooks import AlignDevicesHook, add_hook_to_module

        class Leaf(torch.nn.Module):
            def __init__(self) -> None:
                super().__init__()
                self.weight = torch.nn.Parameter(
                    torch.empty(2, dtype=torch.bfloat16, device="meta")
                )

        leaf = Leaf()
        backing = torch.tensor([2.0, 3.0], dtype=torch.bfloat16)
        add_hook_to_module(
            leaf,
            AlignDevicesHook(
                execution_device=torch.device("cpu"),
                offload=True,
                weights_map={"weight": backing},
            ),
        )

        policy._remove_accelerate_hook(leaf)
        self.assertEqual("meta", leaf.weight.device.type)
        policy._restore_accelerate_tensor(leaf, "weight", backing)

        self.assertEqual("cpu", leaf.weight.device.type)
        self.assertTrue(torch.equal(backing, leaf.weight.detach()))

    def test_pinned_accelerate_root_preload_materializes_indirect_weight(self) -> None:
        import torch
        from accelerate.big_modeling import cpu_offload

        class IndirectWeight(torch.nn.Module):
            def __init__(self) -> None:
                super().__init__()
                self.weight = torch.nn.Parameter(
                    torch.tensor([2.0], dtype=torch.bfloat16)
                )

        class Qwen3VLVisionModel(torch.nn.Module):
            def __init__(self) -> None:
                super().__init__()
                self.child = IndirectWeight()

            def forward(self, value):
                # Qwen's parent vision code likewise reads a registered child
                # parameter before invoking that child's forward method.
                return value + self.child.weight

        visual = Qwen3VLVisionModel()
        cpu_offload(
            visual,
            execution_device=torch.device("cpu"),
            offload_buffers=False,
            preload_module_classes=[
                policy.GROUNDED_VISION_PRELOAD_MODULE_CLASS
            ],
        )

        self.assertEqual("meta", visual.child.weight.device.type)
        hooks = policy._accelerate_hooks(visual._hf_hook)
        self.assertTrue(
            any(
                hook.offload is True and hook.place_submodules is True
                for hook in hooks
                if hasattr(hook, "offload")
            )
        )
        self.assertFalse(hasattr(visual.child, "_hf_hook"))

        result = visual(torch.tensor([1.0], dtype=torch.bfloat16))

        self.assertEqual(3.0, result.item())
        self.assertEqual("meta", visual.child.weight.device.type)

    def test_grounded_placement_rejects_visual_cuda_or_language_cpu(self) -> None:
        class Device:
            def __init__(self, device_type: str, index: int | None = None) -> None:
                self.type = device_type
                self.index = index

        class Tensor:
            def __init__(self, device_type: str, index: int | None = None) -> None:
                self.device = Device(device_type, index)
                self.dtype = "bf16"

            @staticmethod
            def is_floating_point() -> bool:
                return True

        class Qwen3VLVisionModel:
            def __init__(self, visual_parameter) -> None:
                self._hf_hook = SimpleNamespace(
                    offload=True,
                    place_submodules=True,
                    execution_device=Device("cuda", 0),
                    weights_map={"patch_embed.weight": Tensor("cpu")},
                )
                self.patch_embed = SimpleNamespace(
                    named_parameters=lambda recurse=False: iter(
                        [("weight", visual_parameter)]
                    ),
                )

        class Model:
            is_quantized = False
            quantization_method = None
            hf_device_map = dict(policy.GROUNDED_MODEL_DEVICE_MAP)

            def __init__(self, visual_device: str, language_device: str) -> None:
                self.visual_device = visual_device
                self.language_device = language_device
                self.visual_parameter = Tensor(visual_device, 0)
                self.visual = Qwen3VLVisionModel(self.visual_parameter)

            def get_submodule(self, name):
                if name != policy.GROUNDED_VISION_MODULE:
                    raise KeyError(name)
                return self.visual

            def named_modules(self):
                language = SimpleNamespace(
                    named_parameters=lambda recurse=False: iter(
                        [("weight", Tensor(self.language_device, 0))]
                    ),
                )
                return iter(
                    [
                        (
                            "model.visual.patch_embed",
                            self.visual.patch_embed,
                        ),
                        (
                            "model.language_model.layers.0",
                            language,
                        ),
                    ]
                )

            @staticmethod
            def named_buffers():
                return iter([])

        torch = SimpleNamespace(bfloat16="bf16")
        with self.assertRaisesRegex(InitializationError, "device placement"):
            policy.validate_grounded_model_placement(
                Model("cuda", "cuda"),
                torch,
            )
        with self.assertRaisesRegex(InitializationError, "device placement"):
            policy.validate_grounded_model_placement(
                Model("meta", "cpu"),
                torch,
            )

    def test_grounded_placement_rejects_unattested_meta_vision_weight(self) -> None:
        class Device:
            def __init__(self, device_type: str, index: int | None = None) -> None:
                self.type = device_type
                self.index = index

        class Tensor:
            def __init__(self, device_type: str, index: int | None = None) -> None:
                self.device = Device(device_type, index)
                self.dtype = "bf16"

            @staticmethod
            def is_floating_point() -> bool:
                return True

        visual_parameter = Tensor("meta")

        class Qwen3VLVisionModel:
            def __init__(self) -> None:
                self._hf_hook = SimpleNamespace(
                    offload=True,
                    place_submodules=True,
                    execution_device=Device("cuda", 0),
                    weights_map={},
                )
                self.patch_embed = SimpleNamespace(
                    named_parameters=lambda recurse=False: iter(
                        [("weight", visual_parameter)]
                    ),
                )

        visual = Qwen3VLVisionModel()
        language = SimpleNamespace(
            named_parameters=lambda recurse=False: iter(
                [("weight", Tensor("cuda", 0))]
            ),
        )
        class Model:
            is_quantized = False
            quantization_method = None
            hf_device_map = dict(policy.GROUNDED_MODEL_DEVICE_MAP)

            @staticmethod
            def get_submodule(name):
                if name != policy.GROUNDED_VISION_MODULE:
                    raise KeyError(name)
                return visual

            @staticmethod
            def named_modules():
                return iter(
                    [
                        ("model.visual.patch_embed", visual.patch_embed),
                        ("model.language_model.layers.0", language),
                    ]
                )

            @staticmethod
            def named_buffers():
                return iter([])

        model = Model()
        with self.assertRaisesRegex(InitializationError, "device placement"):
            policy.validate_grounded_model_placement(
                model,
                SimpleNamespace(bfloat16="bf16"),
            )


if __name__ == "__main__":
    unittest.main()
