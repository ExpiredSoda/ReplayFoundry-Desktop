"""Recording-separated evaluation of raw writing, with output-bound human reviews.

Model acceptance is reported separately from publishability. Missing human
labels, failed generations and abstentions cannot turn into a passing score.
"""
from __future__ import annotations

import argparse
import json
import math
import statistics
from pathlib import Path
from .editorial.writer.data import digest, strict_json
from .recording_index import write_atomic

SCHEMA = "foundry-editorial-evaluation-1"


def validate_cases(cases):
    if not isinstance(cases, list) or not 1 <= len(cases) <= 1000:
        raise ValueError("An evaluation needs 1 to 1000 registered cuts")
    seen, partitions = set(), {}
    for case in cases:
        if not isinstance(case, dict) or not all(isinstance(case.get(key), str) and case[key].strip()
                for key in ("candidateId", "sourceGroup", "gameGroup", "nearDuplicateGroup", "partition")):
            raise ValueError("Every cut needs recording, game, duplicate and partition identities")
        if case["candidateId"] in seen or case["partition"] not in {"development", "qualification"}:
            raise ValueError("Duplicate cut or unknown evaluation partition")
        seen.add(case["candidateId"])
        for field in ("sourceGroup", "gameGroup", "nearDuplicateGroup"):
            key = (field, case[field])
            if key in partitions and partitions[key] != case["partition"]:
                raise ValueError(f"{field} leaks across development and qualification")
            partitions[key] = case["partition"]
    return {case["candidateId"]:case for case in cases}


def summarize(cases, rows, reviews=()):
    registered = validate_cases(cases)
    outputs = {}
    for row in rows:
        key = (row.get("candidateId"), row.get("attempt"))
        if key[0] not in registered or type(key[1]) is not int or key[1] < 0 or key in outputs:
            raise ValueError("Missing cut identity or duplicate attempt in evaluation")
        if row.get("status") not in {"Succeeded", "Failed"}:
            raise ValueError("Unfinished generation is not an evaluated output")
        seconds = row.get("elapsedSeconds")
        if not isinstance(seconds, (float, int)) or not math.isfinite(seconds) or seconds < 0:
            raise ValueError("Invalid evaluation timing")
        if row["status"] == "Succeeded" and not isinstance(row.get("copy"), dict):
            raise ValueError("Successful generation has no raw copy")
        outputs[key] = row
    labels = {}
    for review in reviews:
        key = (review.get("candidateId"), review.get("attempt"))
        if key not in outputs or key in labels or outputs[key]["status"] != "Succeeded":
            raise ValueError("A review must name one existing successful output")
        if review.get("outputSha256") != digest(outputs[key]["copy"]):
            raise ValueError("Reviewed wording changed after assessment")
        if review.get("reviewerKind") not in {"Human", "Assistant", "Model"}:
            raise ValueError("Unknown review provenance")
        if any(type(review.get(field)) is not bool for field in ("factsSupported", "usable", "criticalError", "distinctFromInitial")):
            raise ValueError("Incomplete editorial review")
        labels[key] = review
    human = {key:review for key,review in labels.items() if review["reviewerKind"] == "Human"}
    first = [outputs.get((identity,0)) for identity in registered]
    complete_first = all(row is not None and (row["status"] == "Failed" or (row["candidateId"],0) in human) for row in first)
    usable_first = sum(bool((review := human.get((identity,0))) and review["usable"] and review["factsSupported"] and not review["criticalError"])
                       for identity in registered)
    successful = [row for row in rows if row["status"] == "Succeeded"]
    timings = sorted(row["elapsedSeconds"] for row in rows if not row.get("cacheHit"))
    def pct(value):
        return timings[min(len(timings)-1, max(0, math.ceil(value*len(timings))-1))] if timings else None
    critical = sum(review["criticalError"] for review in human.values())
    unreviewed = len(successful)-len(human)
    groups = len({case["sourceGroup"] for case in cases})
    complete_pilot = len(cases) >= 30 and groups >= 6 and all((identity,attempt) in outputs for identity in registered for attempt in range(5))
    qualified = complete_pilot and unreviewed == 0 and complete_first and critical == 0 and usable_first/len(cases) >= .8
    return {"schema":SCHEMA, "cuts":len(cases), "recordings":groups, "attempts":len(rows),
        "modelAccepted":len(successful), "failedOrAbstained":len(rows)-len(successful),
        "humanReviewed":len(human), "successfulOutputsAwaitingHumanReview":unreviewed,
        "criticalErrors":critical if human else None, "firstDraftUsableRate":usable_first/len(cases) if complete_first else None,
        "uncachedMedianSeconds":statistics.median(timings) if timings else None, "uncachedP95Seconds":pct(.95),
        "cacheHits":sum(bool(row.get("cacheHit")) for row in rows), "pilotCoverageComplete":complete_pilot,
        "firstDraftGatePassed":qualified,
        "releaseQualified":False, # Diversity, persistence and expanded held-out qualification are additional required gates.
        "reviewNote":"Assistant/model ratings are diagnostics. Human review must be bound to unchanged raw output; no manual replacement is scored as AI output."}


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cases",required=True)
    parser.add_argument("--outputs",required=True,nargs="+")
    parser.add_argument("--reviews")
    parser.add_argument("--report",required=True)
    args=parser.parse_args()
    cases=strict_json(Path(args.cases).read_text(encoding="utf-8-sig"))["cases"]
    rows=[row for file in args.outputs for row in strict_json(Path(file).read_text(encoding="utf-8-sig"))["cases"]]
    reviews=strict_json(Path(args.reviews).read_text(encoding="utf-8-sig"))["reviews"] if args.reviews else []
    write_atomic(Path(args.report),summarize(cases,rows,reviews))


if __name__ == "__main__":
    main()
