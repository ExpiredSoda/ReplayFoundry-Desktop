"""Model-free coverage for bounded unexpected-host diagnostics."""
from __future__ import annotations

import unittest

from replayfoundry_visual_semantic import cli


class UnexpectedFailureDiagnosticsTests(unittest.TestCase):
    def test_message_retains_sanitized_trace_frame(self) -> None:
        def fail_from_model_hook() -> None:
            raise NotImplementedError(
                "Cannot copy out of meta tensor; no data!"
            )

        try:
            fail_from_model_hook()
        except Exception as error:
            message = cli._unexpected_failure_message(error)
        else:
            self.fail("Expected the model-hook failure.")

        self.assertIn("NotImplementedError", message)
        self.assertIn("Cannot copy out of meta tensor", message)
        self.assertIn(
            "test_unexpected_failure_diagnostics.py",
            message,
        )
        self.assertIn(":fail_from_model_hook", message)
        self.assertNotIn("A:\\ReplayFoundry", message)


if __name__ == "__main__":
    unittest.main()
