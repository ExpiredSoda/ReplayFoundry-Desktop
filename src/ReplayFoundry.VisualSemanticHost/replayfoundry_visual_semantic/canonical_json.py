"""ReplayFoundry local visual-semantic host implementation module."""
from __future__ import annotations

from .failure_envelope import *  # noqa: F401,F403

# Prompt data is evidence, never an instruction channel.  Keep this projection
# filter here so every provider path can use it without adding another runtime
# module outside the attested host roster.  Normal evidence remains byte-for-
# byte unchanged; only control syntax and explicit instruction-like spans are
# replaced before chat-template rendering.
_PROMPT_INJECTION_MARKER = "[untrusted instruction-like text removed]"
_PROMPT_CONTROL_TOKENS = re.compile(
    r"(?i)<\|(?:im_start|im_end|system|assistant|user|developer|tool)[^>]*\|>"
    r"|\[/?INST\]|<<\/?SYS>>|<\/?(?:system|assistant|developer|tool)>"
)
_PROMPT_ROLE_LINE = re.compile(
    r"(?im)^[ \t]*(?:system|developer|assistant|tool|function)\s*:\s*"
)
_PROMPT_INSTRUCTION_SPAN = re.compile(
    r"(?i)\b(?:ignore|disregard|override|forget)\s+(?:all\s+)?"
    r"(?:previous|prior|above|system|developer)\s+"
    r"(?:instructions?|messages?|prompts?)\b"
    r"|\b(?:reveal|print|repeat|return|show)\s+(?:the\s+)?"
    r"(?:system|developer)\s+(?:prompt|message|instructions?)\b"
    r"|\b(?:follow|obey|execute)\s+(?:these|the\s+following|my)\s+"
    r"(?:instructions?|commands?)\b"
)
_UNSAFE_PROMPT_CODEPOINTS = frozenset(
    list(range(0x00, 0x09))
    + [0x0B, 0x0C]
    + list(range(0x0E, 0x20))
    + list(range(0x7F, 0xA0))
    + [0x200B, 0x200C, 0x200D, 0x2060, 0xFEFF]
    + list(range(0x202A, 0x202F))
    + list(range(0x2066, 0x206A))
)


def _sanitize_untrusted_prompt_text(value: str) -> str:
    """Remove instruction-channel syntax from one untrusted evidence value."""
    text = "".join(
        " " if ord(character) in _UNSAFE_PROMPT_CODEPOINTS else character
        for character in value
    )
    text = _PROMPT_CONTROL_TOKENS.sub(_PROMPT_INJECTION_MARKER, text)
    text = _PROMPT_ROLE_LINE.sub(_PROMPT_INJECTION_MARKER + " ", text)
    return _PROMPT_INSTRUCTION_SPAN.sub(_PROMPT_INJECTION_MARKER, text)


def _secure_model_messages(
    messages: list[dict[str, Any]],
) -> list[dict[str, Any]]:
    """Copy messages and contain every non-system text item as untrusted data."""
    secured = copy.deepcopy(messages)
    for message in secured:
        if message.get("role") == "system":
            continue
        content = message.get("content")
        if isinstance(content, str):
            message["content"] = _sanitize_untrusted_prompt_text(content)
            continue
        if not isinstance(content, list):
            continue
        for item in content:
            if isinstance(item, dict) and item.get("type") == "text":
                text = item.get("text")
                if isinstance(text, str):
                    item["text"] = _sanitize_untrusted_prompt_text(text)
    return secured

def _reject_duplicate_json_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            _fail(UsageOrInputError, f"JSON contains duplicate property '{key}'.")
        result[key] = value
    return result


def _reject_json_constant(value: str) -> NoReturn:
    _fail(
        UsageOrInputError,
        f"JSON contains unsupported non-finite token '{value}'.",
    )


def _load_strict_json(path: Path) -> Any:
    try:
        size = path.stat().st_size
    except OSError as error:
        _fail(UsageOrInputError, f"Could not inspect JSON file '{path}': {error}")

    if size <= 0:
        _fail(UsageOrInputError, f"JSON file '{path}' is empty.")
    if size > MAX_INPUT_JSON_BYTES:
        _fail(
            UsageOrInputError,
            f"JSON file '{path}' exceeds the {MAX_INPUT_JSON_BYTES}-byte limit.",
        )

    try:
        text = path.read_text(encoding="utf-8")
        return json.loads(
            text,
            object_pairs_hook=_reject_duplicate_json_keys,
            parse_constant=_reject_json_constant,
        )
    except UnicodeError as error:
        _fail(UsageOrInputError, f"JSON file '{path}' is not valid UTF-8: {error}")
    except json.JSONDecodeError as error:
        _fail(
            UsageOrInputError,
            f"JSON file '{path}' is invalid at line {error.lineno}, "
            f"column {error.colno}: {error.msg}",
        )


def _require_object(value: Any, location: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        _fail(UsageOrInputError, f"{location} must be a JSON object.")
    return value


def _require_exact_keys(
    value: dict[str, Any],
    expected: set[str],
    location: str,
) -> None:
    actual = set(value)
    missing = sorted(expected - actual)
    extra = sorted(actual - expected)
    if missing or extra:
        details: list[str] = []
        if missing:
            details.append(f"missing: {', '.join(missing)}")
        if extra:
            details.append(f"unexpected: {', '.join(extra)}")
        _fail(
            UsageOrInputError,
            f"{location} has an invalid property set ({'; '.join(details)}).",
        )


def _require_array(
    value: Any,
    location: str,
    *,
    maximum: int | None = None,
) -> list[Any]:
    if not isinstance(value, list):
        _fail(UsageOrInputError, f"{location} must be a JSON array.")
    if maximum is not None and len(value) > maximum:
        _fail(
            UsageOrInputError,
            f"{location} contains {len(value)} entries; maximum is {maximum}.",
        )
    return value


def _require_string(
    value: Any,
    location: str,
    *,
    allow_empty: bool = False,
    maximum: int | None = None,
) -> str:
    if not isinstance(value, str):
        _fail(UsageOrInputError, f"{location} must be a string.")
    if not allow_empty and not value.strip():
        _fail(UsageOrInputError, f"{location} must not be blank.")
    if maximum is not None and len(value) > maximum:
        _fail(
            UsageOrInputError,
            f"{location} exceeds the {maximum}-character limit.",
        )
    return value


def _require_optional_string(
    value: Any,
    location: str,
    *,
    maximum: int,
) -> str | None:
    if value is None:
        return None
    return _require_string(value, location, allow_empty=False, maximum=maximum)


def _require_collection_output_string(
    value: Any,
    location: str,
    *,
    maximum: int,
) -> str:
    if not isinstance(value, str):
        _fail(UsageOrInputError, f"{location} must be a string.")
    trimmed = value.strip()
    if not trimmed:
        _fail(UsageOrInputError, f"{location} must not be blank.")
    if len(trimmed) > maximum:
        _fail(
            UsageOrInputError,
            f"{location} exceeds the {maximum}-character limit.",
        )
    return trimmed


def _require_exact_semantic_string(
    value: Any,
    location: str,
    *,
    maximum: int,
) -> str:
    text = _require_string(
        value,
        location,
        allow_empty=False,
        maximum=maximum,
    )
    if text != text.strip():
        _fail(
            UsageOrInputError,
            f"{location} must not contain surrounding whitespace.",
        )
    return text


def _require_optional_exact_semantic_string(
    value: Any,
    location: str,
    *,
    maximum: int,
) -> str | None:
    if value is None:
        return None
    return _require_exact_semantic_string(
        value,
        location,
        maximum=maximum,
    )


def _require_id(value: Any, location: str) -> str:
    identifier = _require_string(value, location, maximum=128)
    if not SAFE_ID_PATTERN.fullmatch(identifier):
        _fail(
            UsageOrInputError,
            f"{location} contains characters outside the stable identifier policy.",
        )
    return identifier


def _require_sha256(value: Any, location: str) -> str:
    text = _require_string(value, location)
    if not SHA256_PATTERN.fullmatch(text):
        _fail(UsageOrInputError, f"{location} must be a 64-digit SHA-256 value.")
    return text.lower()


def _require_enum(value: Any, allowed: set[str], location: str) -> str:
    text = _require_string(value, location)
    if text not in allowed:
        _fail(
            UsageOrInputError,
            f"{location} has unsupported value '{text}'.",
        )
    return text


def _require_nonnegative_integer(value: Any, location: str) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or value < 0:
        _fail(UsageOrInputError, f"{location} must be a nonnegative integer.")
    return value


def _require_finite_decimal(value: Any, location: str) -> Decimal:
    if isinstance(value, bool) or not isinstance(value, (int, float, Decimal)):
        _fail(UsageOrInputError, f"{location} must be a finite number.")
    try:
        number = Decimal(str(value))
    except InvalidOperation:
        _fail(UsageOrInputError, f"{location} must be a finite number.")
    if not number.is_finite():
        _fail(UsageOrInputError, f"{location} must be a finite number.")
    return number


def _require_utc_timestamp(value: Any, location: str) -> datetime:
    text = _require_string(value, location)
    normalized = text[:-1] + "+00:00" if text.endswith("Z") else text
    try:
        timestamp = datetime.fromisoformat(normalized)
    except ValueError:
        _fail(UsageOrInputError, f"{location} must be an ISO-8601 UTC timestamp.")
    if timestamp.tzinfo is None or timestamp.utcoffset() != timezone.utc.utcoffset(None):
        _fail(UsageOrInputError, f"{location} must have a UTC offset.")
    return timestamp.astimezone(timezone.utc)


def _sha256_file(
    path: Path,
    *,
    error_type: type[HostError] = UsageOrInputError,
) -> str:
    digest = hashlib.sha256()
    try:
        with path.open("rb") as stream:
            while True:
                block = stream.read(4 * 1024 * 1024)
                if not block:
                    break
                digest.update(block)
    except OSError as error:
        _fail(error_type, f"Could not hash '{path}': {error}")
    return digest.hexdigest()


def _canonical_json_sha256(value: Any) -> str:
    try:
        encoded = json.dumps(
            value,
            ensure_ascii=False,
            sort_keys=True,
            separators=(",", ":"),
            allow_nan=False,
        ).encode("utf-8", errors="strict")
    except (TypeError, ValueError, UnicodeError) as error:
        _fail(OutputError, f"Could not hash canonical audit identity: {error}")
    return hashlib.sha256(encoded).hexdigest()


def _prompt_source() -> tuple[str, str]:
    prompt_path = HOST_DIRECTORY / PROMPT_FILE_NAME
    try:
        prompt_text = prompt_path.read_text(encoding="utf-8").strip()
    except (OSError, UnicodeError) as error:
        _fail(InitializationError, f"Could not read the frozen prompt: {error}")

    actual_hash = hashlib.sha256(prompt_text.encode("utf-8")).hexdigest()
    if actual_hash != PROMPT_SHA256:
        _fail(
            InitializationError,
            "The frozen prompt source hash does not match the host constant.",
        )
    return prompt_text, actual_hash


def _normalization_policy_source() -> tuple[str, str]:
    policy_path = HOST_DIRECTORY / NORMALIZATION_POLICY_FILE_NAME
    try:
        policy_bytes = policy_path.read_bytes()
        policy_text = policy_bytes.decode(
            "utf-8",
            errors="strict",
        ).strip()
    except (OSError, UnicodeError) as error:
        _fail(
            InitializationError,
            f"Could not read the frozen output normalization policy: {error}",
        )

    actual_hash = hashlib.sha256(policy_bytes).hexdigest()
    if actual_hash != NORMALIZATION_POLICY_SHA256:
        _fail(
            InitializationError,
            "The frozen output normalization policy hash does not match "
            "the host constant.",
        )
    return policy_text, actual_hash


def _generation_policy_source() -> tuple[str, str]:
    policy_path = HOST_DIRECTORY / GENERATION_POLICY_FILE_NAME
    try:
        policy_bytes = policy_path.read_bytes()
        policy_text = policy_bytes.decode(
            "utf-8",
            errors="strict",
        ).strip()
    except (OSError, UnicodeError) as error:
        _fail(
            InitializationError,
            f"Could not read the frozen generation policy: {error}",
        )

    actual_hash = hashlib.sha256(policy_bytes).hexdigest()
    if actual_hash != GENERATION_POLICY_SHA256:
        _fail(
            InitializationError,
            "The frozen generation policy hash does not match the host "
            "constant.",
        )
    return policy_text, actual_hash


def _identity_binding_policy_source() -> tuple[str, str]:
    policy_path = HOST_DIRECTORY / IDENTITY_BINDING_POLICY_FILE_NAME
    try:
        policy_bytes = policy_path.read_bytes()
        policy_text = policy_bytes.decode(
            "utf-8",
            errors="strict",
        ).strip()
    except (OSError, UnicodeError) as error:
        _fail(
            InitializationError,
            f"Could not read the frozen trusted identity-binding policy: "
            f"{error}",
        )

    actual_hash = hashlib.sha256(policy_bytes).hexdigest()
    if actual_hash != IDENTITY_BINDING_POLICY_SHA256:
        _fail(
            InitializationError,
            "The frozen trusted identity-binding policy hash does not "
            "match the host constant.",
        )
    return policy_text, actual_hash



__all__ = [name for name in globals() if not name.startswith("__")]
