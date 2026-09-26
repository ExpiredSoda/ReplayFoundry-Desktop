"""ReplayFoundry local visual-semantic host implementation module."""
from __future__ import annotations

from .canonical_json import *  # noqa: F401,F403

def _scan_forbidden_input_keys(value: Any, location: str = "$") -> None:
    if isinstance(value, dict):
        for key, child in value.items():
            normalized = re.sub(r"[^a-z0-9]", "", key.lower())
            if normalized in FORBIDDEN_INPUT_KEYS:
                _fail(
                    UsageOrInputError,
                    f"Blinded provider input contains forbidden property "
                    f"'{key}' at {location}.",
                )
            _scan_forbidden_input_keys(child, f"{location}.{key}")
    elif isinstance(value, list):
        for index, child in enumerate(value):
            _scan_forbidden_input_keys(child, f"{location}[{index}]")


def _validate_model_directory(model_path: Path) -> None:
    missing = sorted(
        name for name in REQUIRED_MODEL_FILES if not (model_path / name).is_file()
    )
    if missing:
        _fail(
            InitializationError,
            f"Model directory is incomplete; missing: {', '.join(missing)}.",
        )

    config_path = model_path / "config.json"
    config = _load_strict_json(config_path)
    config_object = _require_object(config, "model config")
    if config_object.get("model_type") != MODEL_TYPE:
        _fail(
            InitializationError,
            f"Model config must declare model_type '{MODEL_TYPE}'.",
        )
    architectures = config_object.get("architectures")
    if not isinstance(architectures, list) or architectures != [MODEL_ARCHITECTURE]:
        _fail(
            InitializationError,
            f"Model config must declare exactly '{MODEL_ARCHITECTURE}'.",
        )
    text_config = config_object.get("text_config")
    if not isinstance(text_config, dict) or text_config.get("dtype") != MODEL_DTYPE:
        _fail(
            InitializationError,
            f"Model config must declare BF16 text weights.",
        )

    model_index = _load_strict_json(model_path / "model.safetensors.index.json")
    model_index_object = _require_object(model_index, "model weight index")
    metadata = model_index_object.get("metadata")
    if (
        not isinstance(metadata, dict)
        or metadata.get("total_size") != 8_875_631_616
    ):
        _fail(
            InitializationError,
            "Model weight index does not match the pinned BF16 checkpoint size.",
        )

    for name, (expected_size, expected_hash) in EXPECTED_WEIGHT_FILES.items():
        path = model_path / name
        try:
            size = path.stat().st_size
        except OSError as error:
            _fail(InitializationError, f"Could not inspect model shard '{name}': {error}")
        if size != expected_size:
            _fail(
                InitializationError,
                f"Model shard '{name}' has size {size}; expected {expected_size}.",
            )
        actual_hash = _sha256_file(
            path,
            error_type=InitializationError,
        )
        if actual_hash != expected_hash:
            _fail(
                InitializationError,
                f"Model shard '{name}' does not match the pinned SHA-256.",
            )

    # A Hugging Face cache snapshot path provides an additional revision check.
    parts = model_path.parts
    if "snapshots" in parts:
        snapshot_index = parts.index("snapshots")
        if snapshot_index + 1 >= len(parts) or parts[snapshot_index + 1] != MODEL_REVISION:
            _fail(
                InitializationError,
                "Hugging Face snapshot path does not match the pinned revision.",
            )


def _package_version(distribution: str) -> str:
    try:
        return importlib.metadata.version(distribution)
    except importlib.metadata.PackageNotFoundError:
        _fail(
            InitializationError,
            f"Required Python distribution '{distribution}' is not installed.",
        )


def _validate_string_array(
    value: Any,
    location: str,
    *,
    maximum_count: int,
    maximum_length: int,
) -> list[str]:
    items = _require_array(value, location, maximum=maximum_count)
    result: list[str] = []
    for index, item in enumerate(items):
        result.append(
            _require_collection_output_string(
                item,
                f"{location}[{index}]",
                maximum=maximum_length,
            )
        )
    return result


def _ordinal_string_key(value: str) -> bytes:
    # Match .NET StringComparer.Ordinal, which compares UTF-16 code units.
    return value.encode("utf-16-be", errors="surrogatepass")


def _validate_geometry(value: Any, location: str) -> dict[str, Any]:
    geometry = _require_object(value, location)
    _require_exact_keys(geometry, {"x", "y", "width", "height"}, location)
    x = _require_finite_decimal(geometry["x"], f"{location}.x")
    y = _require_finite_decimal(geometry["y"], f"{location}.y")
    width = _require_finite_decimal(geometry["width"], f"{location}.width")
    height = _require_finite_decimal(geometry["height"], f"{location}.height")
    if x < 0 or y < 0 or width <= 0 or height <= 0:
        _fail(UsageOrInputError, f"{location} must have positive bounded geometry.")
    if x + width > 1 or y + height > 1:
        _fail(UsageOrInputError, f"{location} must remain inside normalized bounds.")
    return geometry


def _validate_video_policy(value: Any) -> dict[str, Any]:
    location = "$.videoPolicy"
    policy = _require_object(value, location)
    _require_exact_keys(
        policy,
        {
            "schemaVersion",
            "maximumReviewDurationSeconds",
            "maximumWidth",
            "maximumHeight",
            "maximumPixelsPerFrame",
            "minimumFrames",
            "maximumFrames",
            "maximumTotalPixels",
            "framesPerSecond",
            "audioSupplied",
            "videoBackend",
            "samplingPolicyVersion",
            "trimPolicyVersion",
        },
        location,
    )
    if policy["schemaVersion"] != VIDEO_POLICY_SCHEMA:
        _fail(UsageOrInputError, "Video policy schema is unsupported.")
    if (
        _require_finite_decimal(
            policy["maximumReviewDurationSeconds"],
            f"{location}.maximumReviewDurationSeconds",
        )
        != MAX_INPUT_DURATION_SECONDS
    ):
        _fail(
            UsageOrInputError,
            "Video policy maximumReviewDurationSeconds differs from the "
            "frozen host policy.",
        )

    expected_integers = {
        "maximumWidth": VIDEO_MAX_WIDTH,
        "maximumHeight": VIDEO_MAX_HEIGHT,
        "maximumPixelsPerFrame": VIDEO_MAX_PIXELS_PER_FRAME,
        "minimumFrames": VIDEO_MIN_FRAMES,
        "maximumFrames": VIDEO_MAX_FRAMES,
        "maximumTotalPixels": VIDEO_TOTAL_PIXEL_BUDGET,
    }
    for property_name, expected in expected_integers.items():
        if (
            _require_nonnegative_integer(
                policy[property_name],
                f"{location}.{property_name}",
            )
            != expected
        ):
            _fail(
                UsageOrInputError,
                f"Video policy {property_name} differs from the frozen host policy.",
            )

    if (
        _require_finite_decimal(
            policy["framesPerSecond"],
            f"{location}.framesPerSecond",
        )
        != Decimal(str(VIDEO_FPS))
    ):
        _fail(
            UsageOrInputError,
            "Video policy framesPerSecond differs from the frozen host policy.",
        )
    if policy["audioSupplied"] is not False:
        _fail(UsageOrInputError, "Video policy must declare audioSupplied=false.")
    if policy["videoBackend"] != VIDEO_BACKEND:
        _fail(
            UsageOrInputError,
            f"Video policy must use the {VIDEO_BACKEND} backend.",
        )
    if policy["samplingPolicyVersion"] != VIDEO_SAMPLING_POLICY:
        _fail(UsageOrInputError, "Video sampling policy version is unsupported.")
    if policy["trimPolicyVersion"] != VIDEO_TRIM_POLICY:
        _fail(UsageOrInputError, "Video trim policy version is unsupported.")
    return policy



__all__ = [name for name in globals() if not name.startswith("__")]
