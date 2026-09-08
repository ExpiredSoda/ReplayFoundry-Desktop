"""Content-addressed reuse of completed scene checks, never user training labels."""
from __future__ import annotations
import hashlib
import json
from pathlib import Path
import re
from .recording_index import write_atomic


def digest(value):
    return hashlib.sha256(json.dumps(value,sort_keys=True,ensure_ascii=False,allow_nan=False).encode()).hexdigest()


def review_key(request, case, attention_policy):
    # The review video's full hash binds its pixels/audio; the location of an
    # identical private materialization is deliberately not part of identity.
    return digest({"protocol":{key:request[key] for key in
        ("schemaVersion","modelHash","promptHash","factPromptHash","statesPromptHash","scorePromptHash")},
        "attentionPolicy":attention_policy,"sampling":"12-frames-512px-v1",
        "case":{key:value for key,value in case.items() if key != "path"}})


def read_review(directory, key):
    if directory is None: return None
    path=Path(directory)/(key+".scene.json")
    try:
        if path.stat().st_size > 2*1024*1024: return None
        saved=json.loads(path.read_text(encoding="utf-8"))
        row=saved["row"]
        if saved["key"] != key or saved["rowHash"] != digest(row) or row["status"] != "Succeeded": return None
        return row
    except (OSError,ValueError,KeyError,TypeError):
        return None


def save_review(directory, key, row):
    if directory is None or row.get("status") != "Succeeded": return
    root=Path(directory)
    try:
        root.mkdir(parents=True,exist_ok=True)
        write_atomic(root/(key+".scene.json"),{"key":key,"rowHash":digest(row),"row":row})
        # Keep only this cache's recognisable entries; never touch other files.
        entries=[path for path in root.glob('*.scene.json') if re.fullmatch(r'[0-9a-f]{64}\.scene\.json',path.name)]
        entries.sort(key=lambda path:path.stat().st_mtime,reverse=True)
        used=0
        for index,path in enumerate(entries):
            used += path.stat().st_size
            if index >= 4096 or used > 64*1024*1024: path.unlink(missing_ok=True)
    except (OSError,ValueError):
        pass  # An optional cache must never discard completed model work.
