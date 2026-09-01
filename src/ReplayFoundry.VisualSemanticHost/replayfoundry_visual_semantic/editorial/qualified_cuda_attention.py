"""Frozen CUDA-attention policy for qualified editorial observation."""
from __future__ import annotations

from contextlib import contextmanager
import hashlib
from pathlib import Path
from typing import Any

from ..constants import HOST_DIRECTORY
from ..errors import InitializationError, UsageOrInputError


POLICY_VERSION = "qualified-editorial-cuda-attention-1.0"
POLICY_FILE_NAME = (
    "replayfoundry-qualified-editorial-cuda-attention-policy-1.0.txt"
)
POLICY_SHA256 = (
    "b0747a0ed7d160315c6fca9fd869a9afec50221e97cccb0bff74b87b92a6c90d"
)
ATTENTION_IMPLEMENTATION = "sdpa"
SDPA_BACKEND = "CudnnAttention"
SDPA_BACKEND_FORCED = True
ATTENTION_FALLBACK_PERMITTED = False
CACHE_IMPLEMENTATION = "offloaded"


def require_policy_source() -> None:
    path = Path(HOST_DIRECTORY) / POLICY_FILE_NAME
    try:
        text = (
            path.read_text(encoding="utf-8")
            .replace("\r\n", "\n")
            .replace("\r", "\n")
            .strip()
        )
    except OSError as error:
        raise UsageOrInputError(
            "Qualified editorial CUDA-attention policy is unavailable."
        ) from error
    if hashlib.sha256(text.encode("utf-8")).hexdigest() != POLICY_SHA256:
        raise UsageOrInputError(
            "Qualified editorial CUDA-attention policy source changed."
        )


def policy_payload() -> dict[str, Any]:
    return {
        "policyVersion": POLICY_VERSION,
        "policySha256": POLICY_SHA256,
        "attentionImplementation": ATTENTION_IMPLEMENTATION,
        "sdpaBackend": SDPA_BACKEND,
        "sdpaBackendForced": SDPA_BACKEND_FORCED,
        "attentionFallbackPermitted": ATTENTION_FALLBACK_PERMITTED,
        "cacheImplementation": CACHE_IMPLEMENTATION,
    }


@contextmanager
def qualified_cuda_attention_context(torch: Any):
    """Force cuDNN SDPA for one qualified observation inference set."""
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


__all__ = [name for name in globals() if not name.startswith("__")]
