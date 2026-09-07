"""Bounded wording examples and recording-disjoint training/evaluation splits."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path

SCHEMA = "foundry-writer-example-1"
MAXIMUM_EXAMPLES = 10_000
MAXIMUM_EXAMPLE_BYTES = 65_536
FEEDBACK_KINDS = {"HumanCorrection", "ExplicitWordingApproval", "ExplicitWordingPreference"}


def canonical(value) -> str:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"), allow_nan=False)


def digest(value) -> str:
    return hashlib.sha256(canonical(value).encode("utf-8")).hexdigest()


def strict_json(text):
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise ValueError("Duplicate writer JSON property.")
            result[key] = value
        return result
    def constant(value):
        raise ValueError("Non-finite writer JSON number.")
    return json.loads(text, object_pairs_hook=pairs, parse_constant=constant)


def valid_hash(value) -> bool:
    return isinstance(value, str) and len(value) == 64 and all(c in "0123456789abcdef" for c in value)


def validate_copy(value):
    if not isinstance(value, dict) or set(value) != {"titleBody", "description", "tags", "grounding", "temporalVoice"}:
        raise ValueError("A writer target must contain the complete structured metadata object.")
    for name, maximum in (("titleBody", 100), ("description", 420)):
        if not isinstance(value[name], str) or not value[name].strip() or len(value[name]) > maximum:
            raise ValueError("Writer target text exceeds its bounded field contract.")
    if value["temporalVoice"] != "RetrospectivePast":
        raise ValueError("Writer targets must retain their temporal voice.")
    if (not isinstance(value["tags"], list) or not 1 <= len(value["tags"]) <= 8
            or any(not isinstance(tag, str) or not tag or len(tag) > 60 for tag in value["tags"])):
        raise ValueError("Invalid writer target tags.")
    if not isinstance(value["grounding"], list) or len(value["grounding"]) > 24:
        raise ValueError("Invalid writer target grounding.")


def validate_example(value, *, qualification_run=False):
    keys = {"schema", "id", "sourceGroup", "factSha256", "prompt", "chosen", "rejected", "kind"}
    if not isinstance(value, dict) or set(value) != keys or value["schema"] != SCHEMA:
        raise ValueError("Unsupported writer example contract.")
    if any(not valid_hash(value[key]) for key in ("id", "sourceGroup", "factSha256")):
        raise ValueError("Writer evidence identities must be SHA-256 values.")
    allowed = FEEDBACK_KINDS | ({"QualificationFixture"} if qualification_run else set())
    if value["kind"] not in allowed:
        raise ValueError("Clip ratings, exports, publishing and model output are not wording approval.")
    prompt = value["prompt"]
    if (not isinstance(prompt, list) or len(prompt) != 2 or
            [item.get("role") for item in prompt if isinstance(item, dict)] != ["system", "user"]):
        raise ValueError("Writer training requires one bounded fact-conditioned prompt.")
    for item in prompt:
        if set(item) != {"role", "content"} or not isinstance(item["content"], str) or not item["content"]:
            raise ValueError("Writer prompts contain only text, not media or executable tools.")
    if hashlib.sha256(prompt[1]["content"].encode("utf-8")).hexdigest() != value["factSha256"]:
        raise ValueError("Writer prompt facts no longer match their captured identity.")
    validate_copy(value["chosen"])
    if value["rejected"] is not None:
        validate_copy(value["rejected"])
        if value["chosen"] == value["rejected"]:
            raise ValueError("A preference pair must contain two different wordings.")
    if value["kind"] in {"HumanCorrection", "ExplicitWordingPreference"} and value["rejected"] is None:
        raise ValueError("A wording correction requires its rejected wording under the same facts.")
    identity = {key: item for key, item in value.items() if key != "id"}
    if digest(identity) != value["id"]:
        raise ValueError("Writer example identity changed.")
    return value


def load_examples(path: Path, *, qualification_run=False):
    seen, examples = set(), []
    with path.open("rb") as stream:
        while line := stream.readline(MAXIMUM_EXAMPLE_BYTES + 1):
            if len(line) > MAXIMUM_EXAMPLE_BYTES:
                raise ValueError("Writer example exceeds its size limit.")
            value = validate_example(strict_json(line), qualification_run=qualification_run)
            if value["id"] not in seen:
                seen.add(value["id"])
                examples.append(value)
            if len(examples) > MAXIMUM_EXAMPLES:
                raise ValueError("Writer dataset exceeds its bounded capacity.")
    return examples


def split_by_recording(examples, *, evaluation_fraction=0.2):
    """Every cut and wording from a recording stays in the same partition."""
    if not 0 < evaluation_fraction < 1:
        raise ValueError("Evaluation fraction must be between zero and one.")
    groups = sorted({row["sourceGroup"] for row in examples}, key=lambda group: digest(["writer-split-1", group]))
    if len(groups) < 2:
        raise ValueError("Writer qualification requires at least two independent recordings.")
    heldout = set(groups[:max(2 if len(groups) >= 6 else 1, min(len(groups)-1, round(len(groups)*evaluation_fraction)))])
    train = [row for row in examples if row["sourceGroup"] not in heldout]
    evaluation = [row for row in examples if row["sourceGroup"] in heldout]
    # Exact copied targets across recordings are excluded from training to
    # prevent duplicate wording leaking into the held-out comparison.
    evaluation_targets = {digest(row["chosen"]) for row in evaluation}
    train = [row for row in train if digest(row["chosen"]) not in evaluation_targets]
    if not train or not evaluation:
        raise ValueError("The recording-disjoint writer split has no independent examples.")
    return train, evaluation
