#!/usr/bin/env python3
"""Generate compact cross-language Prompt 2.0 attempt fixtures."""
from __future__ import annotations

import argparse
import itertools
import json
import sys
from pathlib import Path
from unittest.mock import patch

HOST_ROOT = Path(__file__).resolve().parents[1]
if str(HOST_ROOT) not in sys.path:
    sys.path.insert(0, str(HOST_ROOT))

from replayfoundry_visual_semantic.editorial import attempts
from replayfoundry_visual_semantic.editorial import (
    constrained_development_command,
)
from replayfoundry_visual_semantic.editorial import development_command
from replayfoundry_visual_semantic.editorial import protocol
from replayfoundry_visual_semantic.editorial.contract import (
    parse_and_canonicalize_editorial_output,
)
from replayfoundry_visual_semantic.editorial.inference import _json_numbers


class _Cuda:
    @staticmethod
    def empty_cache() -> None:
        pass


class _Torch:
    cuda = _Cuda()


def _request(run_kind: str, ordinal: int) -> dict:
    prefix = run_kind.casefold()
    return {
        "caseId": f"fixture-{prefix}-{ordinal:02d}",
        "caseHash": f"{ordinal:064x}",
        "candidate": {
            "id": f"fixture-candidate-{prefix}-{ordinal:02d}",
            "startRelativeSeconds": 4,
            "endRelativeSeconds": 12,
        },
        "reviewVideo": {
            "path": f"C:\\fixture-media\\{prefix}-{ordinal:02d}.mkv",
            "reviewVideoDurationSeconds": 20,
        },
        "_validated": {
            "videoDuration": 20,
            "candidateStart": 4,
            "candidateEnd": 12,
            "sourceAbsoluteOffset": 0,
            "expectedVideoHash": "a" * 64,
            "expectedVideoLength": 1,
            "expectedLastWriteUtc": None,
        },
    }


def _requests(run_kind: str, count: int) -> list[dict]:
    return [_request(run_kind, index) for index in range(1, count + 1)]


def _observation() -> tuple[dict, dict]:
    value = {
        "observableContentType": "Action",
        "hasDistinctEvent": "Yes",
        "hasObservablePayoff": "Yes",
        "routineTraversalOrMenuOnly": "No",
        "candidateRequiresMissingContext": "No",
        "candidateContainsOnlyAmbientChange": "No",
        "transcriptContextSupport": "NotSupplied",
        "observedChanges": [{
            "description": "A visible action begins and resolves.",
            "evidenceBasis": "Visual",
            "evidenceIntervalIds": ["fixture-evidence"],
        }],
        "evidenceIntervals": [{
            "id": "fixture-evidence",
            "startSeconds": 5.125,
            "endSeconds": 7.875,
            "description": "The visible action changes and resolves.",
            "evidenceBasis": "Visual",
        }],
        "uncertaintyReasons": [],
        "editorialDisposition": "Keep",
        "rejectReason": "None",
        "dispositionRationale":
            "The distinct visible action has an in-context payoff.",
    }
    observation, audit = parse_and_canonicalize_editorial_output(
        json.dumps(value, separators=(",", ":")),
        review_duration_seconds=20,
        candidate_start_seconds=4,
        candidate_end_seconds=12,
    )
    return _json_numbers(observation), audit


def _structured_audit(
    strict: bool | None,
    generated_tokens: int | None,
    termination: str | None,
) -> dict:
    return {
        "policyVersion":
            "visual-semantic-editorial-structured-decoding-1.0",
        "backendName": "XGrammar",
        "backendVersion": "0.2.2",
        "schemaVersion":
            "visual-semantic-editorial-constrained-schema-1.0",
        "schemaSha256": "4" * 64,
        "representation": "JsonSchema",
        "cudaMaskBackend": "torch_native",
        "compileElapsedSeconds": 0.01,
        "generatedTokenCount": generated_tokens,
        "grammarTerminationState": termination,
        "strictParserAccepted": strict,
        "unconstrainedFallbackUsed": False,
        "semanticRepairApplied": False,
    }


def _success(
    request: dict,
    ordinal: int,
    run_kind: str,
    constrained: bool,
) -> dict:
    observation, audit = _observation()
    result = {
        "observation": observation,
        "canonicalizationAudit": audit,
        "requestBinding": {
            "caseId": request["caseId"],
            "candidateId": request["candidate"]["id"],
            "caseOrdinal": ordinal,
            "runKind": run_kind,
            "semanticPayloadSha256": "b" * 64,
            "trustedEnvelopeSha256": "c" * 64,
            "boundAtUtc": "2026-01-01T00:00:00Z",
        },
        "generation": {
            "generatedTokenCount": 64,
            "terminationReason": "EndOfSequence",
            "decodedTextSha256": "d" * 64,
        },
        "executionTiming": {},
        "sampling": {},
        "elapsedSeconds": 0.25,
    }
    if constrained:
        result["structuredDecodingAudit"] = _structured_audit(
            True,
            64,
            "EndOfSequence",
        )
    return result


def _attempt_set(
    run_kind: str,
    requests: list[dict],
    failures: dict[int, str] | None = None,
    constrained: bool = False,
) -> dict:
    failures = failures or {}

    def infer(request: dict, ordinal: int, kind: str, *_args):
        failure = failures.get(ordinal)
        if failure is None:
            return _success(request, ordinal, kind, constrained)
        if failure == "OutputValidation":
            attempts._set_failure_stage("OutputValidation")
            attempts._set_failure_provider_output(
                rawGeneratedTextSha256="e" * 64
            )
            if constrained:
                attempts._set_failure_structured_decoding(
                    _structured_audit(False, None, None)
                )
            raise attempts.InferenceError("fixture output rejected")
        if failure == "GenerationBudget":
            attempts._set_failure_stage("Generation")
            attempts._set_case_generation({
                "generatedTokenCount": 2048,
                "terminationReason": "MaximumNewTokens",
                "decodedTextSha256": "f" * 64,
            })
            if constrained:
                attempts._set_failure_structured_decoding(
                    _structured_audit(
                        None,
                        2048,
                        "MaximumNewTokens",
                    )
                )
            raise attempts.GenerationTokenBudgetExceededError(
                "fixture generation budget reached"
            )
        if failure == "UnexpectedTermination":
            attempts._set_failure_stage("Generation")
            attempts._set_case_generation({
                "generatedTokenCount": 16,
                "terminationReason": "UnexpectedTermination",
                "decodedTextSha256": "1" * 64,
            })
            if constrained:
                attempts._set_failure_structured_decoding(
                    _structured_audit(
                        None,
                        16,
                        "UnexpectedTermination",
                    )
                )
            raise attempts.UnexpectedGenerationTerminationError(
                "fixture generation terminated unexpectedly"
            )
        if failure == "VideoSampling":
            attempts._set_failure_stage("VideoSampling")
            attempts._set_failure_sampling(
                actualPtsSeconds=[0.0, 2.0],
                actualFrameDurationsSeconds=[2.0, 2.0],
                frameCount=2,
            )
            if constrained:
                attempts._set_failure_structured_decoding(
                    _structured_audit(None, None, None)
                )
            raise attempts.InferenceError("fixture sampling failed")
        raise AssertionError(f"Unknown fixture failure: {failure}")

    ticks = itertools.count(start=100.0, step=0.125)
    with (
        patch.object(attempts, "infer_editorial_case", infer),
        patch.object(
            attempts.time,
            "perf_counter",
            side_effect=lambda: next(ticks),
        ),
    ):
        return attempts.attempt_editorial_set(
            run_kind,
            requests,
            "fixture prompt",
            object(),
            object(),
            _Torch(),
            object(),
            object(),
            object() if constrained else None,
        )


def _plan(primary: list[dict], repeat: list[dict], visual: list[dict]) -> dict:
    def public(request: dict) -> dict:
        return {
            "caseId": request["caseId"],
            "candidate": request["candidate"],
            "reviewVideo": request["reviewVideo"],
        }

    return {
        "schemaVersion": protocol.EDITORIAL_PLAN_SCHEMA,
        "configurationLockCanonicalHash":
            protocol.CONFIGURATION_LOCK_SHA256,
        "sets": [
            {
                "runKind": "Primary",
                "requests": [public(row) for row in primary],
            },
            {
                "runKind": "Repeat",
                "requests": [public(row) for row in repeat],
            },
            {
                "runKind": "VisualOnly",
                "requests": [public(row) for row in visual],
            },
        ],
    }


def _not_run_set(
    run_kind: str,
    requests: list[dict],
    constrained: bool,
) -> dict:
    result = attempts.not_run_editorial_set(
        run_kind,
        requests,
        "NotRunPrimaryIncomplete",
    )
    if constrained:
        result["schemaVersion"] = (
            "visual-semantic-editorial-constrained-attempt-set-1.0"
        )
        result.pop("canonicalHash")
        result["canonicalHash"] = (
            development_command._canonical_json_sha256(result)
        )
    return result


def _root(
    plan: dict,
    primary: dict,
    repeat: dict,
    visual: dict,
    constrained: bool,
) -> dict:
    if constrained:
        return constrained_development_command._attempt_payload(
            {
                "configurationLockCanonicalHash":
                    protocol.CONFIGURATION_LOCK_SHA256
            },
            {"canonicalHash": "5" * 64},
            {
                "sourceArtifactSha256": "2" * 64,
                "parityCaseCount": 30,
                "parityCanonicalHash": "3" * 64,
            },
            primary,
            repeat,
            visual,
            7.5,
            1.5 if primary["failedCount"] == 0 else None,
            2.5 if primary["failedCount"] == 0 else None,
            1024,
            2048,
            12.0,
        )
    return development_command._attempt_payload(
        {
            "configurationLockCanonicalHash":
                protocol.CONFIGURATION_LOCK_SHA256
        },
        {
            "sourceArtifactSha256": "2" * 64,
            "parityCaseCount": 30,
            "parityCanonicalHash": "3" * 64,
        },
        primary,
        repeat,
        visual,
        None,
        1.0,
    )


def _write(path: Path, value: dict) -> None:
    path.write_text(
        json.dumps(value, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def _recache_attempt(value: dict) -> None:
    for name in ("primary", "repeat", "visualOnly"):
        value[name].pop("canonicalHash", None)
        value[name]["canonicalHash"] = (
            development_command._canonical_json_sha256(value[name])
        )
    value.pop("canonicalHash", None)
    value["canonicalHash"] = (
        development_command._canonical_json_sha256(value)
    )


def generate(output: Path, constrained: bool) -> None:
    output.mkdir(parents=True, exist_ok=False)
    primary = _requests("Primary", 30)
    repeat = _requests("Repeat", 6)
    visual = _requests("VisualOnly", 12)
    plan = _plan(primary, repeat, visual)
    _write(output / "plan.json", plan)

    all_success = {
        "primary": _attempt_set(
            "Primary", primary, constrained=constrained),
        "repeat": _attempt_set(
            "Repeat", repeat, constrained=constrained),
        "visual": _attempt_set(
            "VisualOnly", visual, constrained=constrained),
    }
    _write(
        output / "all-success.json",
        _root(plan, **all_success, constrained=constrained),
    )
    if constrained:
        constrained_success = _root(
            plan,
            **all_success,
            constrained=True,
        )
        completed_success = (
            constrained_development_command._completed_payload(
                plan,
                constrained_success,
            )
        )
        _write(output / "all-success-completed.json", completed_success)
        invalid_completed = json.loads(json.dumps(completed_success))
        invalid_completed["primary"]["cases"][0]["observation"][
            "dispositionRationale"] = "Changed after the attempt."
        invalid_completed["primary"].pop("canonicalHash")
        invalid_completed["primary"]["canonicalHash"] = (
            development_command._canonical_json_sha256(
                invalid_completed["primary"]
            )
        )
        invalid_completed.pop("canonicalHash")
        invalid_completed["canonicalHash"] = (
            development_command._canonical_json_sha256(invalid_completed)
        )
        _write(
            output / "invalid-completed-inconsistency.json",
            invalid_completed,
        )
        invalid_mutations = {
            "fallback-used": lambda value:
                value["primary"]["outcomes"][0][
                    "structuredDecodingAudit"].__setitem__(
                        "unconstrainedFallbackUsed",
                        True,
                    ),
            "semantic-repair": lambda value:
                value["primary"]["outcomes"][0][
                    "structuredDecodingAudit"].__setitem__(
                        "semanticRepairApplied",
                        True,
                    ),
            "audit-schema-mismatch": lambda value:
                value["primary"]["outcomes"][0][
                    "structuredDecodingAudit"].__setitem__(
                        "schemaSha256",
                        "8" * 64,
                    ),
            "binding-schema-mismatch": lambda value:
                value["constraintSchemaBindings"][0].__setitem__(
                    "schemaSha256",
                    "9" * 64,
                ),
        }
        for name, mutate in invalid_mutations.items():
            invalid_value = json.loads(json.dumps(constrained_success))
            mutate(invalid_value)
            _recache_attempt(invalid_value)
            _write(output / f"invalid-{name}.json", invalid_value)

    variants = {
        "output-validation": {2: "OutputValidation"},
        "generation-budget": {2: "GenerationBudget"},
        "unexpected-termination": {2: "UnexpectedTermination"},
        "video-sampling": {2: "VideoSampling"},
        "multiple-primary-failures": {
            2: "OutputValidation",
            7: "GenerationBudget",
            29: "VideoSampling",
        },
    }
    for name, failures in variants.items():
        attempted = _attempt_set(
            "Primary",
            primary,
            failures,
            constrained=constrained,
        )
        _write(
            output / f"{name}.json",
            _root(
                plan,
                attempted,
                _not_run_set("Repeat", repeat, constrained),
                _not_run_set("VisualOnly", visual, constrained),
                constrained=constrained,
            ),
        )

    _write(
        output / "secondary-case-failures.json",
        _root(
            plan,
            all_success["primary"],
            _attempt_set(
                "Repeat",
                repeat,
                {3: "OutputValidation"},
                constrained=constrained,
            ),
            _attempt_set(
                "VisualOnly",
                visual,
                {4: "UnexpectedTermination"},
                constrained=constrained,
            ),
            constrained=constrained,
        ),
    )

    invalid = json.loads(
        json.dumps(
            _root(
                plan,
                _attempt_set(
                    "Primary",
                    primary,
                    {2: "OutputValidation", 7: "GenerationBudget"},
                    constrained=constrained,
                ),
                _not_run_set("Repeat", repeat, constrained),
                _not_run_set("VisualOnly", visual, constrained),
                constrained=constrained,
            )
        )
    )
    for index in (1, 6):
        invalid["primary"]["outcomes"][index]["caseOrdinal"] = 99
    invalid["primary"].pop("canonicalHash")
    invalid["primary"]["canonicalHash"] = (
        development_command._canonical_json_sha256(invalid["primary"])
    )
    invalid.pop("canonicalHash")
    invalid["canonicalHash"] = (
        development_command._canonical_json_sha256(invalid)
    )
    _write(output / "invalid-multiple-case-rows.json", invalid)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--constrained", action="store_true")
    arguments = parser.parse_args()
    generate(arguments.output.resolve(), arguments.constrained)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
