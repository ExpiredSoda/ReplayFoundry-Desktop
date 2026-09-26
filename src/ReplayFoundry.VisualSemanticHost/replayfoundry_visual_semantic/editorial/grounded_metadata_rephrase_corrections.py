"""Exact source-rejection instructions for bounded editorial rephrasing."""
from __future__ import annotations

from typing import Any


def _required_language_form(
    source_rejection_code: str | None,
    authority: dict[str, Any],
) -> str | None:
    if source_rejection_code not in {
        "ThirdPersonCreatorFraming",
        "UnsupportedCreatorEmbodiment",
    }:
        return None
    if authority.get("copyObjective") == "BalancedActionAndCommentary" \
            and authority.get("automaticCreatorCommentary"):
        return (
            "Retain the separately required I wondered/I compared attribution in its "
            "assigned field. In the visual field use first-person gameplay only when "
            "typed actor authority establishes that action; otherwise use neutral "
            "retrospective narration. The attribution authorizes no body, possession, "
            "dialogue, emotion, outcome or second creator action."
        )
    primary = authority.get("primaryVisual", {})
    if (
        primary.get("actorAuthority") == "CreatorControlled"
        and primary.get("creatorExperienceRelation") == "CreatorActed"
    ):
        controlled_form = (
            "The typed primary event establishes CreatorControlled plus "
            "CreatorActed. Remove every generic human role such as man, woman, "
            "person, player, or character. Narrate only the supported controlled "
            "action retrospectively as I or my in both titleBody and description. "
            "Place an unambiguous simple-past verb immediately after I; never use a "
            "base, present-tense, or gerund verb there. An explicit I title is "
            "authorized here. Do not transfer an action, "
            "body detail, or outcome absent from primaryVisual."
        )
        if source_rejection_code == "UnsupportedCreatorEmbodiment":
            return (
                controlled_form
                + " Keep another person's body detail, dialogue, emotion, and "
                  "action neutral; never convert those into my body, words, "
                  "feelings, or action."
            )
        return controlled_form
    if source_rejection_code == "UnsupportedCreatorEmbodiment":
        limited_attribution = (
            " Keep only the separately required bounded I wondered/I compared attribution; "
            "it never refers to the visible person's body or action."
            if authority.get("copyObjective") == "BalancedActionAndCommentary"
            and authority.get("automaticCreatorCommentary") else ""
        )
        return (
            "The typed creator-experience relation does not establish that the "
            "visible person is the creator. Remove unsupported gameplay I, we, my, and our."
            + limited_attribution + " A neutral "
            "human subject such as a person is permitted when primaryVisual "
            "literally supports that visible subject and action; player, "
            "character, streamer, creator, and camera wearer remain forbidden. "
            "Use unmistakable retrospective past tense in both titleBody and "
            "description. Never convert the visible person's body, weapon, "
            "dialogue, emotion, or action into the creator's experience."
        )
    return (
        "Do not invent I or we. When typed authority is Unknown or OtherPerson, "
        "a neutral human subject such as a person is permitted when primaryVisual "
        "literally supports that visible subject and action. Player, character, "
        "streamer, creator, and camera wearer remain forbidden. Use unmistakable "
        "retrospective past tense in both titleBody and description."
    )


def _required_literal_form(source_rejection_code: str | None, authority: dict[str, Any] | None = None) -> str | None:
    if source_rejection_code != "UnsupportedMentalState":
        return None
    authority = authority or {}
    if authority.get("copyObjective") == "BalancedActionAndCommentary" \
            and authority.get("automaticCreatorCommentary"):
        return (
            "Remove unsupported emotions, intentions, causes and interpretations. "
            "Use only primaryVisual's literal supported action or presentation in "
            "the visual field; keep only the independently permitted bounded "
            "attributed thought in the other field. The attribution establishes "
            "no mental state, action, cause or outcome inside the game."
        )
    return (
        "The rejected copy added interpretation beyond the typed primary visual. "
        "Create both audience fields from the literal primaryVisual environment, "
        "subjectsAndObjects, and actions only. Use concrete visible nouns and "
        "completed physical actions already stated there. Omit emotion, intent, "
        "reaction, causality, significance, success, completion, transition, "
        "defeat, destruction, disappearance, return, and any inferred outcome."
    )


def _required_output_language_form(
    source_rejection_code: str | None,
) -> str | None:
    if source_rejection_code != "OutputLanguage":
        return None
    return (
        "Create titleBody and description in English from typedAuthority because "
        "the rejected audience copy contained at least one non-Latin letter. Do "
        "not repeat, translate, transliterate, or infer meaning from the withheld "
        "wording. Copy each source tag exactly and in order unless that tag "
        "contains a non-Latin letter; omit only such a tag. Add, replace, rewrite, "
        "or reorder no tag. Preserve at least one valid source tag."
    )


def _required_temporal_form(source_rejection_code: str | None, authority: dict[str, Any]) -> str | None:
    if source_rejection_code != "NonRetrospectiveVoice":
        return None
    primary = authority.get("primaryVisual", {})
    creator_controlled = (
        primary.get("actorAuthority") == "CreatorControlled"
        and primary.get("creatorExperienceRelation") == "CreatorActed"
    )
    balanced = authority.get("copyObjective") == "BalancedActionAndCommentary" \
        and bool(authority.get("automaticCreatorCommentary"))
    opening = (
        "Keep the required field allocation: use a past-tense grounded visual "
        "action in one field and the bounded past-tense I wondered/I compared "
        "attribution in the other. The attribution grants no gameplay embodiment."
        if balanced else
        "audienceFrame requires NarrativePresentation. Begin titleBody with "
        "its presentation subject or locator, then place an unmistakable past-"
        "tense presentation predicate or literal action in that opening clause; "
        "the action must not replace the presentation anchor."
        if authority.get("audienceFrame", {}).get("centerKind") == "NarrativePresentation"
        else
        "When the typed primary event supports CreatorControlled plus "
        "CreatorActed, titleBody may begin with I or we followed immediately "
        "by an unmistakable past-tense action."
        if creator_controlled
        else
        "Creator embodiment is not established. Do not invent I or we; begin "
        "titleBody with an unmistakable past-tense action, completed visible "
        "result, or visible state supported by primaryVisual."
    )
    return (
        "temporalVoice is RetrospectivePast, so titleBody and description must "
        "both be grammatically retrospective. "
        + opening
        + " Do not begin titleBody with a command, bare infinitive, simple-"
          "present verb, or gerund. Do not describe any action in present "
          "tense. Preserve the supported event while changing its grammatical "
          "form; do not add a new action, actor, result, or interpretation."
    )


def _required_editorial_frame_form(
    source_rejection_code: str | None, authority: dict[str, Any],
) -> str | None:
    if source_rejection_code != "EditorialFrameDrift":
        return None
    if authority.get("copyObjective") == "BalancedActionAndCommentary" \
            and authority.get("automaticCreatorCommentary"):
        return (
            "Keep the supported presentation or action as the visual field's center "
            "and retain a literal case-local primary fact there. The other field "
            "carries the required bounded attributed thought; it does not need the "
            "same physical subject, presentation opening or scene inventory. "
            "Neither field may add facts, actors, causes or outcomes."
        )
    presentation = authority.get("editorialFraming", {}).get(
        "primaryPresentationKind", "Unclear")
    primary = authority.get("primaryVisual", {})
    creator_controlled = (
        primary.get("actorAuthority") == "CreatorControlled"
        and primary.get("creatorExperienceRelation") == "CreatorActed"
    )
    if presentation in {"CinematicSequence", "InWorldRecording"}:
        presentation_label = (
            "cutscene or narrative sequence"
            if presentation == "CinematicSequence"
            else "recording or briefing"
        )
        presentation_form = (
            "Use a NarrativePresentation frame without locking either field to a fixed template or vocabulary. Establish the "
            + presentation_label + " naturally through a presentation subject, an introductory locator, a contextual clause, "
            "or a supported result tied back to that presentation. Vary the opening, clause order, predicate, and description plan across rerolls. "
            "Both titleBody and description must carry at least one literal case-local fact from primaryVisual or stableReadableText; presentation words and the game name do not satisfy that requirement. "
            "Preserve a retained participant-action-object relation or action-linked candidate subject instead of reducing it to moment, scene, or something. "
            "Every completed finite observation action must be simple past. holds→held, speaks→spoke, shows→showed, points→pointed, and gestures→gestured are morphology examples only, never evidence or required wording. "
            "Description may use a natural game-qualified presentation locator when confirmedGameIdentity exists: combine In this, its exact supplied name, and cutscene or recording; never print a placeholder. "
            "A generic actor may occur only inside the action complement or clause, never as either field's grammatical center. "
            "Permission elsewhere to use a neutral human subject is subordinate to this presentation rule. Omit clothing, cameras, filming, "
            "inferred roles, relationships, purpose, meaning, significance, and unsupported reveals."
        )
    elif presentation == "DocumentOrLore":
        presentation_form = (
            "Use a document-review-led form. Center a supported review, discovery, document "
            "subject, or action-linked candidate rather than a screen or text display. A "
            "stable header, issuer, or game name is not automatically the document subject. "
            "Use safe follow-through and add supported information rather than repetition."
        )
    elif presentation == "InteractiveGameplay":
        presentation_form = (
            "Use a creator-led playthrough form with I plus an unambiguous past action and "
            "distinct supported chronology; add no unsupported speed or completeness."
            if creator_controlled else
            "Use a neutral past action, objective progression, choice, transition, or "
            "visible result with distinct chronology; never begin with a generic actor."
        )
    else:
        presentation_form = "Center the supported presentation or event itself."
    return (
        "The rejected copy reduced a supported story role to an inventory of "
        "people, props, cameras, screens, or display contents. audienceFrame is "
        "controlling grammatical shape; primaryVisual, chronologicalProgression, "
        "and editorialFraming supply facts and story role only. candidateSubjects "
        "are action-linked, articleless noun phrases, not new factual authority. Use "
        "one only when it is a useful center, and adjust only its leading article and "
        "sentence-initial capitalization for grammar. "
        + presentation_form
        + " Keep retrospective grammar and allow structurally different openings, "
          "clause order, and description plans across rerolls. The premise selects "
          "story shape only; paraphrase it freely. stableReadableText may name a useful "
          "supported subject but never grants scene facts. State the supported story "
          "presentation rather than who stood before a camera, and never open either "
          "field with a generic man, woman, person, player, or character."
    )


def _required_stable_readable_text_form(
    source_rejection_code: str | None,
    authority: dict[str, Any],
) -> str | None:
    if source_rejection_code != "UnstableReadableTextReuse":
        return None
    stable = authority.get("stableReadableText")
    availability = (
        "Only exact entries in typedAuthority.stableReadableText may be reused, "
        "and each remains subordinate to the supported presentation or event."
        if isinstance(stable, list) and stable
        else
        "typedAuthority has no stableReadableText, so use no readable wording and "
        "do not reconstruct, correct, translate, or paraphrase any omitted text."
    )
    return (
        "The rejected copy used readable wording that did not have repeated local "
        "authority. Omitted readable wording is non-evidence. "
        + availability
    )


