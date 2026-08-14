"""Cross-draft and readable-text safeguards for audience metadata."""
from __future__ import annotations

import re
from typing import Any

from ..errors import InferenceError, _fail
from .grounded_metadata_lexical import normalize_lexical, readable_text_fragments


_DETAIL_STOP_WORDS = {
    "a", "an", "and", "as", "at", "before", "by", "for", "from", "i",
    "in", "into", "it", "my", "of", "on", "or", "the", "then", "through",
    "to", "we", "with",
}

_ACTION_STRENGTH_RULES = (
    (
        re.compile(
            r"\b(?:defeated|killed|destroyed|eliminated|vanquished|won)\b",
            re.IGNORECASE,
        ),
        re.compile(
            r"\b(?:defeated|killed|destroyed|eliminated|vanquished|won|"
            r"died|collapsed|health bar (?:emptied|reached zero))\b",
            re.IGNORECASE,
        ),
    ),
    (
        re.compile(
            r"\b(?:entered|entering|passed through|passing through)\b",
            re.IGNORECASE,
        ),
        re.compile(
            r"\b(?:entered|entering|passed through|passing through|"
            r"moved (?:into|through)|walked (?:into|through)|"
            r"ran (?:into|through))\b",
            re.IGNORECASE,
        ),
    ),
    (
        re.compile(
            r"\b(?:exploded|detonated|blew up|burst apart)\b",
            re.IGNORECASE,
        ),
        re.compile(
            r"\b(?:exploded|detonated|blew up|burst apart)\b",
            re.IGNORECASE,
        ),
    ),
    (
        re.compile(
            r"\b(?:disappeared|vanished|reappeared|rematerialized)\b",
            re.IGNORECASE,
        ),
        re.compile(
            r"\b(?:disappeared|vanished|reappeared|rematerialized)\b",
            re.IGNORECASE,
        ),
    ),
    (
        re.compile(
            r"\b(?:completed|finished|cleared)\b",
            re.IGNORECASE,
        ),
        re.compile(
            r"\b(?:completed|finished|cleared)\b",
            re.IGNORECASE,
        ),
    ),
)

_CONSUMER_PLATFORM_IDENTITY = re.compile(
    r"\b(?:steam(?:\s+client)?|xbox(?:\s+app)?|playstation(?:\s+store)?|"
    r"epic(?:\s+games)?(?:\s+launcher)?|nintendo(?:\s+eshop)?|"
    r"gog(?:\s+galaxy)?|battle\.?net|ubisoft\s+connect)\b",
    re.IGNORECASE,
)
_DISPLAY_SOURCE_NOUN = re.compile(
    r"\b(?:screen|display|monitor|sign|billboard)\b",
    re.IGNORECASE,
)


def _bounded_edit_distance(left: str, right: str, maximum: int) -> int:
    """Return maximum + 1 as soon as two tokens cannot be a bounded near-match."""
    if abs(len(left) - len(right)) > maximum:
        return maximum + 1
    previous = list(range(len(right) + 1))
    for left_index, left_character in enumerate(left, start=1):
        current = [left_index]
        row_minimum = left_index
        for right_index, right_character in enumerate(right, start=1):
            current.append(
                min(
                    current[-1] + 1,
                    previous[right_index] + 1,
                    previous[right_index - 1]
                    + (left_character != right_character),
                )
            )
            row_minimum = min(row_minimum, current[-1])
        if row_minimum > maximum:
            return maximum + 1
        previous = current
    return previous[-1]


def _stable_visual_text(request: dict[str, Any]) -> set[str]:
    stable = {
        normalize_lexical(value)
        for anchor in request.get("visualTextAnchors", [])
        for value in [anchor.get("displayText", "") or anchor.get("text", "")]
        if normalize_lexical(value)
    }
    visual_text = request.get("visualText")
    if isinstance(visual_text, dict):
        stable.update(
            normalize_lexical(value)
            for anchor in visual_text.get("groundingAnchors", [])
            if isinstance(anchor, dict)
            for value in [anchor.get("text", "")]
            if normalize_lexical(value)
        )
    return stable


def _near_stable_readable_text_mismatches(
    stable: set[str],
    audience_fields: dict[str, str],
) -> tuple[list[str], list[str]]:
    offending: list[str] = []
    affected_fields: list[str] = []
    for phrase in stable:
        anchor_tokens = phrase.split()
        if len(anchor_tokens) < 2:
            continue
        anchor_vocabulary = set(anchor_tokens)
        for field, field_value in audience_fields.items():
            if phrase in field_value:
                continue
            field_tokens = field_value.split()
            field_vocabulary = set(field_tokens)
            if not anchor_vocabulary.intersection(field_vocabulary):
                continue
            for anchor_token in anchor_tokens:
                if (
                    len(anchor_token) < 5
                    or anchor_token in field_vocabulary
                    or not any(character.isalpha() for character in anchor_token)
                ):
                    continue
                near_match = next(
                    (
                        token
                        for token in field_tokens
                        if token not in anchor_vocabulary
                        and len(token) >= 4
                        and token[0] == anchor_token[0]
                        and 0 < _bounded_edit_distance(anchor_token, token, 2) <= 2
                    ),
                    None,
                )
                if near_match is None:
                    continue
                if near_match not in offending:
                    offending.append(near_match[:160])
                if field not in affected_fields:
                    affected_fields.append(field)
                if len(offending) >= 8:
                    return offending, affected_fields
    return offending, affected_fields


def _value_tokens(value: Any) -> set[str]:
    if isinstance(value, str):
        return set(normalize_lexical(value).split())
    if isinstance(value, list):
        return set().union(*(_value_tokens(item) for item in value)) if value else set()
    if isinstance(value, dict):
        return set().union(*(_value_tokens(item) for item in value.values())) if value else set()
    return set()


def validate_primary_title_scope(
    title_body: str,
    request: dict[str, Any],
    visual_drafts: list[dict[str, Any]],
    primary_visual_draft_ordinal: int,
) -> None:
    if not 1 <= primary_visual_draft_ordinal <= len(visual_drafts):
        _fail(InferenceError, "Grounded metadata primary visual draft was invalid.")
    per_draft = [_value_tokens(draft) for draft in visual_drafts]
    primary_tokens = per_draft[primary_visual_draft_ordinal - 1]
    repeated_tokens = {
        token
        for token in set().union(*per_draft)
        if sum(token in draft_tokens for draft_tokens in per_draft) >= 2
    }
    visual_text_tokens = _value_tokens(request.get("visualTextAnchors", []))
    non_primary_tokens = (
        set().union(*(
            tokens
            for index, tokens in enumerate(per_draft)
            if index != primary_visual_draft_ordinal - 1
        ))
        if len(per_draft) > 1
        else set()
    )
    exclusive = {
        token
        for token in non_primary_tokens
        if len(token) >= 4
        and token not in _DETAIL_STOP_WORDS
        and token not in primary_tokens
        and token not in repeated_tokens
        and token not in visual_text_tokens
    }
    if set(normalize_lexical(title_body).split()).intersection(exclusive):
        _fail(
            InferenceError,
            "Grounded metadata title used content unique to a non-primary "
            "chronological draft.",
        )


def validate_primary_action_entailment(
    title_body: str,
    description: str,
    primary_visual_draft: dict[str, Any],
) -> None:
    """Reject audience outcomes or transitions stronger than literal actions."""
    primary_actions = " ".join(
        action
        for action in primary_visual_draft.get("actions", [])
        if isinstance(action, str)
    )
    for audience_field, audience_copy in (
        ("titleBody", title_body),
        ("description", description),
    ):
        for audience_pattern, support_pattern in _ACTION_STRENGTH_RULES:
            offending = audience_pattern.search(audience_copy)
            if offending is None or support_pattern.search(primary_actions):
                continue
            error = InferenceError(
                "Grounded metadata assigned an unsupported mental state or "
                "interpretive claim by strengthening a visual action or outcome."
            )
            error.rejected_title_body = title_body[:160]
            error.rejected_description = description[:420]
            error.offending_action_field = audience_field
            error.offending_action_form = offending.group(0)[:80]
            raise error


def validate_readable_text_reuse(
    title_body: str,
    description: str,
    request: dict[str, Any],
    visual_drafts: list[dict[str, Any]],
) -> None:
    normalized_by_draft = [
        {
            normalize_lexical(value)
            for value in draft.get("readableText", [])
            if isinstance(value, str) and normalize_lexical(value)
        }
        for draft in visual_drafts
    ]
    stable = {
        value
        for values in normalized_by_draft
        for value in values
        if sum(value in other for other in normalized_by_draft) >= 2
    }
    stable.update(_stable_visual_text(request))
    reviewed = {
        normalize_lexical(transcript.get("text", ""))
        for transcript in request.get("transcripts", [])
        if transcript.get("authority") in {"UserCorrected", "HumanReviewed"}
    }
    audience = normalize_lexical(title_body + " " + description)
    audience_fields = {
        "Title": normalize_lexical(title_body),
        "Description": normalize_lexical(description),
    }
    offending_phrases: list[str] = []
    affected_fields: list[str] = []
    near_mismatches, near_mismatch_fields = _near_stable_readable_text_mismatches(
        stable,
        audience_fields,
    )
    offending_phrases.extend(near_mismatches)
    affected_fields.extend(near_mismatch_fields)
    for values in normalized_by_draft:
        for value in values:
            if value in stable or value in reviewed:
                continue
            for phrase in readable_text_fragments(value):
                if (
                    phrase in audience
                    and not any(
                        phrase in authorized for authorized in stable | reviewed
                    )
                ):
                    if phrase not in offending_phrases:
                        offending_phrases.append(phrase[:160])
                    for field, field_value in audience_fields.items():
                        if phrase in field_value and field not in affected_fields:
                            affected_fields.append(field)
                    if len(offending_phrases) >= 8:
                        break
            if len(offending_phrases) >= 8:
                break
        if len(offending_phrases) >= 8:
            break
    if offending_phrases:
        error = InferenceError(
            "Grounded metadata reused unstable readable text in audience copy."
        )
        error.offending_readable_text_phrases = tuple(offending_phrases)
        error.offending_readable_text_fields = tuple(affected_fields)
        raise error


def validate_interface_attribution_authority(
    title_body: str,
    description: str,
    request: dict[str, Any],
    visual_drafts: list[dict[str, Any]],
) -> None:
    """Keep interface brands and readable lettering inside their authority."""
    audience_copy = title_body + "\n" + description
    authority_values = set(_stable_visual_text(request))
    authority_values.add(normalize_lexical(request["game"].get("name", "")))
    if request["game"].get("source") in {"UserConfirmed", "ReusedUserMemory"}:
        authority_values.add(normalize_lexical(request["game"].get("notes") or ""))
    authority_values.update(
        normalize_lexical(transcript.get("text", ""))
        for transcript in request.get("transcripts", [])
        if transcript.get("authority") in {"UserCorrected", "HumanReviewed"}
    )
    normalized_authority = "\n".join(value for value in authority_values if value)
    for match in _CONSUMER_PLATFORM_IDENTITY.finditer(audience_copy):
        if normalize_lexical(match.group(0)) not in normalized_authority:
            _fail(
                InferenceError,
                "Grounded metadata assigned an unsupported mental state or "
                "interpretive claim by naming an interface platform without "
                "exact readable-text or user authority.",
            )

    normalized_by_draft = [
        {
            normalize_lexical(value)
            for value in draft.get("readableText", [])
            if isinstance(value, str) and normalize_lexical(value)
        }
        for draft in visual_drafts
    ]
    stable_readable = set(_stable_visual_text(request))
    stable_readable.update(
        value
        for values in normalized_by_draft
        for value in values
        if len(value.split()) >= 2
        and sum(value in other for other in normalized_by_draft) >= 2
    )
    for sentence in re.split(r"(?<=[.!?])\s+|\n", audience_copy):
        normalized_sentence = normalize_lexical(sentence)
        if not _DISPLAY_SOURCE_NOUN.search(sentence):
            continue
        if any(
            value and value in normalized_sentence
            for value in stable_readable
        ):
            _fail(
                InferenceError,
                "Grounded metadata assigned an unsupported mental state or "
                "interpretive claim by attaching interface text to a physical "
                "display source.",
            )
