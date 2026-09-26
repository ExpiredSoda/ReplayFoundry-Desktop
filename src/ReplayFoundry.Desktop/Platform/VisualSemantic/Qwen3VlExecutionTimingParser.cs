using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonCollections;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonPrimitives;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonValues;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlExecutionTimingParser
{
    internal static VisualSemanticExecutionTimingManifest
        Parse(
            JsonElement value,
            VisualSemanticBatchRequest request)
    {
        const string path = "$.executionTiming";
        RequireExactProperties(
            value,
            path,
            "schemaVersion",
            "coveragePolicy",
            "timingSource",
            "caseCount",
            "cases",
            "canonicalExecutionTimingSha256");
        RequireExactValue(
            RequireString(
                value,
                "schemaVersion",
                path,
                128),
            VisualSemanticExecutionTimingManifest
                .SupportedSchemaVersion,
            $"{path}.schemaVersion");
        VisualSemanticExecutionTimingCoveragePolicy policy =
            ParseExecutionTimingPolicy(
                RequireObject(
                    value,
                    "coveragePolicy",
                    path));
        VisualSemanticExecutionTimingSource timingSource =
            RequireEnum<VisualSemanticExecutionTimingSource>(
                value,
                "timingSource",
                path);

        if (timingSource !=
            VisualSemanticExecutionTimingSource
                .TorchCodecFrameBatchActualPtsAndDuration)
        {
            throw Failure(
                $"{path}.timingSource is not the authoritative TorchCodec actual-PTS source.");
        }

        int caseCount =
            RequireInt32(
                value,
                "caseCount",
                path);
        JsonElement[] caseElements =
            RequireArray(
                value,
                "cases",
                path);

        if (caseCount != request.Requests.Count ||
            caseElements.Length != caseCount)
        {
            throw Failure(
                $"{path}.caseCount and cases must match the submitted batch.");
        }

        VisualSemanticCaseExecutionTiming[] cases =
            caseElements
                .Select(
                    (element, index) =>
                        Qwen3VlCaseExecutionTimingParser.Parse(
                            element,
                            request.Requests[index],
                            index + 1,
                            policy,
                            request.VideoPolicy,
                            $"{path}.cases[{index}]"))
                .ToArray();
        string canonicalHash =
            RequireLowerSha256(
                value,
                "canonicalExecutionTimingSha256",
                path);
        string independentlyComputedHash =
            Qwen3VlCanonicalJson.ComputeObjectSha256(
                value,
                "canonicalExecutionTimingSha256");

        if (!string.Equals(
                canonicalHash,
                independentlyComputedHash,
                StringComparison.Ordinal))
        {
            throw Failure(
                $"{path}.canonicalExecutionTimingSha256 does not match the canonical timing payload.");
        }

        return new VisualSemanticExecutionTimingManifest(
            policy,
            timingSource,
            cases,
            canonicalHash);
    }

    private static VisualSemanticExecutionTimingCoveragePolicy
        ParseExecutionTimingPolicy(JsonElement value)
    {
        const string path =
            "$.executionTiming.coveragePolicy";
        RequireExactProperties(
            value,
            path,
            "version",
            "frozenSamplingFramesPerSecond",
            "frozenSamplingIntervalSeconds",
            "minimumDistinctCandidateFrames",
            "candidateIntervalSemantics",
            "reviewFrameIntervalTolerance",
            "candidateEdgeDistanceTolerance",
            "inferredTimestampUse",
            "inferredActualDriftWarningTolerance",
            "containerTimestampResolutionToleranceSeconds",
            "candidateMutationPermitted");
        string version =
            RequireString(value, "version", path, 128);
        double framesPerSecond =
            RequireFiniteDouble(
                value,
                "frozenSamplingFramesPerSecond",
                path);
        double interval =
            RequireFiniteDouble(
                value,
                "frozenSamplingIntervalSeconds",
                path);
        int minimumDistinct =
            RequireInt32(
                value,
                "minimumDistinctCandidateFrames",
                path);
        string intervalSemantics =
            RequireString(
                value,
                "candidateIntervalSemantics",
                path,
                128);
        string reviewTolerance =
            RequireString(
                value,
                "reviewFrameIntervalTolerance",
                path,
                128);
        string edgeTolerance =
            RequireString(
                value,
                "candidateEdgeDistanceTolerance",
                path,
                128);
        string inferredUse =
            RequireString(
                value,
                "inferredTimestampUse",
                path,
                128);
        string driftTolerance =
            RequireString(
                value,
                "inferredActualDriftWarningTolerance",
                path,
                128);
        double containerTolerance =
            RequireFiniteDouble(
                value,
                "containerTimestampResolutionToleranceSeconds",
                path);
        bool mutationPermitted =
            RequireBoolean(
                value,
                "candidateMutationPermitted",
                path);

        RequireExactValue(
            version,
            VisualSemanticExecutionTimingManifest
                .SupportedCoveragePolicyVersion,
            $"{path}.version");
        RequireExactValue(
            intervalSemantics,
            "HalfOpenFrameIntervals",
            $"{path}.candidateIntervalSemantics");
        RequireExactValue(
            reviewTolerance,
            "MaximumActualFrameDuration",
            $"{path}.reviewFrameIntervalTolerance");
        RequireExactValue(
            edgeTolerance,
            "FrozenSamplingIntervalPlusMaximumActualFrameDuration",
            $"{path}.candidateEdgeDistanceTolerance");
        RequireExactValue(
            inferredUse,
            "DiagnosticsOnly",
            $"{path}.inferredTimestampUse");
        RequireExactValue(
            driftTolerance,
            "MaximumActualFrameDuration",
            $"{path}.inferredActualDriftWarningTolerance");

        if (framesPerSecond != 0.5 ||
            interval != 2.0 ||
            minimumDistinct != 2 ||
            containerTolerance !=
                Qwen3VlActualPtsCoverageCalculator
                    .ContainerTimestampResolutionToleranceSeconds ||
            mutationPermitted)
        {
            throw Failure(
                $"{path} does not match frozen Candidate Sampling Coverage Policy 1.0.");
        }

        return new VisualSemanticExecutionTimingCoveragePolicy(
            version,
            framesPerSecond,
            interval,
            minimumDistinct,
            intervalSemantics,
            reviewTolerance,
            edgeTolerance,
            inferredUse,
            driftTolerance,
            containerTolerance,
            mutationPermitted);
    }

}
