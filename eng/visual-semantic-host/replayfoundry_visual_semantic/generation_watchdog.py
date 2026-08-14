"""Frozen cooperative wall-clock bounds for local Qwen generation."""
from __future__ import annotations

from contextlib import contextmanager
from contextvars import ContextVar
from dataclasses import dataclass
import hashlib
import math
from pathlib import Path
import time
from typing import Any, Iterator

from .errors import GenerationWallClockBudgetExceededError
from .failure_state import _set_failure_generation_watchdog


POLICY_VERSION = "visual-semantic-generation-watchdog-1.0"
POLICY_FILE_NAME = "replayfoundry-generation-watchdog-policy-1.0.txt"
POLICY_SHA256 = (
    "a8f797b610de464de2c81cfa2beeb0b5bc732d65766be53c2e2a0b009143917e"
)
MAXIMUM_GENERATION_WALL_CLOCK_SECONDS = 240.0
MAXIMUM_GROUNDED_CASE_WALL_CLOCK_SECONDS = 900.0
TIMEOUT_BEHAVIOR = "FailClosed"
GENERATION_TIMEOUT_REASON = (
    "GenerationInvocationWallClockBudgetExceeded"
)
CASE_TIMEOUT_REASON = "GroundedCaseWallClockBudgetExceeded"


@dataclass
class _GroundedCaseWatchdogState:
    case_id: str
    candidate_id: str
    case_ordinal: int
    started_at: float
    generation_invocation_count: int = 0
    triggered: bool = False
    timeout_reason: str | None = None


@dataclass(frozen=True)
class _GenerationWatchdogInvocation:
    case_state: _GroundedCaseWatchdogState | None
    invocation_ordinal: int
    started_at: float
    effective_maximum_seconds: float
    limiting_timeout_reason: str


_CASE_WATCHDOG: ContextVar[_GroundedCaseWatchdogState | None] = ContextVar(
    "replayfoundry_generation_case_watchdog",
    default=None,
)


def _watchdog_clock() -> float:
    return time.monotonic()


def _normalized_policy_source() -> tuple[str, str]:
    path = Path(__file__).resolve().parent.parent / POLICY_FILE_NAME
    text = path.read_text(encoding="utf-8").replace("\r\n", "\n").replace(
        "\r", "\n"
    ).strip()
    return text, hashlib.sha256(text.encode("utf-8")).hexdigest()


def generation_watchdog_policy_payload() -> dict[str, Any]:
    return {
        "policyVersion": POLICY_VERSION,
        "policySha256": POLICY_SHA256,
        "maximumGenerationWallClockSeconds":
            MAXIMUM_GENERATION_WALL_CLOCK_SECONDS,
        "maximumGroundedCaseWallClockSeconds":
            MAXIMUM_GROUNDED_CASE_WALL_CLOCK_SECONDS,
        "timeoutBehavior": TIMEOUT_BEHAVIOR,
    }


def _rounded_seconds(value: float) -> float:
    if not math.isfinite(value) or value < 0:
        raise ValueError("Watchdog elapsed time must be finite and non-negative.")
    return round(value, 6)


def _failure_watchdog_payload(
    state: _GroundedCaseWatchdogState | None,
    invocation_ordinal: int,
    effective_maximum_seconds: float | None,
    elapsed_generation_seconds: float | None,
    elapsed_case_seconds: float | None,
    triggered: bool,
    timeout_reason: str | None,
) -> dict[str, Any]:
    return {
        **generation_watchdog_policy_payload(),
        "caseId": None if state is None else state.case_id,
        "candidateId": None if state is None else state.candidate_id,
        "caseOrdinal": None if state is None else state.case_ordinal,
        "generationInvocationOrdinal": invocation_ordinal,
        "effectiveMaximumGenerationWallClockSeconds": (
            None
            if effective_maximum_seconds is None
            else _rounded_seconds(effective_maximum_seconds)
        ),
        "elapsedGenerationWallClockSeconds": (
            None
            if elapsed_generation_seconds is None
            else _rounded_seconds(elapsed_generation_seconds)
        ),
        "elapsedCaseWallClockSeconds": (
            None
            if elapsed_case_seconds is None
            else _rounded_seconds(elapsed_case_seconds)
        ),
        "triggered": triggered,
        "timeoutReason": timeout_reason,
    }


@contextmanager
def grounded_case_watchdog(
    case_id: str,
    candidate_id: str,
    case_ordinal: int,
) -> Iterator[_GroundedCaseWatchdogState]:
    if (
        not isinstance(case_id, str)
        or not case_id
        or not isinstance(candidate_id, str)
        or not candidate_id
        or isinstance(case_ordinal, bool)
        or not isinstance(case_ordinal, int)
        or case_ordinal <= 0
    ):
        raise ValueError("Grounded case watchdog identity is invalid.")
    state = _GroundedCaseWatchdogState(
        case_id,
        candidate_id,
        case_ordinal,
        _watchdog_clock(),
    )
    token = _CASE_WATCHDOG.set(state)
    _set_failure_generation_watchdog(
        _failure_watchdog_payload(
            state,
            0,
            None,
            None,
            0.0,
            False,
            None,
        )
    )
    try:
        yield state
    finally:
        _CASE_WATCHDOG.reset(token)


def prepare_generation_watchdog() -> _GenerationWatchdogInvocation | None:
    now = _watchdog_clock()
    state = _CASE_WATCHDOG.get()
    if state is None:
        return None

    elapsed_case = max(0.0, now - state.started_at)
    remaining_case = (
        MAXIMUM_GROUNDED_CASE_WALL_CLOCK_SECONDS - elapsed_case
    )
    next_ordinal = state.generation_invocation_count + 1
    if remaining_case <= 0:
        state.triggered = True
        state.timeout_reason = CASE_TIMEOUT_REASON
        _set_failure_generation_watchdog(
            _failure_watchdog_payload(
                state,
                next_ordinal,
                0.0,
                None,
                elapsed_case,
                True,
                CASE_TIMEOUT_REASON,
            )
        )
        raise GenerationWallClockBudgetExceededError(
            "Grounded editorial case exhausted its 900-second wall-clock "
            "budget before another model generation could start."
        )

    state.generation_invocation_count = next_ordinal
    case_limited = (
        remaining_case < MAXIMUM_GENERATION_WALL_CLOCK_SECONDS
    )
    effective_maximum = min(
        MAXIMUM_GENERATION_WALL_CLOCK_SECONDS,
        remaining_case,
    )
    timeout_reason = (
        CASE_TIMEOUT_REASON if case_limited else GENERATION_TIMEOUT_REASON
    )
    _set_failure_generation_watchdog(
        _failure_watchdog_payload(
            state,
            next_ordinal,
            effective_maximum,
            0.0,
            elapsed_case,
            False,
            None,
        )
    )
    return _GenerationWatchdogInvocation(
        state,
        next_ordinal,
        now,
        effective_maximum,
        timeout_reason,
    )


def complete_generation_watchdog(
    invocation: _GenerationWatchdogInvocation,
) -> tuple[float, bool, str | None]:
    now = _watchdog_clock()
    elapsed_generation = max(0.0, now - invocation.started_at)
    state = invocation.case_state
    elapsed_case = (
        None
        if state is None
        else max(0.0, now - state.started_at)
    )
    time_limit_reached = (
        elapsed_generation >= invocation.effective_maximum_seconds
    )
    timeout_reason = (
        invocation.limiting_timeout_reason if time_limit_reached else None
    )
    if state is not None and time_limit_reached:
        state.triggered = True
        state.timeout_reason = timeout_reason
    _set_failure_generation_watchdog(
        _failure_watchdog_payload(
            state,
            invocation.invocation_ordinal,
            invocation.effective_maximum_seconds,
            elapsed_generation,
            elapsed_case,
            time_limit_reached,
            timeout_reason,
        )
    )
    return elapsed_generation, time_limit_reached, timeout_reason


def record_generation_watchdog_exception(
    invocation: _GenerationWatchdogInvocation,
) -> None:
    """Record a model exception without disguising it as a watchdog timeout."""
    now = _watchdog_clock()
    state = invocation.case_state
    elapsed_generation = max(0.0, now - invocation.started_at)
    elapsed_case = (
        None
        if state is None
        else max(0.0, now - state.started_at)
    )
    _set_failure_generation_watchdog(
        _failure_watchdog_payload(
            state,
            invocation.invocation_ordinal,
            invocation.effective_maximum_seconds,
            elapsed_generation,
            elapsed_case,
            False,
            None,
        )
    )


def grounded_case_watchdog_success_payload(
    state: _GroundedCaseWatchdogState,
) -> dict[str, Any]:
    if state is not _CASE_WATCHDOG.get():
        raise ValueError("Grounded case watchdog is not the active case.")
    if state.triggered:
        raise ValueError("A triggered watchdog cannot attest success.")
    elapsed_case = max(0.0, _watchdog_clock() - state.started_at)
    if elapsed_case > MAXIMUM_GROUNDED_CASE_WALL_CLOCK_SECONDS:
        state.triggered = True
        state.timeout_reason = CASE_TIMEOUT_REASON
        _set_failure_generation_watchdog(
            _failure_watchdog_payload(
                state,
                state.generation_invocation_count,
                None,
                None,
                elapsed_case,
                True,
                CASE_TIMEOUT_REASON,
            )
        )
        raise GenerationWallClockBudgetExceededError(
            "Grounded editorial case exceeded its 900-second wall-clock "
            "budget before success could be committed."
        )
    if state.generation_invocation_count <= 0:
        raise ValueError("Grounded case completed without a generation invocation.")
    return {
        **generation_watchdog_policy_payload(),
        "generationInvocationCount": state.generation_invocation_count,
        "elapsedCaseWallClockSeconds": _rounded_seconds(elapsed_case),
        "triggered": False,
        "timeoutReason": None,
    }


__all__ = [name for name in globals() if not name.startswith("__")]
