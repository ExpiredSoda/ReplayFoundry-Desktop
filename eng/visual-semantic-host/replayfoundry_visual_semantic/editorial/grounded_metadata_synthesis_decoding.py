"""Frozen decoding policy for the duplicate-authorized recovery pool."""
from __future__ import annotations

from dataclasses import dataclass
from typing import Any


POLICY_VERSION = "grounded-editorial-synthesis-recovery-pool-1.9"
POLICY_FILE_NAME = (
    "replayfoundry-grounded-editorial-synthesis-recovery-pool-policy-1.9.txt"
)
# SHA-256 of the normalized policy text beside the host entry point.
POLICY_SHA256 = (
    "65d105bccf11e28c5fe15edf8b8b2d62b14437d8d654a03f960810f1a2ae1af2"
)
TRIGGER = "BoundedSemanticRecoveryActivated"
SOURCE_REASON_ORIGINAL_FIRST_REJECTED = "OriginalFirstRejected"
SOURCE_REASON_PRIMARY_ONLY_CROSS_DRAFT_COPY_WITHHELD = (
    "PrimaryOnlyCrossDraftAudienceCopyWithheld"
)
SOURCE_REASON_CROSS_DRAFT_REJECTED_COPY_WITHHELD = (
    "CrossDraftRejectedAudienceCopyWithheld"
)
SOURCE_REASON_CREATOR_AUTHORITY_REJECTED_COPY_WITHHELD = (
    "CreatorAuthorityRejectedAudienceCopyWithheld"
)
STICKY_GRAMMAR_SOURCE_RULE = "NonRetrospectiveVoice"
LOGICAL_PASS_ORDINAL = 4
POOL_SIZE = 4
SEEDS = (3407, 3408, 3409, 3410)
BATCH_SIZE = 1
DO_SAMPLE = True
NUMBER_OF_BEAMS = 1
USE_CACHE = True
TEMPERATURE = 0.7
TOP_P = 0.8
TOP_K = 20
UNCONSTRAINED_FALLBACK_PERMITTED = False
SEMANTIC_REPAIR_PERMITTED = False
RETRYABLE_COMPLETED_SEMANTIC_REJECTIONS = (
    "ThirdPersonCreatorFraming",
    "UnsupportedCreatorEmbodiment",
    "GenericOpening",
    "UnsupportedInterfaceAttribution",
    "UnsupportedMentalState",
    "UnreviewedTranscriptReuse",
    "TitleDescriptionRepetition",
    "RedundantGameIdentity",
    "AnalysisBookkeeping",
    "OutputLanguage",
    "NonRetrospectiveVoice",
    "IncompleteTitle",
    "CrossDraftTitleContamination",
    "UnstableReadableTextReuse",
    "FirstPersonTitleSubject",
    "GameHashtag",
    "UncoupledKnowledgeReference",
    "UnsupportedTag",
    "TagShape",
    "UnsupportedKnowledgeGrounding",
    "GroundedRefinementUnchanged",
    "UnresolvedVisualGrounding",
    "RerollTitleTooSimilar",
)
RETRYABLE_COMPLETED_SEMANTIC_REJECTIONS_SHA256 = (
    "ddeade8875faf6b75a0ae8a474a7de63493473497b16ab0a37fded81d1b473b7"
)


def rejected_audience_copy_withholding(
    rejected_json: str | None,
    primary_only_evidence: bool,
    rejection_code: str | None,
) -> tuple[bool, str | None]:
    if not rejected_json:
        return False, None
    if rejection_code == "CrossDraftTitleContamination" and primary_only_evidence:
        return True, SOURCE_REASON_CROSS_DRAFT_REJECTED_COPY_WITHHELD
    if rejection_code == "UnsupportedCreatorEmbodiment":
        return True, SOURCE_REASON_CREATOR_AUTHORITY_REJECTED_COPY_WITHHELD
    return False, None


@dataclass(frozen=True)
class GroundedMetadataSynthesisDecoding:
    policy_version: str
    policy_sha256: str
    trigger: str
    logical_pass_ordinal: int
    candidate_ordinal: int
    batch_size: int
    do_sample: bool
    number_of_beams: int
    use_cache: bool
    seed: int
    temperature: float
    top_p: float
    top_k: int

    def generation_arguments(self) -> dict[str, Any]:
        return {
            "do_sample": self.do_sample,
            "num_beams": self.number_of_beams,
            "use_cache": self.use_cache,
            "temperature": self.temperature,
            "top_p": self.top_p,
            "top_k": self.top_k,
        }


SYNTHESIS_RECOVERY_POOL_DECODINGS = tuple(
    GroundedMetadataSynthesisDecoding(
        POLICY_VERSION,
        POLICY_SHA256,
        TRIGGER,
        LOGICAL_PASS_ORDINAL,
        candidate_ordinal,
        BATCH_SIZE,
        DO_SAMPLE,
        NUMBER_OF_BEAMS,
        USE_CACHE,
        seed,
        TEMPERATURE,
        TOP_P,
        TOP_K,
    )
    for candidate_ordinal, seed in enumerate(SEEDS, start=1)
)


__all__ = [name for name in globals() if not name.startswith("__")]
