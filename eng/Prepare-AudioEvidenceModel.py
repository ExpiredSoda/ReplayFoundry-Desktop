"""Fetch only the pinned public CLAP weights/tokenizer; the application itself stays offline."""
from pathlib import Path
import argparse
import hashlib
import json
import urllib.request

root = Path(__file__).resolve().parent.parent


def fingerprint(path, expected):
    data = path.read_bytes()
    return (hashlib.sha256(data).hexdigest() if len(expected) == 64 else
            hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest())


def prepare(destination):
    lock = json.loads((root / "src/ReplayFoundry.VisualSemanticHost/audio-evidence-model-lock.json").read_text())
    destination = destination.resolve()
    if not destination.is_relative_to(root):
        raise ValueError("Prepare the model inside the repository workspace.")
    destination.mkdir(parents=True, exist_ok=True)
    for name, expected in lock["files"].items():
        output = destination / name
        if output.is_file() and fingerprint(output, expected) == expected:
            continue
        url = f'https://huggingface.co/{lock["repository"]}/resolve/{lock["revision"]}/{name}'
        print("Downloading", name, flush=True)
        temporary = output.with_suffix(output.suffix + ".download")
        with urllib.request.urlopen(url, timeout=60) as response, temporary.open("wb") as target:
            while chunk := response.read(1024 * 1024):
                target.write(chunk)
        if fingerprint(temporary, expected) != expected:
            raise ValueError("Pinned audio model checksum mismatch: " + name)
        temporary.replace(output)
    # Store a model-specific notice; third-party notices are retained by cleanup.
    license_text = urllib.request.urlopen("https://www.apache.org/licenses/LICENSE-2.0.txt", timeout=30).read()
    (destination / "LICENSE.txt").write_bytes(license_text)
    (destination / "model-provenance.json").write_text(json.dumps(lock, indent=2), encoding="utf-8")
    print("Verified audio evidence model:", destination, flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("destination", type=Path)
    prepare(parser.parse_args().destination)
