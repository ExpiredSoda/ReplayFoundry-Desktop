"""Model-free provenance roster checks for split grounded-metadata modules."""
from __future__ import annotations

import re
import unittest

from _test_bootstrap import HOST_ROOT, REPOSITORY_ROOT

from replayfoundry_visual_semantic.editorial.grounded_metadata_pipeline import (
    GROUNDED_METADATA_MODULE_FILES,
)


PREVIOUS_158_MODULES = (
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

PREVIOUS_160_MODULES = (
    PREVIOUS_158_MODULES[:1]
    + (("isolatedFieldAuthoring", "grounded_metadata_isolated_fields.py"),
       ("groundingPacketHandoff", "grounded_packet_handoff.py"))
    + PREVIOUS_158_MODULES[1:]
)
RESPONSIBILITY_MODULES = (
    ("automaticCommentary", "grounded_metadata_automatic_commentary.py"),
    ("editorialFraming", "grounded_metadata_editorial_framing.py"),
    ("rephraseCorrections", "grounded_metadata_rephrase_corrections.py"),
    ("rephraseFrame", "grounded_metadata_rephrase_frame.py"),
)
EXPECTED_MODULES = PREVIOUS_160_MODULES + RESPONSIBILITY_MODULES


def desktop_policy_text() -> str:
    return (REPOSITORY_ROOT / (
        "src/ReplayFoundry.Desktop/Platform/VisualSemantic/Editorial/"
        "Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.cs"
    )).read_text(encoding="utf-8")


def desktop_current_modules(policy_text: str) -> tuple[tuple[str, str], ...]:
    # Match the exact current property, not a longer historical property name
    # or the following derived rosters.
    declaration = re.search(
        r'\bGroundedMetadataModules\s*\{\s*get;\s*\}\s*=\s*Array\.AsReadOnly<.*?>\(\s*\[(.*?)\]\);',
        policy_text, re.DOTALL,
    )
    if declaration is None:
        raise AssertionError("The desktop current module declaration was not found.")
    return tuple(re.findall(r'\(\s*"([^"]+)",\s*"([^"]+\.py)"\s*\)', declaration.group(1)))


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
        self.assertEqual(
            EXPECTED_MODULES,
            desktop_current_modules(desktop_policy_text()),
            "Desktop and host must attest the same current module roster in order.",
        )

    def test_desktop_historical_158_roster_keeps_its_exact_previous_modules(self) -> None:
        policy_text = desktop_policy_text()
        previous = re.search(
            r'\bPreviousIsolatedFieldAuthoringGroundedMetadataModules\s*\{\s*get;\s*\}\s*='
            r'\s*Array\.AsReadOnly\(GroundedMetadataModules\s*\.Where\(static value => '
            r'value\.ModuleName is not \((.*?)\)\)\s*\.ToArray\(\)\);',
            policy_text, re.DOTALL,
        )
        self.assertIsNotNone(previous, "The historical roster must derive from the current roster with explicit exclusions.")
        excluded = tuple(re.findall(r'"([^"]+)"', previous.group(1)))
        self.assertEqual(("isolatedFieldAuthoring", "groundingPacketHandoff")
                         + tuple(name for name, _ in RESPONSIBILITY_MODULES), excluded)
        self.assertEqual(PREVIOUS_158_MODULES, tuple(
            item for item in desktop_current_modules(policy_text) if item[0] not in excluded
        ), "Historical 1.58 evidence must retain its original ordered module identities.")

    def test_desktop_159_and_160_roster_preserves_exact_pre_refactor_modules(self) -> None:
        policy_text = desktop_policy_text()
        previous = re.search(
            r'\bPreviousResponsibilitySplitGroundedMetadataModules\s*\{\s*get;\s*\}\s*='
            r'\s*Array\.AsReadOnly\(GroundedMetadataModules\s*\.Where\(static value => '
            r'value\.ModuleName is not \((.*?)\)\)\s*\.ToArray\(\)\);',
            policy_text, re.DOTALL,
        )
        self.assertIsNotNone(previous)
        excluded = tuple(re.findall(r'"([^"]+)"', previous.group(1)))
        self.assertEqual(tuple(name for name, _ in RESPONSIBILITY_MODULES), excluded)
        self.assertEqual(PREVIOUS_160_MODULES, tuple(
            item for item in desktop_current_modules(policy_text) if item[0] not in excluded
        ))


if __name__ == "__main__":
    unittest.main()
