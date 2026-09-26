using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlHostFailureArrayReader;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlHostFailureJsonReader;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlHostFailureParserValidation;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlHostFailurePayloadParser
{
    internal static Qwen3VlHostFailureCase? ParseCase(
        JsonElement root,
        Qwen3VlHostFailureParseContext request)
    {
        JsonElement value = Property(root, "case", "$");

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        Exact(
            value,
            "$.case",
            "caseId",
            "candidateId",
            "caseOrdinal");
        int ordinal =
            Integer(
                value,
                "caseOrdinal",
                "$.case");

        if (ordinal < 1 ||
            ordinal > request.Cases.Count)
        {
            throw Failure(
                "$.case.caseOrdinal is outside the submitted batch.");
        }

        Qwen3VlHostFailureSubmittedCase expected =
            request.Cases[ordinal - 1];
        string caseId =
            Text(value, "caseId", "$.case", 256);
        string candidateId =
            Text(value, "candidateId", "$.case", 256);

        if (!string.Equals(
                caseId,
                expected.CaseId,
                StringComparison.Ordinal) ||
            !string.Equals(
                candidateId,
                expected.CandidateId,
                StringComparison.Ordinal))
        {
            throw Failure(
                "$.case does not belong to the submitted request ordinal.");
        }

        return new Qwen3VlHostFailureCase(
            caseId,
            candidateId,
            ordinal);
    }

    internal static Qwen3VlHostFailureVideoArtifact?
        ParseVideoArtifact(
            JsonElement root,
            Qwen3VlHostFailureSubmittedCase? request)
    {
        JsonElement value =
            Property(
                root,
                "videoArtifact",
                "$");

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        Exact(
            value,
            "$.videoArtifact",
            "sha256",
            "byteLength",
            "reviewDurationSeconds");
        string sha256 =
            Hash(value, "sha256", "$.videoArtifact");
        long byteLength =
            Int64(
                value,
                "byteLength",
                "$.videoArtifact");
        TimeSpan duration =
            Seconds(
                Number(
                    value,
                    "reviewDurationSeconds",
                    "$.videoArtifact"),
                "$.videoArtifact.reviewDurationSeconds");

        if (byteLength < 0)
        {
            throw Failure(
                "$.videoArtifact.byteLength cannot be negative.");
        }

        if (request is not null &&
            (!string.Equals(
                 sha256,
                 request.Input.ReviewVideoSha256,
                 StringComparison.OrdinalIgnoreCase) ||
             byteLength !=
                 request.Input.ReviewVideoByteLength ||
             duration !=
                 request.Input.ReviewVideoDuration))
        {
            throw Failure(
                "$.videoArtifact does not match its attributed request.");
        }

        return new Qwen3VlHostFailureVideoArtifact(
            sha256,
            byteLength,
            duration);
    }

    internal static Qwen3VlHostFailureTiming? ParseTiming(
        JsonElement root,
        Qwen3VlHostFailureSubmittedCase? request)
    {
        JsonElement value =
            Property(root, "timing", "$");

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        Exact(
            value,
            "$.timing",
            "sourceAbsoluteOffsetSeconds",
            "reviewStartSeconds",
            "reviewEndSeconds",
            "candidateRelativeStartSeconds",
            "candidateRelativeEndSeconds",
            "candidateAbsoluteStartSeconds",
            "candidateAbsoluteEndSeconds");
        var timing =
            new Qwen3VlHostFailureTiming(
                SecondsValue(
                    value,
                    "sourceAbsoluteOffsetSeconds",
                    "$.timing"),
                SecondsValue(
                    value,
                    "reviewStartSeconds",
                    "$.timing"),
                SecondsValue(
                    value,
                    "reviewEndSeconds",
                    "$.timing"),
                SecondsValue(
                    value,
                    "candidateRelativeStartSeconds",
                    "$.timing"),
                SecondsValue(
                    value,
                    "candidateRelativeEndSeconds",
                    "$.timing"),
                SecondsValue(
                    value,
                    "candidateAbsoluteStartSeconds",
                    "$.timing"),
                SecondsValue(
                    value,
                    "candidateAbsoluteEndSeconds",
                    "$.timing"));

        if (request is not null &&
            (timing.SourceAbsoluteOffsetSeconds !=
                 request.SourceAbsoluteOffset.TotalSeconds ||
             timing.ReviewStartSeconds !=
                 request.SourceAbsoluteOffset.TotalSeconds ||
             timing.ReviewEndSeconds !=
                 request.SourceAbsoluteOffset.TotalSeconds +
                 request.Input.ReviewVideoDuration.TotalSeconds ||
             timing.CandidateRelativeStartSeconds !=
                 request.CandidateStartRelative.TotalSeconds ||
             timing.CandidateRelativeEndSeconds !=
                 request.CandidateEndRelative.TotalSeconds ||
             timing.CandidateAbsoluteStartSeconds !=
                 request.SourceAbsoluteOffset.TotalSeconds +
                 request.CandidateStartRelative.TotalSeconds ||
             timing.CandidateAbsoluteEndSeconds !=
                 request.SourceAbsoluteOffset.TotalSeconds +
                 request.CandidateEndRelative.TotalSeconds))
        {
            throw Failure(
                "$.timing does not match its attributed request.");
        }

        return timing;
    }

    internal static Qwen3VlHostFailureSampling ParseSampling(
        JsonElement root,
        Qwen3VlHostFailureStage stage)
    {
        JsonElement value =
            Object(root, "sampling", "$");
        Exact(
            value,
            "$.sampling",
            "backend",
            "sourceAverageFramesPerSecond",
            "frameIndices",
            "inferredTimestampsSeconds",
            "actualPtsSeconds",
            "actualFrameDurationsSeconds",
            "frameCount",
            "candidateIntersectingFrameCount");

        string? backend =
            NullableText(
                value,
                "backend",
                "$.sampling",
                128);
        double? fps =
            NullableNumber(
                value,
                "sourceAverageFramesPerSecond",
                "$.sampling");
        int[]? frameIndices =
            NullableIntegerArray(
                value,
                "frameIndices",
                "$.sampling",
                4096);
        double[]? inferred =
            NullableNumberArray(
                value,
                "inferredTimestampsSeconds",
                "$.sampling",
                4096);
        double[]? actualPts =
            NullableNumberArray(
                value,
                "actualPtsSeconds",
                "$.sampling",
                4096);
        double[]? durations =
            NullableNumberArray(
                value,
                "actualFrameDurationsSeconds",
                "$.sampling",
                4096);
        int? frameCount =
            NullableInteger(
                value,
                "frameCount",
                "$.sampling");
        int? intersecting =
            NullableInteger(
                value,
                "candidateIntersectingFrameCount",
                "$.sampling");

        if ((fps.HasValue && fps.Value <= 0) ||
            (frameCount.HasValue && frameCount.Value < 0) ||
            (intersecting.HasValue &&
             (intersecting.Value < 0 ||
              (frameCount.HasValue &&
               intersecting.Value > frameCount.Value))) ||
            frameIndices?.Any(static item => item < 0) == true ||
            durations?.Any(static item => item <= 0) == true)
        {
            throw Failure(
                "$.sampling contains invalid decoded-frame diagnostics.");
        }

        if (!string.Equals(
                backend,
                Qwen3VlBatchHostSettings.SupportedVideoBackend,
                StringComparison.Ordinal))
        {
            throw Failure(
                "$.sampling.backend must identify the exact TorchCodec backend.");
        }

        bool hasQwenSamplingEvidence =
            fps.HasValue ||
            frameIndices is not null ||
            inferred is not null ||
            frameCount.HasValue;
        bool hasActualPtsEvidence =
            actualPts is not null ||
            durations is not null ||
            intersecting.HasValue;

        if (hasQwenSamplingEvidence &&
            (!fps.HasValue ||
             frameIndices is null ||
             inferred is null ||
             !frameCount.HasValue))
        {
            throw Failure(
                "$.sampling must retain the complete available Qwen sampling evidence group.");
        }

        if (hasActualPtsEvidence &&
            (!hasQwenSamplingEvidence ||
             actualPts is null ||
             durations is null ||
             !intersecting.HasValue))
        {
            throw Failure(
                "$.sampling must retain the complete available direct TorchCodec timing evidence group.");
        }

        bool qwenOnlyDirectDecodeFailure =
            stage ==
                Qwen3VlHostFailureStage
                    .DirectTorchCodecDecode &&
            hasQwenSamplingEvidence &&
            !hasActualPtsEvidence;

        if (hasQwenSamplingEvidence &&
            (frameIndices!.Length != frameCount ||
             inferred!.Length != frameCount ||
             !StrictlyIncreasing(frameIndices) ||
             !inferred.SequenceEqual(
                 frameIndices.Select(
                     index =>
                         Math.Round(
                             index / fps!.Value,
                             9,
                             MidpointRounding.ToEven)))))
        {
            throw Failure(
                "$.sampling Qwen arrays and frame count do not reconcile.");
        }

        if (hasActualPtsEvidence &&
            (actualPts!.Length != frameCount ||
             durations!.Length != frameCount ||
             !StrictlyIncreasing(actualPts)))
        {
            throw Failure(
                "$.sampling actual-PTS arrays and frame count do not reconcile.");
        }

        if (hasQwenSamplingEvidence &&
            !hasActualPtsEvidence &&
            !qwenOnlyDirectDecodeFailure)
        {
            throw Failure(
                "Partial Qwen sampling evidence is permitted only when direct TorchCodec decoding fails after Qwen sampling.");
        }

        return new Qwen3VlHostFailureSampling(
            backend,
            fps,
            frameIndices,
            inferred,
            actualPts,
            durations,
            frameCount,
            intersecting);
    }
}
