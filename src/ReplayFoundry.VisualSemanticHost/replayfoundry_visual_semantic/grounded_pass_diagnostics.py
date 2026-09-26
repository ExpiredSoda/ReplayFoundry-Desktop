"""Opt-in content-free pass wall times; never part of provider output."""
from __future__ import annotations

from contextvars import ContextVar
from dataclasses import dataclass
from functools import wraps
import inspect
import itertools
import json
import math
import os
import re
import sys
import time
from typing import Any, Callable

ENVIRONMENT_FLAG = "REPLAYFOUNDRY_EDITORIAL_PASS_TIMING"
PREFIX = "replayfoundry-editorial-pass: "
SCHEMA_VERSION = "editorial-pass-diagnostic-1.0"
MAXIMUM_RECORDS = 1024
_ORDINALS = itertools.count(1)
_ACTIVE: ContextVar[_Pass | None] = ContextVar("editorial_pass_timing", default=None)


def _pass_kind(audit: Any, attestation: Any, decoding: Any) -> str:
    schema = getattr(audit, "schema_version", None)
    if not isinstance(schema, str):
        return "Unknown"
    for name, kind in (
        ("visual-draft", "VisualDraft"),
        ("visual-event-selection", "VisualEventSelection"),
        ("knowledge-selection", "KnowledgeSelection"),
        ("isolated-visual-field", "IsolatedVisualField"),
        ("isolated-commentary-field", "IsolatedCommentaryField"),
    ):
        if re.fullmatch(r"grounded-editorial-" + name + r"-json-schema-\d+\.\d+", schema):
            return kind
    if not re.fullmatch(r"grounded-editorial-metadata-json-schema-\d+\.\d+", schema):
        return "Unknown"
    if isinstance(attestation, dict) and attestation.get("stage") == "EditorialRephrase":
        return "EditorialRephrase"
    return "SynthesisRecovery" if decoding is not None else "Synthesis"


def _seconds(value: Any) -> float | None:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    if not math.isfinite(value) or not 0 <= value <= 86_400:
        return None
    return round(value, 6)


def _count(value: Any) -> int | None:
    return value if type(value) is int and 1 <= value <= 10_000_000 else None


@dataclass
class _Pass:
    candidate_id: str
    attempt: int
    case_ordinal: int
    ordinal: int
    kind: str
    started: float
    generation_started: float | None = None
    generation_finished: float | None = None
    trace: Any = None

    def report(self, status: str) -> None:
        finished = time.perf_counter()
        preparation_end = self.generation_started if self.generation_started is not None else finished
        record = {
            "schemaVersion": SCHEMA_VERSION,
            "candidateId": self.candidate_id,
            "attempt": self.attempt,
            "caseOrdinal": self.case_ordinal,
            "passOrdinal": self.ordinal,
            "passKind": self.kind,
            "status": status,
            "preparationWallSeconds": _seconds(preparation_end - self.started),
            "generationCallWallSeconds": _seconds(self.generation_finished - self.generation_started)
                if self.generation_finished is not None and self.generation_started is not None else None,
            "watchdogGenerationWallSeconds": _seconds(getattr(self.trace, "generation_wall_clock_seconds", None)),
            "postprocessingWallSeconds": _seconds(finished - self.generation_finished)
                if self.generation_finished is not None else None,
            "totalWallSeconds": _seconds(finished - self.started),
            "inputTokenCount": _count(getattr(self.trace, "input_token_count", None)),
            "generatedTokenCount": _count(getattr(self.trace, "generated_token_count", None)),
        }
        if record["preparationWallSeconds"] is None or record["totalWallSeconds"] is None:
            return
        line = PREFIX + json.dumps(record, separators=(",", ":"), allow_nan=False)
        if len(line) <= 2048:
            print(line, file=sys.stderr, flush=True)


def observe_grounded_pass(function: Callable[..., Any]) -> Callable[..., Any]:
    signature = inspect.signature(function)

    @wraps(function)
    def observed(*args: Any, **kwargs: Any) -> Any:
        if os.environ.get(ENVIRONMENT_FLAG) != "1":
            return function(*args, **kwargs)
        scope = None
        try:
            bound = signature.bind(*args, **kwargs).arguments
            request = bound["request"]
            candidate_id, attempt = request["candidateId"], request["attempt"]
            case_ordinal = bound["case_ordinal"]
            if isinstance(candidate_id, str) and re.fullmatch(r"[A-Za-z0-9_.:-]{1,160}", candidate_id) \
                    and type(attempt) is int and 0 <= attempt <= 10_000 \
                    and type(case_ordinal) is int and 1 <= case_ordinal <= 32:
                ordinal = next(_ORDINALS)
                if ordinal <= MAXIMUM_RECORDS:
                    scope = _Pass(candidate_id, attempt, case_ordinal, ordinal,
                                  _pass_kind(bound["audit"], bound.get("synthesis_attestation_context"),
                                             bound.get("synthesis_decoding")), time.perf_counter())
        except Exception:
            # Invalid diagnostic input must not replace the provider's own validation.
            pass
        if scope is None:
            return function(*args, **kwargs)
        token = _ACTIVE.set(scope)
        status = "Raised"
        try:
            result = function(*args, **kwargs)
            status = "Returned"
            return result
        finally:
            _ACTIVE.reset(token)
            try:
                scope.report(status)
            except Exception:
                pass  # Diagnostic sinks cannot change generation or exception identity.

    return observed


def generation_started() -> None:
    scope = _ACTIVE.get()
    if scope is not None:
        scope.generation_started = time.perf_counter()


def generation_finished() -> None:
    scope = _ACTIVE.get()
    if scope is not None:
        scope.generation_finished = time.perf_counter()


def retain_generation_trace(trace: Any) -> None:
    scope = _ACTIVE.get()
    if scope is not None:
        scope.trace = trace
