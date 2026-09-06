"""Frozen grounded-metadata synthesis message assembly."""
from __future__ import annotations
import json
from typing import Any

from .grounded_metadata_creator_authority import (
    _draft_temporal_role,
    _safe_automatic_commentary_angle,
    _requires_balanced_copy,
    _scoped_retry_authority,
    _without_automatic_commentary,
)
from .grounded_metadata_rephrase_messages import BALANCED_COPY_GATE, SYNTHESIS_STORY_SHAPING_GATE, _compact_balanced_messages
from .grounded_metadata_reroll_similarity import REROLL_DIVERSITY_POLICY_VERSION
from .grounded_metadata_draft_validation import _title_scope_draft_ordinals
from .grounded_metadata_output_schema import title_body_maximum
from .grounded_metadata_synthesis import (
    STABLE_READABLE_TEXT_POLICY_VERSION,
    SYNTHESIS_EVIDENCE_POLICY_VERSION,
    _model_context,
    _stable_readable_text,
    _synthesis_draft,
    _typed_retry_authority_anchor,
    _variant_intent_guidance,
)
from .grounded_metadata_synthesis_retry_messages import (
    build_retry_messages,
    validate_retry_message_plan,
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
    if primary_only_evidence:
        frame = request.get("_editorialFraming")
        if isinstance(frame, dict):
            request = {**request, "_editorialFraming": {
                **frame, "supportingDraftOrdinals": [primary_visual_draft_ordinal],
                "supportingPresentationKinds": [],
            }}
    balanced_copy = _requires_balanced_copy(request)
    field_expansion_gate = (
        "The attributed field supplies the nominated thought, not extra visible "
        "scene details. Keep it distinct from the grounded visual field and never "
        "use I watch or I see. "
        if balanced_copy else
        "Begin a cutscene description directly with the supported setting or action, "
        "not I watch or I see. The description must not contain the complete "
        "titleBody phrase and must add at least two grounded content words absent "
        "from titleBody through a distinct visible action, result, setting, or concrete detail. "
    )
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
    retry_plan = validate_retry_message_plan(
        validation_feedback,
        schema_valid_rejected_json,
        rejected_rule_codes,
        retry_correction_envelope,
        sticky_non_retrospective_envelope,
        typed_retry_authority_anchor,
        withhold_rejected_audience_copy,
        primary_only_evidence,
    )
    transcript_safety = (
        "\nAuthoritative retry safety: no spoken or readable wording is authorized "
        "for audience copy in this pass. Do not quote, paraphrase, summarize, name, "
        "or complete any message content. Describe only supported physical action, "
        "objects, setting, or visible outcome."
        " Replay Foundry retains the confirmed game identity in separate tags; "
        "titleBody must remain hashtag-free."
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
            **_draft_temporal_role(
                draft, grounded_drafts[primary_visual_draft_ordinal - 1],
                ordinal, primary_visual_draft_ordinal,
            ),
            "draft": _synthesis_draft(draft, stable_readable_text),
        }
        for ordinal, draft in draft_items
    ]
    title_scope_ordinals = _title_scope_draft_ordinals(
        request,
        primary_visual_draft_ordinal,
        len(grounded_drafts),
        primary_only_evidence,
    )
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
    reviewed_creator_commentary = [
        item["text"]
        for item in request.get("transcripts", [])
        if item.get("role") == "CreatorSpeech"
        and item.get("authority") in {"UserCorrected", "HumanReviewed"}
    ]
    creator_commentary_gate = (
        "\nReviewed creator-commentary gate: UserCorrected or HumanReviewed "
        "CreatorSpeech is available. Prefer its grounded premise, opinion, joke, "
        "or payoff as the human editorial angle across every variant intent. "
        "Preserve the recognizable idea through a concise natural paraphrase or "
        "short supported phrase instead of a literal scene inventory. The speech "
        "does not authorize an unseen fact or creator embodiment; verify every "
        "referenced object, action, identity, rule, cause, and outcome against the "
        "bounded visual and typed authority."
        if reviewed_creator_commentary
        else ""
    )
    has_automatic_creator_commentary = any(
        item.get("role") == "CreatorSpeech"
        and item.get("authority") == "AutomaticUnreviewed"
        for item in request.get("transcripts", [])
    )
    editorial_brief = request.get("editorialBrief") or {}
    has_safe_automatic_creator_angle = _safe_automatic_commentary_angle(request) is not None
    automatic_creator_angle_gate = (
        "\nAutomatic creator-angle gate: AutomaticUnreviewed CreatorSpeech may "
        "nominate which concrete subject, oddity, or payoff deserves emphasis only "
        "when the same subject is independently established by the bounded visual "
        "drafts, stable readable text, or authorized game knowledge, except for the separately attributed comparison subject permitted below. Derive every "
        "objective event fact from that independent evidence. "
        + (
            "editorialBrief marks AutomaticCreatorReactionAngleAvailable, so "
            "safeCommentaryAngle is a host-curated reaction-shaped passage from a "
            "human-confirmed CreatorSpeech track. It may shape only a clearly "
            "creator-attributed reaction, analogy, question, or joke such as I "
            "compared or I wondered whether/if/about; do not coordinate another creator action with that attribution. It never establishes that a "
            "comparison, outside work, program, person, cause, or story detail exists "
            "in the game. An isolated distinctive comparison name from "
            "safeCommentaryAngle may remain only inside that explicit attribution. "
            if has_safe_automatic_creator_angle
            else "The automatic transcript selects an angle but supplies no "
            "quotation, exact phrase, proper noun, claim, or creator-embodiment "
            "authority. "
        )
        + "Do not quote or copy any four-word automatic-transcript sequence, and "
        "never use the passage to authorize another person's body, dialogue, action, "
        "or creator embodiment."
        if has_automatic_creator_commentary and not withhold_unreviewed_transcripts and not primary_only_evidence
        else ""
    )
    variant_intent = request["profile"]["variantIntent"]
    title_body_budget = title_body_maximum(request["game"]["hashtag"])
    reroll_diversity_gate = (
        "\nDynamic title-angle and house-style policy "
        + REROLL_DIVERSITY_POLICY_VERSION
        + ": variantIntent="
        + variant_intent
        + ". "
        + _variant_intent_guidance(variant_intent)
        + " The constrained titleBody budget is exactly "
        + str(title_body_budget)
        + " characters because the app appends one space plus the exact canonical "
        "game hashtag after validation. Complete the thought inside that body budget; "
        "never include, abbreviate, or duplicate the hashtag yourself."
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
        "their chronology. Measured reviewStartSeconds/reviewEndSeconds are relative to the current cut; sourceStartSeconds/sourceEndSeconds use the recording clock. These are evidence coordinates, never audience copy. A LeadIn ends before the primary window starts; a FollowThrough starts after the primary window ends. Never describe a LeadIn action as a later result of the primary action. OverlapsPrimary means the windows do not establish their internal event order. Use no before/after/then link for overlapping or temporally unestablished events. Window order establishes no cause, successful arrival, completion, disappearance, or other outcome. Prefer one primary action and one compatible detail when the event sequence is uncertain. The deterministic title factual scope is the following "
        "validated draft-ordinal array: "
        + json.dumps(title_scope_ordinals, separators=(",", ":"))
        + ". The title may combine only facts from those drafts plus supplied stable "
        "readable text. A StoryShapeOnly frame can authorize this clip-wide factual "
        "scope, but it cannot authorize a fact. Content unique to every other draft "
        "is forbidden from the title. Other drafts "
        "may add only directly observed lead-in or outcome context to the description; "
        "do not merge separate events. "
        "Audience actions and outcomes must be no stronger than the literal action "
        "clauses in the title-scope drafts. Never upgrade an attempt, attack, "
        "ongoing interaction, movement, effect, or visible backdrop into completion, "
        "success, defeat, entry, crossing, destruction, disappearance, return, or a "
        "causal transition. When no title-scope action states that stronger event, "
        "describe only the ongoing interaction or visible state. "
        "Stable readable text, when present, was "
        "observed with the same normalized wording in at least two different "
        "chronological drafts. It remains fallible visual evidence. You may copy "
        "only that supplied stable wording when it directly labels a title-scope "
        "event, object, or objective. When short stable objective wording is directly "
        "advanced by a compatible visible title-scope action, preserve its useful nouns "
        "while expressing the supported action, transition, or result around them. "
        "Readable wording is never a complete title or description by itself. Never "
        "describe the act of evidence detection with phrases such as on-screen text "
        "reads, on-screen text includes, or the screen shows. Never infer an unstated "
        "identity, location, "
        "cause, or story fact from it. No other readable text is authorized. "
        "When their independent people, relationships, objects, or setting align "
        "with the CurrentEventCandidate, replace generic roles with supported canonical "
        "identities and relationships. Use the exact canonical proper name for each "
        "grounded entity supported by both the draft and passage; do not combine that "
        "name with a generic replacement role. Apart from those names and "
        "relationships, do not add a visual object, action, or location absent from the "
        "title-scope drafts; remove any draft object or location absent from the verified "
        "current-event passage. When ImmediatelyPriorContext is supplied and the current "
        "event is verified, use one concise explicit After clause in the description "
        "unless it would exceed the description limit. If the "
        "draft and passage do not align, preserve a "
        "conservative visual description and use no knowledge grounding. First-pass "
        "Independent visualTextAnchors in the request context were read by the "
        "local Windows OCR provider with identical normalized wording in at least "
        "two distinct Gameplay frames. They remain fallible visual evidence. Use an "
        "anchor only when the bounded review independently shows that it labels the "
        "title-scope event, object, objective, person, or location; never complete or "
        "reinterpret it, and ignore all one-frame diagnostic readings. "
        "First-pass "
        "draft JSON:\n"
        + json.dumps(
            {
                "primaryChronologicalChunk": primary_visual_draft_ordinal,
                "titleScopeDraftOrdinals": title_scope_ordinals,
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
                        + "\nEditorial-brief gate: Follow the host-authored copyGoal. "
                        "Confirmed and Supported claims may supply only the audience "
                        "fields listed in fieldAuthorizations. Ambiguous claims are "
                        "hypotheses only: use "
                        "one only when the selected primary visual draft independently "
                        "corroborates it. AutomaticTranscriptCue may suggest which "
                        "already-visible beat matters, but it never authorizes exact "
                        "wording, identity, causality, or a story fact. StructuralReroll "
                        "requires a different supported angle or sentence structure, "
                        "not synonym substitution."
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
                        + SYNTHESIS_STORY_SHAPING_GATE
                        + (BALANCED_COPY_GATE if balanced_copy else "")
                        + "\nMandatory audience-copy gate: For DirectAction, "
                        "SpecificCuriosity, OutcomeFocused, and ConcreteDetail, "
                        "retain the grounded gameplay event while a permitted attributed question/comparison may supply its human angle, and omit routine "
                        "presenter movement. Gameplay first-person action requires creator control; "
                        "the separate safe automatic question/comparison gate does not grant it. If an unidentified cutscene human needs an actor "
                        "reference, use only the neutral noun person, including as the "
                        "subject when that is the clearest literal description. The title "
                        "and description must not contain the words "
                        "player, character, streamer, or creator. "
                        + field_expansion_gate
                        + "When another person performs the primary "
                        "action, omit routine presenter description; a safe attributed commentary angle may remain. Use an exact canonical identity "
                        "only when authorized by UserConfirmed or ReusedUserMemory notes plus "
                        "this bounded review, or by selected authorized game knowledge plus its "
                        "required grounding binding; otherwise begin the visual field with the supported completed "
                        "action, an accurate passive past construction, a visible result, or "
                        "the visible setting or state. Do not label movement a reaction or assign an "
                        "unseen cause. Apply the same generic-role and reaction ban to tags."
                        + evidence_scope
                        + actor_authority_gate
                        + creator_commentary_gate
                        + automatic_creator_angle_gate
                        + reroll_diversity_gate
                        + refinement
                        + transcript_safety
                    ),
                },
            ],
        },
    ]
    if balanced_copy:
        authority = _typed_retry_authority_anchor(
            request, grounded_drafts, primary_visual_draft_ordinal,
            primary_actor_authority, primary_creator_experience_relation,
        )
        messages = _compact_balanced_messages(
            authority, request["profile"]["variantIntent"],
            title_maximum=title_body_maximum(request["game"]["hashtag"]),
            prior_titles=prior_accepted_title_bodies,
        )
    if not retry_plan.requested:
        return messages
    messages.extend(build_retry_messages(
        retry_plan,
        validation_feedback,
        schema_valid_rejected_json,
        rejected_rule_codes,
        duplicate_synthesis_recovery_applied,
        retry_correction_envelope,
        sticky_non_retrospective_envelope,
        typed_retry_authority_anchor,
        withhold_rejected_audience_copy,
        primary_actor_authority,
        primary_creator_experience_relation,
    ))
    return messages
