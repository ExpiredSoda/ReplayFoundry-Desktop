"""Lossless, evidence-bound edit records for future supervised/preference learning."""
from __future__ import annotations
from difflib import SequenceMatcher

FIELDS = ("titleBody", "description")


def derive(row):
    """Legacy pairs remain immutable; the derived trace never invents intent."""
    if "edits" in row:
        validate(row["edits"], row)
        return row["edits"]
    fields = []
    if row.get("rejected") is not None:
        for field in FIELDS:
            before, after = row["rejected"][field], row["chosen"][field]
            if before == after:
                continue
            operations = []
            for op, a, b, c, d in SequenceMatcher(None, before, after, autojunk=False).get_opcodes():
                if op == "equal": operations.append(dict(op="keep", text=before[a:b]))
                else:
                    if a != b: operations.append(dict(op="remove", text=before[a:b]))
                    if c != d: operations.append(dict(op="add", text=after[c:d]))
            fields.append(dict(field=field, before=before, after=after, operations=operations))
    return dict(schema="foundry-wording-edits-1", algorithm="unicode-sequence-1", parentExampleId=None, fields=fields)


def validate(trace, row):
    from .data import valid_hash
    if (not isinstance(trace, dict) or set(trace) != {"schema", "algorithm", "parentExampleId", "fields"}
            or trace["schema"] != "foundry-wording-edits-1"
            or trace["algorithm"] not in {"unicode-lcs-1", "unicode-sequence-1"}
            or trace["parentExampleId"] is not None and not valid_hash(trace["parentExampleId"])
            or not isinstance(trace["fields"], list) or len(trace["fields"]) > 2):
        raise ValueError("Invalid wording edit trace")
    expected = {field for field in FIELDS if row.get("rejected") is not None
                and row["chosen"][field] != row["rejected"][field]}
    seen = set()
    for field in trace["fields"]:
        if (not isinstance(field, dict) or set(field) != {"field", "before", "after", "operations"}
                or field["field"] not in expected or field["field"] in seen
                or field["before"] != row["rejected"][field["field"]]
                or field["after"] != row["chosen"][field["field"]]
                or not isinstance(field["operations"], list) or not 1 <= len(field["operations"]) <= 10000):
            raise ValueError("Edit trace changed the captured before/after wording")
        seen.add(field["field"])
        for operation in field["operations"]:
            if (not isinstance(operation, dict) or set(operation) != {"op", "text"}
                    or operation["op"] not in {"keep", "remove", "add"}
                    or not isinstance(operation["text"], str) or not operation["text"]
                    or len(operation["text"]) > 5000):
                raise ValueError("Invalid edit operation")
        before = "".join(op["text"] for op in field["operations"] if op["op"] != "add")
        after = "".join(op["text"] for op in field["operations"] if op["op"] != "remove")
        if (before, after) != (field["before"], field["after"]):
            raise ValueError("Edit operations do not reconstruct the saved wording")
    if seen != expected:
        raise ValueError("Edit trace omitted a changed wording field")


def training_record(row):
    from .contracts import eligibility_issue, supervised_fields
    from .data import digest
    trace = derive(row)
    validate(trace, row)
    return dict(schema="foundry-edit-supervision-1", exampleId=row["id"], sourceGroup=row["sourceGroup"],
                factSha256=row["factSha256"], prompt=row["prompt"], evidence=row.get("evidence"),
                preferred=row["chosen"], previous=row.get("rejected"), edits=trace, editSha256=digest(trace),
                supervisedFields=sorted(supervised_fields(row)), feedback=row.get("feedback"),
                exclusionReason=eligibility_issue(row),
                authority="HumanWordingFeedback; source observations remain model evidence unless separately reviewed")
