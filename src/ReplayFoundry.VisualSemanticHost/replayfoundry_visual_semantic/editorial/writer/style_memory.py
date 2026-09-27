"""Small, explicit, local style references; no training and no factual authority."""
from __future__ import annotations

from pathlib import Path
from .data import MAXIMUM_EXAMPLE_BYTES, strict_json, validate_example


def load_style_examples(root, maximum=3):
    if root is None:
        return []
    directory = Path(root) / "examples"
    if not directory.is_dir():
        return []
    result, seen = [], set()
    # Bound file reads and exclude unreviewed fact corrections and partial-field
    # edits. Only complete, explicitly approved pairs provide both field styles.
    try:
        candidates = sorted(directory.glob("*.json"), key=lambda path: path.stat().st_mtime_ns, reverse=True)[:128]
    except OSError:
        return []
    for path in candidates:
        try:
            if path.is_symlink() or path.stat().st_size > MAXIMUM_EXAMPLE_BYTES:
                continue
            example = validate_example(strict_json(path.read_text(encoding="utf-8-sig")))
            feedback = example.get("feedback", {})
            approved = example["kind"] == "ExplicitWordingApproval" or (
                feedback.get("factsReviewed") is True and {"titleBody", "description"} <= set(feedback.get("fields", [])))
            explicit_style = example["kind"] in {"HumanCorrection", "ExplicitWordingPreference"} and feedback.get("reason") in {"Style", "TooGeneric"}
            if not (approved or explicit_style) or feedback.get("reason", "Unspecified") not in {"Unspecified", "Style", "TooGeneric"}:
                continue
            chosen = example["chosen"]
            fields = {"titleBody", "description"} if approved else set(feedback.get("fields", [])) & {"titleBody", "description"}
            # Preserve extended edits in storage without expanding each generation prompt.
            fields = {field for field in fields if len(chosen[field]) <= (100 if field == "titleBody" else 420)}
            if not fields:
                continue
            selected = {key:chosen[key] for key in sorted(fields)}
            identity = tuple((key,value.casefold()) for key,value in selected.items())
            if identity in seen:
                continue
            seen.add(identity)
            result.append({"id":example["id"], **selected,
                           "purpose":"Style only; names, events, outcomes and quotations are NOT facts for this clip."})
            if len(result) == maximum:
                break
        except (OSError, ValueError, KeyError, TypeError):
            continue
    return result
