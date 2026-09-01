from __future__ import annotations

from contextlib import contextmanager
import unittest

from replayfoundry_visual_semantic.editorial import qualified_cuda_attention
from replayfoundry_visual_semantic.errors import InitializationError


class _Cudnn:
    @staticmethod
    def is_available() -> bool:
        return True


class _CudaBackends:
    @staticmethod
    def cudnn_sdp_enabled() -> bool:
        return True


class _Backend:
    CUDNN_ATTENTION = "cudnn"


class _Attention:
    SDPBackend = _Backend
    entered = False
    exited = False

    @classmethod
    @contextmanager
    def sdpa_kernel(cls, backend):
        if backend != "cudnn":
            raise AssertionError("unexpected backend")
        cls.entered = True
        try:
            yield
        finally:
            cls.exited = True


class _Torch:
    class backends:
        cudnn = _Cudnn()
        cuda = _CudaBackends()

    class nn:
        attention = _Attention


class QualifiedCudaAttentionTests(unittest.TestCase):
    def test_policy_source_payload_and_context_are_exact(self) -> None:
        qualified_cuda_attention.require_policy_source()
        payload = qualified_cuda_attention.policy_payload()
        self.assertEqual(
            "qualified-editorial-cuda-attention-1.0",
            payload["policyVersion"],
        )
        self.assertEqual("sdpa", payload["attentionImplementation"])
        self.assertEqual("CudnnAttention", payload["sdpaBackend"])
        self.assertTrue(payload["sdpaBackendForced"])
        self.assertFalse(payload["attentionFallbackPermitted"])
        self.assertEqual("offloaded", payload["cacheImplementation"])
        with qualified_cuda_attention.qualified_cuda_attention_context(_Torch()):
            self.assertTrue(_Attention.entered)
        self.assertTrue(_Attention.exited)

    def test_unavailable_backend_fails_closed(self) -> None:
        class UnavailableTorch(_Torch):
            class backends:
                class cudnn:
                    @staticmethod
                    def is_available() -> bool:
                        return False

                cuda = _CudaBackends()

        with self.assertRaises(InitializationError):
            with qualified_cuda_attention.qualified_cuda_attention_context(
                UnavailableTorch()
            ):
                pass


if __name__ == "__main__":
    unittest.main()
