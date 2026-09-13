"""Compare two discovery runs against a frozen human-reviewed recording set (JSON).

Labels: schema=moment-labels-1, recordings=[id, sha256, duration, split,
reviewedByHuman, completeCoverage, events=[id, category, start, end, setupStart,
payoffEnd, speaker (CreatorSpeech/GameDialogue/MixedSpeech/Unknown)]].
Runs: schema=moment-run-1, policy, hardware, budget={clips,review,maximumClipSeconds}, recordings=[id, sha256,
clips=[id,start,end,categories,speaker], proposed=[start,end], reviewed=[start,end]].
Budgets apply per recording. Complete-cut acceptance allows at most two seconds
of extra material by default; human events may specify maximumExtraSeconds.
No automatic model promotion is performed. No model predictions become labels.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import math
from pathlib import Path

CATEGORIES = ("Action","Humor","Commentary","Lore","Discovery","Failure","Clutch","Tutorial","Reaction")
SPEAKERS = ("CreatorSpeech", "GameDialogue", "MixedSpeech", "Unknown")


def finite(value):
    return type(value) in (int,float) and math.isfinite(value)


def digest(value):
    return hashlib.sha256(json.dumps(value,sort_keys=True,allow_nan=False,separators=(",",":")).encode()).hexdigest()


def interval(row, duration):
    left, right = row["start"], row["end"]
    if any(type(value) not in (int,float) or not math.isfinite(value) for value in (left,right)) or not 0 <= left < right <= duration:
        raise ValueError("Invalid source interval")


def validate_labels(labels):
    if labels.get("schema") != "moment-labels-1" or not labels.get("recordings"): raise ValueError("Human labels are required")
    ids, hashes = set(), set()
    for recording in labels["recordings"]:
        if recording["id"] in ids or recording["sha256"] in hashes: raise ValueError("Recording duplicates would leak across evaluation groups")
        ids.add(recording["id"]); hashes.add(recording["sha256"])
        if len(recording["sha256"]) != 64 or any(char not in "0123456789abcdef" for char in recording["sha256"]): raise ValueError("Missing source SHA-256")
        if recording.get("reviewedByHuman") is not True or recording.get("completeCoverage") is not True:
            raise ValueError("Whole-recording human review is required to measure missed events")
        if recording["split"] not in ("train","validation","test"): raise ValueError("Unknown evaluation split")
        if not finite(recording["duration"]) or recording["duration"] <= 0: raise ValueError("Invalid recording duration")
        event_ids = set()
        for event in recording["events"]:
            interval(event, recording["duration"])
            if event["id"] in event_ids or event["category"] not in CATEGORIES: raise ValueError("Invalid event identity or category")
            event_ids.add(event["id"])
            if any(not finite(event[key]) for key in ("setupStart", "payoffEnd")) or not 0 <= event["setupStart"] <= event["start"] < event["end"] <= event["payoffEnd"] <= recording["duration"]:
                raise ValueError("Human setup/payoff must surround the event")
            if event.get("speaker") not in SPEAKERS: raise ValueError("Invalid human speaker provenance")
            extra = event.get("maximumExtraSeconds", 2)
            if not finite(extra) or extra < 0: raise ValueError("Invalid human cut tolerance")


def covers(clip, event, complete=False):
    left, right = (event["setupStart"],event["payoffEnd"]) if complete else (event["start"],event["end"])
    return clip["start"] <= left+.05 and clip["end"] >= right-.05


def ratio(successes, total):
    return None if total == 0 else successes/total


def evaluate(labels, run):
    validate_labels(labels)
    if run.get("schema") != "moment-run-1" or not run.get("policy"): raise ValueError("A versioned run is required")
    budget = run.get("budget", {})
    if any(type(budget.get(key)) is not int or budget[key] < 1 for key in ("clips", "review")) or not finite(budget.get("maximumClipSeconds")) or budget["maximumClipSeconds"] <= 0:
        raise ValueError("Declare positive per-recording output, review and clip duration budgets")
    recorded = {row["id"]:row for row in run["recordings"]}
    if len(recorded) != len(run["recordings"]): raise ValueError("Duplicate run recording")
    expected = {row["id"] for row in labels["recordings"] if row["split"] == "test"}
    if not expected or set(recorded) != expected: raise ValueError("Compare exactly the frozen test recordings")
    totals = {category:dict(returned=0,correct=0,events=0,found=0,complete=0,proposalMisses=0,reviewMisses=0,
        knownSpeaker=0,attributedSpeaker=0,correctSpeaker=0,redundant=0) for category in CATEGORIES}
    for recording in labels["recordings"]:
        if recording["split"] != "test": continue
        actual = recorded[recording["id"]]
        if actual["sha256"] != recording["sha256"]: raise ValueError("The run analyzed a different source file")
        if len(actual["clips"]) > budget["clips"] or len(actual["reviewed"]) > budget["review"]:
            raise ValueError("The run exceeded its declared output or review budget")
        for stage in ("clips","proposed","reviewed"):
            for row in actual[stage]: interval(row, recording["duration"])
        if len({clip["id"] for clip in actual["clips"]}) != len(actual["clips"]): raise ValueError("Duplicate output clip identity")
        seen_events = {category:set() for category in CATEGORIES}
        for clip in actual["clips"]:
            if clip.get("speaker") not in SPEAKERS or clip["end"]-clip["start"] > budget["maximumClipSeconds"]:
                raise ValueError("Invalid speaker or output duration")
            if len(set(clip["categories"])) != len(clip["categories"]) or any(category not in CATEGORIES for category in clip["categories"]):
                raise ValueError("Invalid predicted categories")
            for category in clip["categories"]:
                counts = totals[category]; counts["returned"] += 1
                matches = [event for event in recording["events"] if event["category"] == category and covers(clip,event)]
                counts["correct"] += bool(matches)
                counts["redundant"] += bool(matches) and all(event["id"] in seen_events[category] for event in matches)
                seen_events[category].update(event["id"] for event in matches)
                counts["complete"] += any(covers(clip,event,True) and
                    clip["end"]-clip["start"] <= event["payoffEnd"]-event["setupStart"]+event.get("maximumExtraSeconds",2) for event in matches)
                known = [event for event in matches if event.get("speaker") in ("CreatorSpeech","GameDialogue")]
                counts["knownSpeaker"] += bool(known)
                counts["attributedSpeaker"] += bool(known) and clip.get("speaker") in ("CreatorSpeech","GameDialogue")
                counts["correctSpeaker"] += any(clip.get("speaker") == event["speaker"] for event in known)
        for event in recording["events"]:
            counts = totals[event["category"]]; counts["events"] += 1
            counts["found"] += any(event["category"] in clip["categories"] and covers(clip,event) for clip in actual["clips"])
            proposed = any(covers(clip,event,True) for clip in actual["proposed"])
            counts["proposalMisses"] += not proposed
            counts["reviewMisses"] += proposed and not any(covers(clip,event,True) for clip in actual["reviewed"])
    return {category:{**counts,"precision":ratio(counts["correct"],counts["returned"]),
        "recall":ratio(counts["found"],counts["events"]), "completeCutAcceptance":ratio(counts["complete"],counts["returned"]),
        "uniqueEventPrecision":ratio(counts["correct"]-counts["redundant"],counts["returned"]),
        "speakerAccuracy":ratio(counts["correctSpeaker"],counts["attributedSpeaker"]),
        "speakerCoverage":ratio(counts["attributedSpeaker"],counts["knownSpeaker"])} for category,counts in totals.items()}


def compare(labels, baseline, candidate):
    validate_labels(labels)
    if any(baseline.get(key) != candidate.get(key) or baseline.get(key) is None for key in ("hardware","budget")):
        raise ValueError("Compare at the same hardware and output/review budget")
    return {"schema":"moment-comparison-1", "labelsSha256":digest(labels), "baselineSha256":digest(baseline),
        "candidateSha256":digest(candidate), "baseline":evaluate(labels,baseline), "candidate":evaluate(labels,candidate),
        "automaticallyPromoted":False, "limitation":"Descriptive results on the supplied human-reviewed test set; inspect category counts and recording diversity before judging a general improvement."}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    for name in ("labels","baseline","candidate","output"): parser.add_argument("--"+name,required=True,type=Path)
    args = parser.parse_args()
    result = compare(*(json.loads(path.read_text(encoding="utf-8-sig")) for path in (args.labels,args.baseline,args.candidate)))
    if args.output.resolve() in {path.resolve() for path in (args.labels,args.baseline,args.candidate)}:
        raise ValueError("The evaluation report must not replace its inputs")
    args.output.write_text(json.dumps(result,indent=2,allow_nan=False),encoding="utf-8")
