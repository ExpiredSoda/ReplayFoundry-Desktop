using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonCollections;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonPrimitives;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonValues;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlCaseExecutionTimingParser
{
    internal static VisualSemanticCaseExecutionTiming
        Parse(
            JsonElement value,
            VisualSemanticRequest request,
            int expectedOrdinal,
            VisualSemanticExecutionTimingCoveragePolicy policy,
            VisualSemanticVideoInputPolicy videoPolicy,
            string path)
    {
        RequireExactProperties(
            value,
            path,
            "caseId",
            "candidateId",
            "caseOrdinal",
            "reviewVideoSha256",
            "requestedAbsoluteReviewStartSeconds",
            "requestedAbsoluteReviewEndSeconds",
            "candidateAbsoluteStartSeconds",
            "candidateAbsoluteEndSeconds",
            "sourceBeginStreamSeconds",
            "sourceEndStreamSeconds",
            "sourceAverageFramesPerSecond",
            "selectedFrameIndices",
            "inferredTimestampsSeconds",
            "actualPtsSeconds",
            "actualFrameDurationsSeconds",
            "qwenFinalTensorSha256",
            "qwenFinalFrameSha256",
            "directCompatibleTensorSha256",
            "directCompatibleFrameSha256",
            "compatibleTensorIdentityEqual",
            "compatibleFrameIdentityEqual",
            "candidateIntersectingFrameCount",
            "hasAtLeastTwoTemporallyDistinctFrames",
            "beginningJudgmentSupportable",
            "outcomeJudgmentSupportable",
            "nearestSampleDistanceToCandidateStartSeconds",
            "nearestFrameEndDistanceToCandidateEndSeconds",
            "maximumGapSeconds",
            "allActualPtsInsideRequestedReview",
            "allActualFrameIntervalsInsideRequestedReview",
            "requestedTrimHonored",
            "maximumAbsoluteInferredPtsDriftSeconds",
            "meanAbsoluteInferredPtsDriftSeconds",
            "inferredPtsDriftWarningToleranceSeconds",
            "containerDurationExceedsVideoStreamEnd",
            "warningCodes",
            "passed",
            "canonicalCaseTimingSha256");

        string caseId =
            RequireString(value, "caseId", path, 128);
        string candidateId =
            RequireString(value, "candidateId", path, 128);
        int ordinal =
            RequireInt32(value, "caseOrdinal", path);
        string videoHash =
            RequireLowerSha256(
                value,
                "reviewVideoSha256",
                path);
        double reviewStart =
            RequireFiniteDouble(
                value,
                "requestedAbsoluteReviewStartSeconds",
                path);
        double reviewEnd =
            RequireFiniteDouble(
                value,
                "requestedAbsoluteReviewEndSeconds",
                path);
        double candidateStart =
            RequireFiniteDouble(
                value,
                "candidateAbsoluteStartSeconds",
                path);
        double candidateEnd =
            RequireFiniteDouble(
                value,
                "candidateAbsoluteEndSeconds",
                path);
        double expectedReviewStart =
            request.SourceAbsoluteOffset.TotalSeconds;
        double expectedReviewEnd =
            expectedReviewStart +
            request.Input.ReviewVideoDuration.TotalSeconds;
        double expectedCandidateStart =
            expectedReviewStart +
            request.CandidateStartRelative.TotalSeconds;
        double expectedCandidateEnd =
            expectedReviewStart +
            request.CandidateEndRelative.TotalSeconds;

        if (!string.Equals(
                caseId,
                request.CaseId,
                StringComparison.Ordinal) ||
            !string.Equals(
                candidateId,
                request.CandidateId,
                StringComparison.Ordinal) ||
            ordinal != expectedOrdinal ||
            !string.Equals(
                videoHash,
                request.Input.ReviewVideoSha256,
                StringComparison.OrdinalIgnoreCase) ||
            reviewStart != expectedReviewStart ||
            reviewEnd != expectedReviewEnd ||
            candidateStart != expectedCandidateStart ||
            candidateEnd != expectedCandidateEnd ||
            candidateStart < reviewStart ||
            candidateEnd > reviewEnd)
        {
            throw Failure(
                $"{path} does not preserve its exact ordered request, media, review, and candidate coordinates.");
        }

        double? sourceBegin =
            RequireNullableFiniteDouble(
                value,
                "sourceBeginStreamSeconds",
                path);
        double? sourceEnd =
            RequireNullableFiniteDouble(
                value,
                "sourceEndStreamSeconds",
                path);
        double sourceFps =
            RequireFiniteDouble(
                value,
                "sourceAverageFramesPerSecond",
                path);
        int[] indices =
            RequireInt32Array(
                value,
                "selectedFrameIndices",
                path);
        double[] inferred =
            RequireDoubleArray(
                value,
                "inferredTimestampsSeconds",
                path);
        double[] actualPts =
            RequireDoubleArray(
                value,
                "actualPtsSeconds",
                path);
        double[] actualDurations =
            RequireDoubleArray(
                value,
                "actualFrameDurationsSeconds",
                path);
        string qwenTensorHash =
            RequireLowerSha256(
                value,
                "qwenFinalTensorSha256",
                path);
        string[] qwenFrameHashes =
            RequireSha256Array(
                value,
                "qwenFinalFrameSha256",
                path);
        string directTensorHash =
            RequireLowerSha256(
                value,
                "directCompatibleTensorSha256",
                path);
        string[] directFrameHashes =
            RequireSha256Array(
                value,
                "directCompatibleFrameSha256",
                path);
        bool tensorIdentity =
            RequireBoolean(
                value,
                "compatibleTensorIdentityEqual",
                path);
        bool frameIdentity =
            RequireBoolean(
                value,
                "compatibleFrameIdentityEqual",
                path);

        if (!double.IsFinite(sourceFps) ||
            sourceFps <= 0 ||
            indices.Length <
                videoPolicy.MinimumFrames ||
            indices.Length >
                videoPolicy.MaximumFrames ||
            inferred.Length != indices.Length ||
            actualPts.Length != indices.Length ||
            actualDurations.Length != indices.Length ||
            qwenFrameHashes.Length != indices.Length ||
            directFrameHashes.Length != indices.Length ||
            !Qwen3VlActualPtsCoverageCalculator
                .StrictlyIncreasing(indices) ||
            !Qwen3VlActualPtsCoverageCalculator
                .StrictlyIncreasing(actualPts) ||
            actualDurations.Any(
                static duration => duration <= 0) ||
            !inferred.SequenceEqual(
                indices.Select(
                    index =>
                        Qwen3VlActualPtsCoverageCalculator
                            .Round9(index / sourceFps))) ||
            !string.Equals(
                qwenTensorHash,
                directTensorHash,
                StringComparison.Ordinal) ||
            !qwenFrameHashes.SequenceEqual(
                directFrameHashes,
                StringComparer.Ordinal) ||
            !tensorIdentity ||
            !frameIdentity)
        {
            throw Failure(
                $"{path} does not preserve the exact Qwen-selected indices and tensor/frame identity with direct TorchCodec decode.");
        }

        Qwen3VlActualPtsDrift drift =
            Qwen3VlActualPtsCoverageCalculator.CalculateDrift(
                inferred,
                actualPts,
                actualDurations);
        Qwen3VlActualPtsCandidateVisibility visibility =
            Qwen3VlActualPtsCoverageCalculator
                .CalculateCandidateVisibility(
                    indices,
                    actualPts,
                    actualDurations,
                    candidateStart,
                    candidateEnd,
                    policy.FrozenSamplingFramesPerSecond);
        Qwen3VlActualPtsReviewCoverage coverage =
            Qwen3VlActualPtsCoverageCalculator
                .CalculateReviewCoverage(
                    actualPts,
                    actualDurations,
                    reviewStart,
                    reviewEnd,
                    sourceBegin,
                    sourceEnd,
                    policy.FrozenSamplingFramesPerSecond,
                    drift.MaximumAbsoluteSeconds);
        Qwen3VlActualPtsSourceTimeline timeline =
            Qwen3VlActualPtsCoverageCalculator
                .CalculateSourceTimeline(
                    reviewStart,
                    reviewEnd,
                    candidateStart,
                    candidateEnd,
                    sourceBegin,
                    sourceEnd,
                    visibility.SourceFrameTolerance,
                    sourceFps);
        bool containerTail =
            timeline.ReviewOutsideSource &&
            coverage.RequestedTrimHonored &&
            timeline.CandidateInsideSource &&
            timeline.ContainerTailWithinTolerance;
        VisualSemanticExecutionTimingWarningCode[] expectedWarnings =
            ExpectedExecutionTimingWarnings(
                drift.WarningRequired,
                containerTail);
        VisualSemanticExecutionTimingWarningCode[] actualWarnings =
            RequireArray(
                    value,
                    "warningCodes",
                    path)
                .Select(
                    (element, index) =>
                        RequireEnumValue<
                            VisualSemanticExecutionTimingWarningCode>(
                            element,
                            $"{path}.warningCodes[{index}]"))
                .ToArray();
        int intersectingCount =
            RequireInt32(
                value,
                "candidateIntersectingFrameCount",
                path);
        bool hasTwo =
            RequireBoolean(
                value,
                "hasAtLeastTwoTemporallyDistinctFrames",
                path);
        bool beginningSupportable =
            RequireBoolean(
                value,
                "beginningJudgmentSupportable",
                path);
        bool outcomeSupportable =
            RequireBoolean(
                value,
                "outcomeJudgmentSupportable",
                path);
        double? nearestStart =
            RequireNullableFiniteDouble(
                value,
                "nearestSampleDistanceToCandidateStartSeconds",
                path);
        double? nearestEnd =
            RequireNullableFiniteDouble(
                value,
                "nearestFrameEndDistanceToCandidateEndSeconds",
                path);
        double? maximumGap =
            RequireNullableFiniteDouble(
                value,
                "maximumGapSeconds",
                path);
        bool allPtsInside =
            RequireBoolean(
                value,
                "allActualPtsInsideRequestedReview",
                path);
        bool allIntervalsInside =
            RequireBoolean(
                value,
                "allActualFrameIntervalsInsideRequestedReview",
                path);
        bool trimHonored =
            RequireBoolean(
                value,
                "requestedTrimHonored",
                path);
        double maximumDrift =
            RequireFiniteDouble(
                value,
                "maximumAbsoluteInferredPtsDriftSeconds",
                path);
        double meanDrift =
            RequireFiniteDouble(
                value,
                "meanAbsoluteInferredPtsDriftSeconds",
                path);
        double warningTolerance =
            RequireFiniteDouble(
                value,
                "inferredPtsDriftWarningToleranceSeconds",
                path);
        bool reportedContainerTail =
            RequireBoolean(
                value,
                "containerDurationExceedsVideoStreamEnd",
                path);
        bool passed =
            RequireBoolean(
                value,
                "passed",
                path);
        bool independentlyPassed =
            coverage.RequestedTrimHonored &&
            coverage.AllIntervalsInside &&
            visibility.HasAtLeastTwo &&
            visibility.BeginningSupportable &&
            visibility.OutcomeSupportable &&
            timeline.CandidateInsideSource &&
            (!timeline.ReviewOutsideSource ||
             timeline.ContainerTailWithinTolerance) &&
            tensorIdentity &&
            frameIdentity;

        string[] contradictions =
        [
            .. Contradiction(
                intersectingCount !=
                visibility.IntersectingFrameIndices.Length,
                "candidateIntersectingFrameCount"),
            .. Contradiction(
                hasTwo != visibility.HasAtLeastTwo,
                "hasAtLeastTwoTemporallyDistinctFrames"),
            .. Contradiction(
                beginningSupportable !=
                visibility.BeginningSupportable,
                "beginningJudgmentSupportable"),
            .. Contradiction(
                outcomeSupportable !=
                visibility.OutcomeSupportable,
                "outcomeJudgmentSupportable"),
            .. Contradiction(
                !NullableEqual(
                    nearestStart,
                    visibility.NearestStartDistance),
                "nearestSampleDistanceToCandidateStartSeconds"),
            .. Contradiction(
                !NullableEqual(
                    nearestEnd,
                    visibility.NearestEndDistance),
                "nearestFrameEndDistanceToCandidateEndSeconds"),
            .. Contradiction(
                !NullableEqual(
                    maximumGap,
                    visibility.MaximumGap),
                "maximumGapSeconds"),
            .. Contradiction(
                allPtsInside != coverage.AllPtsInside,
                "allActualPtsInsideRequestedReview"),
            .. Contradiction(
                allIntervalsInside != coverage.AllIntervalsInside,
                "allActualFrameIntervalsInsideRequestedReview"),
            .. Contradiction(
                trimHonored != coverage.RequestedTrimHonored,
                "requestedTrimHonored"),
            .. Contradiction(
                maximumDrift != drift.MaximumAbsoluteSeconds,
                "maximumAbsoluteInferredPtsDriftSeconds"),
            .. Contradiction(
                meanDrift != drift.MeanAbsoluteSeconds,
                "meanAbsoluteInferredPtsDriftSeconds"),
            .. Contradiction(
                warningTolerance != drift.WarningToleranceSeconds,
                "inferredPtsDriftWarningToleranceSeconds"),
            .. Contradiction(
                reportedContainerTail != containerTail,
                "containerDurationExceedsVideoStreamEnd"),
            .. Contradiction(
                !actualWarnings.SequenceEqual(expectedWarnings),
                "warningCodes"),
            .. Contradiction(
                !independentlyPassed || !passed,
                "passed"),
        ];

        if (contradictions.Length > 0)
        {
            throw Failure(
                $"{path} contains timing claims contradicted by independently derived canonical actual PTS and durations: {string.Join(", ", contradictions)}.");
        }

        string canonicalHash =
            RequireLowerSha256(
                value,
                "canonicalCaseTimingSha256",
                path);

        if (!string.Equals(
                canonicalHash,
                Qwen3VlCanonicalJson.ComputeObjectSha256(
                    value,
                    "canonicalCaseTimingSha256"),
                StringComparison.Ordinal))
        {
            throw Failure(
                $"{path}.canonicalCaseTimingSha256 does not match the canonical case timing payload.");
        }

        return new VisualSemanticCaseExecutionTiming(
            caseId,
            candidateId,
            ordinal,
            videoHash,
            reviewStart,
            reviewEnd,
            candidateStart,
            candidateEnd,
            sourceBegin,
            sourceEnd,
            sourceFps,
            indices,
            inferred,
            actualPts,
            actualDurations,
            qwenTensorHash,
            qwenFrameHashes,
            directTensorHash,
            directFrameHashes,
            tensorIdentity,
            frameIdentity,
            intersectingCount,
            hasTwo,
            beginningSupportable,
            outcomeSupportable,
            nearestStart,
            nearestEnd,
            maximumGap,
            allPtsInside,
            allIntervalsInside,
            trimHonored,
            maximumDrift,
            meanDrift,
            warningTolerance,
            reportedContainerTail,
            actualWarnings,
            passed,
            canonicalHash);
    }

    private static VisualSemanticExecutionTimingWarningCode[]
        ExpectedExecutionTimingWarnings(
            bool drift,
            bool containerTail)
    {
        var result =
            new List<VisualSemanticExecutionTimingWarningCode>();

        if (drift)
        {
            result.Add(
                VisualSemanticExecutionTimingWarningCode
                    .InferredTimestampDrift);
        }

        if (containerTail)
        {
            result.Add(
                VisualSemanticExecutionTimingWarningCode
                    .ContainerDurationExceedsVideoStreamEnd);
        }

        return result.ToArray();
    }

}
