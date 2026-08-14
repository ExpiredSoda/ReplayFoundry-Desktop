#!/usr/bin/env python3
"""Model-free tests for Qwen/TorchCodec sampling diagnostics."""

from __future__ import annotations

import copy
import hashlib
import json
import tempfile
import unittest
from datetime import datetime, timezone
from decimal import Decimal
from pathlib import Path
from types import SimpleNamespace
from unittest import mock

from replayfoundry_visual_semantic import cli as cli_host
from replayfoundry_visual_semantic import commands as commands_host
from replayfoundry_visual_semantic import generation as generation_host
from replayfoundry_visual_semantic import sampling_audit as host
from replayfoundry_visual_semantic import sampling_timing


def _request(index: int = 1) -> dict:
    return {
        "caseId": f"case-{index}",
        "candidate": {
            "id": f"candidate-{index}",
            "mode": "StandaloneClip",
        },
        "composition": {},
        "transcript": {
            "policy": "Unavailable",
            "evidenceStatus": "Unavailable",
            "spans": [],
            "accuracyWarning": None,
        },
        "deterministicSummary": {},
        "_validated": {
            "videoPath": Path(f"C:/media/video-{index}.mkv"),
            "videoDuration": Decimal("10"),
            "candidateStart": Decimal("2"),
            "candidateEnd": Decimal("8"),
            "sourceAbsoluteOffset": Decimal("20"),
            "expectedVideoHash": f"{index:064x}",
            "expectedVideoLength": 1000 + index,
            "expectedLastWriteUtc": datetime(
                2026,
                1,
                1,
                tzinfo=timezone.utc,
            ),
            "videoPolicy": {},
        },
    }


def _successful_case(index: int, causes: list[str] | None = None) -> dict:
    causes = [] if causes is None else list(causes)
    value = {
        "caseId": f"case-{index}",
        "candidateId": f"candidate-{index}",
        "caseOrdinal": index,
        "mode": "StandaloneClip",
        "inputCaseSha256": f"{index:064x}",
        "sourceVideoSha256": f"{index + 100:064x}",
        "status": "Succeeded",
        "timing": {},
        "qwenMetadata": {},
        "directTorchCodecMetadata": {},
        "comparison": {
            "directRepeatTensorIdentityEqual": True,
            "directRepeatFrameIdentityEqual": True,
            "ptsRepeatEqual": True,
            "durationsRepeatEqual": True,
        },
        "candidateVisibility": {
            "intersectingFrameCount": 2,
        },
        "reviewCoverage": {},
        "legacyValidation": {
            "passed": True,
            "errorCode": None,
            "message": None,
        },
        "correctedPolicyValidation": {
            "passed": True,
            "errorCode": None,
            "message": None,
        },
        "rootCauseCodes": causes,
        "primaryRootCause": causes[0] if causes else None,
        "decisionSupportCode": "LegacyAndActualCoveragePass",
        "warnings": [],
        "failure": None,
    }
    value["caseAuditSha256"] = host._case_audit_sha256(value)
    return value


class _Cookie:
    def close(self) -> None:
        pass


class _ByteView:
    def __init__(self, value: bytes) -> None:
        self._value = value

    def tobytes(self) -> bytes:
        return self._value


class _FakeFrame:
    def __init__(self, value: bytes) -> None:
        self._value = value

    def contiguous(self) -> "_FakeFrame":
        return self

    def numpy(self) -> _ByteView:
        return _ByteView(self._value)


class _FakeTensor:
    def __init__(self, frames: list[bytes]) -> None:
        self._frames = list(frames)
        self.shape = (len(frames), 3, 2, 2)
        self.dtype = "fake.float32"
        self.device = SimpleNamespace(type="cpu")

    def detach(self) -> "_FakeTensor":
        return self

    def cpu(self) -> "_FakeTensor":
        return self

    def contiguous(self) -> "_FakeTensor":
        return self

    def numpy(self) -> _ByteView:
        return _ByteView(b"".join(self._frames))

    def __iter__(self):
        return iter(_FakeFrame(value) for value in self._frames)


class _FakeBatch:
    def __init__(
        self,
        data: _FakeTensor,
        pts: list[float],
        durations: list[float],
    ) -> None:
        self.data = data
        self.pts_seconds = list(pts)
        self.duration_seconds = list(durations)


def _fake_torchcodec(
    tensor: _FakeTensor,
    pts: list[float],
    durations: list[float],
    *,
    repeat_pts_delta: float = 0.0,
    fail_decode: bool = False,
    source_begin: float = 20.0,
    source_end: float = 30.0,
    source_fps: float = 30.0,
    source_frame_count: int = 900,
):
    created = 0
    decoded_indices: list[list[int]] = []

    class Decoder:
        def __init__(self, *args, **kwargs) -> None:
            nonlocal created
            created += 1
            if fail_decode:
                raise host.InferenceError("direct decoder failed")
            self.stream_index = 0
            self.metadata = SimpleNamespace(
                begin_stream_seconds=source_begin,
                end_stream_seconds=source_end,
                num_frames=source_frame_count,
                average_fps=source_fps,
            )
            self._repeat = created > 1

        def get_frames_at(self, *, indices):
            decoded_indices.append(list(indices))
            actual_pts = list(pts)
            if self._repeat and repeat_pts_delta:
                actual_pts[-1] += repeat_pts_delta
            return _FakeBatch(tensor, actual_pts, durations)

    return SimpleNamespace(
        decoders=SimpleNamespace(VideoDecoder=Decoder),
        decoded_indices=decoded_indices,
        decoder_count=lambda: created,
    )


def _fake_qwen_process(
    tensor: _FakeTensor,
    indices: list[int],
    *,
    fps: float = 30.0,
    expected_start: float = 20.0,
    expected_end: float = 30.0,
    total_frames: int = 900,
):
    def process(messages, **kwargs):
        video = messages[1]["content"][0]
        if (
            video["video_start"] != expected_start
            or video["video_end"] != expected_end
        ):
            raise AssertionError("Messages did not preserve review coordinates.")
        return (
            [],
            [
                (
                    tensor,
                    {
                        "video_backend": "torchcodec",
                        "total_num_frames": total_frames,
                        "frames_indices": list(indices),
                        "fps": fps,
                    },
                )
            ],
            {"do_sample_frames": False},
        )

    return process


def _fake_messages(
    start: float = 20.0,
    end: float = 30.0,
) -> list[dict]:
    return [
        {"role": "system", "content": "prompt"},
        {
            "role": "user",
            "content": [
                {
                    "type": "video",
                    "video": "C:/media/video-1.mkv",
                    "video_start": start,
                    "video_end": end,
                }
            ],
        },
    ]


class SamplingAuditTests(unittest.TestCase):
    def setUp(self) -> None:
        host._reset_failure_context("audit-video-sampling")

    def test_frozen_identity_and_semantic_constants(self) -> None:
        self.assertEqual("0.5A.9", host.HOST_VERSION)
        self.assertEqual(
            "visual-semantic-sampling-audit-1.0",
            host.SAMPLING_AUDIT_SCHEMA,
        )
        self.assertEqual(
            "visual-semantic-host-failure-1.4",
            host.FAILURE_SCHEMA,
        )
        self.assertEqual(
            "visual-semantic-observation-batch-1.5",
            host.OUTPUT_SCHEMA,
        )
        self.assertEqual(
            "visual-semantic-execution-timing-1.0",
            host.EXECUTION_TIMING_SCHEMA,
        )
        self.assertEqual(
            "candidate-sampling-coverage-1.0",
            host.CANDIDATE_SAMPLING_COVERAGE_POLICY,
        )
        self.assertEqual(
            "TorchCodecFrameBatchActualPtsAndDuration",
            host.AUTHORITATIVE_SAMPLING_TIMING_SOURCE,
        )
        self.assertEqual(
            "18c738c006b638e770ee0e69efafe43770939ae3528d79220ef253679564e8c9",
            host.PROMPT_SHA256,
        )
        self.assertEqual("visual-semantic-observation-1.0", host.OBSERVATION_SCHEMA)
        self.assertEqual(0.5, host.VIDEO_FPS)
        self.assertEqual((4, 32), (host.VIDEO_MIN_FRAMES, host.VIDEO_MAX_FRAMES))

    def test_execution_timing_uses_canonical_serialized_inputs(self) -> None:
        inferred, actual, durations = (
            host._canonical_execution_timing_inputs(
                [1.00000000049],
                [0.99999999951],
                [0.01600000049],
            )
        )

        self.assertEqual([1.0], inferred)
        self.assertEqual([1.0], actual)
        self.assertEqual([0.016], durations)
        self.assertEqual(
            1e-9,
            round(1.00000000049 - 0.99999999951, 9),
            "The raw values demonstrate the former producer/consumer disagreement.",
        )
        self.assertEqual(
            0.0,
            round(inferred[0] - actual[0], 9),
            "Derived timing must use the values that cross the JSON boundary.",
        )

    def test_execution_timing_rounds_each_drift_before_mean(self) -> None:
        per_frame, maximum, mean, tolerance = (
            host._canonical_execution_timing_drift(
                [0.00000000149, 0.00000000151],
                [0.0, 0.0],
                [0.016, 0.016],
            )
        )

        self.assertEqual([0.000000001, 0.000000002], per_frame)
        self.assertEqual(0.000000002, maximum)
        self.assertEqual(
            0.000000002,
            mean,
            "The producer must average the same rounded per-frame values "
            "that the .NET verifier derives from JSON.",
        )
        self.assertEqual(0.016, tolerance)
        self.assertEqual(
            0.000000001,
            round((0.00000000149 + 0.00000000151) / 2, 9),
            "Averaging unrounded differences reproduces the former "
            "cross-language contradiction.",
        )

    def test_failure_envelope_retains_case_timing_identity_and_sampling(self) -> None:
        request = _request(3)
        host._set_failure_case(request, 3, "a" * 64)
        host._set_failure_stage("VideoSampling")
        host._set_failure_identity(
            inputBatchSha256="b" * 64,
            modelManifestSha256="c" * 64,
            environmentSha256="d" * 64,
        )
        host._set_failure_sampling(
            sourceAverageFramesPerSecond=30.0,
            frameIndices=[600, 660],
            inferredTimestampsSeconds=[20.0, 22.0],
            actualPtsSeconds=[20.006, 22.006],
            actualFrameDurationsSeconds=[0.033, 0.033],
            frameCount=2,
            candidateIntersectingFrameCount=2,
        )
        payload = host._failure_payload(
            "audit-video-sampling",
            "InferenceError",
            4,
            "bounded failure",
        )

        self.assertEqual(3, payload["case"]["caseOrdinal"])
        self.assertEqual("case-3", payload["case"]["caseId"])
        self.assertEqual("candidate-3", payload["case"]["candidateId"])
        self.assertEqual("VideoSampling", payload["stage"])
        self.assertEqual(20.0, payload["timing"]["reviewStartSeconds"])
        self.assertEqual(22.0, payload["timing"]["candidateAbsoluteStartSeconds"])
        self.assertEqual(28.0, payload["timing"]["candidateAbsoluteEndSeconds"])
        self.assertEqual(2, payload["sampling"]["candidateIntersectingFrameCount"])
        self.assertEqual("a" * 64, payload["identity"]["inputCaseSha256"])
        self.assertNotIn("proxy", json.dumps(payload).casefold())
        self.assertNotIn("label", json.dumps(payload).casefold())

    def test_failure_output_is_atomic_external_and_absent_until_failure(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "failure.json"
            self.assertFalse(path.exists())
            host._approve_failure_output()
            host._try_write_failure_output(
                path,
                "probe",
                "InitializationError",
                3,
                "failed",
            )
            self.assertTrue(path.is_file())
            payload = json.loads(path.read_text(encoding="utf-8"))
            self.assertEqual(host.FAILURE_SCHEMA, payload["schemaVersion"])
            self.assertFalse(
                any(path.parent.glob(f".{path.name}.*.tmp"))
            )

    def test_candidate_visibility_uses_half_open_frame_intervals(self) -> None:
        none = host._candidate_visibility(
            [1],
            [0.0],
            [0.5],
            0.5,
            1.0,
        )
        self.assertEqual(0, none["intersectingFrameCount"])

        crossing = host._candidate_visibility(
            [1],
            [0.4],
            [0.2],
            0.5,
            1.0,
        )
        self.assertEqual(1, crossing["intersectingFrameCount"])
        self.assertEqual([], crossing["sampledPtsInsideCandidateSeconds"])

        one = host._candidate_visibility(
            [1],
            [0.5],
            [0.5],
            0.5,
            1.0,
        )
        self.assertEqual(1, one["intersectingFrameCount"])
        self.assertFalse(one["hasAtLeastTwoTemporallyDistinctFrames"])

        two = host._candidate_visibility(
            [1, 2],
            [0.5, 0.9],
            [0.2, 0.2],
            0.5,
            1.0,
        )
        self.assertEqual(2, two["intersectingFrameCount"])
        self.assertTrue(two["hasAtLeastTwoTemporallyDistinctFrames"])
        self.assertTrue(two["beginningJudgmentSupportable"])
        self.assertTrue(two["outcomeJudgmentSupportable"])
        self.assertEqual(2.0, two["frozenSamplingIntervalSeconds"])

    def test_review_coverage_records_nonzero_origin_and_vfr(self) -> None:
        coverage = host._review_coverage(
            [10.0, 11.0, 12.0],
            [0.04, 0.05, 0.04],
            10.0,
            12.04,
            10.0,
            12.04,
            0.002,
        )
        self.assertTrue(coverage["requestedTrimHonored"])
        self.assertTrue(coverage["sourceTimestampOriginNonZero"])
        self.assertTrue(coverage["variableFrameDurationsObserved"])
        self.assertTrue(coverage["averageFpsValidForPtsMapping"])

    def test_root_cause_classification_is_evidence_based(self) -> None:
        visibility = host._candidate_visibility(
            [10],
            [1.0],
            [0.04],
            1.0,
            2.0,
        )
        coverage = host._review_coverage(
            [1.0],
            [0.04],
            0.0,
            3.0,
            0.0,
            3.0,
            0.2,
        )
        causes = host._sampling_root_causes(
            visibility=visibility,
            review_coverage=coverage,
            maximum_absolute_drift=0.2,
            qwen_tensor_equal=False,
            frame_indices_equal=True,
            qwen_frame_count=1,
            direct_frame_count=1,
            timing={
                "requestedAbsoluteReviewStartSeconds": 0.0,
                "requestedAbsoluteReviewEndSeconds": 3.0,
                "candidateRelativeStartSeconds": 1.0,
                "candidateRelativeEndSeconds": 2.0,
                "candidateAbsoluteStartSeconds": 1.0,
                "candidateAbsoluteEndSeconds": 2.0,
                "reviewDurationSeconds": 3.0,
            },
            source_begin=0.0,
            source_end=3.0,
            source_average_fps=25.0,
        )
        self.assertIn("InferredTimestampDrift", causes)
        self.assertIn("CandidateHasOnlyOneSampledFrame", causes)
        self.assertIn("QwenTensorAndDirectTorchCodecFrameMismatch", causes)

    def test_container_tail_beyond_video_stream_is_not_coordinate_loss(self) -> None:
        timing = {
            "requestedAbsoluteReviewStartSeconds": 26.0,
            "requestedAbsoluteReviewEndSeconds": 90.016,
            "candidateRelativeStartSeconds": 3.0,
            "candidateRelativeEndSeconds": 63.0,
            "candidateAbsoluteStartSeconds": 29.0,
            "candidateAbsoluteEndSeconds": 89.0,
            "reviewDurationSeconds": 64.016,
        }
        visibility = host._candidate_visibility(
            [1561, 1826, 5267, 5399],
            [26.017, 30.433, 87.783, 89.983],
            [0.016, 0.016, 0.016, 0.016],
            29.0,
            89.0,
        )
        coverage = host._review_coverage(
            [26.017, 30.433, 87.783, 89.983],
            [0.016, 0.016, 0.016, 0.016],
            26.0,
            90.016,
            0.0,
            89.999,
            0.001,
        )
        (
            review_outside,
            candidate_inside,
            container_tail_within_tolerance,
        ) = (
            host._source_timeline_relation(
                timing,
                0.0,
                89.999,
                0.016,
                60.00066667407416,
            )
        )
        causes = host._sampling_root_causes(
            visibility=visibility,
            review_coverage=coverage,
            maximum_absolute_drift=0.001,
            qwen_tensor_equal=True,
            frame_indices_equal=True,
            qwen_frame_count=4,
            direct_frame_count=4,
            timing=timing,
            source_begin=0.0,
            source_end=89.999,
            source_average_fps=60.00066667407416,
        )

        self.assertTrue(review_outside)
        self.assertTrue(candidate_inside)
        self.assertTrue(container_tail_within_tolerance)
        self.assertTrue(coverage["requestedTrimHonored"])
        self.assertNotIn("ReviewMediaTimelineMismatch", causes)

        timing["candidateAbsoluteEndSeconds"] = 90.02
        _, candidate_inside, _ = host._source_timeline_relation(
            timing,
            0.0,
            89.999,
            0.016,
            60.00066667407416,
        )
        causes = host._sampling_root_causes(
            visibility=visibility,
            review_coverage=coverage,
            maximum_absolute_drift=0.001,
            qwen_tensor_equal=True,
            frame_indices_equal=True,
            qwen_frame_count=4,
            direct_frame_count=4,
            timing=timing,
            source_begin=0.0,
            source_end=89.999,
            source_average_fps=60.00066667407416,
        )
        self.assertFalse(candidate_inside)
        self.assertIn("ReviewMediaTimelineMismatch", causes)

    def test_endpoint_only_legacy_failure_selects_timestamp_decision(self) -> None:
        cases = [
            _successful_case(index, ["InferredTimestampDrift"])
            for index in range(1, 31)
        ]
        for case in cases:
            case["legacyValidation"]["passed"] = False
            case["legacyValidation"]["errorCode"] = (
                "NominalCandidateCoverageInsufficient"
            )
            case["decisionSupportCode"] = "LegacyFailActualPtsPass"
        valid, decision = host._input_policy_decision(cases)
        self.assertTrue(valid)
        self.assertEqual("TimestampValidationDefectOnly", decision)

    def test_six_millisecond_tail_gap_maps_legacy_only_failure_to_timing(self) -> None:
        visibility = host._candidate_visibility(
            [100, 101],
            [88.0, 89.966],
            [0.033, 0.033],
            88.0,
            90.005,
        )
        self.assertAlmostEqual(
            0.006,
            visibility["nearestFrameEndDistanceToCandidateEndSeconds"],
            places=9,
        )
        self.assertTrue(visibility["outcomeJudgmentSupportable"])
        try:
            host._fail_legacy_timing_validation(
                "NominalCandidateCoverageInsufficient",
                "legacy endpoint failure",
            )
        except host.HostError as error:
            legacy_error = error
        causes = host._add_legacy_actual_timing_cause(
            [],
            legacy_error=legacy_error,
            corrected_passed=True,
        )
        self.assertEqual(["InferredTimestampDrift"], causes)

    def test_arbitrary_legacy_failure_cannot_select_timestamp_branch(self) -> None:
        arbitrary = host.InferenceError("unrelated tensor failure")
        self.assertEqual(
            [],
            host._add_legacy_actual_timing_cause(
                [],
                legacy_error=arbitrary,
                corrected_passed=True,
            ),
        )
        cases = [
            _successful_case(index, ["InferredTimestampDrift"])
            for index in range(1, 31)
        ]
        for case in cases:
            case["legacyValidation"] = {
                "passed": False,
                "errorCode": "InferenceError",
                "message": "unrelated tensor failure",
            }
            case["decisionSupportCode"] = "LegacyFailActualPtsPass"
        valid, decision = host._input_policy_decision(cases)
        self.assertFalse(valid)
        self.assertEqual("SamplingAuditInconclusive", decision)

    def test_audit_sampling_case_uses_direct_pts_and_tensor_parity(self) -> None:
        request = _request()
        indices = [600, 660, 720, 780, 840, 899]
        pts = [index / 30.0 for index in indices]
        durations = [1.0 / 30.0] * len(indices)
        tensor = _FakeTensor(
            [bytes([index]) * 12 for index in range(len(indices))]
        )
        host._set_failure_case(request, 1, "a" * 64)
        with mock.patch.object(
            sampling_timing,
            "_resize_direct_frames_like_qwen",
            side_effect=lambda value, element: value,
        ):
            result = host._audit_sampling_case(
                request,
                1,
                "a" * 64,
                "frozen prompt",
                object(),
                _fake_torchcodec(tensor, pts, durations),
                _fake_qwen_process(tensor, indices),
            )

        self.assertEqual("Succeeded", result["status"])
        self.assertTrue(result["correctedPolicyValidation"]["passed"])
        self.assertTrue(
            result["comparison"]["compatibleResizedTensorIdentityEqual"]
        )
        self.assertTrue(result["comparison"]["ptsRepeatEqual"])
        self.assertTrue(result["comparison"]["durationsRepeatEqual"])
        self.assertTrue(
            result["comparison"]["directRepeatTensorIdentityEqual"]
        )
        self.assertTrue(
            result["comparison"]["directRepeatFrameIdentityEqual"]
        )
        self.assertEqual(
            result["directTorchCodecMetadata"]["actualPtsSeconds"],
            result["directTorchCodecMetadata"]["repeatActualPtsSeconds"],
        )
        self.assertEqual(
            result["directTorchCodecMetadata"]["rawTensorSha256"],
            result["directTorchCodecMetadata"]["repeatRawTensorSha256"],
        )
        self.assertEqual([], result["warnings"])

    def test_normal_sampling_uses_actual_pts_and_preserves_qwen_input(self) -> None:
        request = _request()
        original_request = copy.deepcopy(request)
        indices = [600, 660, 720, 780, 838]
        actual_pts = [20.0, 22.0, 24.0, 26.0, 27.99]
        durations = [0.033] * len(indices)
        frame_bytes = [
            bytes([index]) * 12
            for index in range(len(indices))
        ]
        qwen_tensor = _FakeTensor(frame_bytes)
        direct_tensor = _FakeTensor(frame_bytes)
        qwen_process = _fake_qwen_process(qwen_tensor, indices)
        torchcodec = _fake_torchcodec(
            direct_tensor,
            actual_pts,
            durations,
        )

        identity = host._validate_qwen_sampling_structure(
            qwen_tensor,
            {
                "video_backend": "torchcodec",
                "total_num_frames": 900,
                "frames_indices": list(indices),
                "fps": 30.0,
            },
            request,
        )
        with self.assertRaises(host.InferenceError) as legacy:
            host._validate_legacy_nominal_coverage(identity, request)
        self.assertEqual(
            "NominalCandidateCoverageInsufficient",
            legacy.exception.legacy_timing_reason,
        )

        host._reset_failure_context("run")
        host._set_failure_case(request, 7, "a" * 64)
        with mock.patch.object(
            sampling_timing,
            "_resize_direct_frames_like_qwen",
            side_effect=lambda value, element: value,
        ):
            videos, metadata, kwargs, qwen_identity, timing = (
                host._process_video_inputs(
                    request,
                    7,
                    _fake_messages(),
                    qwen_process,
                    torchcodec,
                )
            )

        self.assertIs(qwen_tensor, videos[0])
        self.assertIsNot(direct_tensor, videos[0])
        self.assertEqual(indices, metadata[0]["frames_indices"])
        self.assertEqual(indices, qwen_identity["frameIndices"])
        self.assertEqual(indices, timing["selectedFrameIndices"])
        self.assertEqual(actual_pts, timing["actualPtsSeconds"])
        self.assertTrue(timing["passed"])
        self.assertTrue(timing["compatibleTensorIdentityEqual"])
        self.assertTrue(timing["compatibleFrameIdentityEqual"])
        self.assertTrue(timing["beginningJudgmentSupportable"])
        self.assertTrue(timing["outcomeJudgmentSupportable"])
        self.assertIn("InferredTimestampDrift", timing["warningCodes"])
        self.assertEqual(7, timing["caseOrdinal"])
        self.assertEqual(request, original_request)
        self.assertEqual({"do_sample_frames": False}, kwargs)
        self.assertEqual(1, torchcodec.decoder_count())
        self.assertEqual([indices], torchcodec.decoded_indices)

    def test_execution_timing_manifest_is_versioned_and_deterministic(self) -> None:
        case = {
            "caseId": "case-1",
            "candidateId": "candidate-1",
            "caseOrdinal": 1,
            "passed": True,
        }
        case["canonicalCaseTimingSha256"] = (
            host._execution_timing_case_sha256(case)
        )
        first = host._execution_timing_payload([copy.deepcopy(case)])
        second = host._execution_timing_payload([copy.deepcopy(case)])

        self.assertEqual(
            "visual-semantic-execution-timing-1.0",
            first["schemaVersion"],
        )
        self.assertEqual(
            "candidate-sampling-coverage-1.0",
            first["coveragePolicy"]["version"],
        )
        self.assertEqual(
            "TorchCodecFrameBatchActualPtsAndDuration",
            first["timingSource"],
        )
        self.assertEqual(0.5, first["coveragePolicy"][
            "frozenSamplingFramesPerSecond"
        ])
        self.assertEqual(2.0, first["coveragePolicy"][
            "frozenSamplingIntervalSeconds"
        ])
        self.assertEqual(
            first["canonicalExecutionTimingSha256"],
            second["canonicalExecutionTimingSha256"],
        )
        self.assertEqual(
            case["canonicalCaseTimingSha256"],
            first["cases"][0]["canonicalCaseTimingSha256"],
        )

    def test_direct_decode_failure_retains_partial_qwen_sampling(self) -> None:
        request = _request()
        indices = [600, 660, 720, 780]
        tensor = _FakeTensor([b"a" * 12] * len(indices))
        host._set_failure_case(request, 1, "a" * 64)

        with self.assertRaises(host.InferenceError):
            host._process_video_inputs(
                request,
                1,
                _fake_messages(),
                _fake_qwen_process(tensor, indices),
                _fake_torchcodec(
                    tensor,
                    [20.0, 22.0, 24.0, 26.0],
                    [0.033] * len(indices),
                    fail_decode=True,
                ),
            )

        self.assertEqual(
            "DirectTorchCodecDecode",
            host._FAILURE_CONTEXT["stage"],
        )
        sampling = host._FAILURE_CONTEXT["sampling"]
        self.assertEqual(indices, sampling["frameIndices"])
        self.assertEqual(len(indices), sampling["frameCount"])
        self.assertIsNotNone(sampling["inferredTimestampsSeconds"])
        self.assertIsNone(sampling["actualPtsSeconds"])
        self.assertIsNone(sampling["actualFrameDurationsSeconds"])

    def test_actual_coverage_failure_retains_complete_sampling_evidence(self) -> None:
        request = _request()
        indices = [600, 660, 720, 780]
        pts = [20.0, 20.5, 21.0, 21.5]
        durations = [0.033] * len(indices)
        tensor = _FakeTensor([b"a" * 12] * len(indices))
        host._set_failure_case(request, 1, "a" * 64)

        with (
            mock.patch.object(
                sampling_timing,
                "_resize_direct_frames_like_qwen",
                side_effect=lambda value, element: value,
            ),
            self.assertRaises(host.InferenceError),
        ):
            host._process_video_inputs(
                request,
                1,
                _fake_messages(),
                _fake_qwen_process(tensor, indices),
                _fake_torchcodec(tensor, pts, durations),
            )

        self.assertEqual(
            "SamplingComparison",
            host._FAILURE_CONTEXT["stage"],
        )
        sampling = host._FAILURE_CONTEXT["sampling"]
        self.assertEqual(pts, sampling["actualPtsSeconds"])
        self.assertEqual(durations, sampling["actualFrameDurationsSeconds"])
        self.assertEqual(0, sampling["candidateIntersectingFrameCount"])

    def test_normal_sampling_rejects_compatible_tensor_mismatch(self) -> None:
        request = _request()
        indices = [600, 660, 720, 780, 840, 899]
        pts = [index / 30.0 for index in indices]
        durations = [1.0 / 30.0] * len(indices)
        qwen_tensor = _FakeTensor([b"a" * 12] * len(indices))
        direct_tensor = _FakeTensor([b"b" * 12] * len(indices))
        host._set_failure_case(request, 1, "a" * 64)

        with (
            mock.patch.object(
                sampling_timing,
                "_resize_direct_frames_like_qwen",
                side_effect=lambda value, element: value,
            ),
            self.assertRaises(host.InferenceError),
        ):
            host._process_video_inputs(
                request,
                1,
                _fake_messages(),
                _fake_qwen_process(qwen_tensor, indices),
                _fake_torchcodec(
                    direct_tensor,
                    pts,
                    durations,
                ),
            )

        self.assertEqual(
            "SamplingComparison",
            host._FAILURE_CONTEXT["stage"],
        )

    def test_normal_manifest_records_bounded_container_tail_warning(self) -> None:
        request = _request()
        request["_validated"].update(
            {
                "videoDuration": Decimal("64.016"),
                "candidateStart": Decimal("3"),
                "candidateEnd": Decimal("63"),
                "sourceAbsoluteOffset": Decimal("26"),
            }
        )
        indices = [1561, 1826, 5267, 5399]
        pts = [26.017, 30.433, 87.783, 89.983]
        durations = [0.016] * len(indices)
        tensor = _FakeTensor([b"a" * 12] * len(indices))
        host._set_failure_case(request, 1, "a" * 64)

        with mock.patch.object(
            sampling_timing,
            "_resize_direct_frames_like_qwen",
            side_effect=lambda value, element: value,
        ):
            _, _, _, _, timing = host._process_video_inputs(
                request,
                1,
                _fake_messages(26.0, 90.016),
                _fake_qwen_process(
                    tensor,
                    indices,
                    fps=60.00066667407416,
                    expected_start=26.0,
                    expected_end=90.016,
                    total_frames=5400,
                ),
                _fake_torchcodec(
                    tensor,
                    pts,
                    durations,
                    source_begin=0.0,
                    source_end=89.999,
                    source_fps=60.00066667407416,
                    source_frame_count=5400,
                ),
            )

        self.assertTrue(timing["passed"])
        self.assertTrue(
            timing["containerDurationExceedsVideoStreamEnd"]
        )
        self.assertEqual(
            ["ContainerDurationExceedsVideoStreamEnd"],
            timing["warningCodes"],
        )
        self.assertLessEqual(
            timing["candidateAbsoluteEndSeconds"],
            timing["sourceEndStreamSeconds"],
        )

    def test_candidate_edge_allowance_is_explicit_and_bounded(self) -> None:
        accepted = host._candidate_visibility(
            [1, 2],
            [7.033, 7.5],
            [0.033, 0.033],
            5.0,
            10.0,
        )
        rejected = host._candidate_visibility(
            [1, 2],
            [7.034, 7.5],
            [0.033, 0.033],
            5.0,
            10.0,
        )

        self.assertTrue(accepted["beginningJudgmentSupportable"])
        self.assertFalse(rejected["beginningJudgmentSupportable"])
        self.assertEqual(2.0, accepted["frozenSamplingIntervalSeconds"])
        self.assertEqual(
            0.033,
            accepted["sourceFrameDurationToleranceSeconds"],
        )

    def test_invalid_direct_pts_or_duration_never_reaches_policy_success(self) -> None:
        request = _request()
        indices = [600, 660, 720, 780]
        tensor = _FakeTensor([b"a" * 12] * len(indices))

        for pts, durations in (
            ([20.0, 22.0, 21.0, 26.0], [0.033] * 4),
            ([20.0, 22.0, 24.0, 26.0], [0.033, 0.0, 0.033, 0.033]),
        ):
            host._reset_failure_context("run")
            host._set_failure_case(request, 1, "a" * 64)
            with self.subTest(pts=pts, durations=durations):
                with self.assertRaises(host.InferenceError):
                    host._process_video_inputs(
                        request,
                        1,
                        _fake_messages(),
                        _fake_qwen_process(tensor, indices),
                        _fake_torchcodec(tensor, pts, durations),
                    )
                self.assertEqual(
                    "DirectTorchCodecDecode",
                    host._FAILURE_CONTEXT["stage"],
                )

    def test_sampling_failure_stops_before_model_generation(self) -> None:
        request = _request()
        processor = mock.Mock()
        processor.apply_chat_template.return_value = "rendered"
        model = mock.Mock()
        torch = mock.Mock()

        with (
            mock.patch.object(
                generation_host,
                "_messages_for_request",
                return_value=_fake_messages(),
            ),
            mock.patch.object(
                generation_host,
                "_process_video_inputs",
                side_effect=host.InferenceError(
                    "actual PTS coverage failed"
                ),
            ),
            self.assertRaises(host.InferenceError),
        ):
            generation_host._infer_one(
                request,
                1,
                "prompt",
                model,
                processor,
                torch,
                object(),
                object(),
            )

        model.generate.assert_not_called()

    def test_repeat_pts_mismatch_blocks_corrected_policy(self) -> None:
        request = _request()
        indices = [600, 660, 720, 780, 840, 899]
        pts = [index / 30.0 for index in indices]
        durations = [1.0 / 30.0] * len(indices)
        tensor = _FakeTensor(
            [bytes([index]) * 12 for index in range(len(indices))]
        )
        host._set_failure_case(request, 1, "a" * 64)
        with mock.patch.object(
            sampling_timing,
            "_resize_direct_frames_like_qwen",
            side_effect=lambda value, element: value,
        ):
            result = host._audit_sampling_case(
                request,
                1,
                "a" * 64,
                "frozen prompt",
                object(),
                _fake_torchcodec(
                    tensor,
                    pts,
                    durations,
                    repeat_pts_delta=0.001,
                ),
                _fake_qwen_process(tensor, indices),
            )

        self.assertFalse(result["correctedPolicyValidation"]["passed"])
        self.assertIn("Other", result["rootCauseCodes"])
        self.assertIn(
            "DirectTorchCodecRepeatPtsMismatch",
            result["warnings"],
        )
        self.assertNotIn("QwenAndDirectPtsDiffer", result["warnings"])

    def test_failed_case_retains_completed_qwen_diagnostics(self) -> None:
        request = _request()
        indices = [600, 660, 720, 780, 840, 899]
        tensor = _FakeTensor(
            [bytes([index]) * 12 for index in range(len(indices))]
        )
        host._set_failure_case(request, 1, "a" * 64)
        try:
            host._audit_sampling_case(
                request,
                1,
                "a" * 64,
                "frozen prompt",
                object(),
                _fake_torchcodec(
                    tensor,
                    [index / 30.0 for index in indices],
                    [1.0 / 30.0] * len(indices),
                    fail_decode=True,
                ),
                _fake_qwen_process(tensor, indices),
            )
        except host.HostError as error:
            failed = host._failed_sampling_case(
                request,
                1,
                "a" * 64,
                host._FAILURE_CONTEXT["stage"],
                error,
            )
        self.assertEqual("Failed", failed["status"])
        self.assertIsNotNone(failed["qwenMetadata"])
        self.assertIsNotNone(failed["legacyValidation"])
        self.assertIsNone(failed["directTorchCodecMetadata"])
        self.assertEqual(
            "DirectTorchCodecDecode",
            failed["failure"]["stage"],
        )

    def test_media_revalidation_failure_retains_case_identity(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            first_path = root / "first.mkv"
            second_path = root / "second.mkv"
            first_path.write_bytes(b"first")
            second_path.write_bytes(b"second")
            first = _request(1)
            second = _request(2)
            for request, path in ((first, first_path), (second, second_path)):
                stat = path.stat()
                request["_validated"]["videoPath"] = path
                request["_validated"]["expectedVideoLength"] = stat.st_size
                request["_validated"]["expectedVideoHash"] = hashlib.sha256(
                    path.read_bytes()
                ).hexdigest()
                request["_validated"]["expectedLastWriteUtc"] = (
                    datetime.fromtimestamp(stat.st_mtime, tz=timezone.utc)
                )
            second["_validated"]["expectedVideoLength"] += 1

            with self.assertRaises(host.InferenceError):
                host._revalidate_media_inputs(
                    [first, second],
                    ["a" * 64, "b" * 64],
                )

        self.assertEqual(2, host._FAILURE_CONTEXT["case"]["caseOrdinal"])
        self.assertEqual("case-2", host._FAILURE_CONTEXT["case"]["caseId"])

    def test_sampling_payload_hash_and_order_are_deterministic(self) -> None:
        cases = [_successful_case(index) for index in range(1, 31)]
        first = host._sampling_audit_payload(copy.deepcopy(cases))
        second = host._sampling_audit_payload(copy.deepcopy(cases))
        self.assertEqual(30, first["caseCount"])
        self.assertEqual(
            list(range(1, 31)),
            [case["caseOrdinal"] for case in first["cases"]],
        )
        self.assertEqual(
            first["canonicalAuditSha256"],
            second["canonicalAuditSha256"],
        )
        self.assertEqual(
            list(host.SAMPLING_ROOT_CAUSE_CODES),
            [item["code"] for item in first["rootCauseCounts"]],
        )

    def test_audit_continues_after_case_failure_without_model_load(self) -> None:
        requests = [_request(index) for index in range(1, 31)]
        hashes = [f"{index:064x}" for index in range(1, 31)]
        captured_payload: dict = {}
        process_marker = object()

        def audit_case(
            request,
            case_ordinal,
            input_hash,
            prompt,
            torch,
            torchcodec,
            process_vision_info,
        ):
            self.assertIs(process_marker, process_vision_info)
            if case_ordinal == 3:
                raise host.InferenceError("case decode failed")
            return _successful_case(case_ordinal)

        fake_torch = mock.Mock()
        fake_torch.cuda.empty_cache.return_value = None
        with (
            mock.patch.object(commands_host, "_prompt_source", return_value=("prompt", "x")),
            mock.patch.object(commands_host, "_normalization_policy_source"),
            mock.patch.object(commands_host, "_load_strict_json", return_value={}),
            mock.patch.object(commands_host, "_record_input_failure_identity"),
            mock.patch.object(commands_host, "_input_case_hashes", return_value=hashes),
            mock.patch.object(commands_host, "_validate_input_batch", return_value=requests),
            mock.patch.object(commands_host, "_validate_failure_output_against_media"),
            mock.patch.object(
                commands_host,
                "_load_runtime",
                return_value=(
                    fake_torch,
                    object(),
                    object(),
                    process_marker,
                ),
            ),
            mock.patch.object(commands_host, "_runtime_package_manifest", return_value={}),
            mock.patch.object(commands_host, "_audit_sampling_case", side_effect=audit_case),
            mock.patch.object(commands_host, "_revalidate_media_inputs"),
            mock.patch.object(
                commands_host,
                "_write_json_atomic",
                side_effect=lambda path, payload: captured_payload.update(payload),
            ),
            mock.patch.object(commands_host, "_load_model_and_processor") as load_model,
        ):
            commands_host._audit_video_sampling(
                Path("C:/input.json"),
                Path("C:/output.json"),
                Path("C:/ffmpeg"),
            )

        self.assertEqual(30, len(captured_payload["cases"]))
        self.assertEqual("Failed", captured_payload["cases"][2]["status"])
        self.assertEqual(3, captured_payload["cases"][2]["caseOrdinal"])
        self.assertEqual(29, captured_payload["succeededCaseCount"])
        self.assertEqual(1, captured_payload["failedCaseCount"])
        load_model.assert_not_called()

    def test_main_success_leaves_failure_path_absent(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            input_path = root / "input.json"
            input_path.write_text("{}", encoding="utf-8")
            ffmpeg_path = root / "ffmpeg"
            ffmpeg_path.mkdir()
            output_path = root / "audit.json"
            failure_path = root / "failure.json"
            with (
                mock.patch.object(
                    cli_host,
                    "_configure_ffmpeg_shared_library_directory",
                    return_value=(_Cookie(), None),
                ),
                mock.patch.object(cli_host, "_restore_process_library_path"),
                mock.patch.object(cli_host, "_audit_video_sampling"),
            ):
                exit_code = cli_host.main(
                    [
                        "audit-video-sampling",
                        "--input",
                        str(input_path),
                        "--video-backend",
                        "torchcodec",
                        "--ffmpeg-shared-library-dir",
                        str(ffmpeg_path),
                        "--output",
                        str(output_path),
                        "--failure-output",
                        str(failure_path),
                    ]
                )
            self.assertEqual(0, exit_code)
            self.assertFalse(failure_path.exists())

    def test_main_failure_writes_case_envelope_without_completed_output(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            input_path = root / "input.json"
            input_path.write_text("{}", encoding="utf-8")
            ffmpeg_path = root / "ffmpeg"
            ffmpeg_path.mkdir()
            output_path = root / "audit.json"
            failure_path = root / "failure.json"

            def fail_audit(*args) -> None:
                host._approve_failure_output()
                host._set_failure_case(_request(3), 3, "a" * 64)
                host._set_failure_stage("VideoSampling")
                raise host.InferenceError("case decode failed")

            with (
                mock.patch.object(
                    cli_host,
                    "_configure_ffmpeg_shared_library_directory",
                    return_value=(_Cookie(), None),
                ),
                mock.patch.object(cli_host, "_restore_process_library_path"),
                mock.patch.object(
                    cli_host,
                    "_audit_video_sampling",
                    side_effect=fail_audit,
                ),
                mock.patch("sys.stderr"),
            ):
                exit_code = cli_host.main(
                    [
                        "audit-video-sampling",
                        "--input",
                        str(input_path),
                        "--video-backend",
                        "torchcodec",
                        "--ffmpeg-shared-library-dir",
                        str(ffmpeg_path),
                        "--output",
                        str(output_path),
                        "--failure-output",
                        str(failure_path),
                    ]
                )

            self.assertEqual(4, exit_code)
            self.assertFalse(output_path.exists())
            payload = json.loads(failure_path.read_text(encoding="utf-8"))
            self.assertEqual(3, payload["case"]["caseOrdinal"])
            self.assertEqual("case-3", payload["case"]["caseId"])
            self.assertEqual("VideoSampling", payload["stage"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
