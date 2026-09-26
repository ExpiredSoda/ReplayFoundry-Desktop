"""Exercise the packaged checkpoint loaders against hostile shard indexes."""
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from _test_bootstrap import optional_dependencies_available


@unittest.skipUnless(optional_dependencies_available("torch", "accelerate", "safetensors"),
                     "Requires the qualified runtime dependencies")
class AccelerateCheckpointBackportTests(unittest.TestCase):
    def setUp(self):
        import accelerate
        self.assertEqual("1.14.0+replayfoundry.1", accelerate.__version__)

    def _loaders(self):
        from accelerate import load_checkpoint_and_dispatch
        from accelerate.utils import load_checkpoint_in_model
        return (load_checkpoint_in_model, load_checkpoint_and_dispatch)

    def test_hostile_indexes_never_reach_the_weight_reader(self):
        import torch
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            checkpoint = root / "checkpoint"
            checkpoint.mkdir()
            (checkpoint / "directory.safetensors").mkdir()
            outside = root / "outside.safetensors"
            outside.write_bytes(b"must not be read")
            hostile = ("../outside.safetensors", str(outside),
                       "..\\outside.safetensors", "C:\\outside.safetensors",
                       "\\\\.\\pipe\\replayfoundry-test", "model.safetensors:stream",
                       "directory.safetensors", "missing.safetensors", "NUL", "..", "")
            index = checkpoint / "model.safetensors.index.json"
            for loader in self._loaders():
                for shard in hostile:
                    with self.subTest(loader=loader.__name__, shard=shard):
                        index.write_text(json.dumps({"weight_map": {"weight": shard}}))
                        with patch("accelerate.utils.modeling.load_state_dict",
                                   side_effect=AssertionError("Unsafe file reached reader")):
                            with self.assertRaises(ValueError):
                                loader(torch.nn.Linear(2, 1), str(checkpoint), device_map={"": "cpu"})

    def test_regular_sibling_safetensors_still_load_exact_weights(self):
        import torch
        from safetensors.torch import save_file
        with tempfile.TemporaryDirectory() as temporary:
            checkpoint = Path(temporary)
            reference = torch.nn.Linear(2, 1)
            name = "model-00001-of-00001.safetensors"
            save_file(reference.state_dict(), str(checkpoint / name))
            (checkpoint / "model.safetensors.index.json").write_text(
                json.dumps({"weight_map": {key: name for key in reference.state_dict()}}))
            for loader in self._loaders():
                with self.subTest(loader=loader.__name__):
                    restored = torch.nn.Linear(2, 1)
                    loader(restored, str(checkpoint), device_map={"": "cpu"})
                    for key, value in reference.state_dict().items():
                        self.assertTrue(torch.equal(value, restored.state_dict()[key]))

    def test_symlink_shards_are_rejected_before_reading(self):
        import torch
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            checkpoint = root / "checkpoint"
            checkpoint.mkdir()
            outside = root / "outside.safetensors"
            outside.write_bytes(b"must not be read")
            try:
                (checkpoint / "linked.safetensors").symlink_to(outside)
            except OSError as error:
                self.skipTest(f"Symlink privilege unavailable: {type(error).__name__}")
            (checkpoint / "model.safetensors.index.json").write_text(
                json.dumps({"weight_map": {"weight": "linked.safetensors"}}))
            for loader in self._loaders():
                with patch("accelerate.utils.modeling.load_state_dict",
                           side_effect=AssertionError("Symlink reached reader")):
                    with self.assertRaises(ValueError):
                        loader(torch.nn.Linear(2, 1), str(checkpoint), device_map={"": "cpu"})
