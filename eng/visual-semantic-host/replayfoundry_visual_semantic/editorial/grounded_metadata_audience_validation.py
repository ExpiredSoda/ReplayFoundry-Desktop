"""Strict creator-voice and audience-copy validation."""
from __future__ import annotations

import json
import re
from typing import Any

from ..errors import InferenceError, _fail
from .grounded_metadata_creator_authority import (
    _FIRST_PERSON_GENERIC_OBSERVER_OPENING,
    _GENERIC_PERSON_SUBJECT_OPENING,
    validate_creator_actor_authority,
)
from .grounded_metadata_draft_validation import (
    validate_primary_action_entailment,
    validate_primary_title_scope,
    validate_readable_text_reuse,
    validate_interface_attribution_authority,
)
from .grounded_metadata_grounding_validation import strict_grounding
from .grounded_metadata_lexical import (
    contains_unapproved_non_latin,
    normalize_lexical,
    shared_token_windows,
)
from .grounded_metadata_validation import parse_metadata_shape


_DETAIL_STOP_WORDS = {
    "a", "an", "and", "as", "at", "before", "by", "for", "from", "i",
    "in", "into", "it", "my", "of", "on", "or", "the", "then", "through",
    "to", "we", "with",
}

# General English morphology only. These words never authorize a clip fact.
_NON_RETROSPECTIVE_ACTION_FORMS = {
    "appear", "appears", "attack", "attacks", "beat", "beats", "break", "breaks",
    "chase", "chases", "climb", "climbs", "confront", "confronts", "defeat",
    "defeats", "descend", "descends", "destroy", "destroys", "discover", "discovers",
    "enter", "enters", "erupt", "erupts", "escape", "escapes", "explore", "explores", "fail", "fails",
    "fall", "falls", "fight", "fights", "find", "finds", "float", "floats", "follow", "follows",
    "grab", "grabs", "hang", "hangs", "hold", "holds", "investigate", "investigates", "jump", "jumps", "kill", "kills",
    "leave", "leaves", "lose", "loses", "meet", "meets", "move", "moves", "open",
    "occur", "occurs", "opens", "pulse", "pulses", "reach", "reaches", "rescue", "rescues", "return",
    "returns", "run", "runs", "save", "saves", "say", "says", "shoot", "shoots", "sneak", "sneaks",
    "shift", "shifts", "survive", "survives", "unlock", "unlocks", "upgrade", "upgrades", "win", "wins",
    "glow", "glows", "raise", "raises", "stand", "stands",
    "carry", "carries", "walk", "walks",
}
_THIRD_PERSON_PRESENT = {
    value for value in _NON_RETROSPECTIVE_ACTION_FORMS if value.endswith("s")
}
_IRREGULAR_PAST = {
    "became", "began", "broke", "brought", "built", "bought", "came", "caught",
    "chose", "cut", "did", "drew", "drove", "fell", "felt", "fled", "flew",
    "fought", "found", "gave", "got", "heard", "held", "hid", "hit", "kept",
    "knew", "lay", "led", "left", "lost", "made", "met", "paid", "put", "ran",
    "read", "rode", "said", "saw", "sent", "set", "shot", "spoke", "stood",
    "stole", "struck", "survived", "swam", "took", "told", "thought", "threw",
    "understood", "went", "won", "wore", "wrote",
}
_DANGLING_TITLE_ENDINGS = {
    "a", "an", "and", "as", "at", "before", "but", "by", "for", "from", "in",
    "into", "of", "on", "or", "the", "then", "through", "to", "with",
}
_UNSUPPORTED_MENTAL_STATE = re.compile(
    r"\b(?:appears?|seems?)\s+(?:afraid|angry|anxious|confused|distressed|excited|"
    r"frustrated|happy|nervous|sad|scared|shocked|surprised|tense)|"
    r"\b(?:reacts?|reacted|reacting|reaction)\s+(?:with|to)\b|\bas\s+if\b|"
    r"\bsomething\s+unseen\b|"
    r"\b(?:visible|visibly)\s+(?:afraid|angry|anxious|confused|distressed|excited|"
    r"frustrated|happy|nervous|sad|scared|shocked|surprised|tense)\b|"
    r"\b(?:afraid|angry|anxious|confused|distressed|excited|frustrated|happy|"
    r"nervous|sad|scared|shocked|surprised|tense)\s+(?:demeanou?r|expression|look)\b|"
    r"\btense\s+(?:moment|scene|encounter|exchange|situation)\b|"
    r"\b(?:prepar(?:e|es|ed|ing)|try(?:ing|ies|ied)?|attempt(?:s|ed|ing)?|"
    r"aim(?:s|ed|ing)?|plan(?:s|ned|ning)?|intend(?:s|ed|ing)?)\s+to\b|"
    r"\b(?:await(?:s|ed|ing)?|wait(?:s|ed|ing)?)\s+(?:for|to)\b|"
    r"\babout\s+to\b",
    re.IGNORECASE,
)
_UNSUPPORTED_INTERPRETATION = re.compile(
    r"\b(?:indicat(?:e|es|ed|ing)|suggest(?:s|ed|ing)|impl(?:y|ies|ied|ying))\b",
    re.IGNORECASE,
)
_RELEASE_YEAR_TAG = re.compile(r"(?<!\d)(?:19|20)\d{2}(?!\d)")
_RELEASE_MARKETING_TAG = re.compile(
    r"\b(?:brand\s+new|new|newly\s+released|latest|"
    r"released|release\s+(?:date|year))\b",
    re.IGNORECASE,
)
_UNSUPPORTED_PLATFORM_TAG = re.compile(
    r"^(?:(?:(?:pc|windows(?:\s+\d{1,2})?|xbox(?:\s+(?:one|series\s+[sx]))?|"
    r"playstation(?:\s*[345])?|ps[345]|nintendo(?:\s+switch)?|steam(?:\s+deck)?|"
    r"console|mobile|ios|android|mac(?:os)?)(?:\s+(?:gaming|gameplay|version|edition))?)|"
    r"switch\s+(?:gaming|gameplay|version|edition))$|"
    r"\b(?:pc|xbox(?:\s+(?:one|series\s+[sx]))?|playstation\s*[345]|ps[345]|"
    r"nintendo\s+switch|steam\s+deck|(?:windows|console|mobile)\s+"
    r"(?:gaming|gameplay|version|edition))\b",
    re.IGNORECASE,
)
def contains_unsupported_mental_state(value: str) -> bool:
    return bool(
        _UNSUPPORTED_MENTAL_STATE.search(value)
        or _UNSUPPORTED_INTERPRETATION.search(value)
    )


def contains_unsupported_generated_tag_claim(
    tag: str,
    game_name: str,
    game_hashtag: str,
    explicit_user_tags: list[str] | tuple[str, ...] = (),
) -> bool:
    if (
        tag.casefold() == game_name.casefold()
        or tag.casefold() == game_hashtag.removeprefix("#").casefold()
        or any(
            tag.casefold() == value.casefold()
            for value in explicit_user_tags
        )
    ):
        return False
    return any(
        pattern.search(tag)
        for pattern in (
            _RELEASE_YEAR_TAG,
            _RELEASE_MARKETING_TAG,
            _UNSUPPORTED_PLATFORM_TAG,
        )
    )


def _is_unexpanded_title_repetition(title: str, description: str) -> bool:
    title_tokens = title.split()
    if len(title_tokens) < 3 or title not in description:
        return False
    title_vocabulary = set(title_tokens)
    added_detail = {
        token
        for token in description.split()
        if token not in title_vocabulary and token not in _DETAIL_STOP_WORDS
    }
    return len(added_detail) < 2


def _non_past_opening_form(action_words: list[str]) -> str | None:
    for index, word in enumerate(action_words):
        if (
            word.endswith("ed")
            or word in _IRREGULAR_PAST
            or (
                word in {"was", "were"}
                and index + 1 < len(action_words)
                and (
                    action_words[index + 1].endswith("ing")
                    or action_words[index + 1].endswith("ed")
                    or action_words[index + 1] in _IRREGULAR_PAST
                )
            )
        ):
            return None
        if word in _NON_RETROSPECTIVE_ACTION_FORMS or (index == 0 and word.endswith("ing")):
            return word
    return None


def _non_retrospective_error(
    message: str,
    title_body: str,
    offending_action_form: str | None = None,
    offending_action_field: str | None = None,
) -> InferenceError:
    error = InferenceError(message)
    error.rejected_title_body = title_body
    if offending_action_form is not None:
        error.offending_action_form = offending_action_form
    if offending_action_field is not None:
        error.offending_action_field = offending_action_field
    return error


def _non_retrospective_description_form(
    description: str,
) -> tuple[str, str] | None:
    first_person_non_past = re.compile(
        r"\b(?:i|we)\s+(?P<form>am|are|"
        + "|".join(sorted(_NON_RETROSPECTIVE_ACTION_FORMS))
        + r")\b",
        re.IGNORECASE,
    )
    first_person_present = first_person_non_past.search(description)
    if first_person_present:
        return "creator", first_person_present.group("form").casefold()
    neutral_simple_present = re.compile(
        r"(?:^|[.!?]\s+)(?!(?:i|we)\b)(?:(?:a|an|the|this|that)\s+)?"
        r"(?:[\w'’-]+(?:,\s*|\s+)){0,8}(?P<form>"
        + "|".join(sorted(_THIRD_PERSON_PRESENT))
        + r")\b",
        re.IGNORECASE,
    )
    neutral_present = neutral_simple_present.search(description)
    return (
        ("neutral", neutral_present.group("form").casefold())
        if neutral_present
        else None
    )


def strict_metadata(
    text: str,
    request: dict[str, Any],
    visual_drafts: list[dict[str, Any]] | None = None,
    primary_visual_draft_ordinal: int = 1,
    primary_actor_authority: str | None = None,
    primary_creator_experience_relation: str | None = None,
) -> dict[str, Any]:
    shape = parse_metadata_shape(text, request)
    result = shape["raw"]
    hashtag = request["game"]["hashtag"]
    title_body = shape["titleBody"]
    title = shape["title"]
    description = shape["description"]
    tags = shape["tags"]
    if "#" in title_body or hashtag.casefold() in title_body.casefold():
        _fail(
            InferenceError,
            "Grounded metadata title body included a hashtag that Replay Foundry owns.",
        )
    if len({tag.casefold() for tag in tags}) != len(tags) or any("#" in tag for tag in tags):
        _fail(InferenceError, "Grounded metadata tags must be unique and omit # characters.")
    if any(
        tag.casefold() in {"player", "character", "streamer", "creator", "reaction"}
        for tag in tags
    ):
        _fail(InferenceError, "Grounded metadata used a generic or unsupported tag.")
    if any(
        contains_unsupported_generated_tag_claim(
            tag,
            request["game"].get("name", ""),
            request["game"].get("hashtag", ""),
            request.get("profile", {}).get("defaultTags", []),
        )
        for tag in tags
    ):
        _fail(
            InferenceError,
            "Grounded metadata used a generic or unsupported tag with an "
            "ungrounded release, year, or platform claim.",
        )

    analysis_language = re.compile(
        r"\b(?:evidence|observation|observations|observed|analysis|analyzed|candidate|"
        r"deterministic|sampling|timecode|timestamp|review\s+video|"
        r"visual\s+(?:point|points))\b",
        re.IGNORECASE,
    )
    if analysis_language.search(title) or analysis_language.search(description):
        _fail(InferenceError, "Grounded metadata exposed analysis bookkeeping.")
    combined = title_body + "\n" + description
    if visual_drafts is not None:
        validate_primary_title_scope(
            title_body,
            request,
            visual_drafts,
            primary_visual_draft_ordinal,
        )
        validate_primary_action_entailment(
            title_body,
            description,
            visual_drafts[primary_visual_draft_ordinal - 1],
        )
        validate_readable_text_reuse(title_body, description, request, visual_drafts)
        validate_interface_attribution_authority(
            title_body,
            description,
            request,
            visual_drafts,
        )
        if (
            primary_actor_authority is not None
            and primary_creator_experience_relation is not None
        ):
            validate_creator_actor_authority(
                title_body,
                description,
                tags,
                request,
                visual_drafts[primary_visual_draft_ordinal - 1],
                primary_actor_authority,
                primary_creator_experience_relation,
            )

    title_words = normalize_lexical(title_body).split()
    action_index = 1 if title_words and title_words[0] in {"i", "we"} else 0
    action_words = title_words[action_index:]
    offending_title_form = _non_past_opening_form(action_words)
    description_tense_diagnostic = _non_retrospective_description_form(description)

    generic_person_subject_opening = any(
        _GENERIC_PERSON_SUBJECT_OPENING.search(value)
        for value in (title_body, description)
    )
    neutral_person_subject_permitted = (
        primary_actor_authority in {"Unknown", "OtherPerson"}
        and primary_creator_experience_relation != "CreatorActed"
    )
    if re.search(
        r"\b(?:player|character|streamer|creator|camera\s+wearer)\b",
        combined,
        re.IGNORECASE,
    ) or (
        generic_person_subject_opening
        and not neutral_person_subject_permitted
    ) or _FIRST_PERSON_GENERIC_OBSERVER_OPENING.search(description):
        diagnostic_field = (
            "titleBody"
            if offending_title_form is not None
            else "description"
            if description_tense_diagnostic is not None
            else None
        )
        diagnostic_form = (
            offending_title_form
            if offending_title_form is not None
            else description_tense_diagnostic[1]
            if description_tense_diagnostic is not None
            else None
        )
        raise _non_retrospective_error(
            "Grounded metadata used third-person creator framing or generic observer-person framing. "
            f"Title={title!r}; Description={description!r}",
            title_body,
            diagnostic_form,
            diagnostic_field,
        )

    if offending_title_form is not None:
        raise _non_retrospective_error(
            "Grounded metadata used a command, present-tense, or gerund title opening.",
            title_body,
            offending_title_form,
            "titleBody",
        )
    if title_words and title_words[-1] in _DANGLING_TITLE_ENDINGS:
        _fail(
            InferenceError,
            "Grounded metadata title ended with an incomplete connective or article.",
        )

    if description_tense_diagnostic is not None:
        diagnostic_kind, diagnostic_form = description_tense_diagnostic
        raise _non_retrospective_error(
            "Grounded metadata used non-retrospective creator narration."
            if diagnostic_kind == "creator"
            else "Grounded metadata used non-retrospective neutral narration.",
            title_body,
            diagnostic_form,
            "description",
        )
    if re.search(
        r"^\s*(?:this\s+(?:clip|video)\s+(?:shows|features|captures|is\s+about)|"
        r"in\s+this\s+(?:clip|video)|i\s+(?:watch|see)\b|watch\s+as\b)",
        description,
        re.IGNORECASE,
    ):
        _fail(InferenceError, "Grounded metadata used generic description boilerplate.")

    if contains_unsupported_mental_state(combined):
        _fail(
            InferenceError,
            "Grounded metadata assigned an unsupported mental state or interpretive claim.",
        )

    normalized_title = normalize_lexical(title_body)
    normalized_description = normalize_lexical(description)
    normalized_game_name = normalize_lexical(request["game"]["name"])
    if request["game"]["name"].isascii() and len(normalized_game_name) >= 3 and (
        " " + normalized_game_name + " " in " " + normalized_title + " "
    ):
        _fail(
            InferenceError,
            "Grounded metadata repeated the game name before its canonical hashtag.",
        )
    if _is_unexpanded_title_repetition(normalized_title, normalized_description):
        error = InferenceError(
            "Grounded metadata repeated the title in the description."
        )
        error.rejected_title_body = title_body
        error.rejected_description = description
        raise error
    for transcript in request.get("transcripts", []):
        if transcript.get("authority") != "AutomaticUnreviewed":
            continue
        overlaps = shared_token_windows(combined, transcript["text"], 4)
        if overlaps:
            _fail(
                InferenceError,
                "Grounded metadata reused unreviewed transcript wording. Rejected phrase: "
                + json.dumps(overlaps[0], ensure_ascii=False),
            )
    if (
        contains_unapproved_non_latin(title, request)
        or contains_unapproved_non_latin(description, request)
        or any(contains_unapproved_non_latin(tag, request) for tag in tags)
    ):
        _fail(
            InferenceError,
            "Grounded metadata did not preserve the English audience-copy language policy.",
        )
    grounding = strict_grounding(result["grounding"], request, title, description)
    return {
        "title": title,
        "description": description,
        "tags": tags,
        "grounding": grounding,
    }
