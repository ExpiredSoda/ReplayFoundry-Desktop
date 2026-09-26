"""Shared import and fixture paths for visual-semantic host tests."""

from __future__ import annotations

from importlib.util import find_spec
import sys
from pathlib import Path


TEST_ROOT = Path(__file__).resolve().parent
REPOSITORY_ROOT = TEST_ROOT.parents[1]
HOST_ROOT = REPOSITORY_ROOT / "src" / "ReplayFoundry.VisualSemanticHost"


def configure_import_paths() -> None:
    """Expose the moved host package without relying on the current directory."""
    package_marker = HOST_ROOT / "replayfoundry_visual_semantic" / "__init__.py"
    if not package_marker.is_file():
        raise RuntimeError(
            "Visual-semantic host package was not found at "
            f"'{package_marker}'."
        )

    host_path = str(HOST_ROOT)
    if host_path not in sys.path:
        sys.path.insert(0, host_path)


def optional_dependencies_available(*module_names: str) -> bool:
    """Return whether every optional integration-test dependency is installed."""
    return all(find_spec(module_name) is not None for module_name in module_names)


configure_import_paths()
