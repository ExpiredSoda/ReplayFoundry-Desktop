"""Capture the exact compact factual prompt, separate from preference labels."""
from __future__ import annotations

from contextvars import ContextVar
import hashlib
import json
import os
from pathlib import Path

from .data import canonical

ENVIRONMENT_FLAG = "REPLAYFOUNDRY_WRITER_CAPTURE"
_PROMPTS: ContextVar[dict | None] = ContextVar("writer_prompts", default=None)


def begin():
    return _PROMPTS.set({} if os.environ.get(ENVIRONMENT_FLAG) == "1" else None)


def observe(request, messages, attestation):
    prompts = _PROMPTS.get()
    if prompts is None or not attestation or attestation.get("logicalPassOrdinal") != 1:
        return
    if len(messages) != 2 or [message["role"] for message in messages] != ["system", "user"]:
        return
    prompt = [{"role":message["role"],"content":"".join(item["text"] for item in message["content"]
               if item.get("type") == "text")} for message in messages]
    key = (request["candidateId"], request["attempt"])
    if key not in prompts and len(canonical(prompt).encode("utf-8")) < 49_152:
        prompts[key] = prompt


def write(output_path: Path, results):
    prompts = _PROMPTS.get()
    if prompts is None:
        return
    captures = []
    for result in results:
        key = (result["candidateId"], result["attempt"])
        if key not in prompts or "metadata" not in result:
            continue
        generation = result.get("generation", {})
        attestations = generation.get("synthesisPassAttestations", [])
        # Retries can deliberately withhold identities or narrow the facts.
        # Never bind their wording to a broader first-pass training prompt.
        if (len(attestations) != 1 or attestations[0].get("logicalPassOrdinal") != 1
                or attestations[0].get("accepted") is not True
                or generation.get("primaryOnlySynthesisEvidenceApplied", False)):
            continue
        # Review-required generated wording can be the rejected side of a later
        # human correction; it is never an accepted training target by itself.
        captures.append({"candidateId":key[0],"attempt":key[1],"prompt":prompts[key],
            "factSha256":hashlib.sha256(prompts[key][1]["content"].encode("utf-8")).hexdigest(),
            "generated":{**result["metadata"],"temporalVoice":"RetrospectivePast"},
            "outputCanonicalHash":hashlib.sha256(canonical(result).encode("utf-8")).hexdigest()})
    from ...artifact_writer import _write_json_atomic
    try:
        _write_json_atomic(output_path.with_name("writer-contexts.json"),
            {"schema":"foundry-writer-contexts-1","contexts":captures})
    except OSError:
        from ...commands import _add_failure_diagnostic
        _add_failure_diagnostic("Optional local writer context could not be retained.")


def end(token):
    _PROMPTS.reset(token)
