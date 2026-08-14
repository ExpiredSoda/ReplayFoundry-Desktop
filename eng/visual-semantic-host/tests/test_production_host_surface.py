from __future__ import annotations

import unittest

from replayfoundry_visual_semantic.cli import _build_parser


class ProductionHostSurfaceTests(unittest.TestCase):
    def test_packaged_host_exposes_only_supported_product_commands(self) -> None:
        parser = _build_parser(production_only=True)
        choices = next(
            action.choices
            for action in parser._actions
            if getattr(action, "dest", None) == "command"
        )
        self.assertEqual(
            {
                "verify-editorial-structured-decoding",
                "run-qualified-editorial-batch",
                "run-grounded-editorial-metadata-batch",
            },
            set(choices),
        )

    def test_development_host_retains_qualification_commands(self) -> None:
        parser = _build_parser(production_only=False)
        choices = next(
            action.choices
            for action in parser._actions
            if getattr(action, "dest", None) == "command"
        )
        self.assertIn("audit-video-sampling", choices)
        self.assertIn("run-editorial-constrained-development", choices)


if __name__ == "__main__":
    unittest.main()
