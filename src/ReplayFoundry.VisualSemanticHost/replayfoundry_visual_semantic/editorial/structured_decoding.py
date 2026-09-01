"""Frozen XGrammar integration for Prompt 2.3 research."""
from __future__ import annotations

import time
from dataclasses import dataclass, replace
from decimal import Decimal
from typing import Any

from .constraint_schema import build_editorial_schema_artifact
from .structured_decoding_policy import (
    BACKEND_NAME,
    BACKEND_VERSION,
    CUDA_MASK_BACKEND,
    POLICY_VERSION,
    REPRESENTATION,
    SCHEMA_VERSION,
    SEMANTIC_REPAIR_PERMITTED,
    UNCONSTRAINED_FALLBACK_PERMITTED,
    StructuredDecodingInferenceError,
    StructuredDecodingSchemaCompilationError,
    StructuredDecodingUnavailableError,
    require_frozen_packages,
)


@dataclass(frozen=True)
class StructuredDecodingAudit:
    policy_version: str
    backend_name: str
    backend_version: str
    schema_version: str
    schema_sha256: str
    representation: str
    cuda_mask_backend: str
    compile_elapsed_seconds: float
    generated_token_count: int | None
    grammar_termination_state: str | None
    strict_parser_accepted: bool | None
    unconstrained_fallback_used: bool
    semantic_repair_applied: bool

    def with_generation(
        self,
        generated_token_count: int,
        termination_state: str,
    ) -> "StructuredDecodingAudit":
        return replace(
            self,
            generated_token_count=generated_token_count,
            grammar_termination_state=termination_state,
        )

    def with_parser_outcome(
        self,
        accepted: bool,
    ) -> "StructuredDecodingAudit":
        return replace(self, strict_parser_accepted=accepted)

    def to_json(self) -> dict[str, Any]:
        return {
            "policyVersion": self.policy_version,
            "backendName": self.backend_name,
            "backendVersion": self.backend_version,
            "schemaVersion": self.schema_version,
            "schemaSha256": self.schema_sha256,
            "representation": self.representation,
            "cudaMaskBackend": self.cuda_mask_backend,
            "compileElapsedSeconds": round(
                max(0.0, self.compile_elapsed_seconds),
                6,
            ),
            "generatedTokenCount": self.generated_token_count,
            "grammarTerminationState": self.grammar_termination_state,
            "strictParserAccepted": self.strict_parser_accepted,
            "unconstrainedFallbackUsed":
                self.unconstrained_fallback_used,
            "semanticRepairApplied": self.semantic_repair_applied,
        }


class StructuredDecodingSession:
    """Own one frozen compiler and creates fresh per-case processors."""

    def __init__(
        self,
        tokenizer: Any,
        model_vocab_size: int,
    ) -> None:
        if model_vocab_size <= 0:
            raise StructuredDecodingUnavailableError(
                "Model vocabulary size must be positive."
            )
        require_frozen_packages()
        try:
            import xgrammar as xgr

            tokenizer_info = xgr.TokenizerInfo.from_huggingface(
                tokenizer,
                vocab_size=model_vocab_size,
            )
            compiler = xgr.GrammarCompiler(
                tokenizer_info,
                max_threads=1,
                cache_enabled=True,
            )
        except StructuredDecodingUnavailableError:
            raise
        except Exception as error:
            raise StructuredDecodingUnavailableError(
                "Could not initialize the frozen XGrammar compiler: "
                f"{type(error).__name__}: {error}"
            ) from error
        self._xgr = xgr
        self._compiler = compiler

    def compile_case(
        self,
        review_duration_seconds: Decimal,
        candidate_start_seconds: Decimal,
        candidate_end_seconds: Decimal,
    ) -> tuple[Any, StructuredDecodingAudit, str]:
        try:
            _, canonical, schema_sha256 = build_editorial_schema_artifact(
                review_duration_seconds,
                candidate_start_seconds,
                candidate_end_seconds,
            )
            grammar, audit = self.compile_json_schema(
                canonical,
                SCHEMA_VERSION,
                schema_sha256,
            )
        except Exception as error:
            if isinstance(error, StructuredDecodingSchemaCompilationError):
                raise
            failure = StructuredDecodingSchemaCompilationError(
                "XGrammar could not compile this case's frozen Prompt 2.3 "
                "schema: "
                f"{type(error).__name__}: {error}"
            )
            raise failure from error
        return grammar, audit, canonical

    def compile_json_schema(
        self,
        canonical_schema: str,
        schema_version: str,
        schema_sha256: str,
        *,
        any_whitespace: bool = True,
    ) -> tuple[Any, StructuredDecodingAudit]:
        """Compile one caller-frozen canonical JSON schema without fallback."""
        if not canonical_schema or not schema_version or len(schema_sha256) != 64:
            raise StructuredDecodingSchemaCompilationError(
                "Structured decoding requires a canonical versioned schema."
            )

        started = time.perf_counter()
        audit = StructuredDecodingAudit(
            policy_version=POLICY_VERSION,
            backend_name=BACKEND_NAME,
            backend_version=BACKEND_VERSION,
            schema_version=schema_version,
            schema_sha256=schema_sha256,
            representation=REPRESENTATION,
            cuda_mask_backend=CUDA_MASK_BACKEND,
            compile_elapsed_seconds=0.0,
            generated_token_count=None,
            grammar_termination_state=None,
            strict_parser_accepted=None,
            unconstrained_fallback_used=UNCONSTRAINED_FALLBACK_PERMITTED,
            semantic_repair_applied=SEMANTIC_REPAIR_PERMITTED,
        )
        try:
            grammar = self._compiler.compile_json_schema(
                canonical_schema,
                any_whitespace=any_whitespace,
                strict_mode=True,
            )
        except Exception as error:
            failure = StructuredDecodingSchemaCompilationError(
                "XGrammar could not compile the frozen JSON schema: "
                f"{type(error).__name__}: {error}"
            )
            failure.audit = replace(
                audit,
                compile_elapsed_seconds=time.perf_counter() - started,
            )
            raise failure from error

        return grammar, replace(
            audit,
            compile_elapsed_seconds=time.perf_counter() - started,
        )

    def new_logits_processor(
        self,
        compiled_grammar: Any,
        eos_token_ids: list[int],
    ) -> Any:
        try:
            return ReplayFoundryXGrammarLogitsProcessor(
                compiled_grammar,
                eos_token_ids,
            )
        except Exception as error:
            raise StructuredDecodingInferenceError(
                "Could not create a fresh XGrammar logits processor: "
                f"{type(error).__name__}: {error}"
            ) from error


class ReplayFoundryXGrammarLogitsProcessor:
    """Pinned batch-one HF seam using XGrammar's portable CUDA mask."""

    def __init__(
        self,
        compiled_grammar: Any,
        eos_token_ids: list[int],
    ) -> None:
        self._xgr = __import__("xgrammar")
        self._matcher = self._xgr.GrammarMatcher(compiled_grammar)
        self._full_vocab_size = (
            compiled_grammar.tokenizer_info.vocab_size
        )
        if (
            not eos_token_ids
            or any(
                isinstance(token_id, bool)
                or not isinstance(token_id, int)
                or token_id < 0
                or token_id >= self._full_vocab_size
                for token_id in eos_token_ids
            )
        ):
            raise StructuredDecodingUnavailableError(
                "Structured decoding requires valid model EOS token IDs."
            )
        self._eos_token_ids = tuple(sorted(set(eos_token_ids)))
        self._token_bitmask = self._xgr.allocate_token_bitmask(
            1,
            self._full_vocab_size,
        )
        self._prefilled = False

    def __call__(self, input_ids: Any, scores: Any) -> Any:
        if input_ids.shape[0] != 1 or scores.shape[0] != 1:
            raise StructuredDecodingInferenceError(
                "Prompt 2.3 structured decoding requires batch size one."
            )
        if not self._prefilled:
            self._prefilled = True
        elif not self._matcher.is_terminated():
            sampled_token = input_ids[0][-1].item()
            if not self._matcher.accept_token(sampled_token):
                raise StructuredDecodingInferenceError(
                    "Generated token violated the compiled grammar."
                )
        if not self._matcher.is_terminated():
            self._matcher.fill_next_token_bitmask(
                self._token_bitmask,
                0,
            )
        device_type = scores.device.type
        if device_type == "cuda":
            self._xgr.apply_token_bitmask_inplace(
                scores,
                self._token_bitmask.to(scores.device),
                backend=CUDA_MASK_BACKEND,
            )
        elif device_type == "cpu":
            self._xgr.apply_token_bitmask_inplace(
                scores,
                self._token_bitmask,
                backend="cpu",
            )
        else:
            raise StructuredDecodingInferenceError(
                "Unsupported structured-decoding score device: "
                f"{device_type}."
            )
        if not self._matcher.is_completed():
            scores[0, list(self._eos_token_ids)] = float("-inf")
        return scores

    def require_completed(self) -> None:
        if not self._matcher.is_completed():
            raise StructuredDecodingInferenceError(
                "Generation stopped before the compiled JSON grammar "
                "completed."
            )


def model_vocab_size(model: Any) -> int:
    config = getattr(model, "config", None)
    direct = getattr(config, "vocab_size", None)
    text_config = getattr(config, "text_config", None)
    nested = getattr(text_config, "vocab_size", None)
    value = direct if isinstance(direct, int) else nested
    if not isinstance(value, int) or isinstance(value, bool) or value <= 0:
        raise StructuredDecodingUnavailableError(
            "Pinned model has no usable text vocabulary size."
        )
    return value


__all__ = [name for name in globals() if not name.startswith("__")]
