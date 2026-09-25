"""Case-local reuse of exact Qwen image features during inference-only review."""
from __future__ import annotations

import hashlib
import json
import time


class VisionFeatureReuse:
    """Retain the complete pooled + DeepStack output, never language/KV state.

    The identity includes every input byte, grid, dtype, device and option. The
    bounded cache lives only for one cut and is never persisted or shared across
    models. Training and gradient-enabled calls always execute normally.
    """
    def __init__(self, model, torch, maximum_bytes=256*1024*1024):
        self.owner = model.model
        self.had_override = "get_image_features" in vars(self.owner)
        self.original = self.owner.get_image_features
        self.torch = torch
        self.maximum_bytes = maximum_bytes
        self.saved = {}
        self.hits = self.misses = self.bytes = 0
        self.encoder_seconds = self.lookup_seconds = 0.0
        self.owner.get_image_features = self.get_image_features

    def clear(self):
        self.saved.clear()
        self.hits = self.misses = self.bytes = 0
        self.encoder_seconds = self.lookup_seconds = 0.0

    def close(self):
        if self.owner is None:
            return
        if self.had_override:
            self.owner.get_image_features = self.original
        else:
            # Restore the class descriptor, not a bound instance method that
            # would keep the entire GPU model alive through a reference cycle.
            del self.owner.get_image_features
        self.owner = self.original = None
        self.clear()

    def key(self, pixels, grid, options):
        digest = hashlib.sha256(json.dumps(options, sort_keys=True, allow_nan=False).encode())
        for value in (pixels, grid):
            if value is None:
                digest.update(b"none")
                continue
            digest.update(str((tuple(value.shape), value.dtype, value.device)).encode())
            digest.update(value.detach().contiguous().view(self.torch.uint8).cpu().numpy().tobytes())
        return digest.hexdigest()

    def get_image_features(self, pixel_values, image_grid_thw=None, **kwargs):
        if self.owner.training or self.torch.is_grad_enabled():
            return self.original(pixel_values, image_grid_thw, **kwargs)
        started = time.perf_counter()
        try:
            key = self.key(pixel_values, image_grid_thw, kwargs)
        except (TypeError, ValueError):
            return self.original(pixel_values, image_grid_thw, **kwargs)
        self.lookup_seconds += time.perf_counter()-started
        if key in self.saved:
            self.hits += 1
            return self.saved[key]
        started = time.perf_counter()
        output = self.original(pixel_values, image_grid_thw, **kwargs)
        self.encoder_seconds += time.perf_counter()-started
        self.misses += 1
        # Only the pinned Qwen structured output is supported. Unknown output
        # types run normally without caching; no features may be discarded.
        if not hasattr(output, "pooler_output") or not hasattr(output, "deepstack_features"):
            return output
        tensors = [*(output.pooler_output or ()), *(output.deepstack_features or ())]
        if getattr(output, "last_hidden_state", None) is not None:
            tensors.append(output.last_hidden_state)
        size = sum(value.numel()*value.element_size() for value in tensors)
        if self.bytes + size <= self.maximum_bytes:
            self.saved[key] = output
            self.bytes += size
        return output

    def diagnostics(self):
        return dict(hits=self.hits, misses=self.misses, retainedBytes=self.bytes,
                    encoderSeconds=self.encoder_seconds, lookupSeconds=self.lookup_seconds)
