"""ReplayFoundry local visual-semantic host implementation module."""
from __future__ import annotations

from .input_contract import *  # noqa: F401,F403


def _validate_review_video(
    review_video_value: Any,
    location: str,
    media_hash_cache: dict[Path, str],
) -> dict[str, Any]:
    review_video = _require_object(review_video_value, location)
    _require_exact_keys(
        review_video,
        {
            "path",
            "sha256",
            "byteLength",
            "lastWriteTimeUtc",
            "reviewVideoDurationSeconds",
        },
        location,
    )
    video_path = _require_absolute_external_path(
        review_video["path"],
        f"{location}.path",
        must_exist=True,
        must_be_file=True,
    )
    expected_video_hash = _require_sha256(
        review_video["sha256"],
        f"{location}.sha256",
    )
    expected_length = _require_nonnegative_integer(
        review_video["byteLength"],
        f"{location}.byteLength",
    )
    try:
        initial_stat = video_path.stat()
    except OSError as error:
        _fail(
            UsageOrInputError,
            f"{location} could not be inspected: {error}",
        )
    if expected_length <= 0 or initial_stat.st_size != expected_length:
        _fail(
            UsageOrInputError,
            f"{location} does not match its retained byte length.",
        )
    video_duration = _require_finite_decimal(
        review_video["reviewVideoDurationSeconds"],
        f"{location}.reviewVideoDurationSeconds",
    )
    if video_duration <= 0 or video_duration > MAX_INPUT_DURATION_SECONDS:
        _fail(
            UsageOrInputError,
            f"{location}.reviewVideoDurationSeconds must be positive and no "
            f"longer than {MAX_INPUT_DURATION_SECONDS} seconds.",
        )

    expected_last_write = _require_utc_timestamp(
        review_video["lastWriteTimeUtc"],
        f"{location}.lastWriteTimeUtc",
    )
    actual_last_write = datetime.fromtimestamp(
        initial_stat.st_mtime,
        tz=timezone.utc,
    )
    if abs((actual_last_write - expected_last_write).total_seconds()) > 0.001:
        _fail(
            UsageOrInputError,
            f"{location} changed after input preparation.",
        )

    if video_path not in media_hash_cache:
        media_hash_cache[video_path] = _sha256_file(video_path)
    if media_hash_cache[video_path] != expected_video_hash:
        _fail(
            UsageOrInputError,
            f"{location} SHA-256 does not match.",
        )
    try:
        post_hash_stat = video_path.stat()
    except OSError as error:
        _fail(
            UsageOrInputError,
            f"{location} disappeared during verification: {error}",
        )
    if (
        post_hash_stat.st_size != initial_stat.st_size
        or post_hash_stat.st_mtime_ns != initial_stat.st_mtime_ns
    ):
        _fail(
            UsageOrInputError,
            f"{location} changed during verification.",
        )

    return {
        "videoPath": video_path,
        "videoDuration": video_duration,
        "expectedVideoHash": expected_video_hash,
        "expectedVideoLength": expected_length,
        "expectedLastWriteUtc": expected_last_write,
    }

def _validate_request(
    request_value: Any,
    index: int,
    media_hash_cache: dict[Path, str],
) -> dict[str, Any]:
    location = f"$.requests[{index}]"
    request = _require_object(request_value, location)
    _require_exact_keys(
        request,
        {
            "caseId",
            "caseHash",
            "sourceId",
            "reviewVideo",
            "candidate",
            "composition",
            "transcript",
            "deterministicSummary",
        },
        location,
    )

    _require_id(request["caseId"], f"{location}.caseId")
    _require_sha256(request["caseHash"], f"{location}.caseHash")
    _require_id(request["sourceId"], f"{location}.sourceId")

    validated_video = _validate_review_video(
        request["reviewVideo"],
        f"{location}.reviewVideo",
        media_hash_cache,
    )
    video_path = validated_video["videoPath"]
    video_duration = validated_video["videoDuration"]
    expected_video_hash = validated_video["expectedVideoHash"]
    expected_length = validated_video["expectedVideoLength"]
    expected_last_write = validated_video["expectedLastWriteUtc"]

    candidate = _require_object(request["candidate"], f"{location}.candidate")
    _require_exact_keys(
        candidate,
        {
            "id",
            "mode",
            "startRelativeSeconds",
            "endRelativeSeconds",
            "sourceAbsoluteOffsetSeconds",
        },
        f"{location}.candidate",
    )
    _require_id(candidate["id"], f"{location}.candidate.id")
    candidate_mode = _require_enum(
        candidate["mode"],
        CANDIDATE_MODES,
        f"{location}.candidate.mode",
    )
    candidate_start = _require_finite_decimal(
        candidate["startRelativeSeconds"],
        f"{location}.candidate.startRelativeSeconds",
    )
    candidate_end = _require_finite_decimal(
        candidate["endRelativeSeconds"],
        f"{location}.candidate.endRelativeSeconds",
    )
    source_absolute_offset = _require_finite_decimal(
        candidate["sourceAbsoluteOffsetSeconds"],
        f"{location}.candidate.sourceAbsoluteOffsetSeconds",
    )
    if (
        candidate_start < 0
        or candidate_end <= candidate_start
        or candidate_end > video_duration
        or source_absolute_offset < 0
    ):
        _fail(
            UsageOrInputError,
            f"{location}.candidate interval or source offset is invalid.",
        )

    composition = _require_object(
        request["composition"],
        f"{location}.composition",
    )
    _require_exact_keys(
        composition,
        {"layoutDescription", "coordinateSpace", "regions"},
        f"{location}.composition",
    )
    _require_string(
        composition["layoutDescription"],
        f"{location}.composition.layoutDescription",
        maximum=240,
    )
    coordinate_space = _require_string(
        composition["coordinateSpace"],
        f"{location}.composition.coordinateSpace",
        maximum=100,
    )
    if coordinate_space != COMPOSITION_COORDINATE_SPACE:
        _fail(
            UsageOrInputError,
            f"{location}.composition.coordinateSpace must be "
            f"'{COMPOSITION_COORDINATE_SPACE}'.",
        )
    regions = _require_array(
        composition["regions"],
        f"{location}.composition.regions",
        maximum=16,
    )
    region_ids: set[str] = set()
    has_gameplay_region = False
    for region_index, region_value in enumerate(regions):
        region_location = f"{location}.composition.regions[{region_index}]"
        region = _require_object(region_value, region_location)
        _require_exact_keys(
            region,
            {
                "id",
                "role",
                "geometry",
                "geometrySource",
                "roleSource",
            },
            region_location,
        )
        region_id = _require_id(region["id"], f"{region_location}.id")
        normalized_region_id = region_id.casefold()
        if normalized_region_id in region_ids:
            _fail(
                UsageOrInputError,
                f"{location}.composition has duplicate region ID '{region_id}'.",
            )
        region_ids.add(normalized_region_id)
        region_role = _require_enum(
            region["role"],
            COMPOSITION_ROLES,
            f"{region_location}.role",
        )
        has_gameplay_region = has_gameplay_region or region_role == "Gameplay"
        _validate_geometry(region["geometry"], f"{region_location}.geometry")
        _require_enum(
            region["geometrySource"],
            COMPOSITION_VALUE_SOURCES,
            f"{region_location}.geometrySource",
        )
        _require_enum(
            region["roleSource"],
            COMPOSITION_VALUE_SOURCES,
            f"{region_location}.roleSource",
        )
    if not has_gameplay_region:
        _fail(
            UsageOrInputError,
            f"{location}.composition requires one confirmed Gameplay region.",
        )

    transcript = _require_object(request["transcript"], f"{location}.transcript")
    _require_exact_keys(
        transcript,
        {
            "policy",
            "evidenceStatus",
            "spans",
            "accuracyWarning",
        },
        f"{location}.transcript",
    )
    transcript_policy = _require_enum(
        transcript["policy"],
        TRANSCRIPT_POLICIES,
        f"{location}.transcript.policy",
    )
    transcript_status: str | None
    if transcript["evidenceStatus"] is None:
        transcript_status = None
    else:
        transcript_status = _require_enum(
            transcript["evidenceStatus"],
            TRANSCRIPT_STATUSES,
            f"{location}.transcript.evidenceStatus",
        )
    transcript_spans = _require_array(
        transcript["spans"],
        f"{location}.transcript.spans",
        maximum=64,
    )
    span_ids: set[str] = set()
    lexical_span_count = 0
    non_speech_span_count = 0
    previous_span_key: tuple[Decimal, Decimal, str] | None = None
    for span_index, span_value in enumerate(transcript_spans):
        span_location = f"{location}.transcript.spans[{span_index}]"
        span = _require_object(span_value, span_location)
        _require_exact_keys(
            span,
            {
                "id",
                "text",
                "startSeconds",
                "endSeconds",
                "isNonSpeech",
                "timingPrecision",
            },
            span_location,
        )
        span_id = _require_id(span["id"], f"{span_location}.id")
        normalized_span_id = span_id.casefold()
        if normalized_span_id in span_ids:
            _fail(
                UsageOrInputError,
                f"{location}.transcript has duplicate span ID '{span_id}'.",
            )
        span_ids.add(normalized_span_id)
        _require_string(
            span["text"],
            f"{span_location}.text",
            maximum=1_000,
        )
        span_start = _require_finite_decimal(
            span["startSeconds"],
            f"{span_location}.startSeconds",
        )
        span_end = _require_finite_decimal(
            span["endSeconds"],
            f"{span_location}.endSeconds",
        )
        if span_start < 0 or span_end < span_start or span_end > video_duration:
            _fail(
                UsageOrInputError,
                f"{span_location} is outside the bounded review video.",
            )
        if not isinstance(span["isNonSpeech"], bool):
            _fail(
                UsageOrInputError,
                f"{span_location}.isNonSpeech must be a boolean.",
            )
        _require_enum(
            span["timingPrecision"],
            TRANSCRIPT_TIMING_PRECISIONS,
            f"{span_location}.timingPrecision",
        )
        if span["isNonSpeech"]:
            non_speech_span_count += 1
        else:
            lexical_span_count += 1
        span_key = (span_start, span_end, span_id.casefold())
        if previous_span_key is not None and span_key < previous_span_key:
            _fail(
                UsageOrInputError,
                f"{location}.transcript spans are not deterministically ordered.",
            )
        previous_span_key = span_key
    _require_string(
        transcript["accuracyWarning"],
        f"{location}.transcript.accuracyWarning",
        maximum=300,
    )
    if transcript_policy == "VisualOnlyV1":
        if transcript_status is not None or transcript_spans:
            _fail(
                UsageOrInputError,
                f"{location}.transcript VisualOnlyV1 must not contain "
                "transcript evidence.",
            )
    elif transcript_status is None:
        _fail(
            UsageOrInputError,
            f"{location}.transcript FullContextV1 requires evidenceStatus.",
        )
    elif transcript_status == "LexicalText" and lexical_span_count == 0:
        _fail(
            UsageOrInputError,
            f"{location}.transcript LexicalText requires a lexical span.",
        )
    elif (
        transcript_status == "NonSpeechTokenOnly"
        and (non_speech_span_count == 0 or lexical_span_count != 0)
    ):
        _fail(
            UsageOrInputError,
            f"{location}.transcript NonSpeechTokenOnly requires only "
            "non-speech spans.",
        )
    elif transcript_status == "EmptyProviderOutput" and transcript_spans:
        _fail(
            UsageOrInputError,
            f"{location}.transcript EmptyProviderOutput requires zero spans.",
        )

    summary_value = request["deterministicSummary"]
    if transcript_policy == "VisualOnlyV1":
        if summary_value is not None:
            _fail(
                UsageOrInputError,
                f"{location}.deterministicSummary must be null for VisualOnlyV1.",
            )
    else:
        summary = _require_object(summary_value, f"{location}.deterministicSummary")
        _require_exact_keys(
            summary,
            {
                "candidateDurationSeconds",
                "sceneBoundaryCount",
                "gameplayActivityBurstCount",
                "audioNoveltyEventCount",
                "presenterSupportEventCount",
                "integrityStatus",
                "eventNeighborhood",
                "mode",
                "confirmedRegionRoles",
            },
            f"{location}.deterministicSummary",
        )
        summary_duration = _require_finite_decimal(
            summary["candidateDurationSeconds"],
            f"{location}.deterministicSummary.candidateDurationSeconds",
        )
        if summary_duration != candidate_end - candidate_start:
            _fail(
                UsageOrInputError,
                f"{location}.deterministicSummary duration does not match candidate.",
            )
        for count_name in (
            "sceneBoundaryCount",
            "gameplayActivityBurstCount",
            "audioNoveltyEventCount",
            "presenterSupportEventCount",
        ):
            _require_nonnegative_integer(
                summary[count_name],
                f"{location}.deterministicSummary.{count_name}",
            )
        _require_enum(
            summary["integrityStatus"],
            INTEGRITY_STATUSES,
            f"{location}.deterministicSummary.integrityStatus",
        )
        if summary["mode"] != candidate_mode:
            _fail(
                UsageOrInputError,
                f"{location}.deterministicSummary.mode does not match candidate.",
            )
        confirmed_roles = _require_array(
            summary["confirmedRegionRoles"],
            f"{location}.deterministicSummary.confirmedRegionRoles",
            maximum=16,
        )
        validated_confirmed_roles: list[str] = []
        for role_index, role in enumerate(confirmed_roles):
            validated_confirmed_roles.append(
                _require_enum(
                role,
                {"Gameplay", "Presenter"},
                f"{location}.deterministicSummary.confirmedRegionRoles[{role_index}]",
            )
            )
        canonical_confirmed_roles = [
            role
            for role in ("Gameplay", "Presenter")
            if role in validated_confirmed_roles
        ]
        if (
            len(set(validated_confirmed_roles)) != len(validated_confirmed_roles)
            or validated_confirmed_roles != canonical_confirmed_roles
        ):
            _fail(
                UsageOrInputError,
                f"{location}.deterministicSummary.confirmedRegionRoles must be "
                "unique and canonically ordered.",
            )
        neighborhood = summary["eventNeighborhood"]
        if neighborhood is not None:
            neighborhood_object = _require_object(
                neighborhood,
                f"{location}.deterministicSummary.eventNeighborhood",
            )
            _require_exact_keys(
                neighborhood_object,
                {"startSeconds", "peakSeconds", "endSeconds"},
                f"{location}.deterministicSummary.eventNeighborhood",
            )
            neighborhood_start = _require_finite_decimal(
                neighborhood_object["startSeconds"],
                f"{location}.deterministicSummary.eventNeighborhood.startSeconds",
            )
            neighborhood_peak = _require_finite_decimal(
                neighborhood_object["peakSeconds"],
                f"{location}.deterministicSummary.eventNeighborhood.peakSeconds",
            )
            neighborhood_end = _require_finite_decimal(
                neighborhood_object["endSeconds"],
                f"{location}.deterministicSummary.eventNeighborhood.endSeconds",
            )
            if not (
                Decimal(0)
                <= neighborhood_start
                <= neighborhood_peak
                <= neighborhood_end
                <= video_duration
            ):
                _fail(
                    UsageOrInputError,
                    f"{location}.deterministicSummary event neighborhood is invalid.",
                )

    request["_validated"] = {
        "videoPath": video_path,
        "videoDuration": video_duration,
        "candidateStart": candidate_start,
        "candidateEnd": candidate_end,
        "sourceAbsoluteOffset": source_absolute_offset,
        "transcriptPolicy": transcript_policy,
        "expectedVideoHash": expected_video_hash,
        "expectedVideoLength": expected_length,
        "expectedLastWriteUtc": expected_last_write,
    }
    return request


def _validate_input_batch(value: Any) -> list[dict[str, Any]]:
    batch = _require_object(value, "$")
    _require_exact_keys(
        batch,
        {"schemaVersion", "prompt", "model", "videoPolicy", "requests"},
        "$",
    )
    if batch["schemaVersion"] != INPUT_SCHEMA:
        _fail(
            UsageOrInputError,
            f"Input schema must be '{INPUT_SCHEMA}'.",
        )

    _scan_forbidden_input_keys(batch)

    prompt = _require_object(batch["prompt"], "$.prompt")
    _require_exact_keys(
        prompt,
        {"schemaVersion", "name", "version", "text", "sha256", "frozenAtUtc"},
        "$.prompt",
    )
    if prompt["schemaVersion"] != PROMPT_MANIFEST_SCHEMA:
        _fail(UsageOrInputError, "Prompt manifest schema is unsupported.")
    if prompt["name"] != PROMPT_NAME or prompt["version"] != PROMPT_VERSION:
        _fail(UsageOrInputError, "Prompt identity does not match frozen prompt 1.0.")
    prompt_text, prompt_hash = _prompt_source()
    if prompt["text"] != prompt_text:
        _fail(UsageOrInputError, "Input prompt text differs from frozen prompt source.")
    if _require_sha256(prompt["sha256"], "$.prompt.sha256") != prompt_hash:
        _fail(UsageOrInputError, "Input prompt hash differs from frozen prompt source.")
    _require_utc_timestamp(prompt["frozenAtUtc"], "$.prompt.frozenAtUtc")

    model = _require_object(batch["model"], "$.model")
    _require_exact_keys(
        model,
        {"schemaVersion", "repositoryId", "revision", "manifestSha256"},
        "$.model",
    )
    if model["schemaVersion"] != MODEL_MANIFEST_SCHEMA:
        _fail(UsageOrInputError, "Model manifest schema is unsupported.")
    if (
        model["repositoryId"] != MODEL_REPOSITORY
        or model["revision"] != MODEL_REVISION
    ):
        _fail(
            UsageOrInputError,
            "Input model identity differs from the pinned official model.",
        )
    _require_sha256(model["manifestSha256"], "$.model.manifestSha256")
    video_policy = _validate_video_policy(batch["videoPolicy"])

    request_values = _require_array(
        batch["requests"],
        "$.requests",
        maximum=MAX_BATCH_CASES,
    )
    if not request_values:
        _fail(UsageOrInputError, "$.requests must contain at least one case.")

    media_hash_cache: dict[Path, str] = {}
    requests: list[dict[str, Any]] = []
    case_ids: set[str] = set()
    candidate_ids: set[str] = set()
    for index, request_value in enumerate(request_values):
        request = _validate_request(request_value, index, media_hash_cache)
        case_id = request["caseId"]
        candidate_id = request["candidate"]["id"]
        if case_id in case_ids:
            _fail(UsageOrInputError, f"Input contains duplicate case ID '{case_id}'.")
        if candidate_id in candidate_ids:
            _fail(
                UsageOrInputError,
                f"Input contains duplicate candidate ID '{candidate_id}'.",
            )
        case_ids.add(case_id)
        candidate_ids.add(candidate_id)
        request["_validated"]["videoPolicy"] = video_policy
        requests.append(request)
    return requests


def _context_for_model(request: dict[str, Any]) -> dict[str, Any]:
    validated = request["_validated"]
    transcript = request["transcript"]
    context: dict[str, Any] = {
        "caseId": request["caseId"],
        "candidateId": request["candidate"]["id"],
        "candidateMode": request["candidate"]["mode"],
        "reviewVideoDurationSeconds": float(validated["videoDuration"]),
        "candidateIntervalSeconds": {
            "start": float(validated["candidateStart"]),
            "end": float(validated["candidateEnd"]),
        },
        "composition": request["composition"],
        "transcript": {
            "policy": transcript["policy"],
            "evidenceStatus": transcript["evidenceStatus"],
            "spans": transcript["spans"],
            "accuracyWarning": transcript["accuracyWarning"],
        },
        "deterministicSummary": request["deterministicSummary"],
        "videoPolicy": validated["videoPolicy"],
    }
    return context


def _messages_for_request(
    request: dict[str, Any],
    prompt_text: str,
) -> list[dict[str, Any]]:
    video_path: Path = request["_validated"]["videoPath"]
    video_start = float(request["_validated"]["sourceAbsoluteOffset"])
    video_end = video_start + float(request["_validated"]["videoDuration"])
    context_json = json.dumps(
        _context_for_model(request),
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
        allow_nan=False,
    )
    return [
        {
            "role": "system",
            "content": [{"type": "text", "text": prompt_text}],
        },
        {
            "role": "user",
            "content": [
                {
                    "type": "video",
                    "video": str(video_path),
                    "max_pixels": VIDEO_MAX_PIXELS_PER_FRAME,
                    "total_pixels": VIDEO_TOTAL_PIXEL_BUDGET,
                    "fps": VIDEO_FPS,
                    "min_frames": VIDEO_MIN_FRAMES,
                    "max_frames": VIDEO_MAX_FRAMES,
                    "video_start": video_start,
                    "video_end": video_end,
                },
                {
                    "type": "text",
                    "text": (
                        "Analyze this case using the frozen instructions. "
                        "Case context JSON follows:\n" + context_json
                    ),
                },
            ],
        },
    ]



__all__ = [name for name in globals() if not name.startswith("__")]
