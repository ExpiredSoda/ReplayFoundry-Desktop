"""Public grounded-metadata validation facade and retry guidance."""
from __future__ import annotations

import json
from typing import Any

from ..errors import (
    InferenceError,
    RerollTitleTooSimilarError,
    _fail,
)
from ..request_validation import _require_array, _require_exact_keys, _require_object
from .grounded_metadata_contract_values import bounded_text
from .grounded_metadata_grounding_validation import (
    grounding_binding_id,
    knowledge_claim_is_specific,
    strict_grounding,
)
from .grounded_metadata_lexical import (
    contains_unapproved_non_latin,
    normalize_lexical,
    shared_token_windows,
    shares_token_window,
)
from .grounded_metadata_output_schema import metadata_schema, title_body_maximum
from .grounded_metadata_reroll_similarity import (
    normalize_terminal_single_period_title_body,
)


def parse_metadata_shape(
    text: str,
    request: dict[str, Any],
) -> dict[str, Any]:
    """Parse transport structure without applying audience-copy policy."""
    def reject_constant(value: str) -> Any:
        raise ValueError(f"non-finite JSON token {value}")

    try:
        value = json.loads(text, parse_constant=reject_constant)
    except (json.JSONDecodeError, ValueError) as error:
        _fail(InferenceError, f"Grounded metadata output is not strict JSON: {error}")
    result = _require_object(value, "provider output")
    _require_exact_keys(
        result,
        {"titleBody", "description", "tags", "grounding", "temporalVoice"},
        "provider output",
    )
    hashtag = request["game"]["hashtag"]
    title_body = bounded_text(
        result["titleBody"],
        "provider output.titleBody",
        title_body_maximum(hashtag),
    )
    title_body = normalize_terminal_single_period_title_body(title_body)
    if result["temporalVoice"] != "RetrospectivePast":
        _fail(InferenceError, "Grounded metadata did not declare retrospective past voice.")
    description = bounded_text(result["description"], "provider output.description", 420)
    tags_value = _require_array(result["tags"], "provider output.tags", maximum=8)
    if not tags_value:
        _fail(InferenceError, "Grounded metadata must include at least one tag.")
    tags = [bounded_text(tag, "provider output.tags", 60) for tag in tags_value]
    return {
        "raw": result,
        "titleBody": title_body,
        "title": title_body + " " + hashtag,
        "description": description,
        "tags": tags,
    }


def strict_metadata(*args: Any, **kwargs: Any) -> dict[str, Any]:
    # Lazy import preserves the public validation facade without creating a
    # module cycle with the focused audience-copy policy.
    from .grounded_metadata_audience_validation import strict_metadata as validate
    return validate(*args, **kwargs)


def validation_failure_code(error: InferenceError) -> str:
    if isinstance(error, RerollTitleTooSimilarError):
        return "RerollTitleTooSimilar"
    message = str(error)
    rules = (
        ("unsupported creator embodiment", "UnsupportedCreatorEmbodiment"),
        ("third-person creator framing", "ThirdPersonCreatorFraming"),
        ("generic description boilerplate", "GenericOpening"),
        ("command, present-tense, or gerund", "NonRetrospectiveVoice"),
        ("non-retrospective creator narration", "NonRetrospectiveVoice"),
        ("non-retrospective neutral narration", "NonRetrospectiveVoice"),
        ("retrospective past voice", "NonRetrospectiveVoice"),
        ("did not begin with a retrospective action", "NonRetrospectiveVoice"),
        ("incomplete connective or article", "IncompleteTitle"),
        ("non-primary chronological draft", "CrossDraftTitleContamination"),
        ("unstable readable text", "UnstableReadableTextReuse"),
        ("explicit first-person title subject", "FirstPersonTitleSubject"),
        (
            "interface text to a physical display source",
            "UnsupportedInterfaceAttribution",
        ),
        ("unsupported mental state", "UnsupportedMentalState"),
        ("unreviewed transcript wording", "UnreviewedTranscriptReuse"),
        ("repeated the title", "TitleDescriptionRepetition"),
        ("repeated the game name", "RedundantGameIdentity"),
        ("analysis bookkeeping", "AnalysisBookkeeping"),
        ("English audience-copy language", "OutputLanguage"),
        ("exact game hashtag", "GameHashtag"),
        (
            "did not use a canonical name or two distinctive cited-passage terms",
            "UncoupledKnowledgeReference",
        ),
        ("game knowledge", "UnsupportedKnowledgeGrounding"),
        ("knowledge claim", "UnsupportedKnowledgeGrounding"),
        ("knowledge citations", "UnsupportedKnowledgeGrounding"),
        ("generic or unsupported tag", "UnsupportedTag"),
        ("tags must be unique", "TagShape"),
    )
    return next(
        (code for marker, code in rules if marker in message),
        "StrictOutputValidation",
    )


def validation_feedback(code: str) -> str:
    return {
        "UnsupportedCreatorEmbodiment": "remove first-person ownership of another person's body, dialogue, emotion, or primary action; use neutral retrospective past-action or canonical-identity wording without player or character labels, while retaining first-person only for the supplied creator-controlled, creator-affected, or creator-encounter relation",
        "ThirdPersonCreatorFraming": "remove generic role labels; preserve or use an exact canonical identity only when UserConfirmed or ReusedUserMemory game notes plus the bounded review support it, without a game-knowledge binding, or when selected authorized game knowledge plus its bounded clip evidence support it and every required grounding binding is present; SourcePathHint notes, automatic transcript text, and path wording cannot authorize a canonical identity; otherwise lead with a neutral retrospective setting or action; never force I or we without typed creator authority",
        "GenericOpening": "begin directly with the visible action, change, object, setting, or outcome",
        "NonRetrospectiveVoice": "correct grammatical voice while preserving any exact canonical identity when UserConfirmed or ReusedUserMemory game notes plus the bounded review support it, without a game-knowledge binding, or when selected authorized game knowledge plus its bounded clip evidence support it and every required grounding binding is retained or added; SourcePathHint notes, automatic transcript text, and path wording cannot authorize a canonical identity; do not replace a supported canonical identity with a generic role merely to evade correction; rewrite every completed action in retrospective past tense, including actions by non-player people, vehicles, interfaces, and the environment; do not use an imperative, bare infinitive, simple present, or gerund opening; also remove any unsupported intent, purpose, unseen cause, or readable wording absent from the supplied stable-readable-text list",
        "IncompleteTitle": "replace the title with one shorter complete past-tense action and move every secondary event into the description",
        "CrossDraftTitleContamination": "describe only the marked primary chronological event and omit every earlier or later event from the title, description, and tags",
        "UnstableReadableTextReuse": "remove every copied readable-text phrase that is absent from the supplied stable-readable-text list and not authorized by a human-reviewed transcript; delete every exact forbiddenReadableTextPhrases value named by the compact correction target from each affectedAudienceFields value and do not retain or paraphrase it",
        "FirstPersonTitleSubject": "remove the explicit first-person title subject and begin with one concise simple-past action; keep secondary detail in the description",
        "UnsupportedInterfaceAttribution": "keep screen-space menus, HUD elements, and readable interface text separate from physical objects in the scene; describe a menu or overlay directly as an interface element, and do not claim that its lettering appeared on, in, or was shown by a screen, display, monitor, sign, or billboard unless the marked primary visual draft explicitly identifies that physical source",
        "UnsupportedMentalState": "use only literal physical action and setting from the marked primary draft; remove emotion, expression interpretation, reaction, intent, unseen causality, and any strengthened completion, transition, defeat, destruction, disappearance, or return that its action clauses do not state",
        "UnreviewedTranscriptReuse": "paraphrase only what is independently grounded and do not reuse the automatic transcript phrase",
        "TitleDescriptionRepetition": "keep the supported title, but completely rewrite the description so it does not contain the full title-body phrase and adds at least two supported content words absent from the title using a distinct visible action, result, setting, or concrete detail",
        "RedundantGameIdentity": "remove the game name from the content portion of the title because the exact hashtag already identifies it",
        "AnalysisBookkeeping": "remove all analysis and internal-system language",
        "OutputLanguage": "write the surrounding audience copy in English",
        "GameHashtag": "end the title with one space and the exact supplied hashtag",
        "UncoupledKnowledgeReference": "either remove the grounding item entirely or rewrite its bound audience field with a supported canonical name or at least two distinctive terms from the cited passage; use canonical identities only when the supplied clip linkage and visible current event support them",
        "UnsupportedKnowledgeGrounding": "remove unsupported story detail or bind only Title or Description to a supplied clip-linked or bounded-visual-review passage and its supplied clip-evidence IDs",
        "GroundedRefinementUnchanged": "choose a materially different supported audience-copy angle from the immediately repeated draft using only another completed primary-event action, visible outcome, supported canonical identity, or concrete object in the unchanged evidence; remove draft details absent from the verified current-event passage and do not add a new fact",
        "UnresolvedVisualGrounding": "compare every CurrentEventCandidate against all ordered visual drafts; use and cite one only when at least two independent distinctive visible details align, otherwise remove any setting or object not consistently supported by the drafts",
        "UnsupportedTag": "remove generic role, reaction, emotion, intent, release, year, and unsupported platform tags",
        "TagShape": "return unique plain tags without hash characters",
        "RerollTitleTooSimilar": "choose a materially different audience-copy angle using only another completed primary-event action, visible outcome, supported canonical entity, or concrete object already present in the supplied evidence; do not echo a prior title and do not add a new fact",
        "StrictOutputValidation": "re-check every prompt rule before returning the new draft",
    }[code]


def reviewable_metadata(
    text: str,
    request: dict,
    visual_drafts: list[dict] | None = None,
    primary_visual_draft_ordinal: int = 1,
    primary_actor_authority: str | None = None,
    primary_creator_experience_relation: str | None = None,
) -> dict:
    """Return schema-valid copy even when an audience policy needs review.

    The strict validator remains the single source of truth for diagnostics.
    Only its semantic rejection is converted to a typed review issue.  A
    malformed JSON package, missing field, invalid bound, or unusable value
    still raises because there is no complete draft to retain.
    """
    try:
        metadata = strict_metadata(
            text,
            request,
            visual_drafts,
            primary_visual_draft_ordinal,
            primary_actor_authority,
            primary_creator_experience_relation,
        )
        metadata["_reviewIssues"] = []
        return metadata
    except InferenceError as error:
        shape = parse_metadata_shape(text, request)
        review_issues = [validation_failure_code(error)]
        try:
            grounding = strict_grounding(
                shape["raw"]["grounding"],
                request,
                shape["title"],
                shape["description"],
            )
        except InferenceError as grounding_error:
            grounding = []
            grounding_code = validation_failure_code(grounding_error)
            if grounding_code not in review_issues:
                review_issues.append(grounding_code)
        return {
            "title": shape["title"],
            "description": shape["description"],
            "tags": shape["tags"],
            "grounding": grounding,
            "_reviewIssues": review_issues,
        }
