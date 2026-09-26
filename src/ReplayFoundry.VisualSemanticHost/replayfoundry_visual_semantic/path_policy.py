"""ReplayFoundry local visual-semantic host implementation module."""
from __future__ import annotations

from .failure_state import *  # noqa: F401,F403

def _repository_root() -> Path | None:
    """Return the source repository root, or None for a packaged host."""
    resolved = HOST_ENTRY_PATH.resolve()
    for candidate in resolved.parents:
        if (candidate / "ReplayFoundry.slnx").is_file():
            return candidate
    return None


def _is_relative_to(path: Path, parent: Path) -> bool:
    try:
        path.relative_to(parent)
        return True
    except ValueError:
        return False


def _require_absolute_external_path(
    raw_path: str,
    location: str,
    *,
    must_exist: bool,
    must_be_file: bool | None = None,
) -> Path:
    if not isinstance(raw_path, str) or not raw_path.strip():
        _fail(UsageOrInputError, f"{location} must be a nonblank absolute path.")

    candidate = Path(raw_path)
    if not candidate.is_absolute():
        _fail(UsageOrInputError, f"{location} must be an absolute path.")

    try:
        resolved = candidate.resolve(strict=must_exist)
    except OSError as error:
        _fail(UsageOrInputError, f"{location} could not be resolved: {error}")

    repository_root = _repository_root()
    if repository_root is not None and _is_relative_to(resolved, repository_root):
        _fail(
            UsageOrInputError,
            f"{location} must remain outside the ReplayFoundry repository.",
        )

    if must_exist:
        if must_be_file is True and not resolved.is_file():
            _fail(UsageOrInputError, f"{location} must identify a readable file.")
        if must_be_file is False and not resolved.is_dir():
            _fail(UsageOrInputError, f"{location} must identify a directory.")

    return resolved


def _require_path_outside_roots(
    path: Path,
    protected_roots: list[tuple[str, Path]],
    location: str,
) -> None:
    try:
        resolved_path = path.resolve(strict=False)
    except OSError as error:
        _fail(
            UsageOrInputError,
            f"{location} could not be resolved: {error}",
        )
    for description, protected_root in protected_roots:
        try:
            resolved_root = protected_root.resolve(strict=True)
        except OSError as error:
            _fail(
                UsageOrInputError,
                f"Could not resolve protected {description}: {error}",
            )
        if (
            resolved_path == resolved_root
            or _is_relative_to(resolved_path, resolved_root)
        ):
            _fail(
                UsageOrInputError,
                f"{location} must remain outside the {description}.",
            )



__all__ = [name for name in globals() if not name.startswith("__")]
