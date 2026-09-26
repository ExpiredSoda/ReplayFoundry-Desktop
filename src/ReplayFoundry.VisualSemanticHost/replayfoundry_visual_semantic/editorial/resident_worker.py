"""Private parent-owned stdio protocol. No listener, network port or shell."""
from __future__ import annotations

import contextlib
import io
import json
import os
import sys

from . import grounded_metadata_command as command

SCHEMA = "replayfoundry-editorial-worker-1"
MAXIMUM_REQUEST_BYTES = 131_072
MAXIMUM_DIAGNOSTIC_CHARS = 524_288
REQUEST_ENVIRONMENT = {
    "REPLAYFOUNDRY_GROUNDING_HANDOFF", "REPLAYFOUNDRY_GROUNDING_IMPORT_SHA256",
    "REPLAYFOUNDRY_WRITER_CAPTURE",
    "REPLAYFOUNDRY_WRITER_ROOT",
}


class BoundedDiagnostics(io.TextIOBase):
    def __init__(self):
        self.parts: list[str] = []
        self.length = 0

    def write(self, value: str) -> int:
        remaining = max(0, MAXIMUM_DIAGNOSTIC_CHARS - self.length)
        kept = value[:remaining]
        self.parts.append(kept) if kept else None
        self.length += len(kept)
        return len(value)

    def flush(self):
        pass

    def text(self) -> str:
        return "".join(self.parts)


def validate_request(value):
    if not isinstance(value, dict) or set(value) != {"schema", "id", "arguments", "environment"}:
        raise ValueError("Invalid worker request fields.")
    if value["schema"] != SCHEMA or not isinstance(value["id"], str) or len(value["id"]) != 32:
        raise ValueError("Invalid worker request identity.")
    arguments = value["arguments"]
    if (not isinstance(arguments, list) or not 2 <= len(arguments) <= 32
            or any(not isinstance(item, str) or len(item) > 32768 for item in arguments)
            or arguments[0] != "run-grounded-editorial-metadata-batch"):
        raise ValueError("Unsupported worker command.")
    environment = value["environment"]
    if (not isinstance(environment, dict) or not set(environment).issubset(REQUEST_ENVIRONMENT)
            or any(not isinstance(item, str) or len(item) > 32768 for item in environment.values())):
        raise ValueError("Unsupported worker environment.")
    return value


def main() -> int:
    from ..cli import main as run_command
    command._RETAIN_RUNTIME = True
    transport = sys.stdout
    try:
        while True:
            line = sys.stdin.buffer.readline(MAXIMUM_REQUEST_BYTES + 1)
            if not line:
                return 0
            if len(line) > MAXIMUM_REQUEST_BYTES or not line.endswith(b"\n"):
                return 2
            try:
                request = validate_request(json.loads(line))
            except (ValueError, UnicodeError):
                return 2
            # Clear absent flags so an earlier operation cannot contribute facts.
            for name in REQUEST_ENVIRONMENT:
                os.environ.pop(name, None)
            os.environ.update(request["environment"])
            diagnostic = BoundedDiagnostics()
            with contextlib.redirect_stdout(diagnostic), contextlib.redirect_stderr(diagnostic):
                try:
                    code = run_command(request["arguments"])
                except SystemExit:
                    code = 2
            if code != 0:
                command.close_resident_runtime()
            response = {"schema": SCHEMA, "id": request["id"], "exitCode": code,
                        "diagnostics": diagnostic.text()}
            transport.write(json.dumps(response, ensure_ascii=True, separators=(",", ":")) + "\n")
            transport.flush()
    finally:
        command.close_resident_runtime()
        command._RETAIN_RUNTIME = False
