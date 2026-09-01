"""ReplayFoundry local visual-semantic host implementation module."""
from __future__ import annotations

from __future__ import annotations

import argparse
import copy
import gc
import hashlib
import importlib.metadata
import json
import math
import os
import re
import sys
import tempfile
import time
from datetime import datetime, timezone
from decimal import Decimal, InvalidOperation
from pathlib import Path
from typing import Any, NamedTuple, NoReturn


# These variables must be set before importing Hugging Face or Qwen modules.
os.environ["HF_HUB_OFFLINE"] = "1"
os.environ["TRANSFORMERS_OFFLINE"] = "1"
os.environ["HF_DATASETS_OFFLINE"] = "1"
os.environ["HF_HUB_DISABLE_TELEMETRY"] = "1"
os.environ["DO_NOT_TRACK"] = "1"
os.environ["TOKENIZERS_PARALLELISM"] = "false"
os.environ["TRANSFORMERS_NO_ADVISORY_WARNINGS"] = "1"
os.environ["FORCE_QWENVL_VIDEO_READER"] = "torchcodec"


HOST_VERSION = "0.5A.9"
MODEL_REPOSITORY = "Qwen/Qwen3-VL-4B-Instruct"
MODEL_REVISION = "ebb281ec70b05090aa6165b016eac8ec08e71b17"
MODEL_ARCHITECTURE = "Qwen3VLForConditionalGeneration"
MODEL_TYPE = "qwen3_vl"
MODEL_DTYPE = "bfloat16"

INPUT_SCHEMA = "visual-semantic-input-batch-1.0"
PROMPT_MANIFEST_SCHEMA = "visual-semantic-prompt-manifest-1.0"
MODEL_MANIFEST_SCHEMA = "visual-semantic-model-manifest-1.0"
PROBE_SCHEMA = "qwen3-vl-host-probe-1.0"
OBSERVATION_SCHEMA = "visual-semantic-observation-1.0"
OUTPUT_SCHEMA = "visual-semantic-observation-batch-1.5"
ATTEMPT_SCHEMA = "visual-semantic-provider-attempt-batch-1.0"
RAW_OUTPUT_AUDIT_SCHEMA = "visual-semantic-raw-output-audit-1.2"
FAILURE_SCHEMA = "visual-semantic-host-failure-1.4"
GENERATION_MANIFEST_SCHEMA = "visual-semantic-generation-manifest-1.0"
SAMPLING_AUDIT_SCHEMA = "visual-semantic-sampling-audit-1.0"
EXECUTION_TIMING_SCHEMA = "visual-semantic-execution-timing-1.0"
CANDIDATE_SAMPLING_COVERAGE_POLICY = (
    "candidate-sampling-coverage-1.0"
)
AUTHORITATIVE_SAMPLING_TIMING_SOURCE = (
    "TorchCodecFrameBatchActualPtsAndDuration"
)

PROMPT_NAME = "ReplayFoundry Visual Semantic Observation Prompt"
PROMPT_VERSION = "1.0"
PROMPT_FILE_NAME = "replayfoundry-visual-semantic-observation-prompt-1.0.txt"
PROMPT_SHA256 = "18c738c006b638e770ee0e69efafe43770939ae3528d79220ef253679564e8c9"
NORMALIZATION_POLICY_VERSION = (
    "visual-semantic-output-normalization-1.1"
)
NORMALIZATION_POLICY_FILE_NAME = (
    "replayfoundry-visual-semantic-output-normalization-policy-1.1.txt"
)
NORMALIZATION_POLICY_SHA256 = (
    "51a3d6b67ca18546b38aa4c63d698bd1f499fc2d7330bf9090c83dfa429c98d8"
)
GENERATION_POLICY_VERSION = "visual-semantic-generation-budget-1.0"
GENERATION_POLICY_FILE_NAME = (
    "replayfoundry-visual-semantic-generation-budget-policy-1.0.txt"
)
GENERATION_POLICY_SHA256 = (
    "42813a9b29ff774343cf9a2fa149d53cef780e1ad7a7fd0ad3e3312858ee9bbd"
)
IDENTITY_BINDING_POLICY_VERSION = (
    "visual-semantic-trusted-identity-binding-1.0"
)
IDENTITY_BINDING_POLICY_FILE_NAME = (
    "replayfoundry-visual-semantic-trusted-identity-binding-policy-1.0.txt"
)
IDENTITY_BINDING_POLICY_SHA256 = (
    "3512b5e94caaa50f8eb6d241d02048a02424ebb078076489fe84599349b309c6"
)

DEVICE = "cuda:0"
VIDEO_BACKEND = "torchcodec"
BACKEND = f"transformers+{VIDEO_BACKEND}"
VIDEO_DECODE_DEVICE = "cpu"

# The policy is deliberately bounded for one local 16 GiB research GPU.
MAX_BATCH_CASES = 30
MAX_INPUT_JSON_BYTES = 32 * 1024 * 1024
VIDEO_POLICY_SCHEMA = "visual-semantic-video-policy-1.1"
VIDEO_SAMPLING_POLICY = "uniform-fps-bounded-1.0"
VIDEO_TRIM_POLICY = "virtual-source-offset-1.0"
MAX_INPUT_DURATION_SECONDS = Decimal("70")
VIDEO_MAX_WIDTH = 640
VIDEO_MAX_HEIGHT = 640
VIDEO_MAX_PIXELS_PER_FRAME = 131_072
VIDEO_MIN_FRAMES = 4
VIDEO_MAX_FRAMES = 32
VIDEO_FPS = 0.5
VIDEO_TOTAL_PIXEL_BUDGET = 4_194_304
# Retained MKV timestamps in the frozen corpus use a 1 ms stream time base.
# This is used only to distinguish an audio/container tail from lost video
# candidate coverage; it never widens candidate start/end coverage.
CONTAINER_TIMESTAMP_RESOLUTION_TOLERANCE_SECONDS = 0.001
LEGACY_DIAGNOSTIC_MAX_NEW_TOKENS = 768
GROUNDED_EDITORIAL_MAX_NEW_TOKENS = 768
ACTIVE_POLICY_MAX_NEW_TOKENS = 2048
# PHASE-A GATE: keep the only runtime generation path at the legacy ceiling
# until the frozen ordinal-6 diagnostic proves budget exhaustion. After the
# external Phase-A artifact is frozen and accepted, change only this assignment
# to ACTIVE_POLICY_MAX_NEW_TOKENS. This is not a CLI-selectable fallback.
MAX_NEW_TOKENS = ACTIVE_POLICY_MAX_NEW_TOKENS
NUMBER_OF_BEAMS = 1
MAX_RAW_AUDIT_TEXT_BYTES = 64 * 1024
MAX_FAILURE_DIAGNOSTICS = 8
MAX_FAILURE_DIAGNOSTIC_LENGTH = 500
MAX_FAILURE_MESSAGE_LENGTH = 2_000

MAX_VISIBLE_STATE_CHANGE = 320
MAX_RATIONALE = 400
MAX_EVIDENCE_INTERVALS = 6
MAX_UNCERTAINTIES = 8
MAX_LIMITATIONS = 4
MAX_DETAIL_TEXT = 240

EXPECTED_PACKAGE_VERSIONS = {
    "torch": "2.12.0+cu130",
    "torchvision": "0.27.0+cu130",
    "transformers": "4.57.6",
    "accelerate": "1.14.0",
    "qwen-vl-utils": "0.0.14",
    "torchcodec": "0.15.0+cpu",
    "psutil": "7.2.2",
}

REQUIRED_FFMPEG_DLL_PREFIXES = (
    "avcodec-",
    "avformat-",
    "avutil-",
    "swresample-",
    "swscale-",
)

EXPECTED_WEIGHT_FILES = {
    "model-00001-of-00002.safetensors": (
        4_967_229_296,
        "30a01a0556622645a3cce87b655bbbbbc1f170c196099f1b666c93202c3339a9",
    ),
    "model-00002-of-00002.safetensors": (
        3_908_490_048,
        "046296a2a387efb43b0c997d5833c789604d168834f6e0d3064bf7bb13d002a6",
    ),
}

REQUIRED_MODEL_FILES = {
    "chat_template.json",
    "config.json",
    "generation_config.json",
    "merges.txt",
    "model-00001-of-00002.safetensors",
    "model-00002-of-00002.safetensors",
    "model.safetensors.index.json",
    "preprocessor_config.json",
    "tokenizer.json",
    "tokenizer_config.json",
    "video_preprocessor_config.json",
    "vocab.json",
}

OBSERVABLE_CONTENT_TYPES = {
    "Action",
    "Dialogue",
    "Discovery",
    "Failure",
    "Humor",
    "Story",
    "MenuOrTraversal",
    "Cinematic",
    "Other",
    "Unknown",
}

YES_NO_UNSURE = {"Yes", "No", "Unsure"}
YES_NO_UNKNOWN = {"Yes", "No", "Unknown"}
REVIEW_CERTAINTIES = {"High", "Medium", "Low"}
UNCERTAINTY_CODE_ORDER = (
    "InsufficientVisualEvidence",
    "AmbiguousEventBoundary",
    "TranscriptMayBeInaccurate",
    "OccludedOrObscured",
    "FrameSamplingMayMissBriefEvent",
    "CompositionRegionUnavailable",
    "SpokenContentNotDirectlyObserved",
    "Other",
)
UNCERTAINTY_CODES = set(UNCERTAINTY_CODE_ORDER)

PROVIDER_OBSERVATION_KEYS = {
    "caseId",
    "candidateId",
    "schemaVersion",
    "observableContentType",
    "visibleStateChange",
    "hasClearBeginning",
    "hasClearOutcome",
    "menuOrTraversalPresent",
    "spokenContentAppearsRelevant",
    "suggestedWorthReviewing",
    "reviewCertainty",
    "evidenceIntervals",
    "uncertainties",
    "limitations",
    "conciseRationale",
}

CANDIDATE_MODES = {"StandaloneClip", "MontageSegment"}
TRANSCRIPT_POLICIES = {"FullContextV1", "VisualOnlyV1"}
TRANSCRIPT_STATUSES = {
    "LexicalText",
    "NonSpeechTokenOnly",
    "EmptyProviderOutput",
}
TRANSCRIPT_TIMING_PRECISIONS = {
    "SegmentApproximate",
    "SegmentBoundaryClamped",
    "HumanReviewedReference",
    "Unknown",
}
COMPOSITION_ROLES = {"Gameplay", "Presenter", "ChatOrText", "Overlay", "Unknown"}
COMPOSITION_VALUE_SOURCES = {
    "NotAvailable",
    "UserConfirmed",
    "RecordingProfile",
    "AutomaticAnalyzer",
    "DefaultAssumption",
}
COMPOSITION_COORDINATE_SPACE = "EffectiveDisplayNormalizedBeforeCrop"
INTEGRITY_STATUSES = {
    "Clear",
    "FullFrameBlack",
    "FullFrameFrozen",
    "FullFrameBlackAndFrozen",
}

FORBIDDEN_INPUT_KEYS = {
    "proxy",
    "proxylabel",
    "proxyworthclipping",
    "worthclipping",
    "knownoutcome",
    "historicaloutcome",
    "expectedoutcome",
    "expectedmatch",
    "falsepositive",
    "deterministicscore",
    "heuristicscore",
    "score",
    "scores",
    "scorecomponents",
    "rank",
    "ranking",
    "selected",
    "selectionstate",
    "disposition",
    "failureattribution",
    "reviewnotes",
    "reviewsummary",
    "futureholdout",
    "benchmarkmetrics",
}

SHA256_PATTERN = re.compile(r"^[0-9a-fA-F]{64}$")
SAFE_ID_PATTERN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,199}$")

FAILURE_STAGES = (
    "ArgumentValidation",
    "PathValidation",
    "LibraryConfiguration",
    "InputLoading",
    "InputValidation",
    "RuntimeInitialization",
    "VideoSampling",
    "DirectTorchCodecDecode",
    "SamplingComparison",
    "ModelInitialization",
    "Inference",
    "Generation",
    "OutputSafety",
    "OutputValidation",
    "MediaRevalidation",
    "OutputWrite",
    "AttemptCompletedWithCaseFailures",
)

SAMPLING_ROOT_CAUSE_CODES = (
    "InferredTimestampDrift",
    "ActualPtsOutsideReview",
    "RequestedTrimNotHonored",
    "CandidateCoordinateMismatch",
    "ReviewMediaTimelineMismatch",
    "AverageFpsInvalidForPtsMapping",
    "CandidateHasNoSampledFrame",
    "CandidateHasOnlyOneSampledFrame",
    "CandidateStartCoverageInsufficient",
    "CandidateEndCoverageInsufficient",
    "SourceFrameMetadataMismatch",
    "SamplingCardinalityMismatch",
    "QwenTensorAndDirectTorchCodecFrameMismatch",
    "Other",
)

SAMPLING_DECISION_SUPPORT_CODES = (
    "LegacyAndActualCoveragePass",
    "LegacyPassActualPtsFail",
    "LegacyFailActualPtsPass",
    "LegacyAndActualCoverageFail",
    "AuditFailed",
)

INPUT_POLICY_VALIDITY_DECISIONS = (
    "TimestampValidationDefectOnly",
    "CoordinateMappingDefect",
    "FrozenSamplingPolicyInvalid",
    "SamplingAuditInconclusive",
)

LEGACY_TIMING_VALIDATION_REASONS = (
    "NominalTimestampOutsideReview",
    "NominalCandidateCoverageInsufficient",
)



__all__ = [name for name in globals() if not name.startswith("__")]

HOST_DIRECTORY = Path(__file__).resolve().parent.parent
HOST_ENTRY_PATH = HOST_DIRECTORY / "qwen3_vl_batch_host.py"
__all__ = [name for name in globals() if not name.startswith("__")]
