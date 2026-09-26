"""ReplayFoundry local visual-semantic host implementation module."""
from __future__ import annotations

from .media_integrity import *  # noqa: F401,F403

PROHIBITED_REASONING_TEXT_FRAGMENTS = (
    "chain of thought",
    "chain-of-thought",
    "step-by-step reasoning",
    "step by step reasoning",
    "my hidden reasoning",
    "internal reasoning",
    "hidden analysis",
    "private scratchpad",
)

PROHIBITED_REASONING_PROPERTY_KEYS = {
    "chainofthought",
    "reasoningsteps",
    "hiddenreasoning",
    "reasoning",
    "thinking",
    "scratchpad",
    "deliberation",
    "internalanalysis",
    "analysis",
    "thoughts",
}


def _reject_chain_of_thought_content(value: Any, location: str) -> None:
    if isinstance(value, str):
        lowered = value.casefold()
        if any(
            fragment in lowered
            for fragment in PROHIBITED_REASONING_TEXT_FRAGMENTS
        ):
            _fail(
                InferenceError,
                f"Provider output exposes prohibited reasoning at {location}.",
            )
    elif isinstance(value, list):
        for index, child in enumerate(value):
            _reject_chain_of_thought_content(child, f"{location}[{index}]")
    elif isinstance(value, dict):
        for key, child in value.items():
            normalized_key = re.sub(r"[^a-z0-9]", "", key.casefold())
            if normalized_key in PROHIBITED_REASONING_PROPERTY_KEYS:
                _fail(
                    InferenceError,
                    f"Provider output contains prohibited property '{key}'.",
                )
            _reject_chain_of_thought_content(child, f"{location}.{key}")


def _reject_chain_of_thought_text(raw_text: str) -> None:
    lowered = raw_text.casefold()
    if any(
        fragment in lowered
        for fragment in PROHIBITED_REASONING_TEXT_FRAGMENTS
    ):
        _fail(
            InferenceError,
            "Provider output exposes prohibited reasoning text.",
        )

    # Raw-audit diagnostics may intentionally retain malformed JSON. Inspect
    # every complete JSON-like property key visible before the malformed
    # boundary so a prohibited reasoning property cannot bypass the safety
    # gate merely because a later string was truncated.
    key_pattern = re.compile(
        r'"(?P<key>[^"\\]*(?:\\.[^"\\]*)*)"\s*:',
    )
    for match in key_pattern.finditer(raw_text):
        try:
            key = json.loads(f'"{match.group("key")}"')
        except (json.JSONDecodeError, TypeError):
            continue
        normalized_key = re.sub(
            r"[^a-z0-9]",
            "",
            str(key).casefold(),
        )
        if normalized_key in PROHIBITED_REASONING_PROPERTY_KEYS:
            _fail(
                InferenceError,
                f"Provider output contains prohibited property '{key}'.",
            )


def _provider_output_text_safety_gate(
    raw_text: str,
) -> tuple[bytes, str]:
    if not isinstance(raw_text, str):
        _fail(InferenceError, "Provider output must be decoded text.")
    try:
        raw_bytes = raw_text.encode("utf-8", errors="strict")
    except UnicodeEncodeError:
        _fail(
            InferenceError,
            "Provider output is not valid strict UTF-8 text.",
        )
    if len(raw_bytes) > MAX_RAW_AUDIT_TEXT_BYTES:
        _fail(
            InferenceError,
            "Provider output exceeds the bounded raw-audit text limit.",
        )

    stripped = raw_text.strip()
    if not stripped:
        _fail(InferenceError, "Provider returned an empty observation.")
    if stripped.startswith("```") or stripped.endswith("```"):
        _fail(InferenceError, "Provider returned markdown instead of bare JSON.")
    _reject_chain_of_thought_text(stripped)
    return raw_bytes, stripped


def _provider_output_safety_gate(
    raw_text: str,
) -> tuple[bytes, dict[str, Any]]:
    raw_bytes, stripped = _provider_output_text_safety_gate(raw_text)

    try:
        value = json.loads(
            stripped,
            object_pairs_hook=_reject_duplicate_json_keys,
            parse_constant=_reject_json_constant,
        )
    except HostError:
        raise
    except json.JSONDecodeError as error:
        _fail(
            InferenceError,
            f"Provider returned invalid JSON at line {error.lineno}, "
            f"column {error.colno}: {error.msg}",
        )
    observation = _require_object(value, "provider observation")
    _reject_chain_of_thought_content(observation, "provider observation")
    return raw_bytes, observation


def _unterminated_json_string_reaches_boundary(
    value: str,
    start_index: int,
) -> bool:
    if (
        start_index < 0
        or start_index >= len(value)
        or value[start_index] != '"'
    ):
        return False
    escaped = False
    for character in value[start_index + 1:]:
        if escaped:
            escaped = False
            continue
        if character == "\\":
            escaped = True
            continue
        if character == '"':
            return False
        if ord(character) < 0x20:
            return False
    return True


def _parse_provider_json_for_audit(
    stripped: str,
) -> tuple[Any | None, dict[str, Any]]:
    try:
        value = json.loads(
            stripped,
            object_pairs_hook=_reject_duplicate_json_keys,
            parse_constant=_reject_json_constant,
        )
    except json.JSONDecodeError as error:
        return None, {
            "succeeded": False,
            "line": error.lineno,
            "column": error.colno,
            "message": error.msg,
            "failureAtGeneratedTextBoundary": (
                error.msg == "Unterminated string starting at"
                and _unterminated_json_string_reaches_boundary(
                    stripped,
                    error.pos,
                )
            ),
        }
    except HostError as error:
        return None, {
            "succeeded": False,
            "line": None,
            "column": None,
            "message": str(error),
            "failureAtGeneratedTextBoundary": False,
        }
    return value, {
        "succeeded": True,
        "line": None,
        "column": None,
        "message": None,
        "failureAtGeneratedTextBoundary": None,
    }



__all__ = [name for name in globals() if not name.startswith("__")]
