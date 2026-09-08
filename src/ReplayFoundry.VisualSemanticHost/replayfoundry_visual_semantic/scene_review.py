"""Grounded, timed scene review; deliberately separate from the legacy compact wire."""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import subprocess
import tempfile
import time

from .recording_index import fingerprint, write_atomic

VERSION = "scene-review-1.4"
FRAME_COUNT = 12
KINDS = ["Action", "Dialogue", "Discovery", "Failure", "Humor", "Story", "MenuOrTraversal", "Cinematic", "Other", "Unknown"]
FLAGS = ["hasDistinctEvent", "hasPayoff", "onlyRoutineMovementOrMenus", "needsEarlierContext", "onlyLightingOrCameraChanges"]
SUPPORT = ["Supports", "DoesNotSupport", "NotSupplied", "UnreliableOrAmbiguous"]


class SceneClaimsUnverifiedError(ValueError):
    """The model already exhausted its bounded factual correction attempt."""


def validate(value, frame_count, has_transcript):
    if not isinstance(value, dict) or set(value) != {"setup", "event", "outcome", "firstFrame", "lastFrame", "kind", "transcriptSupport", "editorialValue", "recommendation", *FLAGS}:
        raise ValueError("Invalid scene review shape")
    for field in ("setup", "event", "outcome"):
        if not isinstance(value[field], str) or not value[field].strip() or len(value[field]) > 180:
            raise ValueError("Invalid scene evidence description")
    for field in ("firstFrame", "lastFrame"):
        if type(value[field]) is not int or not 0 <= value[field] < frame_count:
            raise ValueError("Scene evidence references an unavailable frame")
    if value["firstFrame"] > value["lastFrame"]:
        raise ValueError("Scene evidence is not chronological")
    if value["kind"] not in KINDS or value["transcriptSupport"] not in SUPPORT or any(value[key] not in ("Yes", "No", "Unsure") for key in FLAGS):
        raise ValueError("Invalid scene labels")
    if not has_transcript and value["transcriptSupport"] != "NotSupplied":
        raise ValueError("Scene review invented transcript support")
    if type(value["editorialValue"]) not in (int,float) or not math.isfinite(value["editorialValue"]) or not 0 <= value["editorialValue"] <= 100 or value["recommendation"] not in ("Keep", "Reject", "Unsure"):
        raise ValueError("Invalid neural editorial judgment")
    return value


def schema(has_transcript=True):
    properties = {"event":{"type":"string","minLength":1,"maxLength":180},
        "firstFrame":{"type":"integer","minimum":0,"maximum":FRAME_COUNT-1},
        "lastFrame":{"type":"integer","minimum":0,"maximum":FRAME_COUNT-1},
        "kind":{"type":"string","enum":KINDS},
        **{key:{"type":"string","enum":["Yes","No","Unsure"]} for key in FLAGS},
        "transcriptSupport":{"type":"string","enum":SUPPORT if has_transcript else ["NotSupplied"]},
        "recommendation":{"type":"string","enum":["Keep","Reject","Unsure"]}}
    properties["firstFrame"] = {"type":"integer", "enum":[0,1]}
    properties["lastFrame"] = {"type":"integer", "enum":[FRAME_COUNT-2,FRAME_COUNT-1]}
    return json.dumps({"type":"object","additionalProperties":False,"properties":properties,"required":list(properties)})


def combine_assessment(states, judgment, has_transcript, editorial_value):
    if not isinstance(states, dict) or set(states) != {"setup", "outcome"}:
        raise ValueError("Invalid opening and closing frame descriptions")
    if not isinstance(judgment, dict) or any(key in judgment for key in ("setup","outcome","editorialValue")):
        raise ValueError("Scene judgment attempted to replace independent frame descriptions")
    value = validate({**judgment, **states, "editorialValue":editorial_value}, FRAME_COUNT, has_transcript)
    if value["firstFrame"] not in (0, 1) or value["lastFrame"] not in (FRAME_COUNT-2, FRAME_COUNT-1):
        raise ValueError("Scene judgment did not cite the independently described frame groups")
    return value


def run(args):
    for name in ("HF_HUB_OFFLINE", "TRANSFORMERS_OFFLINE", "HF_DATASETS_OFFLINE", "HF_HUB_DISABLE_TELEMETRY", "DO_NOT_TRACK"):
        os.environ[name] = "1"
    from .editorial.qualified_cuda_attention import qualified_cuda_attention_context, CACHE_IMPLEMENTATION, require_policy_source, POLICY_SHA256
    from .scene_value import score, PROMPT_HASH as SCORE_PROMPT_HASH
    from .scene_cache import review_key, read_review, save_review

    started = time.perf_counter()
    require_policy_source()
    request = json.loads(Path(args.input).read_text(encoding="utf-8-sig"))
    prompt_root = Path(__file__).resolve().parent.parent
    prompt = prompt_root.joinpath("replayfoundry-scene-review-prompt-1.4.txt").read_text(encoding="utf-8").strip()
    fact_prompt = prompt_root.joinpath("replayfoundry-scene-fact-check-prompt-1.2.txt").read_text(encoding="utf-8").strip()
    fact_prompt_hash = hashlib.sha256(fact_prompt.encode()).hexdigest()
    prompt_hash = hashlib.sha256(prompt.encode()).hexdigest()
    states_prompt = prompt_root.joinpath("replayfoundry-scene-states-prompt-1.0.txt").read_text(encoding="utf-8").strip()
    states_prompt_hash = hashlib.sha256(states_prompt.encode()).hexdigest()
    if request["schemaVersion"] != VERSION or request["promptHash"].lower() != prompt_hash or request["factPromptHash"].lower() != fact_prompt_hash or request["statesPromptHash"].lower() != states_prompt_hash or request["scorePromptHash"].lower() != SCORE_PROMPT_HASH or not 1 <= len(request["cases"]) <= 8:
        raise ValueError("Scene review protocol mismatch")
    if not isinstance(request["modelHash"],str) or len(request["modelHash"]) != 64:
        raise ValueError("Scene review requires its verified model identity")
    pass_diagnostics = []
    rows = []
    def emit():
        write_atomic(Path(args.output), {"schemaVersion":VERSION,"modelHash":request["modelHash"],
            "promptHash":prompt_hash,"factPromptHash":fact_prompt_hash,"statesPromptHash":states_prompt_hash,
            "scorePromptHash":SCORE_PROMPT_HASH,"cases":rows,"cacheHits":sum(row.get("cacheHit",False) for row in rows),
            "elapsedSeconds":time.perf_counter()-started,
            "peakAllocatedGpuBytes":max((item["peakAllocatedBytes"] for item in pass_diagnostics), default=0)})
    cache_directory = getattr(args,"cache",None)
    keys = {case["caseId"]:review_key(request,case,POLICY_SHA256) for case in request["cases"]}
    cached = {}
    for case in request["cases"]:
        cache_started=time.perf_counter()
        row=read_review(cache_directory,keys[case["caseId"]])
        if row is None: continue
        try:
            if row["caseId"] != case["caseId"] or row["inputHash"] != case["inputHash"] or not row["factReview"]["grounded"]:
                continue
            validate(row["assessment"],FRAME_COUNT,bool(case["transcript"]))
            if fingerprint(Path(case["path"])).lower() != case["inputHash"].lower(): continue
            row.update(cacheHit=True,cachedInferenceSeconds=row["elapsedSeconds"],
                elapsedSeconds=time.perf_counter()-cache_started,inferenceDiagnostics=[])
            cached[case["caseId"]]=row
        except (OSError,ValueError,KeyError,TypeError):
            continue
    if len(cached) == len(request["cases"]):
        rows.extend(cached[case["caseId"]] for case in request["cases"])
        emit()
        return
    from PIL import Image
    from .model_runtime import _load_model_and_processor
    from .editorial.structured_decoding import StructuredDecodingSession, model_vocab_size
    from .generation import _normalized_eos_token_ids
    import torch
    import transformers
    model, processor = _load_model_and_processor(Path(args.model), torch, transformers)
    session = StructuredDecodingSession(processor.tokenizer, model_vocab_size(model))
    grammars = {}
    for supplied in (False, True):
        wire = schema(supplied)
        grammars[supplied], _ = session.compile_json_schema(wire, VERSION, hashlib.sha256(wire.encode()).hexdigest(), any_whitespace=False)
    fact_wire = json.dumps({"type":"object", "additionalProperties":False,
        "properties":{"reason":{"type":"string", "maxLength":300}, "grounded":{"type":"boolean"}},
        "required":["reason", "grounded"]})
    fact_grammar, _ = session.compile_json_schema(fact_wire, VERSION, hashlib.sha256(fact_wire.encode()).hexdigest(), any_whitespace=False)
    states_wire = json.dumps({"type":"object", "additionalProperties":False,
        "properties":{key:{"type":"string", "minLength":1, "maxLength":180} for key in ("setup","outcome")},
        "required":["setup","outcome"]})
    states_grammar, _ = session.compile_json_schema(states_wire, VERSION, hashlib.sha256(states_wire.encode()).hexdigest(), any_whitespace=False)
    def generate(messages, grammar, limit, kind):
        pass_started = time.perf_counter()
        torch.cuda.reset_peak_memory_stats()
        inputs = processor.apply_chat_template(messages, tokenize=True, add_generation_prompt=True,
            return_dict=True, return_tensors="pt").to(model.device)
        with torch.inference_mode(), qualified_cuda_attention_context(torch):
            tokens = model.generate(**inputs, max_new_tokens=limit, do_sample=False, use_cache=True, cache_implementation=CACHE_IMPLEMENTATION,
                logits_processor=[session.new_logits_processor(grammar, _normalized_eos_token_ids(model))])
        text = processor.decode(tokens[0,inputs["input_ids"].shape[1]:], skip_special_tokens=True).strip()
        diagnostic = {"stage":"scene-pass", "passKind":kind, "inputTokens":inputs["input_ids"].shape[1],
            "outputTokens":tokens.shape[1]-inputs["input_ids"].shape[1],
            "elapsedSeconds":time.perf_counter()-pass_started, "peakAllocatedBytes":torch.cuda.max_memory_allocated(),
            "reservedBytes":torch.cuda.memory_reserved()}
        pass_diagnostics.append(diagnostic)
        print(json.dumps(diagnostic), flush=True)
        del inputs, tokens
        # Return large unused vision buffers to the driver between independent
        # passes, so a long review does not retain the previous peak allocation.
        if torch.cuda.memory_reserved() - torch.cuda.memory_allocated() > 512 * 1024 * 1024:
            torch.cuda.empty_cache()
        return text
    try:
        for case in request["cases"]:
            if case["caseId"] in cached:
                rows.append(cached[case["caseId"]])
                emit()
                continue
            row_started = time.perf_counter()
            first_pass = len(pass_diagnostics)
            row = {"caseId":case["caseId"], "inputHash":case["inputHash"]}
            images = []
            checks = []
            raw = None
            try:
                source = Path(case["path"]).resolve(strict=True)
                if fingerprint(source).lower() != case["inputHash"].lower():
                    raise ValueError("Review input changed")
                left, right = float(case["start"]), float(case["end"])
                if not math.isfinite(left) or not math.isfinite(right) or left < 0 or right <= left or right > 1200:
                    raise ValueError("Invalid review range")
                times = [round(left + (right-left-min(.15, (right-left)/4))*index/(FRAME_COUNT-1), 4) for index in range(FRAME_COUNT)]
                with tempfile.TemporaryDirectory(prefix="replayfoundry-scene-") as scratch:
                    content = []
                    for index, timestamp in enumerate(times):
                        path = Path(scratch) / f"{index}.jpg"
                        subprocess.run([args.ffmpeg,"-nostdin","-v","error","-ss",str(timestamp),"-i",str(source),
                            "-frames:v","1","-an","-vf","scale=512:-2","-q:v","4",str(path)],
                            check=True, timeout=45, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
                        image = Image.open(path).convert("RGB")
                        images.append(image)
                        content.extend([{"type":"text","text":f"Frame {index} at {timestamp:.4f} seconds:"}, {"type":"image","image":image}])
                    words = case["transcript"]
                    content.append({"type":"text","text":"Speech transcript (may contain recognition errors or game dialogue): " + json.dumps(words, ensure_ascii=False)})
                    score_started = time.perf_counter()
                    torch.cuda.reset_peak_memory_stats()
                    neural_value = score(model, processor, images, words, torch, case["candidateMode"])
                    score_elapsed = time.perf_counter()-score_started
                    pass_diagnostics.append({"stage":"scene-pass","passKind":"neural-relevance","elapsedSeconds":score_elapsed,
                        "peakAllocatedBytes":torch.cuda.max_memory_allocated(),"reservedBytes":torch.cuda.memory_reserved()})
                    feedback = []
                    for attempt in range(2):
                        state_content = [{"type":"text", "text":"Opening state:"}, *content[:4],
                            {"type":"text", "text":"Closing state:"}, *content[2*(FRAME_COUNT-2):2*FRAME_COUNT]]
                        states = json.loads(generate([
                            {"role":"system", "content":[{"type":"text", "text":states_prompt}]},
                            {"role":"user", "content":[*state_content, *feedback]}], states_grammar, 190, "frame-states"))
                        messages = [{"role":"system","content":[{"type":"text","text":prompt}]},
                                    {"role":"user","content":[*content,*feedback]}]
                        raw = generate(messages, grammars[bool(words)], 320, "scene-judgment")
                        assessment = combine_assessment(states, json.loads(raw), bool(words), neural_value["value"]*100)
                        claims = json.dumps({key:assessment[key] for key in ("setup", "event", "outcome")}, ensure_ascii=False)
                        check = json.loads(generate([
                            {"role":"system","content":[{"type":"text","text":fact_prompt}]},
                            {"role":"user","content":[*content, {"type":"text","text":"Proposed claims: " + claims}]}], fact_grammar, 160, "fact-check"))
                        if type(check.get("grounded")) is not bool or not isinstance(check.get("reason"), str):
                            raise ValueError("Invalid visual fact check")
                        checks.append(check)
                        if check["grounded"]:
                            row.update(assessment=assessment, factReview=check, inferencePasses=3*(attempt+1)+2,
                                neuralValue=neural_value, scoreElapsedSeconds=score_elapsed)
                            break
                        feedback = [{"type":"text","text":"A separate frame check found an unsupported claim. Correct only what the supplied frames can establish; do not infer an unseen cause or action. Check: " + check["reason"]}]
                    if "assessment" not in row:
                        raise SceneClaimsUnverifiedError("Scene claims remained unsupported after one correction")
                    row["frameTimes"] = times
                if fingerprint(source).lower() != case["inputHash"].lower():
                    raise ValueError("Review input changed")
                row["status"] = "Succeeded"
            except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
                row = {"caseId":case["caseId"], "inputHash":case["inputHash"], "status":"Failed", "errorCode":type(error).__name__, "detail":str(error)[:300]}
                row["factReviews"] = checks
                if os.environ.get("REPLAYFOUNDRY_SCENE_DEBUG") == "1":
                    row["raw"] = raw
            finally:
                for image in images:
                    image.close()
            row["elapsedSeconds"] = time.perf_counter()-row_started
            row["inferenceDiagnostics"] = pass_diagnostics[first_pass:]
            row["cacheHit"] = False
            save_review(cache_directory,keys[case["caseId"]],row)
            rows.append(row)
            emit()
            print(json.dumps({"stage":VERSION,"caseId":case["caseId"],"status":row["status"],"elapsedSeconds":row["elapsedSeconds"]}), flush=True)
    finally:
        del model, processor
        torch.cuda.empty_cache()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    for name in ("input", "output", "model", "ffmpeg"):
        parser.add_argument("--"+name, required=True)
    parser.add_argument("--cache")
    run(parser.parse_args())
