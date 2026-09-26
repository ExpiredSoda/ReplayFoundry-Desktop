"""ReplayFoundry local visual-semantic host implementation module."""
from __future__ import annotations

from .constants import *  # noqa: F401,F403

class HostError(Exception):
    """Expected failure with a stable process exit code."""

    exit_code = 1


class UsageOrInputError(HostError):
    exit_code = 2


class InitializationError(HostError):
    exit_code = 3


class InferenceError(HostError):
    exit_code = 4


class OutputError(HostError):
    exit_code = 5


class RawAuditCaptured(HostError):
    exit_code = 6


class GenerationTokenBudgetExceededError(InferenceError):
    exit_code = 7


class UnexpectedGenerationTerminationError(InferenceError):
    exit_code = 8


class GenerationWallClockBudgetExceededError(HostError):
    """Cooperative generation or grounded-case wall-clock bound expired."""

    exit_code = 10


class NoDistinctPrimaryVisualEventError(InferenceError):
    """No assessed visual draft contains positive primary-event support."""


class RerollTitleTooSimilarError(InferenceError):
    """A reroll only repeated or trivially rephrased accepted audience copy."""


class ProviderCaseFailuresDetected(HostError):
    exit_code = 9


class NetworkProhibitedError(InitializationError):
    pass


class _GenerationTrace(NamedTuple):
    sequences: Any
    generated_token_ids: list[int]
    input_token_count: int
    generated_token_count: int
    maximum_new_tokens: int
    eos_token_ids: list[int]
    first_eos_generated_index: int | None
    terminal_token_id: int
    termination_reason: str
    generated_token_ids_sha256: str
    legacy_prefix_token_count: int
    legacy_prefix_token_ids_sha256: str
    generation_wall_clock_seconds: float | None
    maximum_generation_wall_clock_seconds: float | None
    generation_watchdog_triggered: bool
    generation_watchdog_timeout_reason: str | None


def _fail(error_type: type[HostError], message: str) -> NoReturn:
    raise error_type(message)


def _fail_legacy_timing_validation(reason: str, message: str) -> NoReturn:
    if reason not in LEGACY_TIMING_VALIDATION_REASONS:
        raise ValueError(f"Unsupported legacy timing reason: {reason}")
    error = InferenceError(message)
    error.legacy_timing_reason = reason
    raise error



__all__ = [name for name in globals() if not name.startswith("__")]
