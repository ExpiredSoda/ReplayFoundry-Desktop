"""Generation result and manifest payload construction."""
from __future__ import annotations

import hashlib
from typing import Any

from .canonical_json import _canonical_json_sha256
from .constants import (
    ACTIVE_POLICY_MAX_NEW_TOKENS,
    GENERATION_MANIFEST_SCHEMA,
    GENERATION_POLICY_SHA256,
    GENERATION_POLICY_VERSION,
    LEGACY_DIAGNOSTIC_MAX_NEW_TOKENS,
    MAX_NEW_TOKENS,
    NUMBER_OF_BEAMS,
)
from .errors import InferenceError, OutputError, _fail


def _generation_case_payload(
    request: dict[str, Any],
    case_ordinal: int,
    trace: Any,
    decoded_text: str,
) -> dict[str, Any]:
    try:
        raw_bytes = decoded_text.encode("utf-8", errors="strict")
    except UnicodeEncodeError:
        _fail(
            InferenceError,
            "Provider output is not valid strict UTF-8 text.",
        )
    return {
        "caseId": request["caseId"],
        "candidateId": request["candidate"]["id"],
        "caseOrdinal": case_ordinal,
        "inputTokenCount": trace.input_token_count,
        "generatedTokenCount": trace.generated_token_count,
        "maximumNewTokens": trace.maximum_new_tokens,
        "endOfSequenceTokenIds": trace.eos_token_ids,
        "firstEndOfSequenceGeneratedIndex": trace.first_eos_generated_index,
        "terminalTokenId": trace.terminal_token_id,
        "terminationReason": trace.termination_reason,
        "generatedTokenIdsSha256": trace.generated_token_ids_sha256,
        "legacyPrefixTokenCount": trace.legacy_prefix_token_count,
        "legacyPrefixTokenIdsSha256": trace.legacy_prefix_token_ids_sha256,
        "decodedTextSha256": hashlib.sha256(raw_bytes).hexdigest(),
        "decodedTextUtf8ByteCount": len(raw_bytes),
    }


def _failure_generation_payload(
    generation_case: dict[str, Any],
    *,
    policy_version: str = GENERATION_POLICY_VERSION,
    policy_sha256: str = GENERATION_POLICY_SHA256,
    do_sample: bool = False,
    number_of_beams: int = NUMBER_OF_BEAMS,
    use_cache: bool = True,
) -> dict[str, Any]:
    return {
        "policyVersion": policy_version,
        "policySha256": policy_sha256,
        "maximumNewTokens": generation_case["maximumNewTokens"],
        "doSample": do_sample,
        "numberOfBeams": number_of_beams,
        "useCache": use_cache,
        "caseId": generation_case["caseId"],
        "candidateId": generation_case["candidateId"],
        "caseOrdinal": generation_case["caseOrdinal"],
        "inputTokenCount": generation_case["inputTokenCount"],
        "generatedTokenCount": generation_case["generatedTokenCount"],
        "endOfSequenceTokenIds": generation_case["endOfSequenceTokenIds"],
        "firstEndOfSequenceGeneratedIndex": generation_case[
            "firstEndOfSequenceGeneratedIndex"
        ],
        "terminalTokenId": generation_case["terminalTokenId"],
        "terminationReason": generation_case["terminationReason"],
        "generatedTokenIdsSha256": generation_case["generatedTokenIdsSha256"],
        "legacyPrefixTokenCount": generation_case["legacyPrefixTokenCount"],
        "legacyPrefixTokenIdsSha256": generation_case[
            "legacyPrefixTokenIdsSha256"
        ],
        "decodedTextSha256": generation_case["decodedTextSha256"],
        "decodedTextUtf8ByteCount": generation_case["decodedTextUtf8ByteCount"],
    }


def _generation_manifest_payload(cases: list[dict[str, Any]]) -> dict[str, Any]:
    if not cases:
        _fail(OutputError, "Generation manifest requires at least one case.")
    if MAX_NEW_TOKENS != ACTIVE_POLICY_MAX_NEW_TOKENS:
        _fail(
            OutputError,
            "The active generation policy is still blocked by the Phase-A "
            "legacy diagnostic gate.",
        )
    if [case.get("caseOrdinal") for case in cases] != list(
        range(1, len(cases) + 1)
    ):
        _fail(
            OutputError,
            "Generation manifest cases changed stable request order.",
        )
    if (
        len({case.get("caseId") for case in cases}) != len(cases)
        or len({case.get("candidateId") for case in cases}) != len(cases)
    ):
        _fail(OutputError, "Generation manifest contains duplicate case identity.")
    expected_eos_ids = cases[0].get("endOfSequenceTokenIds")
    if any(
        case["terminationReason"] != "EndOfSequence"
        or case["maximumNewTokens"] != ACTIVE_POLICY_MAX_NEW_TOKENS
        or not isinstance(case["generatedTokenCount"], int)
        or case["generatedTokenCount"] <= 0
        or case["generatedTokenCount"] >= ACTIVE_POLICY_MAX_NEW_TOKENS
        or case["firstEndOfSequenceGeneratedIndex"]
        != case["generatedTokenCount"] - 1
        or case["terminalTokenId"] not in case["endOfSequenceTokenIds"]
        or case["legacyPrefixTokenCount"]
        != min(LEGACY_DIAGNOSTIC_MAX_NEW_TOKENS, case["generatedTokenCount"])
        or case["endOfSequenceTokenIds"] != expected_eos_ids
        or case["endOfSequenceTokenIds"]
        != sorted(set(case["endOfSequenceTokenIds"]))
        or not case["endOfSequenceTokenIds"]
        or (
            case["generatedTokenCount"] <= LEGACY_DIAGNOSTIC_MAX_NEW_TOKENS
            and case["generatedTokenIdsSha256"]
            != case["legacyPrefixTokenIdsSha256"]
        )
        for case in cases
    ):
        _fail(
            OutputError,
            "Completed generation manifest contains an incomplete case.",
        )
    payload = {
        "schemaVersion": GENERATION_MANIFEST_SCHEMA,
        "policyVersion": GENERATION_POLICY_VERSION,
        "policySha256": GENERATION_POLICY_SHA256,
        "maximumNewTokens": ACTIVE_POLICY_MAX_NEW_TOKENS,
        "doSample": False,
        "numberOfBeams": NUMBER_OF_BEAMS,
        "useCache": True,
        "caseCount": len(cases),
        "cases": cases,
    }
    payload["canonicalGenerationSha256"] = _canonical_json_sha256(payload)
    return payload
