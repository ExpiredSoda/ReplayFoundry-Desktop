"""Bounded, per-stream acoustic observations. Similarity is not a speaker or emotion verdict."""
from __future__ import annotations
import hashlib
import json
import math
from pathlib import Path
import wave

VERSION = "audio-evidence-1"
RATE = 48000
LABELS = {
    "Speech": "The sound of a person speaking normally.",
    "Laughter": "The sound of a person laughing or chuckling.",
    "Shouting": "The sound of a person shouting or exclaiming loudly.",
    "Crying": "The sound of a person crying or sobbing.",
    "Music": "The sound of background music.",
    "Gunfire": "The sound of rapid gunfire in a video game.",
    "Explosion": "The sound of an explosion in a video game.",
    "Interface": "The sound of electronic menu beeps and interface clicks.",
    "Movement": "The sound of footsteps and movement.",
    "Silence": "A quiet recording with no distinct sounds.",
}
POLICY_HASH = hashlib.sha256(json.dumps({"version":VERSION,"rate":RATE,"labels":LABELS,
    "sampling":"five-second-windows-stratified-and-anchored-1","input":"ten-second-zero-padding-1"},
    sort_keys=True).encode()).hexdigest()


def verify_model(directory):
    lock = json.loads(Path(__file__).resolve().parent.parent.joinpath("audio-evidence-model-lock.json").read_text())
    for name, expected in lock["files"].items():
        path = Path(directory) / name
        digest = hashlib.sha256() if len(expected) == 64 else hashlib.sha1()
        if len(expected) == 40: digest.update(b"blob " + str(path.stat().st_size).encode() + b"\0")
        with path.open("rb") as source:
            while block := source.read(1024 * 1024): digest.update(block)
        if digest.hexdigest() != expected: raise ValueError("Audio model identity changed: " + name)
    return hashlib.sha256(json.dumps(lock, sort_keys=True).encode()).hexdigest()


def window_starts(duration, anchors=(), maximum=12):
    if not math.isfinite(duration) or not 0 < duration <= 1200: raise ValueError("Invalid audio duration")
    if type(maximum) is not int or maximum < 2: raise ValueError("Invalid audio window budget")
    length = min(5., duration)
    focused = [min(duration-length, max(0., float(time)-length/2)) for time in anchors
               if type(time) in (int, float) and math.isfinite(time) and 0 <= time < duration]
    # Retain coverage across the cut; up to half of the budget may inspect anchors.
    chosen = list(dict.fromkeys(round(value, 4) for value in focused))[:maximum//2]
    count = min(maximum-len(chosen), math.ceil(duration/length))
    uniform = [(duration-length)*index/max(1, count-1) for index in range(count)]
    for value in uniform:
        if all(abs(value-other) >= .5 for other in chosen): chosen.append(round(value, 4))
    return sorted(chosen), length


def read_pcm(track, duration):
    import numpy as np
    path = Path(track["path"])
    if path.stat().st_size > math.ceil(duration+.1)*RATE*2 + 65536:
        raise ValueError("Audio artifact exceeds its bounded review")
    if hashlib.sha256(path.read_bytes()).hexdigest() != track["sha256"]:
        raise ValueError("Audio input changed")
    with wave.open(str(path), "rb") as source:
        if source.getnchannels() != 1 or source.getsampwidth() != 2 or source.getframerate() != RATE:
            raise ValueError("Expected 48 kHz mono PCM16 audio evidence")
        data = np.frombuffer(source.readframes(source.getnframes()), dtype="<i2").astype(np.float32)/32768
    if abs(len(data)/RATE-duration) > .15: raise ValueError("Audio evidence duration changed")
    return data


class AcousticClassifier:
    def __init__(self, directory):
        self.identity = verify_model(directory)
        import torch
        from transformers import ClapModel, ClapProcessor
        self.torch = torch
        self.device = "cuda" if torch.cuda.is_available() else "cpu"
        self.model = ClapModel.from_pretrained(directory, local_files_only=True, weights_only=True).to(self.device).eval()
        self.processor = ClapProcessor.from_pretrained(directory, local_files_only=True)
        with torch.inference_mode():
            encoded = self.processor(text=list(LABELS.values()), return_tensors="pt", padding=True).to(self.device)
            values = self.model.get_text_features(**encoded)
            self.text = values.pooler_output if hasattr(values, "pooler_output") else values
            self.text = torch.nn.functional.normalize(self.text, dim=-1)

    def classify(self, samples):
        import numpy as np
        # Unfused CLAP consumes ten seconds. Deterministic padding avoids random crops.
        padded = np.pad(samples, (0, max(0, RATE*10-len(samples))))[:RATE*10]
        encoded = self.processor(audio=padded, sampling_rate=RATE, return_tensors="pt").to(self.device)
        with self.torch.inference_mode():
            values = self.model.get_audio_features(**encoded)
            values = values.pooler_output if hasattr(values, "pooler_output") else values
            similarity = (self.torch.nn.functional.normalize(values, dim=-1) @ self.text.T)[0].float().cpu().tolist()
        if any(not math.isfinite(value) for value in similarity): raise ValueError("Invalid acoustic similarity")
        return {label: round(value, 5) for label, value in zip(LABELS, similarity)}

    def close(self):
        del self.model, self.text
        if self.torch.cuda.is_available(): self.torch.cuda.empty_cache()


def analyze(case, classifier=None):
    import numpy as np
    prepared = case.get("audio", {"status":"Unavailable", "tracks":[]})
    duration = float(case["end"])-float(case["start"])
    starts, length = window_starts(duration, case.get("eventAnchors", []))
    result = {"version":VERSION, "status":prepared["status"], "modelIdentity":getattr(classifier, "identity", None),
              "similaritiesAreProbabilities":False, "tracks":[]}
    if prepared["status"] in ("Unavailable", "NoAudio"): return result
    hashes = {}
    for track in prepared["tracks"]:
        samples = read_pcm(track, duration)
        duplicate = hashes.get(track["sha256"])
        windows = []
        for index, start in enumerate(starts):
            stop = min(duration, start+length)
            data = samples[round(start*RATE):round(stop*RATE)]
            if len(data) == 0: continue
            rms = float(np.sqrt(np.mean(data.astype(np.float64)**2)))
            peak = float(np.max(np.abs(data)))
            windows.append({"id":f'audio-{track["streamIndex"]}-{index}', "start":start, "end":round(stop,4),
                "rmsDb":round(20*math.log10(max(rms, 1e-8)), 2), "peakDb":round(20*math.log10(max(peak, 1e-8)), 2),
                "similarities":classifier.classify(data) if classifier is not None and duplicate is None and rms > 1e-5 else {}})
        hashes[track["sha256"]] = track["streamIndex"]
        result["tracks"].append({"streamIndex":track["streamIndex"], "role":track["role"], "roleSource":track["roleSource"],
            "audioSha256":track["sha256"], "duplicateOf":duplicate, "speech":track["speech"], "windows":windows})
    result["status"] = "Analyzed" if classifier is not None else "AcousticOnly"
    return result


def analyze_batch(cases, model_directory=None):
    classifier = None
    if model_directory is not None and Path(model_directory).is_dir() and any(case.get("audio",{}).get("tracks") for case in cases):
        classifier = AcousticClassifier(model_directory)
    try: return [analyze(case, classifier) for case in cases]
    finally:
        if classifier is not None: classifier.close()
