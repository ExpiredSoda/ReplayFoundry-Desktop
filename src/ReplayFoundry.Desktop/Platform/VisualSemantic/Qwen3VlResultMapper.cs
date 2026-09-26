using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlResultMapper
{
    public static Qwen3VlObservationWithAttemptResult Map(
        InferenceProviderIdentity identity,
        Qwen3VlBatchHostSettings settings,
        VisualSemanticBatchRequest request,
        Qwen3VlParsedBatchResult parsed,
        Qwen3VlProviderAttemptBatch attemptBatch,
        string pythonExecutableSha256,
        string hostScriptSha256,
        string probeOutput,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc)
    {
        IReadOnlyList<VisualSemanticWarning> batchWarnings =
            Qwen3VlProviderWarningFactory.CreateBatchWarnings(
                parsed.PeakAllocatedGpuBytes);
        VisualSemanticResult[] results =
            request.Requests
                .Select(
                    (item, index) =>
                        new VisualSemanticResult(
                            item,
                            parsed.Results[index].Observation,
                            parsed.Results[index].Elapsed,
                            CreateResultWarnings(
                                item,
                                parsed.Results[index]
                                    .IdentityBindingAudit,
                                parsed.Results[index]
                                    .NormalizationAudit,
                                parsed.ExecutionTiming
                                    .Cases[index]),
                            parsed.Results[index]
                                .NormalizationAudit))
                .ToArray();
        var execution =
            new VisualSemanticExecutionManifest(
                identity,
                settings.PythonExecutablePath,
                pythonExecutableSha256,
                settings.HostScriptPath,
                hostScriptSha256,
                request.Model.ManifestSha256,
                request.Prompt.Sha256,
                probeOutput,
                parsed.Device,
                parsed.Backend,
                parsed.PeakAllocatedGpuBytes,
                startedAtUtc,
                completedAtUtc,
                completedAtUtc - startedAtUtc,
                parsed.ExecutionTiming,
                batchWarnings);

        return new Qwen3VlObservationWithAttemptResult(
            new VisualSemanticBatchResult(
                request,
                results,
                execution,
                parsed.Generation,
                batchWarnings),
            attemptBatch);
    }

    private static IEnumerable<VisualSemanticWarning>
        CreateResultWarnings(
            VisualSemanticRequest request,
            VisualSemanticIdentityBindingAudit identityBindingAudit,
            VisualSemanticOutputNormalizationAudit? normalizationAudit,
            VisualSemanticCaseExecutionTiming executionTiming)
    {
        if (identityBindingAudit.AnyEchoMismatch)
        {
            yield return new VisualSemanticWarning(
                VisualSemanticWarningCode
                    .ProviderIdentityEchoMismatch,
                "The local Qwen provider echoed a foreign case or candidate identifier. Replay Foundry retained the trusted host-request identity and audited the mismatch without rerouting semantic fields.",
                request.CaseId);
        }

        if (request.Transcript.Policy ==
                VisualSemanticTranscriptContextPolicy.FullContextV1 &&
            request.Transcript.Spans.Any(
                static value =>
                    value.TimingPrecision !=
                    TranscriptTimingPrecision
                        .HumanReviewedReference))
        {
            yield return new VisualSemanticWarning(
                VisualSemanticWarningCode.TranscriptApproximate,
                request.Transcript.TranscriptAccuracyWarning,
                request.CaseId);
        }

        if (normalizationAudit is not null)
        {
            yield return new VisualSemanticWarning(
                VisualSemanticWarningCode.OutputNormalized,
                "The local Qwen host canonicalized exact duplicate or ordering-only list representation under " +
                $"'{normalizationAudit.NormalizationPolicyVersion}'. Semantic observation text was not changed.",
                request.CaseId);
        }

        foreach (
            VisualSemanticExecutionTimingWarningCode warning
            in executionTiming.WarningCodes)
        {
            switch (warning)
            {
                case VisualSemanticExecutionTimingWarningCode
                    .InferredTimestampDrift:
                    yield return new VisualSemanticWarning(
                        VisualSemanticWarningCode
                            .InferredTimestampDrift,
                        "Legacy frame-index/average-FPS timestamps drift from authoritative TorchCodec PTS beyond the versioned maximum-frame-duration tolerance. Inferred timestamps remain diagnostic only.",
                        request.CaseId);
                    break;

                case VisualSemanticExecutionTimingWarningCode
                    .ContainerDurationExceedsVideoStreamEnd:
                    yield return new VisualSemanticWarning(
                        VisualSemanticWarningCode
                            .ContainerDurationExceedsVideoStreamEnd,
                        "The bounded container duration extends slightly beyond the decoded video-stream end within the frozen container timestamp tolerance.",
                        request.CaseId);
                    break;

                default:
                    throw new Qwen3VlInferenceException(
                        $"Execution timing for '{request.CaseId}' contains an unsupported warning.");
            }
        }
    }
}
