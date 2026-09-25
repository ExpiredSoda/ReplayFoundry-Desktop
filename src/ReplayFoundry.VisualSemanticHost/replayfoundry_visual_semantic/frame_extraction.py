"""Bounded frame extraction without decoding already mapped recording sections."""
from __future__ import annotations

import math
from pathlib import Path
import subprocess


def missing_ranges(expected, retained, duration, window_seconds=45):
    """Coalesce adjacent missing windows, preserving their absolute ownership."""
    start = None
    for ordinal in range(expected + 1):
        missing = ordinal < expected and ordinal not in retained
        if missing and start is None:
            start = ordinal
        if not missing and start is not None:
            yield start, ordinal, start * window_seconds, min(duration, ordinal * window_seconds)
            start = None


def extract_index_frames(ffmpeg, source, scratch, expected, retained, duration, roi, context_roi):
    primary, context = {}, {}
    x, y, w, h = roi
    context_crop = ""
    if context_roi is not None:
        cx, cy, cw, ch = context_roi
        context_crop = f"crop=iw*{cw}:ih*{ch}:iw*{cx}:ih*{cy},"
    decoded_seconds = 0.0
    for first, stop, left, right in missing_ranges(expected, retained, duration):
        folder = Path(scratch) / str(first)
        folder.mkdir()
        filters = (f"trim=duration={right-left},fps=1/5,split=2[primary][context];"
            f"[primary]crop=iw*{w}:ih*{h}:iw*{x}:ih*{y},scale=384:-2[game];"
            f"[context]fps=1/15,{context_crop}scale=288:-2[person]")
        subprocess.run([str(ffmpeg), "-nostdin", "-v", "error", "-skip_frame", "nokey",
            "-ss", str(left), "-i", str(source), "-filter_complex", filters,
            "-map", "[game]", "-an", "-q:v", "5", str(folder / "game-%06d.jpg"),
            "-map", "[person]", "-an", "-q:v", "5", str(folder / "context-%06d.jpg")],
            check=True, timeout=1800, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
        games = sorted(folder.glob("game-*.jpg"))
        people = sorted(folder.glob("context-*.jpg"))
        for ordinal in range(first, stop):
            start = (ordinal-first)*45
            end = min(right-left, start+45)
            primary[ordinal] = games[int(start/5):math.ceil(end/5)]
            context[ordinal] = people[int(start/15):math.ceil(end/15)]
        decoded_seconds += right-left
    return primary, context, decoded_seconds


def extract_review_frames(ffmpeg, source, scratch, times):
    """Decode once, selecting the first 10-fps review frame at/after each sample.

    Review media is materialized at a qualified constant 10 fps. Sample times
    map to those frame ordinals, retaining the existing seek semantics.
    Duplicate target frames are intentionally shared, never renumbered.
    """
    ticks = [math.ceil(round(timestamp * 10, 6)) for timestamp in times]
    unique = sorted(set(ticks))
    expression = "+".join(f"eq(n,{tick})" for tick in unique)
    subprocess.run([str(ffmpeg), "-nostdin", "-v", "error", "-i", str(source),
        "-an", "-vf", f"select='{expression}',scale=512:-2", "-fps_mode", "vfr",
        "-frames:v", str(len(unique)), "-q:v", "4", str(Path(scratch)/"review-%03d.jpg")],
        check=True, timeout=120, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
    frames = sorted(Path(scratch).glob("review-*.jpg"))
    if len(frames) != len(unique):
        raise ValueError("Review video did not contain every requested sample")
    by_tick = dict(zip(unique, frames))
    return [by_tick[tick] for tick in ticks]
