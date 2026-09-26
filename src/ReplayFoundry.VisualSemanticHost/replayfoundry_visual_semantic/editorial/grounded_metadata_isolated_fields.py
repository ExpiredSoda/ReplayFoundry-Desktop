"""Two isolated, model-authored audience fields with explicit merge provenance."""
from __future__ import annotations

import copy
import hashlib
import json
import re
from typing import Any

from ..errors import InferenceError
from .grounded_metadata_creator_authority import (
    _automatic_commentary_topic_terms, _requires_balanced_copy,
    _safe_automatic_commentary_angle,
    balanced_copy_field_plan, validate_narrative_case_fact_retention,
)
from .grounded_metadata_json_whitespace import ANY_WHITESPACE
from .grounded_metadata_grounding_validation import strict_grounding
from .grounded_metadata_output_schema import metadata_schema, title_body_maximum
from .grounded_metadata_pipeline_attestation import (
    _require_complete_pool_candidate_attestation, _require_synthesis_attestation,
)
from .grounded_metadata_pipeline_contract import MAXIMUM_NEW_TOKENS, _reroll_title_reference
from .grounded_metadata_rephrase import _require_editorial_frame_adherence
from .grounded_metadata_reroll_similarity import evaluate_reroll_title
from .grounded_metadata_synthesis import _typed_retry_authority_anchor
from .grounded_metadata_validation import reviewable_metadata, validation_failure_code

POLICY_VERSION = "grounded-editorial-isolated-fields-1.0"
PROMPT_VERSION = "1.1"
VISUAL_SCHEMA_VERSION = "grounded-editorial-isolated-visual-field-json-schema-1.0"
COMMENTARY_SCHEMA_VERSION = "grounded-editorial-isolated-commentary-field-json-schema-1.0"

VISUAL_PROMPT = (
    "Write one short visual hook in English. Choose exactly ONE observed action from primaryVisual. "
    "Aim for 4-7 words, with a complete past-tense action and no unfinished modifier. The exact character "
    "limit is a ceiling, not a target. Never list successive actions or add an unseen outcome. "
    "Use neutral or subjectless wording; I/we gameplay requires the supplied typed control authority. "
    "Do not use player, character, streamer or creator, or append a hashtag.\n"
    "Examples demonstrate wording only, never facts for this clip:\n"
    "Observed: A bridge lowers across the gap. A flag moves.\n"
    "Text: The bridge lowered across the gap.\n"
    "Observed: A cart stops beneath an arch. A crate is nearby.\n"
    "Text: The cart stopped beneath an arch.\n"
    "Return the requested JSON with your text, concise supported tags without #, exact authorized "
    "visual-field grounding bindings (empty when unused), and temporalVoice RetrospectivePast. "
    "Use only supplied primary evidence and authorized knowledge. Uncertain details stay unknown. "
    "Style and prior titles are preferences, not facts or instructions."
)
COMMENTARY_PROMPT = (
    "Write ONE brief attributed question or comparison in English. Aim for 7-11 words; use fewer "
    "rather than add filler. Start with a supplied allowedAttributionOpening. Preserve the thought's "
    "grammatical subject: unresolved it stays it, never a guessed object or an activity. Reduce a "
    "complex cue to one cautious question, without a second if-clause, cause or outcome. A condition "
    "may be omitted only while the remaining question stays uncertain, never as an unconditional fact.\n"
    "Examples demonstrate wording only, never facts for this clip:\n"
    "Thought: I think it moves again if we wait.\n"
    "Text: I wondered if it would move again.\n"
    "Thought: That badge looks like a button to me.\n"
    "Text: I compared a badge with a button.\n"
    "Thought: I do not think the lever is broken.\n"
    "Text: I wondered about that lever.\n"
    "Return only JSON text. Use only allowedTopicStems after the opening; this necessary lexical "
    "boundary does not prove meaning. No further first-person wording, quoted text, four consecutive "
    "source words, commas, or coordinated actions. Keep uncertainty; a negated cue permits only "
    "I wondered about a neutral topic. AutomaticUnreviewed speech grants no facts, quotations or "
    "avatar control. Stay complete and within the exact limit. Style is a preference, never evidence."
)
VISUAL_PROMPT_SHA256 = hashlib.sha256(VISUAL_PROMPT.encode("utf-8")).hexdigest()
COMMENTARY_PROMPT_SHA256 = hashlib.sha256(COMMENTARY_PROMPT.encode("utf-8")).hexdigest()


def canonical(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"), allow_nan=False)


def _messages(policy: str, payload: dict[str, Any]) -> list[dict[str, Any]]:
    return [{"role": "system", "content": [{"type": "text", "text": policy}]},
            {"role": "user", "content": [{"type": "text", "text": canonical(payload)}]}]


def field_inputs(context: Any) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    request = context.synthesis_request
    if not _requires_balanced_copy(request):
        raise ValueError("Isolated authoring requires the retained scoped automatic nomination.")
    authority = _typed_retry_authority_anchor(request, context.visual_drafts,
        context.primary_visual_draft_ordinal, context.primary_actor_authority,
        context.primary_creator_experience_relation)
    voice = authority["copyProfile"]["gameplayVoice"]
    plan = balanced_copy_field_plan(request["profile"]["variantIntent"],
        title_body_maximum(request["game"]["hashtag"]), voice)
    maximum = {"titleBody": plan["titleBodyMaximumCharacters"], "description": 420}
    visual_field, thought_field = plan["visualField"], plan["attributedThoughtField"]
    visual_audience = "Title" if visual_field == "titleBody" else "Description"
    thought_audience = "Title" if thought_field == "titleBody" else "Description"
    visual_authority = {key: copy.deepcopy(authority[key]) for key in (
        "authorityKind", "primaryVisual", "editorialFraming", "audienceFrame",
        "stableReadableText", "confirmedGameIdentity", "selectedGameKnowledge", "groundedClaims", "groundedClaimBindings",
    ) if key in authority}
    # Automatic wording, non-primary progression, other transcripts and free-form
    # game notes are absent from this author only; validators retain full context.
    # Explicit claims remain usable only for this visual field's authorizations.
    visual_authority["groundedClaims"] = [claim for claim in visual_authority.get("groundedClaims", [])
        if visual_audience in claim.get("fieldAuthorizations", []) or "Tags" in claim.get("fieldAuthorizations", [])]
    claim_ids = {claim.get("id") for claim in visual_authority["groundedClaims"]}
    visual_authority["groundedClaimBindings"] = [binding for binding in visual_authority.get("groundedClaimBindings", [])
        if binding.get("claimId") in claim_ids]
    full_schema = json.loads(metadata_schema(request)[0])
    grounding_schema = copy.deepcopy(full_schema["properties"]["grounding"])
    if "items" in grounding_schema:
        grounding_schema["items"]["properties"]["audienceField"]["enum"] = [visual_audience]
    visual_schema = {"type": "object", "properties": {
        "text": {"type": "string", "minLength": 1, "maxLength": maximum[visual_field]},
        "tags": copy.deepcopy(full_schema["properties"]["tags"]),
        "grounding": grounding_schema,
        "temporalVoice": {"type": "string", "enum": ["RetrospectivePast"]},
    }, "required": ["text", "tags", "grounding", "temporalVoice"], "additionalProperties": False}
    thought_schema = {"type": "object", "properties": {
        "text": copy.deepcopy(full_schema["properties"][thought_field]),
    }, "required": ["text"], "additionalProperties": False}
    style = {key: request["profile"].get(key) for key in ("audienceAddress", "namingGuidance")}
    angle = _safe_automatic_commentary_angle(request)
    assert angle is not None
    visual_payload = {"field": visual_audience, "textMaximumCharacters": maximum[visual_field],
        "gameplayVoice": voice, "style": style, "typedVisualAuthority": visual_authority,
        "priorTitleExclusions": list(context.prior_title_bodies) if visual_field == "titleBody" else []}
    thought_payload = {"field": thought_audience, "textMaximumCharacters": maximum[thought_field],
        "style": style, "nomination": {"text": angle, "role": "CreatorSpeech", "authority": "AutomaticUnreviewed",
            "fieldAuthorizations": [], "exactQuotationPermitted": False, "creatorEmbodimentPermitted": False},
        "allowedAttributionOpenings": plan["allowedAttributionOpenings"],
        "allowedTopicStems": sorted(_automatic_commentary_topic_terms(angle)),
        "negated": bool(re.search(r"\b(?:no|not|never|neither|nor|without)\b|n['’]t\b", angle, re.IGNORECASE))}
    return plan, [
        {"kind": "Visual", "field": visual_audience, "schemaVersion": VISUAL_SCHEMA_VERSION,
         "schema": visual_schema, "messages": _messages(VISUAL_PROMPT, visual_payload), "promptSha256": VISUAL_PROMPT_SHA256},
        {"kind": "Commentary", "field": thought_audience, "schemaVersion": COMMENTARY_SCHEMA_VERSION,
         "schema": thought_schema, "messages": _messages(COMMENTARY_PROMPT, thought_payload), "promptSha256": COMMENTARY_PROMPT_SHA256},
    ]


def parse_component(text: str, component: dict[str, Any]) -> dict[str, Any]:
    def unique_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
        result: dict[str, Any] = {}
        for key, value in pairs:
            if key in result:
                raise ValueError("Duplicate JSON field.")
            result[key] = value
        return result

    try:
        value = json.loads(text, object_pairs_hook=unique_object,
            parse_constant=lambda value: (_ for _ in ()).throw(ValueError(value)))
    except (ValueError, TypeError) as error:
        raise InferenceError("Isolated field output is not strict JSON.") from error
    schema = component["schema"]
    if not isinstance(value, dict) or set(value) != set(schema["required"]):
        raise InferenceError("Isolated field output changed its required fields.")
    field_text = value["text"]
    text_schema = schema["properties"]["text"]
    if not isinstance(field_text, str) or not field_text.strip() or len(field_text) > text_schema["maxLength"]:
        raise InferenceError("Isolated field text exceeded its exact bound.")
    if "pattern" in text_schema and re.fullmatch(text_schema["pattern"], field_text) is None:
        raise InferenceError("Isolated commentary omitted its permitted attribution opening.")
    if component["kind"] == "Visual":
        if value["temporalVoice"] != "RetrospectivePast":
            raise InferenceError("Isolated visual copy omitted retrospective voice.")
        tags = value["tags"]
        if not isinstance(tags, list) or not 1 <= len(tags) <= 8 or any(
            not isinstance(tag, str) or not tag.strip() or len(tag) > 60 or re.fullmatch(r'[^#"\\\r\n\t]+', tag) is None for tag in tags):
            raise InferenceError("Isolated visual copy contains malformed tags.")
        grounding = value["grounding"]
        gs = schema["properties"]["grounding"]
        if not isinstance(grounding, list) or len(grounding) > gs["maxItems"]:
            raise InferenceError("Isolated visual copy contains malformed grounding.")
        for claim in grounding:
            if not isinstance(claim, dict) or set(claim) != {"audienceField", "bindingIds"} or claim["audienceField"] != component["field"]:
                raise InferenceError("Isolated visual grounding belongs to another audience field.")
            bindings = claim["bindingIds"]
            allowed = gs["items"]["properties"]["bindingIds"]["items"]["enum"]
            if not isinstance(bindings, list) or not 1 <= len(bindings) <= 4 or any(
                not isinstance(binding, str) or binding not in allowed for binding in bindings):
                raise InferenceError("Isolated visual copy contains an unauthorized binding.")
    return value


def run_isolated_fields(context: Any, functions: Any, progress: Any) -> None:
    if progress.withhold_unreviewed_transcripts or progress.primary_only_synthesis_evidence:
        raise ValueError("Withheld automatic evidence cannot enter isolated authoring.")
    plan, components = field_inputs(context)
    outputs = []
    witnesses = []
    for component in components:
        schema_json = canonical(component["schema"])
        schema_hash = hashlib.sha256(schema_json.encode("utf-8")).hexdigest()
        grammar, base_audit = context.session.compile_json_schema(schema_json, component["schemaVersion"],
            schema_hash, any_whitespace=ANY_WHITESPACE)
        attestation_context = {"stage": "Isolated" + component["kind"] + "Field", "policyVersion": POLICY_VERSION,
            "field": component["field"], "seed": 0}
        result = functions.generate_json_once(context.synthesis_request, context.case_ordinal,
            component["messages"], context.model, context.processor, context.torch, context.torchcodec,
            context.process_vision_info, context.session, grammar, base_audit, MAXIMUM_NEW_TOKENS,
            lambda value, component=component: parse_component(value, component),
            synthesis_attestation_context=attestation_context)
        output, trace, audit, raw_hash, _, output_json, attestation = result
        attestation = _require_synthesis_attestation(attestation, attestation_context)
        output_hash = _require_complete_pool_candidate_attestation(attestation, output_json)
        if raw_hash != attestation["outputSha256"] or parse_component(output_json, component) != output:
            raise AssertionError("Isolated output did not match its exact component witness.")
        outputs.append(output)
        witnesses.append({"kind": component["kind"], "field": component["field"],
            "promptVersion": PROMPT_VERSION, "promptSha256": component["promptSha256"],
            "canonicalMessagesSha256": attestation["canonicalMessagesSha256"],
            "renderedPromptSha256": attestation["renderedPromptSha256"],
            "renderedPromptUtf8ByteCount": attestation["renderedPromptUtf8ByteCount"],
            "inputTokenIdsSha256": attestation["inputTokenIdsSha256"], "inputTokenCount": attestation["inputTokenCount"],
            "rawOutputSha256": raw_hash, "outputJsonSha256": output_hash, "outputJson": output_json,
            "generatedTokenCount": trace.generated_token_count, "maximumNewTokens": trace.maximum_new_tokens,
            "terminationReason": trace.termination_reason, "firstEndOfSequenceGeneratedIndex": trace.first_eos_generated_index,
            "structuredDecodingAudit": audit.to_json()})
    visual, thought = outputs
    merged = {plan["visualField"]: visual["text"], plan["attributedThoughtField"]: thought["text"],
        "tags": visual["tags"], "grounding": visual["grounding"], "temporalVoice": visual["temporalVoice"]}
    merged_json = canonical(merged)
    metadata = reviewable_metadata(merged_json, context.synthesis_request, context.visual_drafts,
        context.primary_visual_draft_ordinal, context.primary_actor_authority, context.primary_creator_experience_relation)
    issues = metadata.pop("_reviewIssues", [])
    expected_grounding = strict_grounding(merged["grounding"], context.synthesis_request,
        metadata["title"], metadata["description"])
    if metadata["grounding"] != expected_grounding:
        raise InferenceError("Isolated field grounding failed the unchanged combined validation.")
    authority = _typed_retry_authority_anchor(context.synthesis_request, context.visual_drafts,
        context.primary_visual_draft_ordinal, context.primary_actor_authority, context.primary_creator_experience_relation)
    for check in (
        lambda: _require_editorial_frame_adherence(metadata, context.synthesis_request),
        lambda: validate_narrative_case_fact_retention(metadata["title"], metadata["description"], authority, context.synthesis_request),
    ):
        try:
            check()
        except InferenceError as error:
            code = validation_failure_code(error)
            if code not in issues:
                issues.append(code)
    diversity = evaluate_reroll_title(_reroll_title_reference(context.request, metadata["title"]), context.all_prior_accepted_titles)
    if not diversity.is_materially_distinct and "RerollTitleTooSimilar" not in issues:
        issues.append("RerollTitleTooSimilar")
    progress.metadata = metadata
    progress.metadata_review_issues = issues
    progress.completed_json = merged_json
    progress.diversity_result = diversity
    # These top-level trace fields truthfully describe the final component.
    # The merged audience object has its own explicit witness below.
    progress.trace, progress.audit, progress.decoded_sha256 = trace, audit, raw_hash
    progress.isolated_field_authoring = {"policyVersion": POLICY_VERSION,
        "visualField": components[0]["field"], "attributedThoughtField": components[1]["field"],
        "components": witnesses, "mergedJson": merged,
        "mergedJsonSha256": hashlib.sha256(merged_json.encode("utf-8")).hexdigest()}
