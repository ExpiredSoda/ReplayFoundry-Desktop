#!/usr/bin/env python3
"""Thin CLI entry point for the ReplayFoundry visual-semantic host."""

from __future__ import annotations

import os

# These values must be set before any Hugging Face or Qwen import.
os.environ["HF_HUB_OFFLINE"] = "1"
os.environ["TRANSFORMERS_OFFLINE"] = "1"
os.environ["HF_DATASETS_OFFLINE"] = "1"
os.environ["HF_HUB_DISABLE_TELEMETRY"] = "1"
os.environ["DO_NOT_TRACK"] = "1"
os.environ["TOKENIZERS_PARALLELISM"] = "false"
os.environ["TRANSFORMERS_NO_ADVISORY_WARNINGS"] = "1"
os.environ["FORCE_QWENVL_VIDEO_READER"] = "torchcodec"

from replayfoundry_visual_semantic.cli import main


if __name__ == "__main__":
    import sys
    if sys.argv[1:] == ["editorial-worker"]:
        from replayfoundry_visual_semantic.editorial.resident_worker import main as worker_main
        raise SystemExit(worker_main())
    raise SystemExit(main())
