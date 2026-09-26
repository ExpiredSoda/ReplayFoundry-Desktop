"""Sealed, bounded fact-packet handoff within one desktop retry operation.

The desktop owns the operation and only forwards packets from fully parsed
successful outputs. Each child reads/writes siblings of its own batch files;
there is no persistent cache, model state, or independently writable cache root.
"""
from __future__ import annotations

import hashlib
import json
import math
import os
from pathlib import Path
from typing import Any

from .grounded_metadata_pipeline_contract import (
    GROUNDING_PACKET_SCHEMA_VERSION, GroundingPacket, _canonical_json,
    _grounding_reuse_identity,
)

ENVIRONMENT_FLAG = "REPLAYFOUNDRY_GROUNDING_HANDOFF"
IMPORT_HASH_FLAG = "REPLAYFOUNDRY_GROUNDING_IMPORT_SHA256"
SCHEMA = "grounded-editorial-packet-handoff-1.0"
MAXIMUM_ENTRIES = 30
MAXIMUM_ENTRY_BYTES = 262_144
MAXIMUM_FILE_BYTES = 8_388_608
IMPORT_FILE = "grounding-import.json"
EXPORT_FILE = "grounding-export.json"
_ENTRY_KEYS = {
    "schemaVersion", "requestIdentitySha256", "canonicalRequestIdentity",
    "factSha256", "canonicalFacts", "sourceAttempt", "groundingPassCount",
    "groundingElapsedSeconds", "runtimeIdentitySha256", "editorialBriefSha256",
}


def _sha(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def _strict_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate handoff property.")
        result[key] = value
    return result


def _json(text: str):
    return json.loads(text, object_pairs_hook=_strict_object,
                      parse_constant=lambda _: (_ for _ in ()).throw(ValueError("Nonfinite handoff value.")))


def _runtime_identity(lock: dict[str, Any]) -> str:
    # Include every current host module, not just the editorial attestation subset.
    package = Path(__file__).resolve().parent.parent
    files = sorted(package.rglob("*.py"))
    files += sorted(package.parent.glob("qwen3_vl*_host.py"))
    if not files or len(files) > 512:
        raise ValueError("Host identity exceeds the bounded module roster.")
    modules = []
    for path in files:
        if path.is_symlink() or path.stat().st_size > 4_194_304:
            raise ValueError("Host identity is not a bounded regular module.")
        modules.append([path.relative_to(package.parent).as_posix(),
                        hashlib.sha256(path.read_bytes()).hexdigest()])
    return _sha(_canonical_json({"qualificationLock": lock, "hostModules": modules}))


def _fact_hash(request_hash: str, canonical_facts: str) -> str:
    # Preserve the exact canonical bytes emitted by _new_grounding_packet,
    # including JSON number representation; never round-trip its fact hash.
    return _sha('{"facts":' + canonical_facts + ',"requestIdentitySha256":'
                + json.dumps(request_hash) + ',"schemaVersion":'
                + json.dumps(GROUNDING_PACKET_SCHEMA_VERSION) + '}')


class GroundingPacketHandoff:
    def __init__(self, input_path: Path, output_path: Path, lock: dict[str, Any]):
        self.enabled = os.environ.get(ENVIRONMENT_FLAG) == "1"
        self._entries: dict[str, dict[str, Any]] = {}
        self._exports: dict[str, dict[str, Any]] = {}
        self._directory = input_path.resolve().parent
        self._lock = lock
        self._runtime = ""
        if not self.enabled:
            return
        if (output_path.resolve().parent != self._directory or input_path.is_symlink()
                or output_path.is_symlink()):
            raise ValueError("Packet handoff must stay within the owned batch workspace.")
        self._runtime = _runtime_identity(lock)
        path = self._directory / IMPORT_FILE
        expected = os.environ.get(IMPORT_HASH_FLAG, "")
        if not expected:
            return
        try:
            if path.is_symlink() or path.stat().st_size > MAXIMUM_FILE_BYTES:
                return
            with path.open("rb") as stream:
                data = stream.read(MAXIMUM_FILE_BYTES + 1)
            if len(data) > MAXIMUM_FILE_BYTES:
                return
            if hashlib.sha256(data).hexdigest() != expected:
                return
            payload = _json(data.decode("utf-8"))
            if (not isinstance(payload, dict) or set(payload) != {"schemaVersion", "entries"}
                    or payload["schemaVersion"] != SCHEMA
                    or not isinstance(payload["entries"], list)
                    or len(payload["entries"]) > MAXIMUM_ENTRIES):
                return
            entries = {}
            for entry in payload["entries"]:
                self._validate_entry(entry)
                identity = entry["requestIdentitySha256"]
                if identity in entries:
                    return
                entries[identity] = entry
            self._entries = entries
        except (OSError, ValueError, TypeError, KeyError, UnicodeError):
            # A cache miss is safe; ordinary grounding still runs and validates.
            self._entries = {}

    def _validate_entry(self, entry: Any) -> None:
        if (not isinstance(entry, dict) or set(entry) != _ENTRY_KEYS
                or len(_canonical_json(entry).encode("utf-8")) > MAXIMUM_ENTRY_BYTES
                or entry["schemaVersion"] != GROUNDING_PACKET_SCHEMA_VERSION):
            raise ValueError("Invalid packet handoff entry.")
        for field in ("requestIdentitySha256", "factSha256", "runtimeIdentitySha256", "editorialBriefSha256"):
            value = entry[field]
            if not isinstance(value, str) or len(value) != 64 or any(c not in "0123456789abcdef" for c in value):
                raise ValueError("Invalid packet handoff identity.")
        for field, minimum, maximum in (("sourceAttempt", 0, 100), ("groundingPassCount", 1, 32)):
            value = entry[field]
            if type(value) is not int or not minimum <= value <= maximum:
                raise ValueError("Invalid packet handoff count.")
        elapsed = entry["groundingElapsedSeconds"]
        if type(elapsed) not in (int, float) or not math.isfinite(elapsed) or not 0 <= elapsed <= 86_400:
            raise ValueError("Invalid packet handoff duration.")
        identity, facts = entry["canonicalRequestIdentity"], entry["canonicalFacts"]
        if not isinstance(identity, str) or not isinstance(facts, str):
            raise ValueError("Packet canonical documents are required.")
        if not isinstance(_json(identity), dict) or not isinstance(_json(facts), dict):
            raise ValueError("Packet canonical documents must be objects.")
        if _sha(identity) != entry["requestIdentitySha256"] or _fact_hash(entry["requestIdentitySha256"], facts) != entry["factSha256"]:
            raise ValueError("Packet canonical identity changed.")

    def restore(self, request: dict[str, Any]) -> GroundingPacket | None:
        if not self.enabled:
            return None
        identity, canonical = _grounding_reuse_identity(request)
        entry = self._entries.get(identity)
        if (entry is None or entry["canonicalRequestIdentity"] != canonical
                or entry["runtimeIdentitySha256"] != self._runtime
                or entry["editorialBriefSha256"] != _sha(_canonical_json(request.get("editorialBrief")))
                or entry["sourceAttempt"] == request["attempt"]):
            return None
        return GroundingPacket(entry["schemaVersion"], identity, entry["factSha256"],
                               entry["sourceAttempt"], entry["groundingPassCount"],
                               entry["groundingElapsedSeconds"], entry["canonicalFacts"])

    def retain(self, request: dict[str, Any], packet: GroundingPacket) -> None:
        if not self.enabled:
            return
        identity, canonical = _grounding_reuse_identity(request)
        entry = dict(schemaVersion=packet.schema_version, requestIdentitySha256=identity,
                     canonicalRequestIdentity=canonical, factSha256=packet.fact_sha256,
                     canonicalFacts=packet.canonical_facts, sourceAttempt=packet.source_attempt,
                     groundingPassCount=packet.grounding_pass_count,
                     groundingElapsedSeconds=packet.grounding_elapsed_seconds,
                     runtimeIdentitySha256=self._runtime,
                     editorialBriefSha256=_sha(_canonical_json(request.get("editorialBrief"))))
        try:
            self._validate_entry(entry)
        except (ValueError, TypeError):
            return
        if identity in self._exports or len(self._exports) < MAXIMUM_ENTRIES:
            self._exports[identity] = entry

    def write(self) -> None:
        if not self.enabled:
            return
        # A source edit during inference cannot produce a reusable receipt.
        if _runtime_identity(self._lock) != self._runtime:
            return
        data = _canonical_json({"schemaVersion": SCHEMA, "entries": list(self._exports.values())}).encode("utf-8")
        if len(data) > MAXIMUM_FILE_BYTES:
            return
        path = self._directory / EXPORT_FILE
        # The workspace is exclusive to this child; never replace an existing path.
        try:
            with path.open("xb") as stream:
                stream.write(data)
        except OSError:
            # Copy has already been written successfully. Reuse is optional.
            return
