"""Compact authoring inputs; full evidence remains owned by the validators."""
from __future__ import annotations

import json
from typing import Any

from .grounded_metadata_creator_authority import (
    _requires_balanced_copy, _scoped_retry_authority, _without_automatic_commentary,
)
from .grounded_metadata_output_schema import title_body_maximum
from .grounded_metadata_rephrase_messages import _compact_balanced_messages
from .grounded_metadata_synthesis import (
    _effective_voice_perspective, _typed_retry_authority_anchor, _variant_intent_guidance,
)
from .grounded_metadata_synthesis_retry_messages import (
    build_retry_messages, validate_retry_message_plan,
)


def _metadata_messages(
    request: dict[str, Any],
    prompt_text: str,
    validation_feedback: str | None = None,
    grounded_drafts: list[dict[str, Any]] | None = None,
    primary_visual_draft_ordinal: int = 1,
    withhold_unreviewed_transcripts: bool = False,
    primary_only_evidence: bool = False,
    primary_actor_authority: str = "Unknown",
    primary_creator_experience_relation: str = "Unestablished",
    prior_accepted_title_bodies: tuple[str, ...] = (),
    schema_valid_rejected_json: str | None = None,
    rejected_rule_codes: tuple[str, ...] = (),
    duplicate_synthesis_recovery_applied: bool = False,
    retry_correction_envelope: dict[str, Any] | None = None,
    sticky_non_retrospective_envelope: dict[str, Any] | None = None,
    typed_retry_authority_anchor: dict[str, Any] | None = None,
    withhold_rejected_audience_copy: bool = False,
) -> list[dict[str, Any]]:
    if not grounded_drafts:
        raise ValueError("Metadata synthesis requires at least one visual draft.")
    if not 1 <= primary_visual_draft_ordinal <= len(grounded_drafts):
        raise ValueError("Metadata synthesis primary draft is out of range.")
    if withhold_unreviewed_transcripts or primary_only_evidence:
        request = _without_automatic_commentary(request)
        typed_retry_authority_anchor = _scoped_retry_authority(
            typed_retry_authority_anchor, withhold_unreviewed_transcripts, primary_only_evidence,
        )
    retry_plan = validate_retry_message_plan(
        validation_feedback, schema_valid_rejected_json, rejected_rule_codes,
        retry_correction_envelope, sticky_non_retrospective_envelope,
        typed_retry_authority_anchor, withhold_rejected_audience_copy, primary_only_evidence,
    )
    authority = _typed_retry_authority_anchor(
        request, grounded_drafts, primary_visual_draft_ordinal,
        primary_actor_authority, primary_creator_experience_relation,
    )
    authority = _scoped_retry_authority(
        authority, withhold_unreviewed_transcripts, primary_only_evidence,
    )
    assert authority is not None
    profile = request["profile"]
    title_maximum = title_body_maximum(request["game"]["hashtag"])
    if _requires_balanced_copy(request):
        messages = _compact_balanced_messages(
            authority, profile["variantIntent"], title_maximum=title_maximum,
            prior_titles=prior_accepted_title_bodies,
        )
    else:
        # Exclude raw automatic transcripts, discarded knowledge, source paths
        # and repeated analysis summaries. Keep exact permitted fact bindings.
        payload = {
            "typedAuthority": authority,
            "style": {
                "voicePerspective": _effective_voice_perspective(
                    profile["voicePerspective"], primary_actor_authority,
                    primary_creator_experience_relation,
                ),
                "audienceAddress": profile.get("audienceAddress"),
                "namingGuidance": profile.get("namingGuidance"),
                "defaultTags": profile.get("defaultTags", []),
                "angle": _variant_intent_guidance(profile["variantIntent"]),
                "titleBodyMaximumCharacters": title_maximum,
                "priorTitleExclusions": list(prior_accepted_title_bodies),
            },
        }
        messages = [
            {"role": "system", "content": [{"type": "text", "text": prompt_text}]},
            {"role": "user", "content": [{"type": "text", "text": json.dumps(
                payload, ensure_ascii=False, sort_keys=True, separators=(",", ":"), allow_nan=False,
            )}]},
        ]
        voice = payload["style"]["voicePerspective"]
        closing = (
            "Write my title as I followed by a completed past-tense action. "
            "My description must begin differently and add another supported detail. "
            if voice == "CreatorFirstPerson" and primary_actor_authority == "CreatorControlled"
            and primary_creator_experience_relation == "CreatorActed" else
            "Write a neutral title about the completed event, using past tense. "
            "Begin the description differently and add another supported detail. "
        )
        messages[1]["content"][0]["text"] += "\n" + closing + (
            "The action labels above are notes. Rewrite their grammar: do not output "
            "present-tense labels. Return the finished JSON now."
        )
    if retry_plan.requested:
        messages.extend(build_retry_messages(
            retry_plan, validation_feedback, schema_valid_rejected_json,
            rejected_rule_codes, duplicate_synthesis_recovery_applied,
            retry_correction_envelope, sticky_non_retrospective_envelope,
            typed_retry_authority_anchor, withhold_rejected_audience_copy,
            primary_actor_authority, primary_creator_experience_relation,
        ))
    return messages
