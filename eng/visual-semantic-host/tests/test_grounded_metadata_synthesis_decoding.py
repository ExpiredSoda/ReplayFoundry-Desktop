"""Model-free checks for the duplicate-authorized recovery pool."""
from __future__ import annotations

from contextlib import contextmanager, nullcontext
import hashlib
import json
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from replayfoundry_visual_semantic import failure_state
from replayfoundry_visual_semantic.editorial import grounded_metadata_generation
from replayfoundry_visual_semantic.editorial.grounded_metadata_synthesis_decoding import (
    LOGICAL_PASS_ORDINAL,
    POLICY_FILE_NAME,
    POLICY_SHA256,
    POLICY_VERSION,
    RETRYABLE_COMPLETED_SEMANTIC_REJECTIONS,
    RETRYABLE_COMPLETED_SEMANTIC_REJECTIONS_SHA256,
    SEEDS,
    SOURCE_REASON_ORIGINAL_FIRST_REJECTED,
    STICKY_GRAMMAR_SOURCE_RULE,
    SYNTHESIS_RECOVERY_POOL_DECODINGS,
)
from replayfoundry_visual_semantic.errors import _GenerationTrace


class _TensorRow(list):
    def detach(self):
        return self

    def cpu(self):
        return self

    def tolist(self):
        return list(self)


class _Inputs(dict):
    def __init__(self) -> None:
        rows = [_TensorRow([1, 2])]
        super().__init__(input_ids=rows)
        self.input_ids = rows

    def to(self, _device: str) -> "_Inputs":
        return self


class _CudaOutOfMemoryError(RuntimeError):
    pass


class _FailingInputs(_Inputs):
    def to(self, _device: str) -> "_Inputs":
        raise _CudaOutOfMemoryError("allocator ceiling")


class _Processor:
    def apply_chat_template(self, *_args, **_kwargs) -> str:
        return "rendered prompt"

    def __call__(self, **_kwargs) -> _Inputs:
        return _Inputs()

    def batch_decode(self, *_args, **_kwargs) -> list[str]:
        return ['{"titleBody":"Opened the visible door"}']


class _FailingProcessor(_Processor):
    def __call__(self, **_kwargs) -> _FailingInputs:
        return _FailingInputs()


class _LogitsProcessor:
    def __init__(self) -> None:
        self.completed_checks = 0

    def require_completed(self) -> None:
        self.completed_checks += 1


class _Session:
    def __init__(self) -> None:
        self.processors: list[_LogitsProcessor] = []

    def new_logits_processor(self, _grammar, eos_token_ids):
        if eos_token_ids != [99]:
            raise AssertionError(eos_token_ids)
        processor = _LogitsProcessor()
        self.processors.append(processor)
        return processor


class _Audit:
    def with_generation(self, _count: int, _reason: str) -> "_Audit":
        return self

    def with_parser_outcome(self, _accepted: bool) -> "_Audit":
        return self

    def to_json(self) -> dict:
        return {"strict": True}


class _Torch:
    def __init__(self) -> None:
        self.cpu_seeds: list[int] = []
        self.cuda_seeds: list[int] = []
        self.empty_cache_calls = 0
        self.cuda = SimpleNamespace(
            manual_seed_all=self.cuda_seeds.append,
            empty_cache=self._empty_cache,
            cudnn_sdp_enabled=lambda: True,
        )
        self.backends = SimpleNamespace(
            cudnn=SimpleNamespace(is_available=lambda: True),
            cuda=self.cuda,
        )
        cudnn_attention = object()
        self.sdpa_events: list[tuple[str, object]] = []

        @contextmanager
        def sdpa_kernel(backend):
            self.sdpa_events.append(("enter", backend))
            yield
            self.sdpa_events.append(("exit", backend))

        self.cudnn_attention = cudnn_attention
        self.nn = SimpleNamespace(
            attention=SimpleNamespace(
                SDPBackend=SimpleNamespace(
                    CUDNN_ATTENTION=cudnn_attention,
                ),
                sdpa_kernel=sdpa_kernel,
            )
        )
        self.OutOfMemoryError = _CudaOutOfMemoryError

    def _empty_cache(self) -> None:
        self.empty_cache_calls += 1

    def manual_seed(self, value: int) -> None:
        self.cpu_seeds.append(value)

    @staticmethod
    def inference_mode():
        return nullcontext()


def _trace() -> _GenerationTrace:
    token_hash = hashlib.sha256(b"tokens").hexdigest()
    return _GenerationTrace(
        sequences=[[1, 2, 99]],
        generated_token_ids=[99],
        input_token_count=2,
        generated_token_count=1,
        maximum_new_tokens=768,
        eos_token_ids=[99],
        first_eos_generated_index=0,
        terminal_token_id=99,
        termination_reason="EndOfSequence",
        generated_token_ids_sha256=token_hash,
        legacy_prefix_token_count=1,
        legacy_prefix_token_ids_sha256=token_hash,
        generation_wall_clock_seconds=1.0,
        maximum_generation_wall_clock_seconds=240.0,
        generation_watchdog_triggered=False,
        generation_watchdog_timeout_reason=None,
    )


class GroundedMetadataSynthesisDecodingTests(unittest.TestCase):
    def setUp(self) -> None:
        failure_state._reset_failure_context(
            "run-grounded-editorial-metadata-batch"
        )

    def test_policy_text_hash_and_constants_are_frozen(self) -> None:
        policy_path = Path(__file__).resolve().parent.parent / POLICY_FILE_NAME
        normalized = (
            policy_path.read_text(encoding="utf-8")
            .replace("\r\n", "\n")
            .replace("\r", "\n")
            .strip()
        )
        self.assertEqual(
            POLICY_SHA256,
            hashlib.sha256(normalized.encode("utf-8")).hexdigest(),
        )
        self.assertEqual((3407, 3408, 3409, 3410), SEEDS)
        self.assertEqual("NonRetrospectiveVoice", STICKY_GRAMMAR_SOURCE_RULE)
        self.assertEqual(4, len(SYNTHESIS_RECOVERY_POOL_DECODINGS))
        self.assertEqual(
            list(SEEDS),
            [item.seed for item in SYNTHESIS_RECOVERY_POOL_DECODINGS],
        )
        self.assertEqual(
            [1, 2, 3, 4],
            [item.candidate_ordinal for item in SYNTHESIS_RECOVERY_POOL_DECODINGS],
        )
        for item in SYNTHESIS_RECOVERY_POOL_DECODINGS:
            self.assertEqual(LOGICAL_PASS_ORDINAL, item.logical_pass_ordinal)
            self.assertEqual(0.7, item.temperature)
            self.assertEqual(0.8, item.top_p)
            self.assertEqual(20, item.top_k)
            self.assertEqual(1, item.batch_size)
        expected_retryable = (
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
        self.assertEqual(
            expected_retryable,
            RETRYABLE_COMPLETED_SEMANTIC_REJECTIONS,
        )
        canonical_retryable = json.dumps(
            list(expected_retryable),
            ensure_ascii=False,
            separators=(",", ":"),
        ).encode("utf-8")
        self.assertEqual(
            RETRYABLE_COMPLETED_SEMANTIC_REJECTIONS_SHA256,
            hashlib.sha256(canonical_retryable).hexdigest(),
        )
        self.assertNotIn(
            "StrictOutputValidation",
            RETRYABLE_COMPLETED_SEMANTIC_REJECTIONS,
        )

    def test_pool_candidate_uses_exact_arguments_seed_and_fresh_matcher(self) -> None:
        torch = _Torch()
        session = _Session()
        model = SimpleNamespace(
            generation_config=SimpleNamespace(
                eos_token_id=99,
                forced_eos_token_id=None,
                stop_strings=None,
            )
        )
        request = {
            "caseId": "case-1",
            "candidateId": "candidate-1",
            "candidate": {"id": "candidate-1"},
            "_validated": {},
        }
        messages = [
            {"role": "user", "content": [{"type": "text", "text": "x"}]}
        ]

        with patch.object(
            grounded_metadata_generation,
            "_generate_with_trace",
            return_value=_trace(),
        ) as generate, patch.object(
            grounded_metadata_generation,
            "admit_grounded_generation",
        ) as admission:
            metadata, _, _, _, _, _, attestation = (
                grounded_metadata_generation._generate_json_once(
                    request,
                    1,
                    messages,
                    model,
                    _Processor(),
                    torch,
                    None,
                    None,
                    session,
                    object(),
                    _Audit(),
                    768,
                    lambda text: {"accepted": text},
                    synthesis_decoding=SYNTHESIS_RECOVERY_POOL_DECODINGS[0],
                    synthesis_attestation_context={
                        "logicalPassOrdinal": 4,
                        "candidateOrdinal": 1,
                        "decoding": "RecoveryPool",
                        "seed": 3407,
                        "sourcePassOrdinal": 1,
                        "sourceRejectedJsonSha256": "a" * 64,
                        "sourceSelectionReason":
                            SOURCE_REASON_ORIGINAL_FIRST_REJECTED,
                        "retryAnchorCaptured": False,
                        "retryAnchorApplied": True,
                        "retryAnchorDisabledReason": None,
                        "retryAnchorEnvelopeSha256": "b" * 64,
                        "retryAnchorAuthoritySha256": "c" * 64,
                    },
                )
            )

        self.assertIn("accepted", metadata)
        self.assertEqual([3407], torch.cpu_seeds)
        self.assertEqual([3407], torch.cuda_seeds)
        admission.assert_called_once_with(torch)
        self.assertEqual(1, torch.empty_cache_calls)
        self.assertEqual(
            [
                ("enter", torch.cudnn_attention),
                ("exit", torch.cudnn_attention),
            ],
            torch.sdpa_events,
        )
        self.assertEqual(1, len(session.processors))
        self.assertEqual(1, session.processors[0].completed_checks)
        self.assertEqual(4, attestation["logicalPassOrdinal"])
        self.assertEqual(1, attestation["candidateOrdinal"])
        self.assertEqual(3407, attestation["seed"])
        self.assertEqual(
            hashlib.sha256(b"rendered prompt").hexdigest(),
            attestation["renderedPromptSha256"],
        )
        self.assertEqual(15, attestation["renderedPromptUtf8ByteCount"])
        self.assertEqual(2, attestation["inputTokenCount"])
        self.assertEqual(
            hashlib.sha256(
                b'{"titleBody":"Opened the visible door"}'
            ).hexdigest(),
            attestation["outputSha256"],
        )
        generation_kwargs = generate.call_args.kwargs
        self.assertEqual(
            {
                "do_sample": True,
                "num_beams": 1,
                "use_cache": True,
                "temperature": 0.7,
                "top_p": 0.8,
                "top_k": 20,
            },
            generation_kwargs["approved_generation_arguments"],
        )
        self.assertEqual(
            "offloaded",
            generation_kwargs["cache_implementation"],
        )
        self.assertEqual(
            [session.processors[0]],
            generation_kwargs["logits_processor"],
        )
        failure_generation = failure_state._FAILURE_CONTEXT["generation"]
        self.assertEqual(POLICY_VERSION, failure_generation["policyVersion"])
        self.assertEqual(POLICY_SHA256, failure_generation["policySha256"])
        self.assertTrue(failure_generation["doSample"])
        self.assertEqual(1, failure_generation["numberOfBeams"])
        self.assertTrue(failure_generation["useCache"])
        self.assertTrue(
            any(
                '"seed":3407' in diagnostic
                and '"candidateOrdinal":1' in diagnostic
                and '"logicalPassOrdinal":4' in diagnostic
                and '"temperature":0.7' in diagnostic
                and '"topP":0.8' in diagnostic
                and '"topK":20' in diagnostic
                for diagnostic in failure_state._FAILURE_CONTEXT["diagnostics"]
            )
        )

    def test_input_transfer_oom_records_memory_policy_and_fails_closed(self) -> None:
        torch = _Torch()
        model = SimpleNamespace()
        request = {
            "caseId": "case-1",
            "candidateId": "candidate-1",
            "candidate": {"id": "candidate-1"},
            "_validated": {},
        }
        messages = [
            {"role": "user", "content": [{"type": "text", "text": "x"}]}
        ]
        with patch.object(
            grounded_metadata_generation,
            "record_grounded_cuda_out_of_memory",
        ) as record:
            with self.assertRaisesRegex(
                grounded_metadata_generation.InferenceError,
                "input transfer reached its fixed CUDA allocator limit",
            ):
                grounded_metadata_generation._generate_json_once(
                    request,
                    1,
                    messages,
                    model,
                    _FailingProcessor(),
                    torch,
                    None,
                    None,
                    _Session(),
                    object(),
                    _Audit(),
                    768,
                    lambda value: {"accepted": value},
                )
        record.assert_called_once_with(torch)


if __name__ == "__main__":
    unittest.main()
