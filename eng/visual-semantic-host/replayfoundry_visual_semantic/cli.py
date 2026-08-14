"""ReplayFoundry local visual-semantic host implementation module."""
from __future__ import annotations

from pathlib import Path
import traceback

from .commands import *  # noqa: F401,F403
from .editorial.structured_decoding_capability import (
    verify_structured_decoding,
)
from .editorial.qualified_batch_command import run_qualified_editorial_batch
from .editorial.grounded_metadata_command import (
    run_grounded_editorial_metadata_batch,
)


def _unexpected_failure_message(error: Exception) -> str:
    frames = traceback.extract_tb(error.__traceback__)[-8:]
    trace = " > ".join(
        f"{Path(frame.filename).name}:{frame.lineno}:{frame.name}"
        for frame in frames
    )
    message = f"{type(error).__name__}: {error}"
    return f"{message} [trace: {trace}]" if trace else message

def _build_parser(
    production_only: bool = False,
) -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Offline Qwen3-VL batch host for Replay Foundry."
        )
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    capability = subparsers.add_parser(
        "verify-editorial-structured-decoding",
        help=(
            "Verify the pinned Prompt 2.3 XGrammar boundary without "
            "loading model weights."
        ),
    )
    capability.add_argument("--model", required=True)
    capability.add_argument("--output", required=True)
    capability.add_argument("--environment-output", required=True)
    capability.add_argument("--qualification-lock-output", required=True)

    qualified = subparsers.add_parser(
        "run-qualified-editorial-batch",
        help="Run a bounded qualified Qwen observation batch for Thorough.",
    )
    qualified.add_argument("--model", required=True)
    qualified.add_argument("--input", required=True)
    qualified.add_argument("--qualification-lock", required=True)
    qualified.add_argument("--video-backend", required=True)
    qualified.add_argument("--ffmpeg-shared-library-dir", required=True)
    qualified.add_argument("--attempt-output", required=True)
    qualified.add_argument("--output", required=True)
    qualified.add_argument("--failure-output")

    grounded_metadata = subparsers.add_parser(
        "run-grounded-editorial-metadata-batch",
        help=(
            "Generate a bounded batch of grounded clip titles, "
            "descriptions, and tags with the qualified local Qwen runtime."
        ),
    )
    grounded_metadata.add_argument("--model", required=True)
    grounded_metadata.add_argument("--input", required=True)
    grounded_metadata.add_argument("--qualification-lock", required=True)
    grounded_metadata.add_argument("--video-backend", required=True)
    grounded_metadata.add_argument(
        "--ffmpeg-shared-library-dir",
        required=True,
    )
    grounded_metadata.add_argument("--output", required=True)
    grounded_metadata.add_argument("--failure-output")

    if not production_only:
        _add_development_parsers(subparsers)
    return parser


def _add_development_parsers(subparsers: Any) -> None:
    probe = subparsers.add_parser(
        "probe",
        help="Verify the pinned local environment/model without loading weights.",
    )
    probe.add_argument("--model", required=True)
    probe.add_argument("--input", required=True)
    probe.add_argument("--video-backend", required=True)
    probe.add_argument("--ffmpeg-shared-library-dir", required=True)
    probe.add_argument("--output", required=True)
    probe.add_argument("--failure-output")

    run = subparsers.add_parser(
        "run",
        help="Run one strict development batch.",
    )
    run.add_argument("--model", required=True)
    run.add_argument("--input", required=True)
    run.add_argument("--video-backend", required=True)
    run.add_argument("--ffmpeg-shared-library-dir", required=True)
    run.add_argument("--attempt-output", required=True)
    run.add_argument("--output", required=True)
    run.add_argument("--raw-audit-output")
    run.add_argument("--failure-output")

    specifications = (
        (
            "run-editorial-development",
            "Run the frozen editorial development plan.",
            False,
        ),
        (
            "run-editorial-contract-pilot",
            "Run the frozen editorial contract pilot.",
            False,
        ),
        (
            "run-editorial-constrained-contract-pilot",
            "Run the schema-constrained contract pilot.",
            True,
        ),
        (
            "run-editorial-constrained-development",
            "Run the schema-constrained development plan.",
            True,
        ),
    )
    for command, help_text, requires_lock in specifications:
        development = subparsers.add_parser(command, help=help_text)
        development.add_argument("--model", required=True)
        development.add_argument("--input", required=True)
        if requires_lock:
            development.add_argument("--qualification-lock", required=True)
        development.add_argument("--video-backend", required=True)
        development.add_argument(
            "--ffmpeg-shared-library-dir",
            required=True,
        )
        development.add_argument("--attempt-output", required=True)
        development.add_argument("--output", required=True)
        development.add_argument("--failure-output")

    sampling_audit = subparsers.add_parser(
        "audit-video-sampling",
        help="Audit the qualification sampling path without loading a model.",
    )
    sampling_audit.add_argument("--input", required=True)
    sampling_audit.add_argument("--video-backend", required=True)
    sampling_audit.add_argument(
        "--ffmpeg-shared-library-dir",
        required=True,
    )
    sampling_audit.add_argument("--output", required=True)
    sampling_audit.add_argument("--failure-output")


def main(
    argv: list[str] | None = None,
    *,
    production_only: bool = False,
) -> int:
    failure_output_path: Path | None = None
    command = "unknown"
    _reset_failure_context()
    try:
        arguments = _build_parser(production_only).parse_args(argv)
        command = arguments.command
        _reset_failure_context(command)
        if command == "verify-editorial-structured-decoding":
            _set_failure_stage("PathValidation")
            model_path = _require_absolute_external_path(
                arguments.model,
                "--model",
                must_exist=True,
                must_be_file=False,
            )
            capability_path = _require_absolute_external_path(
                arguments.output,
                "--output",
                must_exist=False,
            )
            environment_path = _require_absolute_external_path(
                arguments.environment_output,
                "--environment-output",
                must_exist=False,
            )
            qualification_lock_path = _require_absolute_external_path(
                arguments.qualification_lock_output,
                "--qualification-lock-output",
                must_exist=False,
            )
            outputs = {
                capability_path,
                environment_path,
                qualification_lock_path,
            }
            if len(outputs) != 3 or any(path.exists() for path in outputs):
                _fail(
                    OutputError,
                    "Structured-decoding verification outputs must be "
                    "distinct and must not already exist.",
                )
            for path, name in (
                (capability_path, "--output"),
                (environment_path, "--environment-output"),
                (
                    qualification_lock_path,
                    "--qualification-lock-output",
                ),
            ):
                _require_path_outside_roots(
                    path,
                    [
                        ("model directory", model_path),
                        ("Python environment", Path(sys.prefix)),
                        ("host-script directory", HOST_DIRECTORY),
                    ],
                    name,
                )
            verify_structured_decoding(
                model_path,
                capability_path,
                environment_path,
                qualification_lock_path,
            )
            return 0
        if arguments.video_backend != VIDEO_BACKEND:
            _fail(
                UsageOrInputError,
                f"--video-backend must be exactly '{VIDEO_BACKEND}'.",
            )
        if os.environ.get("FORCE_QWENVL_VIDEO_READER") != VIDEO_BACKEND:
            _fail(
                InitializationError,
                "FORCE_QWENVL_VIDEO_READER was not set before Qwen imports.",
            )

        _set_failure_stage("PathValidation")
        model_path: Path | None = None
        if arguments.command in {
            "probe",
            "run",
            "run-editorial-development",
            "run-editorial-contract-pilot",
            "run-editorial-constrained-contract-pilot",
            "run-editorial-constrained-development",
            "run-qualified-editorial-batch",
            "run-grounded-editorial-metadata-batch",
        }:
            model_path = _require_absolute_external_path(
                arguments.model,
                "--model",
                must_exist=True,
                must_be_file=False,
            )
        output_path = _require_absolute_external_path(
            arguments.output,
            "--output",
            must_exist=False,
        )
        if output_path.exists():
            _fail(OutputError, f"Output already exists: '{output_path}'.")

        attempt_output_path = None
        attempt_output = getattr(arguments, "attempt_output", None)
        if attempt_output is not None:
            attempt_output_path = _require_absolute_external_path(
                attempt_output,
                "--attempt-output",
                must_exist=False,
            )
            if attempt_output_path.exists():
                _fail(
                    OutputError,
                    "Attempt output already exists: "
                    f"'{attempt_output_path}'.",
                )

        failure_output = getattr(arguments, "failure_output", None)
        if failure_output is not None:
            failure_output_path = _require_absolute_external_path(
                failure_output,
                "--failure-output",
                must_exist=False,
            )
            if failure_output_path.exists():
                _fail(
                    OutputError,
                    "Failure output already exists: "
                    f"'{failure_output_path}'.",
                )

        raw_audit_output_path = None
        raw_audit_output = getattr(
            arguments,
            "raw_audit_output",
            None,
        )
        if raw_audit_output is not None:
            raw_audit_output_path = _require_absolute_external_path(
                raw_audit_output,
                "--raw-audit-output",
                must_exist=False,
            )
            if raw_audit_output_path.exists():
                _fail(
                    OutputError,
                    "Raw-audit output already exists: "
                    f"'{raw_audit_output_path}'.",
                )

        input_path = _require_absolute_external_path(
            arguments.input,
            "--input",
            must_exist=True,
            must_be_file=True,
        )
        ffmpeg_directory = _require_absolute_external_path(
            arguments.ffmpeg_shared_library_dir,
            "--ffmpeg-shared-library-dir",
            must_exist=True,
            must_be_file=False,
        )
        if input_path == output_path:
            _fail(UsageOrInputError, "--input and --output must differ.")
        if attempt_output_path is not None and (
            attempt_output_path == input_path
            or attempt_output_path == output_path
        ):
            _fail(
                UsageOrInputError,
                "--attempt-output must differ from --input and --output.",
            )

        qualification_lock_path = None
        qualification_lock = getattr(
            arguments,
            "qualification_lock",
            None,
        )
        if qualification_lock is not None:
            qualification_lock_path = _require_absolute_external_path(
                qualification_lock,
                "--qualification-lock",
                must_exist=True,
                must_be_file=True,
            )
            if qualification_lock_path in {
                input_path,
                output_path,
                attempt_output_path,
                failure_output_path,
            }:
                _fail(
                    UsageOrInputError,
                    "--qualification-lock must differ from all other "
                    "artifacts.",
                )
        if failure_output_path is not None and (
            failure_output_path == input_path
            or failure_output_path == output_path
            or failure_output_path == attempt_output_path
        ):
            _fail(
                UsageOrInputError,
                "--failure-output must differ from all primary artifacts.",
            )
        if raw_audit_output_path is not None and (
            raw_audit_output_path == input_path
            or raw_audit_output_path == output_path
            or raw_audit_output_path == attempt_output_path
        ):
            _fail(
                UsageOrInputError,
                "--raw-audit-output must differ from all primary artifacts.",
            )
        if (
            failure_output_path is not None
            and raw_audit_output_path is not None
            and failure_output_path == raw_audit_output_path
        ):
            _fail(
                UsageOrInputError,
                "--failure-output and --raw-audit-output must differ.",
            )
        if attempt_output_path is not None:
            attempt_protected_roots = [
                (
                    "shared FFmpeg directory",
                    ffmpeg_directory,
                ),
                (
                    "Python environment",
                    Path(sys.prefix),
                ),
                (
                    "host-script directory",
                    HOST_DIRECTORY,
                ),
            ]
            if model_path is not None:
                attempt_protected_roots.append(
                    ("model directory", model_path)
                )
            _require_path_outside_roots(
                attempt_output_path,
                attempt_protected_roots,
                "--attempt-output",
            )
        if raw_audit_output_path is not None:
            _require_path_outside_roots(
                raw_audit_output_path,
                [
                    ("model directory", model_path),
                    (
                        "shared FFmpeg directory",
                        ffmpeg_directory,
                    ),
                    (
                        "Python environment",
                        Path(sys.prefix),
                    ),
                    (
                        "host-script directory",
                        HOST_DIRECTORY,
                    ),
                    (
                        "input-artifact directory",
                        input_path.parent,
                    ),
                ],
                "--raw-audit-output",
            )

        if failure_output_path is not None:
            protected_roots = [
                (
                    "shared FFmpeg directory",
                    ffmpeg_directory,
                ),
                (
                    "Python environment",
                    Path(sys.prefix),
                ),
                (
                    "host-script directory",
                    HOST_DIRECTORY,
                ),
            ]
            if model_path is not None:
                protected_roots.append(("model directory", model_path))
            _require_path_outside_roots(
                failure_output_path,
                protected_roots,
                "--failure-output",
            )

        _set_failure_stage("LibraryConfiguration")
        dll_cookie, original_path = (
            _configure_ffmpeg_shared_library_directory(
                ffmpeg_directory
            )
        )
        try:
            if arguments.command == "probe":
                assert model_path is not None
                _probe(
                    model_path,
                    input_path,
                    output_path,
                    ffmpeg_directory,
                    failure_output_path,
                )
            elif arguments.command == "run":
                assert model_path is not None
                assert attempt_output_path is not None
                _run(
                    model_path,
                    input_path,
                    output_path,
                    attempt_output_path,
                    ffmpeg_directory,
                    raw_audit_output_path,
                    failure_output_path,
                )
            elif arguments.command == "audit-video-sampling":
                _audit_video_sampling(
                    input_path,
                    output_path,
                    ffmpeg_directory,
                    failure_output_path,
                )
            elif arguments.command == "run-editorial-development":
                from .editorial.development_command import (
                    run_editorial_development,
                )

                assert model_path is not None
                assert attempt_output_path is not None
                run_editorial_development(
                    model_path,
                    input_path,
                    output_path,
                    attempt_output_path,
                    ffmpeg_directory,
                    failure_output_path,
                )
            elif arguments.command == "run-editorial-contract-pilot":
                from .editorial.pilot_command import (
                    run_editorial_contract_pilot,
                )

                assert model_path is not None
                assert attempt_output_path is not None
                run_editorial_contract_pilot(
                    model_path,
                    input_path,
                    output_path,
                    attempt_output_path,
                    ffmpeg_directory,
                    failure_output_path,
                )
            elif (
                arguments.command
                == "run-editorial-constrained-contract-pilot"
            ):
                from .editorial.constrained_pilot_command import (
                    run_editorial_constrained_contract_pilot,
                )

                assert model_path is not None
                assert attempt_output_path is not None
                assert qualification_lock_path is not None
                run_editorial_constrained_contract_pilot(
                    model_path,
                    input_path,
                    output_path,
                    attempt_output_path,
                    qualification_lock_path,
                    ffmpeg_directory,
                    failure_output_path,
                )
            elif (
                arguments.command
                == "run-editorial-constrained-development"
            ):
                from .editorial.constrained_development_command import (
                    run_editorial_constrained_development,
                )

                assert model_path is not None
                assert attempt_output_path is not None
                assert qualification_lock_path is not None
                run_editorial_constrained_development(
                    model_path,
                    input_path,
                    output_path,
                    attempt_output_path,
                    qualification_lock_path,
                    ffmpeg_directory,
                    failure_output_path,
                )
            elif arguments.command == "run-qualified-editorial-batch":
                assert model_path is not None
                assert attempt_output_path is not None
                assert qualification_lock_path is not None
                run_qualified_editorial_batch(
                    model_path,
                    input_path,
                    output_path,
                    attempt_output_path,
                    qualification_lock_path,
                    ffmpeg_directory,
                    failure_output_path,
                )
            elif (
                arguments.command
                == "run-grounded-editorial-metadata-batch"
            ):
                assert model_path is not None
                assert qualification_lock_path is not None
                run_grounded_editorial_metadata_batch(
                    model_path,
                    input_path,
                    output_path,
                    qualification_lock_path,
                    ffmpeg_directory,
                    failure_output_path,
                )
            else:
                _fail(UsageOrInputError, "Unsupported command.")
        finally:
            _restore_process_library_path(
                dll_cookie,
                original_path,
            )
        return 0
    except KeyboardInterrupt:
        _try_write_failure_output(
            failure_output_path,
            command,
            "Cancelled",
            130,
            "Visual-semantic host was cancelled.",
        )
        print(
            json.dumps(
                {
                    "errorCode": "Cancelled",
                    "message": "Visual-semantic host was cancelled.",
                },
                separators=(",", ":"),
            ),
            file=sys.stderr,
        )
        return 130
    except HostError as error:
        _add_failure_diagnostic(
            f"{type(error).__name__}: {error}"
        )
        _try_write_failure_output(
            failure_output_path,
            command,
            type(error).__name__,
            error.exit_code,
            str(error),
        )
        print(
            json.dumps(
                {
                    "errorCode": type(error).__name__,
                    "message": str(error),
                },
                ensure_ascii=False,
                separators=(",", ":"),
            ),
            file=sys.stderr,
        )
        return error.exit_code
    except Exception as error:
        message = _unexpected_failure_message(error)
        _add_failure_diagnostic(message)
        _try_write_failure_output(
            failure_output_path,
            command,
            "UnexpectedHostFailure",
            1,
            message,
        )
        print(
            json.dumps(
                {
                    "errorCode": "UnexpectedHostFailure",
                    "message": message,
                },
                ensure_ascii=False,
                separators=(",", ":"),
            ),
            file=sys.stderr,
        )
        return 1



__all__ = [name for name in globals() if not name.startswith("__")]
