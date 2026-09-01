"""ReplayFoundry local visual-semantic host implementation module."""
from __future__ import annotations

from .artifact_writer import *  # noqa: F401,F403

def _failure_payload(
    command: str,
    error_code: str,
    exit_code: int,
    message: str,
) -> dict[str, Any]:
    return {
        "schemaVersion": FAILURE_SCHEMA,
        "hostVersion": HOST_VERSION,
        "command": command,
        "stage": _FAILURE_CONTEXT["stage"],
        "case": copy.deepcopy(_FAILURE_CONTEXT["case"]),
        "videoArtifact": copy.deepcopy(_FAILURE_CONTEXT["videoArtifact"]),
        "timing": copy.deepcopy(_FAILURE_CONTEXT["timing"]),
        "sampling": copy.deepcopy(_FAILURE_CONTEXT["sampling"]),
        "identity": copy.deepcopy(_FAILURE_CONTEXT["identity"]),
        "generation": copy.deepcopy(_FAILURE_CONTEXT["generation"]),
        "generationWatchdog": copy.deepcopy(
            _FAILURE_CONTEXT["generationWatchdog"]
        ),
        "groundedMemoryPolicy": copy.deepcopy(
            _FAILURE_CONTEXT["groundedMemoryPolicy"]
        ),
        "recoveryPoolLedger": copy.deepcopy(
            _FAILURE_CONTEXT["recoveryPoolLedger"]
        ),
        "failure": {
            "errorCode": error_code,
            "exitCode": exit_code,
            "message": _bounded_failure_message(message),
        },
        "createdAtUtc": (
            datetime.now(timezone.utc)
            .isoformat(timespec="milliseconds")
            .replace("+00:00", "Z")
        ),
        "diagnostics": list(_FAILURE_CONTEXT["diagnostics"]),
    }


def _try_write_failure_output(
    path: Path | None,
    command: str,
    error_code: str,
    exit_code: int,
    message: str,
) -> None:
    if path is None or not _FAILURE_CONTEXT["failureOutputApproved"]:
        return
    try:
        _write_json_atomic(
            path,
            _failure_payload(command, error_code, exit_code, message),
        )
    except Exception as failure_error:
        print(
            json.dumps(
                {
                    "errorCode": "FailureArtifactWriteFailed",
                    "message": (
                        f"{type(failure_error).__name__}: {failure_error}"
                    ),
                },
                ensure_ascii=False,
                separators=(",", ":"),
            ),
            file=sys.stderr,
        )



__all__ = [name for name in globals() if not name.startswith("__")]
