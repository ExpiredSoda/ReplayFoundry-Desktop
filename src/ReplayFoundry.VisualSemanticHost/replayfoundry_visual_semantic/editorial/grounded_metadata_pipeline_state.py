"""Mutable phase state for the bounded grounded-metadata synthesis pipeline."""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Callable

from .grounded_metadata_pipeline_contract import GroundingPacket


@dataclass(frozen=True)
class SynthesisFunctions:
    generate_json_once: Callable[..., Any]
    generate_rephrase_json_once: Callable[..., Any]
    validation_failure_code: Callable[[Exception], str]
    grounded_metadata_module_identities: Callable[[], list[dict[str, str]]]


@dataclass(frozen=True)
class SynthesisContext:
    request: dict[str, Any]
    case_ordinal: int
    prompt_text: str
    packet: GroundingPacket
    grounding_packet_reused: bool
    model: Any
    processor: Any
    torch: Any
    torchcodec: Any
    process_vision_info: Any
    session: Any
    synthesis_started: float
    visual_drafts: list[dict[str, Any]]
    visual_draft_records: list[dict[str, Any]]
    stable_readable_text: list[str]
    visual_event_selection_applied: bool
    actor_authority_assessment_applied: bool
    primary_visual_draft_ordinal: int
    primary_actor_authority: str
    primary_creator_experience_relation: str
    visual_event_selection_assessments: list[dict[str, Any]]
    editorial_frame: dict[str, Any]
    knowledge_selection_applied: bool
    selected_current_passage_id: str
    knowledge_selection_assessments: list[dict[str, Any]]
    synthesis_request: dict[str, Any]
    grammar: Any
    base_audit: Any
    grounded_metadata_module_identities: list[dict[str, str]]
    all_prior_accepted_titles: tuple[Any, ...]
    prior_title_bodies: tuple[str, ...]
    metadata_grammar_cache: dict[str, tuple[Any, Any]] = field(default_factory=dict)


def scoped_metadata_grammar(context: Any, request: dict[str, Any]) -> tuple[Any, Any]:
    """Match the decoder to this pass's authority without changing its evidence."""
    from .grounded_metadata_output_schema import metadata_schema
    from .grounded_metadata_pipeline_contract import METADATA_SCHEMA_VERSION
    from .grounded_metadata_json_whitespace import ANY_WHITESPACE

    canonical, schema_hash = metadata_schema(request)
    _, original_hash = metadata_schema(context.synthesis_request)
    if schema_hash == original_hash:
        return context.grammar, context.base_audit
    cache = getattr(context, "metadata_grammar_cache", None)
    if cache is not None and schema_hash in cache:
        return cache[schema_hash]
    grammar = context.session.compile_json_schema(
        canonical, METADATA_SCHEMA_VERSION, schema_hash, any_whitespace=ANY_WHITESPACE,
    )
    if cache is not None:
        cache[schema_hash] = grammar
    return grammar


@dataclass
class SynthesisProgress:
    rejected_rules: list[str] = field(default_factory=list)
    correction_rule_codes: list[str] = field(default_factory=list)
    validation_feedback: str | None = None
    retry_correction_envelope: dict[str, Any] | None = None
    schema_valid_rejected_json: str | None = None
    schema_valid_rejected_pass_ordinal: int | None = None
    first_schema_valid_rejected_json: str | None = None
    first_schema_valid_rejected_json_sha256: str | None = None
    first_schema_valid_rejection_code: str | None = None
    withhold_unreviewed_transcripts: bool = False
    primary_only_synthesis_evidence: bool = False
    duplicate_synthesis_recovery_applied: bool = False
    duplicate_synthesis_source_pass_ordinal: int | None = None
    duplicate_synthesis_repeated_pass_ordinal: int | None = None
    duplicate_synthesis_source_rejected_json_sha256: str | None = None
    duplicate_synthesis_repeated_rejected_json_sha256: str | None = None
    semantic_exhaustion_recovery_applied: bool = False
    synthesis_recovery_pool_applied: bool = False
    synthesis_recovery_pool_source_json: str | None = None
    synthesis_recovery_pool_source_pass_ordinal: int | None = None
    synthesis_recovery_pool_source_rejected_json_sha256: str | None = None
    synthesis_recovery_pool_source_selection_reason: str | None = None
    synthesis_recovery_pool_selected_candidate_ordinal: int | None = None
    synthesis_recovery_pool_attempted_candidate_count: int = 0
    sticky_retry_envelope: dict[str, Any] | None = None
    sticky_retry_authority: dict[str, Any] | None = None
    sticky_retry_anchor_applied: bool = False
    sticky_retry_source_pass_ordinal: int | None = None
    sticky_retry_source_rule: str | None = None
    sticky_retry_envelope_sha256: str | None = None
    sticky_retry_authority_sha256: str | None = None
    sticky_retry_disabled_reason: str | None = None
    synthesis_pass_attestations: list[dict[str, Any]] = field(default_factory=list)
    synthesis_pass_count: int = 0
    diversity_result: Any = None
    metadata: dict[str, Any] | None = None
    trace: Any = None
    audit: Any = None
    decoded_sha256: str | None = None
    completed_json: str | None = None
    metadata_review_issues: list[str] = field(default_factory=list)
    editorial_rephrase_attempted: bool = False
    editorial_rephrase_applied: bool = False
    editorial_rephrase_outcome: str = "NotAttempted"
    editorial_rephrase_rejection_code: str | None = None
    editorial_rephrase_source_json_sha256: str | None = None
    editorial_rephrase_output_json_sha256: str | None = None
    editorial_rephrase_attestation: dict[str, Any] | None = None
    isolated_field_authoring: dict[str, Any] | None = None
