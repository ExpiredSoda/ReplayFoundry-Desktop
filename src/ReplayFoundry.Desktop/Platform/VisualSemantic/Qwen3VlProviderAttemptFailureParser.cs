using System.Globalization;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlProviderAttemptJsonReader;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlProviderAttemptFailureParser
{
    private static readonly string[] CaseLocalErrorCodes =
    [
        "InferenceError",
        "GenerationTokenBudgetExceededError",
        "UnexpectedGenerationTerminationError",
    ];

    internal static Qwen3VlProviderCaseAttemptFailure Parse(
        JsonElement value,
        string path)
    {
        Exact(
            value,
            path,
            "errorCode",
            "message",
            "rawGeneratedTextSha256",
            "providerEchoCaseId",
            "providerEchoCandidateId");
        string errorCode =
            Text(value, "errorCode", path, 128);

        if (!CaseLocalErrorCodes.Contains(
                errorCode,
                StringComparer.Ordinal))
        {
            throw Failure(
                $"{path}.errorCode is not a supported case-local provider failure.");
        }

        return new Qwen3VlProviderCaseAttemptFailure(
            errorCode,
            Text(value, "message", path, 2_000),
            NullableLowerSha256(
                value,
                "rawGeneratedTextSha256",
                path),
            NullableText(
                value,
                "providerEchoCaseId",
                path,
                128),
            NullableText(
                value,
                "providerEchoCandidateId",
                path,
                128));
    }

    internal static VisualSemanticExecutionTimingCoveragePolicy
        FrozenTimingPolicy() =>
        new(
            VisualSemanticExecutionTimingManifest
                .SupportedCoveragePolicyVersion,
            0.5,
            2.0,
            2,
            "HalfOpenFrameIntervals",
            "MaximumActualFrameDuration",
            "FrozenSamplingIntervalPlusMaximumActualFrameDuration",
            "DiagnosticsOnly",
            "MaximumActualFrameDuration",
            Qwen3VlActualPtsCoverageCalculator
                .ContainerTimestampResolutionToleranceSeconds,
            candidateMutationPermitted: false);
}
