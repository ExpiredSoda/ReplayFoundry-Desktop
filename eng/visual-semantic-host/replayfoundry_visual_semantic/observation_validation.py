"""ReplayFoundry local visual-semantic host implementation module."""
from __future__ import annotations

from .trusted_identity import *  # noqa: F401,F403

def _parse_provider_observation(
    raw_text: str,
    request: dict[str, Any],
    case_ordinal: int = 1,
) -> dict[str, Any]:
    _set_failure_stage("OutputSafety")
    raw_bytes, observation = _provider_output_safety_gate(raw_text)
    _set_failure_stage("OutputValidation")
    _require_exact_keys(
        observation,
        PROVIDER_OBSERVATION_KEYS,
        "provider observation",
    )

    raw_provider_case_id = observation["caseId"]
    raw_provider_candidate_id = observation["candidateId"]
    if (
        isinstance(raw_provider_case_id, str)
        and 0 < len(raw_provider_case_id) <= 128
        and SAFE_ID_PATTERN.fullmatch(raw_provider_case_id) is not None
    ):
        _set_failure_provider_output(
            providerEchoCaseId=raw_provider_case_id,
        )
    if (
        isinstance(raw_provider_candidate_id, str)
        and 0 < len(raw_provider_candidate_id) <= 128
        and SAFE_ID_PATTERN.fullmatch(
            raw_provider_candidate_id
        ) is not None
    ):
        _set_failure_provider_output(
            providerEchoCandidateId=raw_provider_candidate_id,
        )
    provider_case_id = _require_id(
        raw_provider_case_id,
        "provider observation.caseId",
    )
    provider_candidate_id = _require_id(
        raw_provider_candidate_id,
        "provider observation.candidateId",
    )
    _set_failure_provider_output(
        providerEchoCaseId=provider_case_id,
        providerEchoCandidateId=provider_candidate_id,
    )
    if observation["schemaVersion"] != OBSERVATION_SCHEMA:
        _fail(InferenceError, "Provider observation schema is unsupported.")

    observable_content_type = _require_enum(
        observation["observableContentType"],
        OBSERVABLE_CONTENT_TYPES,
        "provider observation.observableContentType",
    )
    visible_state_change = _require_optional_exact_semantic_string(
        observation["visibleStateChange"],
        "provider observation.visibleStateChange",
        maximum=MAX_VISIBLE_STATE_CHANGE,
    )
    has_clear_beginning = _require_enum(
        observation["hasClearBeginning"],
        YES_NO_UNSURE,
        "provider observation.hasClearBeginning",
    )
    has_clear_outcome = _require_enum(
        observation["hasClearOutcome"],
        YES_NO_UNSURE,
        "provider observation.hasClearOutcome",
    )
    menu_or_traversal_present = _require_enum(
        observation["menuOrTraversalPresent"],
        YES_NO_UNSURE,
        "provider observation.menuOrTraversalPresent",
    )
    spoken_content_appears_relevant = _require_enum(
        observation["spokenContentAppearsRelevant"],
        YES_NO_UNKNOWN,
        "provider observation.spokenContentAppearsRelevant",
    )
    suggested_worth_reviewing = _require_enum(
        observation["suggestedWorthReviewing"],
        YES_NO_UNSURE,
        "provider observation.suggestedWorthReviewing",
    )
    review_certainty = _require_enum(
        observation["reviewCertainty"],
        REVIEW_CERTAINTIES,
        "provider observation.reviewCertainty",
    )

    video_duration: Decimal = request["_validated"]["videoDuration"]
    collections = _canonicalize_provider_collections(
        observation,
        video_duration,
    )
    rationale = _require_exact_semantic_string(
        observation["conciseRationale"],
        "provider observation.conciseRationale",
        maximum=MAX_RATIONALE,
    )

    provider_raw_output = {
        "caseId": provider_case_id,
        "candidateId": provider_candidate_id,
        "schemaVersion": observation["schemaVersion"],
        "observableContentType": observable_content_type,
        "visibleStateChange": visible_state_change,
        "hasClearBeginning": has_clear_beginning,
        "hasClearOutcome": has_clear_outcome,
        "menuOrTraversalPresent": menu_or_traversal_present,
        "spokenContentAppearsRelevant":
            spoken_content_appears_relevant,
        "suggestedWorthReviewing": suggested_worth_reviewing,
        "reviewCertainty": review_certainty,
        "evidenceIntervals": collections.raw_evidence_intervals,
        "uncertainties": collections.raw_uncertainties,
        "limitations": collections.raw_limitations,
        "conciseRationale": rationale,
    }
    canonical_provider_output = dict(provider_raw_output)
    canonical_provider_output["evidenceIntervals"] = (
        collections.canonical_evidence_intervals
    )
    canonical_provider_output["uncertainties"] = (
        collections.canonical_uncertainties
    )
    canonical_provider_output["limitations"] = (
        collections.canonical_limitations
    )
    trusted_raw_output, identity_audit = _bind_trusted_identity(
        provider_raw_output,
        request,
        case_ordinal,
        canonical_provider_payload=canonical_provider_output,
    )
    canonical_output = dict(canonical_provider_output)
    canonical_output["caseId"] = request["caseId"]
    canonical_output["candidateId"] = request["candidate"]["id"]

    normalization_kinds: list[str] = []
    if (
        collections.evidence_interval_order_changed
        or collections.duplicate_evidence_interval_count > 0
    ):
        normalization_kinds.append(
            "EvidenceIntervalsCanonicalized"
        )
    if (
        collections.limitation_order_changed
        or collections.duplicate_limitation_count > 0
    ):
        normalization_kinds.append("LimitationsCanonicalized")
    if (
        collections.uncertainty_order_changed
        or collections.duplicate_uncertainty_count > 0
    ):
        normalization_kinds.append("UncertaintiesCanonicalized")

    raw_output_sha256 = _canonical_json_sha256(trusted_raw_output)
    canonical_output_sha256 = _canonical_json_sha256(canonical_output)
    if normalization_kinds:
        if raw_output_sha256 == canonical_output_sha256:
            _fail(
                OutputError,
                "Provider normalization changed representation without "
                "changing the canonical semantic hash.",
            )
        canonical_output["normalizationAudit"] = {
            "caseId": request["caseId"],
            "rawGeneratedTextSha256": hashlib.sha256(
                raw_bytes
            ).hexdigest(),
            "rawOutputSha256": raw_output_sha256,
            "canonicalOutputSha256": canonical_output_sha256,
            "normalizationPolicyVersion":
                NORMALIZATION_POLICY_VERSION,
            "normalizationKinds": normalization_kinds,
            "rawEvidenceIntervalCount":
                len(collections.raw_evidence_intervals),
            "canonicalEvidenceIntervalCount":
                len(collections.canonical_evidence_intervals),
            "exactDuplicateEvidenceIntervalCount":
                collections.duplicate_evidence_interval_count,
            "evidenceIntervalOrderChanged":
                collections.evidence_interval_order_changed,
            "rawLimitationCount": len(collections.raw_limitations),
            "canonicalLimitationCount":
                len(collections.canonical_limitations),
            "exactDuplicateLimitationCount":
                collections.duplicate_limitation_count,
            "limitationOrderChanged":
                collections.limitation_order_changed,
            "rawUncertaintyCount":
                len(collections.raw_uncertainties),
            "canonicalUncertaintyCount":
                len(collections.canonical_uncertainties),
            "exactDuplicateUncertaintyCount":
                collections.duplicate_uncertainty_count,
            "uncertaintyOrderChanged":
                collections.uncertainty_order_changed,
            "semanticTextChanged": False,
            "normalizedAtUtc": (
                datetime.now(timezone.utc)
                .isoformat(timespec="microseconds")
                .replace("+00:00", "Z")
            ),
        }
    else:
        if raw_output_sha256 != canonical_output_sha256:
            _fail(
                OutputError,
                "Canonical provider output changed without an allowlisted "
                "normalization.",
            )
        canonical_output["normalizationAudit"] = None

    canonical_output["identityBindingAudit"] = identity_audit
    return canonical_output


def _classify_provider_observation_for_audit(
    observation: dict[str, Any],
    request: dict[str, Any],
    case_ordinal: int = 1,
) -> tuple[list[str], str | None]:
    failed_invariants: list[str] = []
    try:
        _require_exact_keys(
            observation,
            PROVIDER_OBSERVATION_KEYS,
            "provider observation",
        )

        collections = _canonicalize_provider_collections(
            observation,
            request["_validated"]["videoDuration"],
        )
        if collections.evidence_interval_order_changed:
            failed_invariants.append(
                "EvidenceIntervalsOutOfOrder"
            )
        if collections.duplicate_evidence_interval_count > 0:
            failed_invariants.append(
                "ExactDuplicateEvidenceIntervals"
            )
        if collections.limitation_order_changed:
            failed_invariants.append("LimitationsOutOfOrder")
        if collections.duplicate_limitation_count > 0:
            failed_invariants.append("ExactDuplicateLimitations")
        if collections.uncertainty_order_changed:
            failed_invariants.append("UncertaintiesOutOfOrder")
        if collections.duplicate_uncertainty_count > 0:
            failed_invariants.append("ExactDuplicateUncertainties")

        validation_observation = dict(observation)
        validation_observation["evidenceIntervals"] = (
            collections.canonical_evidence_intervals
        )
        validation_observation["uncertainties"] = (
            collections.canonical_uncertainties
        )
        validation_observation["limitations"] = (
            collections.canonical_limitations
        )
        validation_text = json.dumps(
            validation_observation,
            ensure_ascii=False,
            separators=(",", ":"),
            allow_nan=False,
        )
        _parse_provider_observation(
            validation_text,
            request,
            case_ordinal,
        )
        return failed_invariants, None
    except HostError as error:
        failed_invariants.append("OtherSchemaViolation")
        return failed_invariants, str(error)
    except (KeyError, TypeError, ValueError, UnicodeError) as error:
        failed_invariants.append("OtherSchemaViolation")
        return (
            failed_invariants,
            f"{type(error).__name__}: {error}",
        )


def _capture_provider_output_audit(
    raw_text: str,
    request: dict[str, Any],
    output_path: Path,
    identity: dict[str, Any],
    model_elapsed_seconds: float,
    generation_case: dict[str, Any],
    case_ordinal: int = 1,
) -> NoReturn:
    raw_bytes, stripped = _provider_output_text_safety_gate(raw_text)
    raw_hash = hashlib.sha256(raw_bytes).hexdigest()
    if (
        generation_case["decodedTextSha256"] != raw_hash
        or generation_case["decodedTextUtf8ByteCount"] !=
            len(raw_bytes)
    ):
        _fail(
            OutputError,
            "Generation telemetry does not match the raw provider-output "
            "audit text.",
        )
    parsed_value, json_parse = _parse_provider_json_for_audit(stripped)
    if isinstance(parsed_value, dict):
        observation: dict[str, Any] | None = parsed_value
        failed_invariants, validation_failure = (
            _classify_provider_observation_for_audit(
                observation,
                request,
                case_ordinal,
            )
        )
    else:
        observation = None
        if json_parse["succeeded"]:
            failed_invariants = ["OtherSchemaViolation"]
            validation_failure = (
                "Provider observation must be a JSON object."
            )
        else:
            failed_invariants = ["InvalidJson"]
            line = json_parse["line"]
            column = json_parse["column"]
            if line is None or column is None:
                validation_failure = json_parse["message"]
            else:
                validation_failure = (
                    f"Provider returned invalid JSON at line {line}, "
                    f"column {column}: {json_parse['message']}"
                )
    _revalidate_media_inputs([request])

    payload = {
        "schemaVersion": RAW_OUTPUT_AUDIT_SCHEMA,
        "caseId": request["caseId"],
        "candidateId": request["candidate"]["id"],
        "createdAtUtc": (
            datetime.now(timezone.utc)
            .isoformat(timespec="microseconds")
            .replace("+00:00", "Z")
        ),
        "rawGeneratedTextSha256": raw_hash,
        "rawGeneratedTextUtf8ByteCount": len(raw_bytes),
        "rawGeneratedTextMaximumUtf8ByteCount":
            MAX_RAW_AUDIT_TEXT_BYTES,
        "rawGeneratedText": raw_text,
        "jsonParse": json_parse,
        "parsedPropertyNames": (
            None
            if observation is None
            else list(observation.keys())
        ),
        "rawEvidenceIntervals":
            None
            if observation is None
            else observation.get("evidenceIntervals"),
        "rawLimitations":
            None
            if observation is None
            else observation.get("limitations"),
        "rawUncertainties":
            None
            if observation is None
            else observation.get("uncertainties"),
        "failedInvariants": failed_invariants,
        "strictValidationFailure": validation_failure,
        "modelElapsedSeconds": round(model_elapsed_seconds, 6),
        "generation": generation_case,
        "identity": identity,
    }
    _write_json_atomic(output_path, payload)
    _fail(
        RawAuditCaptured,
        "External raw provider-output audit captured; "
        "ordinary batch output was intentionally withheld.",
    )



__all__ = [name for name in globals() if not name.startswith("__")]
