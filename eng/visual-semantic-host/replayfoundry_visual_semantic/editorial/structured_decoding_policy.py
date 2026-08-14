"""Frozen Prompt 2.3 structured-decoding policy."""
from __future__ import annotations

import importlib.metadata
from typing import Any

from ..errors import InitializationError, InferenceError

POLICY_VERSION = "visual-semantic-editorial-structured-decoding-1.7"
BACKEND_NAME = "XGrammar"
BACKEND_VERSION = "0.2.2"
SOURCE_TAG = "v0.2.2"
SOURCE_COMMIT = "4d145cc13d878c751ebeed36af1c013074be76bc"
WHEEL_FILE_NAME = "xgrammar-0.2.2-cp311-cp311-win_amd64.whl"
WHEEL_SHA256 = (
    "eefb94f9dd84b0d79885943318b0fbf3e6fd23b86ae3dfe6d0e48f090f431e6b"
)
TVM_FFI_VERSION = "0.1.9"
TVM_FFI_WHEEL_FILE_NAME = (
    "apache_tvm_ffi-0.1.9-cp311-cp311-win_amd64.whl"
)
TVM_FFI_WHEEL_SHA256 = (
    "9ee710a9fba3d9ff9747870bbd7e2175eb8d5b9c791f17fd645f35f6dab3f8aa"
)
PYDANTIC_VERSION = "2.12.5"
PYDANTIC_WHEEL_FILE_NAME = "pydantic-2.12.5-py3-none-any.whl"
PYDANTIC_WHEEL_SHA256 = (
    "e561593fccf61e8a20fc46dfc2dfe075b8be7d0188df33f221ad1f0139180f9d"
)
PYDANTIC_CORE_VERSION = "2.41.5"
PYDANTIC_CORE_WHEEL_FILE_NAME = (
    "pydantic_core-2.41.5-cp311-cp311-win_amd64.whl"
)
PYDANTIC_CORE_WHEEL_SHA256 = (
    "76ee27c6e9c7f16f47db7a94157112a2f3a00e958bc626e2f4ee8bec5c328fbe"
)
ANNOTATED_TYPES_VERSION = "0.8.0"
ANNOTATED_TYPES_WHEEL_FILE_NAME = (
    "annotated_types-0.8.0-py3-none-any.whl"
)
ANNOTATED_TYPES_WHEEL_SHA256 = (
    "f072f4d804ea359e4eaf198b1af7a8b0943881a87f31bb764f8bf219bb9419e0"
)
TYPING_INSPECTION_VERSION = "0.4.2"
TYPING_INSPECTION_WHEEL_FILE_NAME = (
    "typing_inspection-0.4.2-py3-none-any.whl"
)
TYPING_INSPECTION_WHEEL_SHA256 = (
    "4ed1cacbdc298c220f1bd249ed5287caa16f34d44ef4e9c3d0cbad5b521545e7"
)
LICENSE_IDENTIFIER = "Apache-2.0"
SCHEMA_VERSION = "visual-semantic-editorial-constrained-schema-1.7"
ATTEMPT_SET_SCHEMA_VERSION = (
    "visual-semantic-editorial-constrained-attempt-set-1.0"
)
COMPLETED_SET_SCHEMA_VERSION = (
    "visual-semantic-editorial-constrained-observation-batch-1.0"
)
REPRESENTATION = "JsonSchema"
CUDA_MASK_BACKEND = "torch_native"
UNCONSTRAINED_FALLBACK_PERMITTED = False
SEMANTIC_REPAIR_PERMITTED = False


class StructuredDecodingUnavailableError(InitializationError):
    """The exact frozen structured-decoding boundary is unavailable."""


class StructuredDecodingInferenceError(InferenceError):
    """The frozen structured-decoding boundary failed during generation."""


class StructuredDecodingSchemaCompilationError(InferenceError):
    """One case's exact frozen schema could not be compiled."""


def require_frozen_packages() -> dict[str, str]:
    required = {
        "xgrammar": BACKEND_VERSION,
        "apache-tvm-ffi": TVM_FFI_VERSION,
        "pydantic": PYDANTIC_VERSION,
        "pydantic-core": PYDANTIC_CORE_VERSION,
        "annotated-types": ANNOTATED_TYPES_VERSION,
        "typing-inspection": TYPING_INSPECTION_VERSION,
    }
    installed: dict[str, str] = {}
    for distribution, expected in required.items():
        try:
            actual = importlib.metadata.version(distribution)
        except importlib.metadata.PackageNotFoundError as error:
            raise StructuredDecodingUnavailableError(
                f"Pinned structured-decoding dependency is missing: "
                f"{distribution}."
            ) from error
        if actual != expected:
            raise StructuredDecodingUnavailableError(
                f"{distribution} is {actual}; required {expected}."
            )
        installed[distribution] = actual
    return installed


def require_frozen_lock(value: Any) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise StructuredDecodingUnavailableError(
            "Structured-decoding qualification lock must be an object."
        )
    expected = {
        "policyVersion": POLICY_VERSION,
        "backendName": BACKEND_NAME,
        "backendVersion": BACKEND_VERSION,
        "representation": REPRESENTATION,
        "cudaMaskBackend": CUDA_MASK_BACKEND,
        "constraintSchemaVersion": SCHEMA_VERSION,
        "unconstrainedFallbackPermitted": False,
        "semanticRepairPermitted": False,
    }
    for key, required in expected.items():
        if value.get(key) != required:
            raise StructuredDecodingUnavailableError(
                f"Structured-decoding qualification lock changed {key}."
            )
    return value


__all__ = [name for name in globals() if not name.startswith("__")]
