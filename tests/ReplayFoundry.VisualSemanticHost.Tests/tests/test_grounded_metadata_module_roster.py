"""Model-free provenance roster checks for split grounded-metadata modules."""
from __future__ import annotations

import re
import unittest

from _test_bootstrap import HOST_ROOT, REPOSITORY_ROOT

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
    (
        "pipelineRecoveryCandidateSelection",
        "grounded_metadata_recovery_candidate_selection.py",
    ),
    ("pipelineResult", "grounded_metadata_pipeline_result.py"),
    ("editorialRephrase", "grounded_metadata_rephrase.py"),
    ("editorialRephraseMessages", "grounded_metadata_rephrase_messages.py"),
    ("synthesis", "grounded_metadata_synthesis.py"),
    (
        "synthesisSanitization",
        "grounded_metadata_synthesis_sanitization.py",
    ),
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
        module_root = HOST_ROOT / (
            "replayfoundry_visual_semantic/editorial"
        )
        self.assertTrue(
            all((module_root / file_name).is_file() for _, file_name in EXPECTED_MODULES)
        )

    def test_desktop_current_roster_matches_host_exactly(self) -> None:
        policy_path = REPOSITORY_ROOT / (
            "src/ReplayFoundry.Desktop/Platform/VisualSemantic/Editorial/"
            "Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.cs"
        )
        policy_text = policy_path.read_text(encoding="utf-8")
        current_roster = policy_text.split(
            "GroundedMetadataModules", 1
        )[1].split(
            "PreviousRecoveryCandidateSelectionGroundedMetadataModules", 1
        )[0]
        desktop_modules = tuple(re.findall(
            r'\(\s*"([^"]+)",\s*"([^"]+\.py)"\s*\)',
            current_roster,
        ))
        self.assertEqual(
            EXPECTED_MODULES,
            desktop_modules,
            "Desktop and host must attest the same current module roster in order.",
        )


if __name__ == "__main__":
    unittest.main()
