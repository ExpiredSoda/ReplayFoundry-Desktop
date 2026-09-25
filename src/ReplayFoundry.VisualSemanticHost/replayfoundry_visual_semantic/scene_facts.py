"""Require a separately checked source citation for each authored scene claim."""
from __future__ import annotations

import re
import hashlib

POLICY_HASH = hashlib.sha256(b"scene-facts-1.0:per-claim-verbatim-source-text").hexdigest()


def fact_properties(frame_count, speech):
    ids = [f"frame-{index}" for index in range(frame_count)] + [row["id"] for row in speech if isinstance(row.get("id"), str)]
    claim = {"type":"object", "additionalProperties":False, "properties":{
        "evidenceIds":{"type":"array","items":{"type":"string","enum":ids},"maxItems":4},
        "verbatimSourceText":{"type":"string","maxLength":100},
        "supported":{"type":"boolean"}},
        "required":["evidenceIds","verbatimSourceText","supported"]}
    return {"claims":{"type":"object","additionalProperties":False,
                "properties":{name:claim for name in ("setup","event","outcome")},
                "required":["setup","event","outcome"]},
            "reason":{"type":"string","maxLength":200}, "grounded":{"type":"boolean"}}


def validate_fact_check(check, claims, frame_count, speech):
    if not isinstance(check, dict) or type(check.get("grounded")) is not bool or not isinstance(check.get("reason"),str):
        raise ValueError("Invalid visual fact check")
    ledger = check.get("claims")
    if not isinstance(ledger, dict) or set(ledger) != set(claims):
        raise ValueError("Each scene claim needs its own evidence check")
    frames = {f"frame-{index}" for index in range(frame_count)}
    words = {row["id"]:row["text"] for row in speech if isinstance(row.get("id"), str)}
    for name, claim in ledger.items():
        ids = claim.get("evidenceIds")
        literal = claim.get("verbatimSourceText")
        if type(claim.get("supported")) is not bool or not isinstance(ids,list) or len(ids)>4 or not isinstance(literal,str):
            raise ValueError("Invalid claim citation")
        if any(ref not in frames and ref not in words for ref in ids):
            raise ValueError("Scene claim cites unavailable evidence")
        if claim["supported"]:
            if not ids:
                raise ValueError("Supported claims require a source citation")
            # Speech excerpts must exist verbatim. Visual text is checked against
            # the cited frames by the independent visual pass; it is not trusted OCR.
            if literal and not frames.intersection(ids) and not any(literal.casefold() in words[ref].casefold() for ref in ids):
                claim["supported"] = False
            numbers = set(re.findall(r"\b\d+\b", claims[name]))
            if not numbers.issubset(set(re.findall(r"\b\d+\b", literal))):
                claim["supported"] = False
            # Repeating the proposed sentence is not independent source text.
            if literal.strip().casefold().rstrip(".!?") == claims[name].strip().casefold().rstrip(".!?"):
                claim["supported"] = False
    if check["grounded"] and any(not row["supported"] for row in ledger.values()):
        check["grounded"] = False
        check["reason"] = "A proposed detail lacks a supporting citation or exact source text; omit the uncertain detail."
    return check


def validate_copy_numbers(copy, context):
    """The writer cannot introduce numbers absent from the reviewed evidence."""
    def texts(value):
        if isinstance(value, dict):
            for key, child in value.items():
                if key in ("text", "explanation", "description") and isinstance(child, str):
                    yield child
                elif isinstance(child, (dict, list)):
                    yield from texts(child)
        elif isinstance(value, list):
            for child in value:
                yield from texts(child)
    evidence = str(context.get("centralEvent", "")) + " " + " ".join(texts(context.get("reviewedContext", {})))
    proposed = set(re.findall(r"\b\d+\b", copy["titleBody"]+" "+copy["description"]))
    if not proposed.issubset(set(re.findall(r"\b\d+\b", evidence))):
        raise ValueError("Copy introduced a number absent from the reviewed evidence")
    return copy
