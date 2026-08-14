"""Model-free verification of the frozen XGrammar boundary."""
from __future__ import annotations

import hashlib
import json
import platform
import sys
from datetime import datetime, timezone
from decimal import Decimal
from pathlib import Path
from typing import Any
from types import SimpleNamespace

from ..commands import (
    _canonical_json_sha256,
    _load_strict_json,
    _set_failure_stage,
    _sha256_file,
    _validate_model_directory,
    _write_json_atomic,
)
from ..generation import _normalized_eos_token_ids
from .constraint_schema import (
    build_editorial_schema_artifact,
    canonical_schema_json,
)
from .contract import (
    EditorialContractError,
    parse_and_canonicalize_editorial_output,
)
from .protocol import (
    CONFIGURATION_LOCK_SHA256,
    MODEL_MANIFEST_SHA256,
    MODEL_REPOSITORY,
    MODEL_REVISION,
    PROMPT_FILE_SHA256,
    PROMPT_SHA256,
)
from .structured_decoding import StructuredDecodingSession, model_vocab_size
from .structured_decoding_policy import (
    BACKEND_NAME,
    BACKEND_VERSION,
    CUDA_MASK_BACKEND,
    POLICY_VERSION,
    REPRESENTATION,
    SCHEMA_VERSION,
    SEMANTIC_REPAIR_PERMITTED,
    SOURCE_COMMIT,
    SOURCE_TAG,
    UNCONSTRAINED_FALLBACK_PERMITTED,
    require_frozen_packages,
)

CAPABILITY_SCHEMA = (
    "visual-semantic-editorial-structured-decoding-capability-1.0"
)
ENVIRONMENT_SCHEMA = (
    "visual-semantic-editorial-structured-decoding-environment-1.0"
)
QUALIFICATION_LOCK_SCHEMA = (
    "visual-semantic-editorial-structured-decoding-lock-1.0"
)


def _valid_observation(
    *,
    disposition: str = "Keep",
    reject_reason: str = "None",
) -> dict[str, Any]:
    value: dict[str, Any] = {
        "t": "A",
        "v": ["Y", "Y", "N", "N", "N"],
        "x": "N",
        "e": [["e1", Decimal("3.5"), "V"]],
    }
    if disposition == "Reject":
        if reject_reason == "RoutineTraversal":
            value["v"][2] = "Y"
        elif reject_reason == "AmbientChangeOnly":
            value["v"][4] = "Y"
        elif reject_reason == "NoDistinctEvent":
            value["v"][0] = "N"
        elif reject_reason == "NoObservablePayoff":
            value["v"][1] = "N"
        elif reject_reason == "MissingRequiredContext":
            value["v"][3] = "Y"
    elif disposition == "Unsure":
        value["v"][0] = "U"
    return value


def _fixture_corpus(
) -> list[tuple[str, dict[str, Any], bool, bool]]:
    fixtures = [
        ("Keep", _valid_observation(), True, True),
        (
            "RejectRoutine",
            _valid_observation(
                disposition="Reject",
                reject_reason="RoutineTraversal",
            ),
            True,
            True,
        ),
        (
            "RejectAmbient",
            _valid_observation(
                disposition="Reject",
                reject_reason="AmbientChangeOnly",
            ),
            True,
            True,
        ),
        (
            "RejectDistinct",
            _valid_observation(
                disposition="Reject",
                reject_reason="NoDistinctEvent",
            ),
            True,
            True,
        ),
        (
            "RejectPayoff",
            _valid_observation(
                disposition="Reject",
                reject_reason="NoObservablePayoff",
            ),
            True,
            True,
        ),
        (
            "RejectContext",
            _valid_observation(
                disposition="Reject",
                reject_reason="MissingRequiredContext",
            ),
            True,
            True,
        ),
        ("Unsure", _valid_observation(disposition="Unsure"), True, True),
    ]
    unknown = _valid_observation()
    unknown["confidence"] = Decimal("0.9")
    fixtures.append(("UnknownProperty", unknown, False, False))
    missing = _valid_observation()
    del missing["t"]
    fixtures.append(("MissingProperty", missing, False, False))
    wrong_enum = _valid_observation()
    wrong_enum["t"] = "Maybe"
    fixtures.append(("WrongEnum", wrong_enum, False, False))
    outside = _valid_observation()
    outside["e"][0][1] = Decimal("10.126")
    fixtures.append(("AboveReviewBound", outside, False, False))
    negative = _valid_observation()
    negative["e"][0][1] = Decimal("-0.001")
    fixtures.append(("BelowZero", negative, False, False))
    bad_branch = _valid_observation()
    bad_branch["e"][0][2] = "B"
    fixtures.append(("InconsistentTranscriptBasis", bad_branch, True, False))
    bad_tuple = _valid_observation()
    bad_tuple["e"][0].append("extra")
    fixtures.append(("WrongEvidenceTupleSize", bad_tuple, False, False))
    bad_id = _valid_observation()
    bad_id["e"][0][0] = " bad"
    fixtures.append(("InvalidEvidenceId", bad_id, False, False))
    return fixtures


def _grammar_accepts(xgr: Any, grammar: Any, value: str) -> bool:
    matcher = xgr.GrammarMatcher(grammar)
    accepted = bool(matcher.accept_string(value))
    return accepted and bool(matcher.is_completed())


def _strict_parser_accepts(value: str) -> bool:
    try:
        parse_and_canonicalize_editorial_output(
            value,
            review_duration_seconds=Decimal("10.125"),
            candidate_start_seconds=Decimal("2"),
            candidate_end_seconds=Decimal("6"),
        )
        return True
    except EditorialContractError:
        return False


def _hash_text(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def verify_structured_decoding(
    model_path: Path,
    output_path: Path,
    environment_output_path: Path,
    qualification_lock_output_path: Path,
) -> None:
    """Verify tokenizer/schema/HF integration without loading model weights."""
    _set_failure_stage("RuntimeInitialization")
    packages = require_frozen_packages()
    _validate_model_directory(model_path)
    try:
        import transformers
        import xgrammar as xgr

        processor = transformers.AutoProcessor.from_pretrained(
            model_path,
            trust_remote_code=False,
            local_files_only=True,
        )
        config = transformers.AutoConfig.from_pretrained(
            model_path,
            trust_remote_code=False,
            local_files_only=True,
        )
    except Exception as error:
        from .structured_decoding_policy import (
            StructuredDecodingUnavailableError,
        )

        raise StructuredDecodingUnavailableError(
            "Could not load the pinned local tokenizer/config for the "
            "model-free capability check: "
            f"{type(error).__name__}: {error}"
        ) from error

    vocabulary_size = model_vocab_size(type("_Model", (), {
        "config": config,
    })())
    session = StructuredDecodingSession(
        processor.tokenizer,
        vocabulary_size,
    )
    grammar, audit, schema_text = session.compile_case(
        Decimal("10.125"),
        Decimal("2"),
        Decimal("6"),
    )
    generation_config = transformers.GenerationConfig.from_pretrained(
        model_path,
        local_files_only=True,
    )
    eos_token_ids = _normalized_eos_token_ids(
        SimpleNamespace(generation_config=generation_config)
    )
    processor_instance = session.new_logits_processor(
        grammar,
        eos_token_ids,
    )
    try:
        import torch

        if not torch.cuda.is_available():
            raise RuntimeError("CUDA is unavailable.")
        input_ids = torch.zeros(
            (1, 1),
            dtype=torch.long,
            device="cuda",
        )
        scores = torch.zeros(
            (1, vocabulary_size),
            dtype=torch.float32,
            device="cuda",
        )
        masked_scores = processor_instance(input_ids, scores)
        eos_masked_before_completion = all(
            not bool(torch.isfinite(masked_scores[0, token_id]).item())
            for token_id in eos_token_ids
        )
        finite_score_count = int(
            torch.isfinite(masked_scores).sum().item()
        )
        cuda_mask_succeeded = (
            masked_scores.device.type == "cuda"
            and 0 < finite_score_count < vocabulary_size
        )
    except Exception:
        cuda_mask_succeeded = False
        finite_score_count = 0
        eos_masked_before_completion = False
    fixture_rows: list[dict[str, Any]] = []
    for (
        name,
        value,
        expected_schema_acceptance,
        expected_parser_acceptance,
    ) in _fixture_corpus():
        text = canonical_schema_json(value)
        schema_accepted = _grammar_accepts(xgr, grammar, text)
        parser_accepted = _strict_parser_accepts(text)
        fixture_rows.append({
            "name": name,
            "expectedSchemaAcceptance": expected_schema_acceptance,
            "expectedStrictParserAcceptance":
                expected_parser_acceptance,
            "schemaAccepted": schema_accepted,
            "strictParserAccepted": parser_accepted,
            "canonicalJsonSha256": _hash_text(text),
        })

    valid_rows = [
        row for row in fixture_rows
        if row["expectedSchemaAcceptance"]
    ]
    invalid_rows = [
        row for row in fixture_rows
        if not row["expectedSchemaAcceptance"]
    ]
    checks = {
        "exactPinnedPackages": True,
        "tokenizerIntegration": True,
        "modelVocabularySize": vocabulary_size > 0,
        "jsonSchemaCompilation": True,
        "requiredPropertyAndEnumCoverage": all(
            row["schemaAccepted"] for row in valid_rows
        ),
        "numericBounds": all(
            not row["schemaAccepted"]
            for row in fixture_rows
            if row["name"] in {"AboveReviewBound", "BelowZero"}
        ),
        "invalidShapeRejection": all(
            not row["schemaAccepted"] for row in invalid_rows
        ),
        "strictParserTruthTableAuthority": all(
            row["strictParserAccepted"] ==
            row["expectedStrictParserAcceptance"]
            for row in fixture_rows
        ),
        "huggingFaceLogitsProcessor": callable(processor_instance),
        "portableCudaMaskApplication": cuda_mask_succeeded,
        "allModelEosMaskedUntilGrammarCompletion":
            eos_masked_before_completion,
        "grammarCompletionAndEosEligibility": all(
            row["schemaAccepted"] for row in valid_rows
        ),
    }
    capability_succeeded = all(checks.values())

    environment = {
        "schemaVersion": ENVIRONMENT_SCHEMA,
        "pythonVersion": platform.python_version(),
        "pythonExecutableSha256": _sha256_file(Path(sys.executable)),
        "platform": platform.platform(),
        "packages": packages,
        "xgrammarSourceTag": SOURCE_TAG,
        "xgrammarSourceCommit": SOURCE_COMMIT,
    }
    environment["canonicalHash"] = _canonical_json_sha256(environment)

    capability = {
        "schemaVersion": CAPABILITY_SCHEMA,
        "policyVersion": POLICY_VERSION,
        "backendName": BACKEND_NAME,
        "backendVersion": BACKEND_VERSION,
        "representation": REPRESENTATION,
        "constraintSchemaVersion": SCHEMA_VERSION,
        "constraintSchemaSha256": audit.schema_sha256,
        "constraintSchemaBytes": len(schema_text.encode("utf-8")),
        "modelRepository": MODEL_REPOSITORY,
        "modelRevision": MODEL_REVISION,
        "modelManifestSha256": MODEL_MANIFEST_SHA256,
        "modelVocabularySize": vocabulary_size,
        "modelEosTokenIds": eos_token_ids,
        "cudaMaskBackend": CUDA_MASK_BACKEND,
        "firstStepAllowedTokenCount": finite_score_count,
        "checks": checks,
        "fixtures": fixture_rows,
        "capabilitySucceeded": capability_succeeded,
    }
    capability["canonicalHash"] = _canonical_json_sha256(capability)

    lock = {
        "schemaVersion": QUALIFICATION_LOCK_SCHEMA,
        "policyVersion": POLICY_VERSION,
        "backendName": BACKEND_NAME,
        "backendVersion": BACKEND_VERSION,
        "representation": REPRESENTATION,
        "cudaMaskBackend": CUDA_MASK_BACKEND,
        "constraintSchemaVersion": SCHEMA_VERSION,
        "constraintSchemaSha256": audit.schema_sha256,
        "environmentCanonicalHash": environment["canonicalHash"],
        "pythonExecutableSha256":
            environment["pythonExecutableSha256"],
        "capabilityCanonicalHash": capability["canonicalHash"],
        "configurationLockCanonicalHash": CONFIGURATION_LOCK_SHA256,
        "promptSha256": PROMPT_SHA256,
        "promptFileSha256": PROMPT_FILE_SHA256,
        "modelManifestSha256": MODEL_MANIFEST_SHA256,
        "unconstrainedFallbackPermitted":
            UNCONSTRAINED_FALLBACK_PERMITTED,
        "semanticRepairPermitted": SEMANTIC_REPAIR_PERMITTED,
        "capabilitySucceeded": capability_succeeded,
        "lockedAtUtc": (
            datetime.now(timezone.utc)
            .isoformat(timespec="microseconds")
            .replace("+00:00", "Z")
        ),
    }
    lock["canonicalHash"] = _canonical_json_sha256(lock)

    _set_failure_stage("OutputWrite")
    _write_json_atomic(environment_output_path, environment)
    _write_json_atomic(output_path, capability)
    _write_json_atomic(qualification_lock_output_path, lock)


__all__ = [name for name in globals() if not name.startswith("__")]
