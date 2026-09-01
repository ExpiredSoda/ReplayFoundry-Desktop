"""Frozen Prompt 2.3 editorial constants."""
from __future__ import annotations

import re

SCHEMA_VERSION = "visual-semantic-editorial-observation-2.0"
CANONICALIZATION_POLICY_VERSION = (
    "visual-semantic-editorial-canonicalization-1.3"
)
WIRE_REPRESENTATION_VERSION = "visual-semantic-editorial-wire-1.1"
WIRE_ROOT_KEYS = {"t", "v", "x", "e"}
MAX_CHANGES = 6
MAX_INTERVALS = 6
MAX_UNCERTAINTIES = 8
MAX_DETAIL = 240
MAX_RATIONALE = 400
ID_PATTERN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$")
CONTENT_TYPES = {
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
TERNARY = {"Yes", "No", "Unsure"}
EVIDENCE_BASIS = {"Visual", "TranscriptContext", "Both"}
TRANSCRIPT_SUPPORT = {
    "Supports",
    "DoesNotSupport",
    "NotSupplied",
    "UnreliableOrAmbiguous",
}
DISPOSITIONS = {"Keep", "Reject", "Unsure"}
REJECT_REASONS = {
    "RoutineTraversal",
    "MenuOrInventoryOnly",
    "NoObservablePayoff",
    "AmbientChangeOnly",
    "MissingRequiredContext",
    "NoDistinctEvent",
    "InsufficientEvidence",
    "None",
}
UNCERTAINTY_ORDER = (
    "InsufficientVisualEvidence",
    "AmbiguousEventBoundary",
    "TranscriptMayBeInaccurate",
    "OccludedOrObscured",
    "FrameSamplingMayMissBriefEvent",
    "CompositionRegionUnavailable",
    "TranscriptContextContradictory",
    "Other",
)
UNCERTAINTY_CODES = set(UNCERTAINTY_ORDER)
ROOT_KEYS = {
    "observableContentType",
    "hasDistinctEvent",
    "hasObservablePayoff",
    "routineTraversalOrMenuOnly",
    "candidateRequiresMissingContext",
    "candidateContainsOnlyAmbientChange",
    "transcriptContextSupport",
    "observedChanges",
    "evidenceIntervals",
    "uncertaintyReasons",
    "editorialDisposition",
    "rejectReason",
    "dispositionRationale",
}
PROHIBITED_REASONING = (
    "chain of thought",
    "chain-of-thought",
    "step-by-step reasoning",
    "step by step reasoning",
    "my hidden reasoning",
    "internal reasoning",
)
MENU_TERMS = (
    "menu",
    "map",
    "inventory",
    "settings",
    "loading",
    "static overlay",
    "static-overlay",
)
