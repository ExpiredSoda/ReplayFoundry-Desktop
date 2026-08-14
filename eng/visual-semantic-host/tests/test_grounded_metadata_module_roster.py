"""Model-free provenance roster checks for split grounded-metadata modules."""
from __future__ import annotations

from pathlib import Path
import unittest

from replayfoundry_visual_semantic.editorial.grounded_metadata_pipeline import (
    GROUNDED_METADATA_MODULE_FILES,
)


EXPECTED_MODULES = (
    ("pipeline", "grounded_metadata_pipeline.py"),
    ("pipelineContract", "grounded_metadata_pipeline_contract.py"),
    ("pipelineAttestation", "grounded_metadata_pipeline_attestation.py"),
    ("pipelineGrounding", "grounded_metadata_pipeline_grounding.py"),
    ("pipelineState", "grounded_metadata_pipeline_state.py"),
    ("pipelineRefinement", "grounded_metadata_pipeline_refinement.py"),
    ("pipelineRecovery", "grounded_metadata_pipeline_recovery.py"),
    (
        "pipelineRecoveryCandidates",
        "grounded_metadata_pipeline_recovery_candidates.py",
    ),
    ("pipelineResult", "grounded_metadata_pipeline_result.py"),
    ("editorialRephrase", "grounded_metadata_rephrase.py"),
    ("editorialRephraseMessages", "grounded_metadata_rephrase_messages.py"),
    ("synthesis", "grounded_metadata_synthesis.py"),
    ("synthesisMessages", "grounded_metadata_synthesis_messages.py"),
    ("generation", "grounded_metadata_generation.py"),
    ("jsonWhitespace", "grounded_metadata_json_whitespace.py"),
    ("validation", "grounded_metadata_validation.py"),
    ("audienceValidation", "grounded_metadata_audience_validation.py"),
    ("creatorAuthority", "grounded_metadata_creator_authority.py"),
    ("groundingValidation", "grounded_metadata_grounding_validation.py"),
    ("structuredDecoding", "structured_decoding.py"),
    ("recoveryPoolPolicy", "grounded_metadata_synthesis_decoding.py"),
)


class GroundedMetadataModuleRosterTests(unittest.TestCase):
    def test_roster_attests_every_focused_implementation_module(self) -> None:
        self.assertEqual(EXPECTED_MODULES, GROUNDED_METADATA_MODULE_FILES)
        self.assertEqual(len(EXPECTED_MODULES), len(set(EXPECTED_MODULES)))
        module_root = Path(__file__).resolve().parents[1] / (
            "replayfoundry_visual_semantic/editorial"
        )
        self.assertTrue(
            all((module_root / file_name).is_file() for _, file_name in EXPECTED_MODULES)
        )


if __name__ == "__main__":
    unittest.main()
