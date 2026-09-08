"""Reproducible Accelerate 1.14.0 backport for GHSA-4j2p-28q2-5m79.

Builds a separately identified wheel; never edits an installed or sealed pack.
The original upstream wheel and all unchanged files retain their exact bytes.
"""
import argparse
import base64
import csv
import hashlib
import io
from pathlib import Path
import zipfile

UPSTREAM_SHA256 = "e94390c2863b873be18f623f9df48a0d8fe5eff13ea7f1a00092b0a7904888c6"
MODELING_SHA256 = "3f603a594d95afcf76df81bb487097095b3282b6aaab50728e691a2a6b17db80"
VERSION = "1.14.0+replayfoundry.1"
OLD = """        checkpoint_files = sorted(list(set(index.values())))
        checkpoint_files = [os.path.join(checkpoint_folder, f) for f in checkpoint_files]
"""
NEW = """        # Replay Foundry security backport: GHSA-4j2p-28q2-5m79.
        # HF sharded checkpoints use regular sibling files. Never follow an
        # index entry outside this folder, into a device, directory, or symlink.
        shard_names = list(index.values())
        if any(
            not isinstance(name, str) or not name or name in (".", "..")
            or any(character in name for character in ("/", "\\\\", ":", "\\0"))
            for name in shard_names
        ):
            raise ValueError("Checkpoint shard must be a plain sibling filename.")
        checkpoint_files = []
        checkpoint_folder_real = os.path.normcase(os.path.realpath(checkpoint_folder))
        for name in sorted(set(shard_names)):
            candidate = os.path.join(checkpoint_folder, name)
            resolved = os.path.realpath(candidate)
            if (
                os.path.normcase(os.path.dirname(resolved)) != checkpoint_folder_real
                or os.path.islink(candidate) or not os.path.isfile(resolved)
            ):
                raise ValueError("Checkpoint shard must be a regular file inside its folder.")
            checkpoint_files.append(resolved)
"""


def build(source: Path, destination: Path) -> Path:
    if hashlib.sha256(source.read_bytes()).hexdigest() != UPSTREAM_SHA256:
        raise ValueError("Expected the exact official Accelerate 1.14.0 wheel.")
    with zipfile.ZipFile(source) as archive:
        files = {name: archive.read(name) for name in archive.namelist()}
    modeling_path = "accelerate/utils/modeling.py"
    if hashlib.sha256(files[modeling_path]).hexdigest() != MODELING_SHA256:
        raise ValueError("Upstream checkpoint loader identity differs.")
    text = files[modeling_path].decode("utf-8")
    if text.count(OLD) != 1:
        raise ValueError("Expected one known checkpoint-loading site.")
    files[modeling_path] = text.replace(OLD, NEW).encode("utf-8")
    files["accelerate/__init__.py"] = files["accelerate/__init__.py"].replace(
        b'__version__ = "1.14.0"', f'__version__ = "{VERSION}"'.encode())
    old_info = "accelerate-1.14.0.dist-info/"
    new_info = f"accelerate-{VERSION}.dist-info/"
    files = {name.replace(old_info, new_info): value for name, value in files.items()}
    metadata = new_info + "METADATA"
    files[metadata] = files[metadata].replace(b"Version: 1.14.0\n", f"Version: {VERSION}\n".encode())
    files[new_info + "REPLAYFOUNDRY-SECURITY-PATCH.txt"] = (
        "GHSA-4j2p-28q2-5m79 / CVE-2026-69112\n"
        "Local backport: reject non-sibling, symlink and non-regular checkpoint shards.\n"
        "Upstream: https://github.com/huggingface/accelerate/tree/v1.14.0\n"
        "Advisory: https://github.com/advisories/GHSA-4j2p-28q2-5m79\n"
        "Reproduction: eng/New-AccelerateSecurityWheel.py in ReplayFoundry public source.\n"
        f"Original wheel SHA-256: {UPSTREAM_SHA256}\n"
    ).encode()
    record = new_info + "RECORD"
    files.pop(record)
    stream = io.StringIO(newline="")
    writer = csv.writer(stream, lineterminator="\n")
    for name, content in sorted(files.items()):
        digest = base64.urlsafe_b64encode(hashlib.sha256(content).digest()).decode().rstrip("=")
        writer.writerow([name, "sha256=" + digest, len(content)])
    writer.writerow([record, "", ""])
    files[record] = stream.getvalue().encode()
    destination.mkdir(parents=True, exist_ok=True)
    output = destination / f"accelerate-{VERSION}-py3-none-any.whl"
    with zipfile.ZipFile(output, "x", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, content in sorted(files.items()):
            entry = zipfile.ZipInfo(name, (2026, 9, 8, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.external_attr = 0o100644 << 16
            archive.writestr(entry, content)
    print(output)
    print("Patched checkpoint loader SHA-256:", hashlib.sha256(files[modeling_path]).hexdigest())
    print("Wheel SHA-256:", hashlib.sha256(output.read_bytes()).hexdigest())
    return output


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    build(args.source, args.destination)
