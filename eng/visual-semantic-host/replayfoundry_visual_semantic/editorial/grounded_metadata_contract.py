"""Validation for the core grounded editorial-metadata request."""
from __future__ import annotations

from pathlib import Path
from typing import Any

from ..commands import UsageOrInputError, _fail
from ..request_validation import (
    _require_array,
    _require_exact_keys,
    _require_object,
    _validate_review_video,
)
from .grounded_metadata_context_contract import (
    GAME_KNOWLEDGE_POLICY_VERSION,
    validate_game_knowledge,
    validate_visual_text,
)
from .grounded_metadata_contract_values import (
    bounded_text,
    finite_number as _finite_number,
    optional_text,
)

SUPPORTED_VARIANT_INTENTS = frozenset(
    {
        "DirectAction",
        "SpecificCuriosity",
        "OutcomeFocused",
        "ConcreteDetail",
        "CommentaryLed",
    }
)


def validate_variant_intent(
    value: str,
    transcripts: list[dict[str, Any]],
    location: str,
) -> str:
    if value not in SUPPORTED_VARIANT_INTENTS:
        _fail(UsageOrInputError, f"{location} is unsupported.")
    if value == "CommentaryLed" and not any(
        transcript["authority"] in {"UserCorrected", "HumanReviewed"}
        for transcript in transcripts
    ):
        _fail(
            UsageOrInputError,
            f"{location} requires reviewed transcript authority.",
        )
    return value


def validate_request(
    value: Any,
    index: int,
    media_hash_cache: dict[Path, str],
) -> dict[str, Any]:
    location = f"$.requests[{index}]"
    request = _require_object(value, location)
    _require_exact_keys(
        request,
        {
            "candidateId",
            "attempt",
            "priorAcceptedTitles",
            "reviewVideo",
            "game",
            "gameKnowledge",
            "visualText",
            "clip",
            "transcripts",
            "evidence",
            "profile",
        },
        location,
    )
    candidate_id = bounded_text(request["candidateId"], f"{location}.candidateId", 160)
    attempt = request["attempt"]
    if isinstance(attempt, bool) or not isinstance(attempt, int) or not 0 <= attempt <= 100:
        _fail(UsageOrInputError, f"{location}.attempt is invalid.")
    prior_title_values = _require_array(
        request["priorAcceptedTitles"],
        f"{location}.priorAcceptedTitles",
        maximum=8,
    )
    prior_accepted_titles = [
        bounded_text(
            title,
            f"{location}.priorAcceptedTitles[{title_index}]",
            100,
        )
        for title_index, title in enumerate(prior_title_values)
    ]
    if len({title.casefold() for title in prior_accepted_titles}) != len(
        prior_accepted_titles
    ):
        _fail(
            UsageOrInputError,
            f"{location}.priorAcceptedTitles contains duplicates.",
        )

    validated_video = _validate_review_video(
        request["reviewVideo"],
        f"{location}.reviewVideo",
        media_hash_cache,
    )

    game = _require_object(request["game"], f"{location}.game")
    _require_exact_keys(game, {"name", "hashtag", "source", "notes"}, f"{location}.game")
    game_name = bounded_text(game["name"], f"{location}.game.name", 120)
    hashtag = bounded_text(game["hashtag"], f"{location}.game.hashtag", 121)
    if not hashtag.startswith("#") or not hashtag[1:].isalnum():
        _fail(UsageOrInputError, f"{location}.game.hashtag is not canonical.")
    source = bounded_text(game["source"], f"{location}.game.source", 40)
    if source not in {"SourcePathHint", "ReusedUserMemory", "UserConfirmed"}:
        _fail(UsageOrInputError, f"{location}.game.source is unsupported.")
    notes = optional_text(game["notes"], f"{location}.game.notes", 1500)

    clip = _require_object(request["clip"], f"{location}.clip")
    _require_exact_keys(
        clip,
        {
            "startSeconds",
            "endSeconds",
            "sourceDurationSeconds",
            "deterministicScore",
            "deterministicReason",
        },
        f"{location}.clip",
    )
    start = _finite_number(clip["startSeconds"], f"{location}.clip.startSeconds", 0, 86_400)
    end = _finite_number(clip["endSeconds"], f"{location}.clip.endSeconds", 0, 86_400)
    duration = _finite_number(
        clip["sourceDurationSeconds"],
        f"{location}.clip.sourceDurationSeconds",
        0.001,
        86_400,
    )
    if end <= start or end > duration:
        _fail(UsageOrInputError, f"{location}.clip interval is invalid.")
    score = _finite_number(
        clip["deterministicScore"],
        f"{location}.clip.deterministicScore",
        0,
        100,
    )
    reason = bounded_text(
        clip["deterministicReason"],
        f"{location}.clip.deterministicReason",
        1000,
    )

    transcripts_value = _require_array(
        request["transcripts"], f"{location}.transcripts", maximum=8
    )
    transcripts: list[dict[str, Any]] = []
    streams: set[int] = set()
    for transcript_index, transcript_value in enumerate(transcripts_value):
        transcript_location = f"{location}.transcripts[{transcript_index}]"
        transcript = _require_object(transcript_value, transcript_location)
        _require_exact_keys(
            transcript,
            {"absoluteAudioStreamIndex", "role", "authority", "text"},
            transcript_location,
        )
        stream = transcript["absoluteAudioStreamIndex"]
        if (
            isinstance(stream, bool)
            or not isinstance(stream, int)
            or stream < 0
            or stream in streams
        ):
            _fail(
                UsageOrInputError,
                f"{transcript_location}.absoluteAudioStreamIndex is invalid.",
            )
        streams.add(stream)
        authority = bounded_text(
            transcript["authority"], f"{transcript_location}.authority", 40
        )
        if authority not in {"AutomaticUnreviewed", "UserCorrected", "HumanReviewed"}:
            _fail(UsageOrInputError, f"{transcript_location}.authority is unsupported.")
        transcripts.append(
            {
                "absoluteAudioStreamIndex": stream,
                "role": bounded_text(
                    transcript["role"], f"{transcript_location}.role", 40
                ),
                "authority": authority,
                "text": bounded_text(
                    transcript["text"], f"{transcript_location}.text", 4000
                ),
            }
        )

    evidence_value = _require_array(
        request["evidence"], f"{location}.evidence", maximum=24
    )
    evidence: list[dict[str, str]] = []
    evidence_ids: set[str] = set()
    for evidence_index, evidence_item in enumerate(evidence_value):
        evidence_location = f"{location}.evidence[{evidence_index}]"
        item = _require_object(evidence_item, evidence_location)
        _require_exact_keys(item, {"id", "kind", "description"}, evidence_location)
        item_id = bounded_text(item["id"], f"{evidence_location}.id", 160)
        if item_id in evidence_ids:
            _fail(UsageOrInputError, f"{location}.evidence has duplicate IDs.")
        evidence_ids.add(item_id)
        evidence.append(
            {
                "id": item_id,
                "kind": bounded_text(item["kind"], f"{evidence_location}.kind", 60),
                "description": bounded_text(
                    item["description"], f"{evidence_location}.description", 1000
                ),
            }
        )

    game_knowledge = validate_game_knowledge(
        request["gameKnowledge"],
        f"{location}.gameKnowledge",
        evidence_ids | {f"stream-{stream}" for stream in streams},
    )
    visual_text = validate_visual_text(
        request["visualText"],
        f"{location}.visualText",
        start,
        end,
    )

    profile = _require_object(request["profile"], f"{location}.profile")
    _require_exact_keys(
        profile,
        {
            "audienceAddress",
            "namingGuidance",
            "reusableDescriptionSignature",
            "defaultTags",
            "voicePerspective",
            "variantIntent",
        },
        f"{location}.profile",
    )
    tags_value = _require_array(
        profile["defaultTags"], f"{location}.profile.defaultTags", maximum=12
    )
    default_tags = [
        bounded_text(tag, f"{location}.profile.defaultTags", 60)
        for tag in tags_value
    ]

    validated_request = {
        "candidateId": candidate_id,
        "attempt": attempt,
        "priorAcceptedTitles": prior_accepted_titles,
        "game": {
            "name": game_name,
            "hashtag": hashtag,
            "source": source,
            "notes": notes,
        },
        "gameKnowledge": game_knowledge,
        "visualText": visual_text,
        "clip": {
            "startSeconds": start,
            "endSeconds": end,
            "sourceDurationSeconds": duration,
            "deterministicScore": score,
            "deterministicReason": reason,
        },
        "transcripts": transcripts,
        "evidence": evidence,
        "profile": {
            "audienceAddress": bounded_text(
                profile["audienceAddress"], f"{location}.profile.audienceAddress", 40
            ),
            "namingGuidance": optional_text(
                profile["namingGuidance"], f"{location}.profile.namingGuidance", 300
            ),
            "reusableDescriptionSignature": optional_text(
                profile["reusableDescriptionSignature"],
                f"{location}.profile.reusableDescriptionSignature",
                1500,
            ),
            "defaultTags": default_tags,
            "voicePerspective": bounded_text(
                profile["voicePerspective"],
                f"{location}.profile.voicePerspective",
                40,
            ),
            "variantIntent": validate_variant_intent(
                bounded_text(
                    profile["variantIntent"],
                    f"{location}.profile.variantIntent",
                    40,
                ),
                transcripts,
                f"{location}.profile.variantIntent",
            ),
        },
    }
    if validated_request["profile"]["voicePerspective"] not in {
        "CreatorFirstPerson",
        "NeutralNoSubject",
    }:
        _fail(UsageOrInputError, f"{location}.profile.voicePerspective is unsupported.")
    validated_request["_validated"] = {
        **validated_video,
        "sourceAbsoluteOffset": 0,
        "candidateStart": 0,
        "candidateEnd": validated_video["videoDuration"],
    }
    return validated_request
