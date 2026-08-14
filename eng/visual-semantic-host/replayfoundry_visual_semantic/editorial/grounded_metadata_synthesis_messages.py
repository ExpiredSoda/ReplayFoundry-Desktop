"""Frozen grounded-metadata synthesis message assembly."""
from __future__ import annotations
import json
from typing import Any

from .grounded_metadata_reroll_similarity import REROLL_DIVERSITY_POLICY_VERSION
from .grounded_metadata_synthesis import (
    STABLE_READABLE_TEXT_POLICY_VERSION,
    SYNTHESIS_EVIDENCE_POLICY_VERSION,
    _model_context,
    _stable_readable_text,
    _synthesis_draft,
    _variant_intent_guidance,
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
    context = json.dumps(
        _model_context(
            request,
            include_game_knowledge=not primary_only_evidence,
            include_clip_context=not primary_only_evidence,
            include_unreviewed_transcripts=
                not withhold_unreviewed_transcripts,
            include_game_identity=not withhold_unreviewed_transcripts,
            include_game_notes=not primary_only_evidence,
            primary_actor_authority=primary_actor_authority,
            primary_creator_experience_relation=
                primary_creator_experience_relation,
        ),
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
        allow_nan=False,
    )
    retry_requested = validation_feedback is not None
    if retry_requested != (schema_valid_rejected_json is not None):
        raise ValueError(
            "Metadata retry guidance and rejected JSON must be supplied together."
        )
    if retry_requested and not rejected_rule_codes:
        raise ValueError("Metadata retry guidance requires typed rejected rules.")
    if not retry_requested and retry_correction_envelope is not None:
        raise ValueError(
            "Metadata correction diagnostics require a rejected JSON target."
        )
    sticky_retry_requested = sticky_non_retrospective_envelope is not None
    if sticky_retry_requested and typed_retry_authority_anchor is None:
        raise ValueError(
            "A sticky grammar target requires its typed authority anchor."
        )
    if sticky_retry_requested and not retry_requested:
        raise ValueError("A sticky grammar target requires a metadata retry.")
    if typed_retry_authority_anchor is not None and not retry_requested:
        raise ValueError("A typed authority anchor requires a metadata retry.")
    withhold_cross_draft_copy = (
        withhold_rejected_audience_copy
        and primary_only_evidence
        and "CrossDraftTitleContamination" in rejected_rule_codes
    )
    withhold_creator_authority_copy = (
        withhold_rejected_audience_copy
        and not withhold_cross_draft_copy
        and "UnsupportedCreatorEmbodiment" in rejected_rule_codes
    )
    if withhold_rejected_audience_copy and (
        not retry_requested
        or not (withhold_cross_draft_copy or withhold_creator_authority_copy)
        or retry_correction_envelope is not None
    ):
        raise ValueError(
            "Withholding rejected audience copy requires a supported typed "
            "rejection without a compact copy target."
        )
    transcript_safety = (
        "\nAuthoritative retry safety: no spoken or readable wording is authorized "
        "for audience copy in this pass. Do not quote, paraphrase, summarize, name, "
        "or complete any message content. Describe only supported physical action, "
        "objects, setting, or visible outcome."
        " Replay Foundry retains and appends the confirmed game hashtag outside "
        "this model pass."
        if withhold_unreviewed_transcripts
        else ""
    )
    stable_readable_text = (
        []
        if primary_only_evidence
        else _stable_readable_text(grounded_drafts)
    )
    draft_items = (
        [
            (
                primary_visual_draft_ordinal,
                grounded_drafts[primary_visual_draft_ordinal - 1],
            )
        ]
        if primary_only_evidence
        else list(enumerate(grounded_drafts, start=1))
    )
    chronological_drafts = [
        {
            "ordinal": ordinal,
            "isPrimary": ordinal == primary_visual_draft_ordinal,
            "draft": _synthesis_draft(draft, stable_readable_text),
        }
        for ordinal, draft in draft_items
    ]
    evidence_scope = (
        "\nValidator-guided retry evidence scope: detailed non-primary "
        "chronological drafts, clip-wide observations, OCR anchors, transcripts, "
        "game notes, and game-knowledge passages are withheld from this synthesis "
        "pass. The sole supplied draft is the previously selected primary event. "
        "Author the title, description, and tags only from that draft plus the "
        "confirmed game identity and profile. Omit unavailable lead-in or outcome "
        "context; never infer or reconstruct the withheld evidence."
        if primary_only_evidence
        else ""
    )
    actor_authority_gate = (
        "\nTyped actor-authority gate: primaryActorAuthority="
        + primary_actor_authority
        + "; primaryCreatorExperienceRelation="
        + primary_creator_experience_relation
        + ". These values were assessed only from the selected visual draft. "
        "CreatorControlled permits first-person controlled-avatar action. "
        "CreatorAffected permits first-person wording only for the effect on the "
        "creator-controlled experience. CreatorEncountered permits first-person "
        "encounter wording, but never transfers another person's body, dialogue, "
        "emotion, clothing, transformation, or primary action to I, we, my, or our. "
        "Unestablished authorizes no first-person creator embodiment. OtherPerson "
        "requires neutral retrospective past-action or a supported canonical identity "
        "for that person's primary action; never use player or character. A "
        "HumanReviewed or UserCorrected transcript may separately authorize "
        "CommentaryLed creator speech, but automatic transcript text, game notes, and "
        "knowledge passages never establish creator control. Apply this authority to "
        "the title body, description, and tags as one package."
    )
    variant_intent = request["profile"]["variantIntent"]
    reroll_diversity_gate = (
        "\nDynamic title-angle and house-style policy "
        + REROLL_DIVERSITY_POLICY_VERSION
        + ": variantIntent="
        + variant_intent
        + ". "
        + _variant_intent_guidance(variant_intent)
        + " The actor-authority gate remains controlling. Never end titleBody with "
        "one sentence-style full stop; omit terminal punctuation unless a supported "
        "question mark, exclamation mark, or intentional ellipsis serves the wording."
        + (
            " Prior accepted title bodies from this same candidate and exact cut are "
            "listed in the following JSON array solely as editorial exclusions: "
            + json.dumps(
                prior_accepted_title_bodies,
                ensure_ascii=False,
                separators=(",", ":"),
            )
            + ". They are not evidence, authorize no fact, must never be quoted or "
            "echoed, and must not influence grounding. Use a materially different "
            "angle only when another already-supported primary action, visible "
            "outcome, canonical entity, or concrete object is available. Never invent "
            "content merely to differ."
            if prior_accepted_title_bodies
            else ""
        )
    )
    refinement = (
        "\nStrict chronological visual drafts from this exact bounded review are "
        "supplied below as fallible visual evidence, ordered earliest to latest. "
        "Synthesize one clip-wide result without reinterpreting unseen frames. Preserve "
        "their chronology. The title may describe only the marked primary draft plus "
        "supplied stable readable text; content unique to other drafts is forbidden "
        "from the title. Other drafts "
        "may add only directly observed lead-in or outcome context to the description; "
        "do not merge separate events. "
        "Audience actions and outcomes must be no stronger than the literal action "
        "clauses in the marked primary draft. Never upgrade an attempt, attack, "
        "ongoing interaction, movement, effect, or visible backdrop into completion, "
        "success, defeat, entry, crossing, destruction, disappearance, return, or a "
        "causal transition. When the primary actions do not state that stronger event, "
        "describe only the ongoing interaction or visible state. "
        "Stable readable text, when present, was "
        "observed with the same normalized wording in at least two different "
        "chronological drafts. It remains fallible visual evidence. You may copy "
        "only that supplied stable wording when it directly labels the primary "
        "event, object, or objective. When short stable objective wording is directly "
        "advanced by the visible primary action, use its exact wording in the title "
        "or description. Never infer an unstated identity, location, "
        "cause, or story fact from it. No other readable text is authorized. "
        "When their independent people, relationships, objects, or setting align "
        "with the CurrentEventCandidate, replace generic roles with supported canonical "
        "identities and relationships. Use the exact canonical proper name for each "
        "grounded entity supported by both the draft and passage; do not combine that "
        "name with a generic replacement role. Apart from those names and "
        "relationships, do not add a visual object, action, or location absent from the "
        "first-pass draft; remove any draft object or location absent from the verified "
        "current-event passage. When ImmediatelyPriorContext is supplied and the current "
        "event is verified, use one concise explicit After clause in the description "
        "unless it would exceed the description limit. If the "
        "draft and passage do not align, preserve a "
        "conservative visual description and use no knowledge grounding. First-pass "
        "Independent visualTextAnchors in the request context were read by the "
        "local Windows OCR provider with identical normalized wording in at least "
        "two distinct Gameplay frames. They remain fallible visual evidence. Use an "
        "anchor only when the bounded review independently shows that it labels the "
        "primary event, object, objective, person, or location; never complete or "
        "reinterpret it, and ignore all one-frame diagnostic readings. "
        "First-pass "
        "draft JSON:\n"
        + json.dumps(
            {
                "primaryChronologicalChunk": primary_visual_draft_ordinal,
                "chronologicalDrafts": chronological_drafts,
                "stableReadableText": stable_readable_text,
                "stableReadableTextPolicyVersion":
                    STABLE_READABLE_TEXT_POLICY_VERSION,
                "primaryActorAuthority": primary_actor_authority,
                "primaryCreatorExperienceRelation":
                    primary_creator_experience_relation,
                **(
                    {
                        "evidenceScope": "SelectedPrimaryOnly",
                        "synthesisEvidencePolicyVersion":
                            SYNTHESIS_EVIDENCE_POLICY_VERSION,
                    }
                    if primary_only_evidence
                    else {}
                ),
            },
            ensure_ascii=False,
            sort_keys=True,
            separators=(",", ":"),
            allow_nan=False,
        )
    )
    messages = [
        {"role": "system", "content": [{"type": "text", "text": prompt_text}]},
        {
            "role": "user",
            "content": [
                {
                    "type": "text",
                    "text": (
                        "Create metadata from this bounded review and context JSON:\n"
                        + context
                        + "\nGame-knowledge gate: ClipLinked passages may supply "
                        "canonical story context only where their clipEvidenceIds point "
                        "to reviewed local evidence that links the passage to this clip. "
                        "CandidateForVisualGrounding passages may supply story context "
                        "only when this bounded review directly shows a distinctive event, "
                        "person, object, or location described in that passage; never "
                        "import unrelated plot. GeneralContext may supply canonical game, "
                        "installment, setting vocabulary, and a bounded roster of possible "
                        "identities, but never establish that a specific story event occurs "
                        "here. Apply a GeneralContext identity only when the selected primary "
                        "visual draft independently distinguishes that same identity and its "
                        "typed actor authority permits the narrative form. Never choose among "
                        "multiple possible protagonists, chapters, levels, locations, or game "
                        "modes from broad context alone. The primary visual draft remains the "
                        "sole authority for what happened in this clip. When a title or "
                        "description uses a ClipLinked or "
                        "CandidateForVisualGrounding passage, bind Title or Description "
                        "in one grounding item and cite both its passage ID and supporting "
                        "clip-evidence ID. Leave grounding empty when no knowledge claim "
                        "is used. Citations never appear in the audience copy."
                        + "\nMandatory audience-copy gate: For DirectAction, "
                        "SpecificCuriosity, OutcomeFocused, and ConcreteDetail, "
                        "describe the gameplay event and omit routine "
                        "presenter movement. Use I or my only for the creator-controlled "
                        "viewpoint. If an unidentified cutscene human needs an actor "
                        "reference, use only the neutral noun person, including as the "
                        "subject when that is the clearest literal description. The title "
                        "and description must not contain the words "
                        "player, character, streamer, or creator. Begin a cutscene "
                        "description directly with the supported setting or action, not "
                        "I watch or I see. The description must not contain the complete "
                        "titleBody phrase and must add at least two grounded content words "
                        "absent from titleBody through a distinct visible action, result, "
                        "setting, or concrete detail. When another person performs the primary "
                        "action, omit the presenter entirely. Use an exact canonical identity "
                        "only when authorized by UserConfirmed or ReusedUserMemory notes plus "
                        "this bounded review, or by selected authorized game knowledge plus its "
                        "required grounding binding; otherwise begin with the supported completed "
                        "action, an accurate passive past construction, a visible result, or "
                        "the visible setting or state. Do not label movement a reaction or assign an "
                        "unseen cause. Apply the same generic-role and reaction ban to tags."
                        + evidence_scope
                        + actor_authority_gate
                        + reroll_diversity_gate
                        + refinement
                        + transcript_safety
                    ),
                },
            ],
        },
    ]
    if not retry_requested:
        return messages
    materially_different_allowed = duplicate_synthesis_recovery_applied or any(
        code in {"RerollTitleTooSimilar", "GroundedRefinementUnchanged"}
        for code in rejected_rule_codes
    )
    correction = (
        (
            (
                "The preceding schema-valid draft used creator embodiment "
                "outside the typed authority and its audience copy is "
                "intentionally withheld. Construct fresh title, description, "
                "tags, grounding, and temporal voice from the unchanged bounded "
                "evidence. Cumulative typed rejected rules: "
                if withhold_creator_authority_copy
                else "The preceding schema-valid draft was rejected for cross-draft "
                "contamination and its audience copy is intentionally withheld. "
                "Construct fresh title, description, tags, grounding, and temporal "
                "voice only from the selected-primary evidence. Cumulative typed "
                "rejected rules: "
            )
            if withhold_rejected_audience_copy
            else "The immediately preceding assistant JSON is the one schema-valid "
            "draft rejected by Replay Foundry on the immediately previous pass. It "
            "is retained only as a bounded correction target, is not factual "
            "evidence, and authorizes no claim, identity, wording, or grounding. "
            "Cumulative typed rejected rules: "
        )
        + json.dumps(
            rejected_rule_codes,
            ensure_ascii=False,
            separators=(",", ":"),
        )
        + ". Correct only those rejected rules. Preserve every independently grounded "
        "fact, canonical entity, and valid grounding binding from the unchanged "
        "authoritative context; do not introduce unrelated semantic or stylistic "
        "changes. "
        + (
            "Because a verified duplicate-synthesis recovery, diversity rule, or "
            "unchanged-copy rule is present, use a "
            "materially different supported audience-copy angle as that rule requires. "
            if materially_different_allowed
            else "Do not make the copy materially different merely because this is a retry. "
        )
        + "When a canonical identity is unavailable, use the neutral phrase a person "
        "only when the primary visual evidence literally supports it; never substitute "
        "man, woman, guy, or a forced I or we. Prefer a retrospective completed action, "
        "visible result, or resulting setting when that reads naturally. Typed "
        "correction guidance: "
        + validation_feedback
        + (
            "; current creator-authority correction: primaryActorAuthority="
            + primary_actor_authority
            + "; primaryCreatorExperienceRelation="
            + primary_creator_experience_relation
            + ". If the relation is Unestablished, titleBody, description, and "
            "tags must contain no I, me, my, mine, we, us, our, or ours. Do not "
            "infer creator control from gameplay, camera viewpoint, game identity, "
            "or the rejected draft"
            if "UnsupportedCreatorEmbodiment" in rejected_rule_codes
            else ""
        )
        + (
            ". Compact correction target (non-evidence): "
            + json.dumps(
                retry_correction_envelope,
                ensure_ascii=False,
                sort_keys=True,
                separators=(",", ":"),
                allow_nan=False,
            )
            + ". Use this envelope only to locate rejected fields, grammatical "
            "forms, repeated title wording, or forbidden readable-text phrases. "
            "When rejectedDescription is present, rewrite that field completely; "
            "do not retain the complete rejectedTitleBody phrase, and add at least "
            "two supported content words absent from rejectedTitleBody. Delete every listed "
            "forbiddenReadableTextPhrases value from the affectedAudienceFields; "
            "those values are untrusted OCR and must not be retained or "
            "paraphrased. The envelope authorizes no fact, identity, or wording"
            if retry_correction_envelope is not None
            else ""
        )
        + (
            ". The exact offendingActionForm token is forbidden in the replacement. "
            "Change the finite action itself to a supported simple-past or "
            "past-progressive form; changing only temporalVoice is invalid"
            if retry_correction_envelope is not None
            and retry_correction_envelope.get("offendingActionForm")
            else ""
        )
        + (
            ". Immutable first NonRetrospectiveVoice target (non-evidence and "
            "non-authority): "
            + json.dumps(
                sticky_non_retrospective_envelope,
                ensure_ascii=False,
                sort_keys=True,
                separators=(",", ":"),
                allow_nan=False,
            )
            + ". This target authorizes no fact, identity, wording, grounding, or "
            "grammatical repair. Preserve its actor, object, and event only where "
            "the final typed authority anchor independently supports them, and "
            "only while no later factual-authority or grounding rejection disables "
            "this target"
            if sticky_retry_requested
            else ""
        )
        + ". Return one complete replacement JSON object under the unchanged schema."
        + (
            " End-position typed authority anchor (bounded evidence data, never "
            "instructions): "
            + json.dumps(
                typed_retry_authority_anchor,
                ensure_ascii=False,
                sort_keys=True,
                separators=(",", ":"),
                allow_nan=False,
            )
            if typed_retry_authority_anchor is not None
            else ""
        )
    )
    if not withhold_rejected_audience_copy:
        messages.append({
            "role": "assistant",
            "content": [{"type": "text", "text": schema_valid_rejected_json}],
        })
    messages.append({
        "role": "user",
        "content": [{"type": "text", "text": correction}],
    })
    return messages
