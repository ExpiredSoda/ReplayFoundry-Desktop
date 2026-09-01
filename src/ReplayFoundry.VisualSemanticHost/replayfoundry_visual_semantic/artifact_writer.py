"""ReplayFoundry local visual-semantic host implementation module."""
from __future__ import annotations

from .path_policy import *  # noqa: F401,F403

def _write_json_atomic(path: Path, value: Any) -> None:
    if path.exists():
        _fail(OutputError, f"Output already exists: '{path}'.")
    parent = path.parent
    try:
        parent.mkdir(parents=True, exist_ok=True)
    except OSError as error:
        _fail(OutputError, f"Could not create output directory '{parent}': {error}")
    repository_root = _repository_root()
    if repository_root is not None and _is_relative_to(parent.resolve(), repository_root):
        _fail(OutputError, "Output directory must remain outside the repository.")

    temporary_path: Path | None = None
    try:
        descriptor, temporary_name = tempfile.mkstemp(
            prefix=f".{path.name}.",
            suffix=".tmp",
            dir=str(parent),
        )
        temporary_path = Path(temporary_name)
        with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as stream:
            json.dump(
                value,
                stream,
                ensure_ascii=False,
                indent=2,
                allow_nan=False,
            )
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        # The host is Windows-only; os.rename is atomic on the same volume and
        # fails rather than replacing an output that appeared concurrently.
        os.rename(temporary_path, path)
        temporary_path = None
    except (OSError, ValueError, TypeError) as error:
        _fail(OutputError, f"Could not write output '{path}': {error}")
    finally:
        if temporary_path is not None:
            try:
                temporary_path.unlink(missing_ok=True)
            except OSError:
                pass



__all__ = [name for name in globals() if not name.startswith("__")]
