using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlHostFailureParserValidation;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlHostFailureReconciliation
{
    internal static void RequireContextCompleteness(
        Qwen3VlHostFailureStage stage,
        Qwen3VlHostFailureCase? failureCase,
        Qwen3VlHostFailureVideoArtifact? videoArtifact,
        Qwen3VlHostFailureTiming? timing,
        Qwen3VlHostFailureIdentity identity,
        Qwen3VlHostFailureDetails details)
    {
        bool perCaseStage =
            stage is
                Qwen3VlHostFailureStage.VideoSampling or
                Qwen3VlHostFailureStage
                    .DirectTorchCodecDecode or
                Qwen3VlHostFailureStage.SamplingComparison or
                Qwen3VlHostFailureStage.Inference or
                Qwen3VlHostFailureStage.Generation or
                Qwen3VlHostFailureStage.OutputSafety ||
            stage == Qwen3VlHostFailureStage.OutputValidation &&
            details.ErrorCode !=
                Qwen3VlHostErrorCode.ProviderCaseFailuresDetected;

        if (perCaseStage &&
            (failureCase is null ||
             videoArtifact is null ||
             timing is null ||
             identity.InputBatchSha256 is null ||
             identity.InputCaseSha256 is null ||
             identity.ModelManifestSha256 is null ||
             identity.EnvironmentSha256 is null ||
             identity.PromptSha256 is null))
        {
            throw Failure(
                "A per-case host failure must retain case, media, timing, and complete input/environment identity.");
        }

        if ((failureCase is null) !=
                (videoArtifact is null) ||
            (failureCase is null) !=
                (timing is null) ||
            (failureCase is null) !=
                (identity.InputCaseSha256 is null))
        {
            throw Failure(
                "Host failure case, media, timing, and case-input identity must be present or absent together.");
        }
    }

    internal static void RequireSamplingReconciles(
        Qwen3VlHostFailureSampling sampling,
        Qwen3VlHostFailureTiming? timing)
    {
        if (!sampling.FrameCount.HasValue)
        {
            return;
        }

        if (sampling.ActualPtsSeconds is null &&
            sampling.ActualFrameDurationsSeconds is null &&
            !sampling.CandidateIntersectingFrameCount.HasValue)
        {
            return;
        }

        if (!sampling.CandidateIntersectingFrameCount.HasValue)
        {
            throw Failure(
                "Complete sampling evidence must retain the candidate-intersecting frame count.");
        }

        if (timing is null)
        {
            throw Failure(
                "Sampling evidence cannot be reconciled without retained case timing.");
        }

        int actual =
            sampling.ActualPtsSeconds!
                .Zip(
                    sampling.ActualFrameDurationsSeconds!,
                    (pts, duration) =>
                        pts <
                            timing.CandidateAbsoluteEndSeconds &&
                        pts + duration >
                            timing.CandidateAbsoluteStartSeconds)
                .Count(static intersects => intersects);

        if (sampling.CandidateIntersectingFrameCount.Value !=
            actual)
        {
            throw Failure(
                "$.sampling.candidateIntersectingFrameCount does not reconcile with retained actual PTS, durations, and candidate timing.");
        }
    }

    internal static void RequireGenerationReconciles(
        Qwen3VlHostFailureStage stage,
        Qwen3VlHostFailureCase? failureCase,
        Qwen3VlHostFailureGeneration? generation,
        Qwen3VlHostFailureGenerationWatchdog? generationWatchdog,
        Qwen3VlHostFailureDetails details)
    {
        bool generationStage =
            stage is
                Qwen3VlHostFailureStage.Inference or
                Qwen3VlHostFailureStage.Generation or
                Qwen3VlHostFailureStage.OutputSafety or
                Qwen3VlHostFailureStage.OutputValidation;

        if (generation is not null &&
            (!generationStage ||
             failureCase is null))
        {
            throw Failure(
                "Generation failure telemetry is permitted only for an attributed inference, generation, output-safety, or output-validation failure.");
        }

        switch (details.ErrorCode)
        {
            case Qwen3VlHostErrorCode
                .GenerationWallClockBudgetExceededError:
                if (!generationStage ||
                    generationWatchdog is null ||
                    !generationWatchdog.Triggered ||
                    generationWatchdog.TimeoutReason is null)
                {
                    throw Failure(
                        "GenerationWallClockBudgetExceededError requires complete triggered watchdog telemetry.");
                }
                break;

            case Qwen3VlHostErrorCode.RawAuditCaptured:
                if (!generationStage ||
                    generation is null)
                {
                    throw Failure(
                        "RawAuditCaptured requires complete attributed generation telemetry.");
                }

                break;

            case Qwen3VlHostErrorCode
                .GenerationTokenBudgetExceededError:
                {
                    bool exhaustedWithoutEndOfSequence =
                        generation?.TerminationReason ==
                            VisualSemanticGenerationTerminationReason
                                .MaximumNewTokensReached &&
                        !generation
                            .FirstEndOfSequenceGeneratedIndex
                            .HasValue;
                    bool endOfSequenceAtCeiling =
                        generation?.TerminationReason ==
                            VisualSemanticGenerationTerminationReason
                                .EndOfSequence &&
                        generation
                            .FirstEndOfSequenceGeneratedIndex ==
                            generation.GeneratedTokenCount - 1;

                    if (!generationStage ||
                        generation is null ||
                        generation.GeneratedTokenCount !=
                            generation.MaximumNewTokens ||
                        (!exhaustedWithoutEndOfSequence &&
                         !endOfSequenceAtCeiling))
                    {
                        throw Failure(
                            "GenerationTokenBudgetExceededError requires complete full-budget telemetry, with either no EOS or terminal EOS exactly at the ceiling.");
                    }

                    break;
                }

            case Qwen3VlHostErrorCode
                .UnexpectedGenerationTerminationError:
                if (!generationStage ||
                    generation is null ||
                    generation.TerminationReason !=
                        VisualSemanticGenerationTerminationReason
                            .UnexpectedStop ||
                    generation.GeneratedTokenCount >=
                        generation.MaximumNewTokens ||
                    generation
                        .FirstEndOfSequenceGeneratedIndex
                        .HasValue)
                {
                    throw Failure(
                        "UnexpectedGenerationTerminationError requires complete early non-EOS generation telemetry.");
                }

                break;

            case Qwen3VlHostErrorCode
                .ProviderCaseFailuresDetected:
                // The complete typed per-case telemetry is authoritative in
                // the separately parsed attempt batch. The aggregate failure
                // envelope may retain whichever case context was active when
                // the host committed that batch.
                break;

            default:
                if (generationWatchdog?.Triggered == true)
                {
                    throw Failure(
                        "Triggered watchdog telemetry requires its matching typed host error code.");
                }
                if (generation?.TerminationReason ==
                        VisualSemanticGenerationTerminationReason
                            .MaximumNewTokensReached ||
                    generation?.TerminationReason ==
                        VisualSemanticGenerationTerminationReason
                            .UnexpectedStop ||
                    generation?.TerminationReason ==
                        VisualSemanticGenerationTerminationReason
                            .EndOfSequence &&
                    generation.GeneratedTokenCount >=
                        generation.MaximumNewTokens)
                {
                    throw Failure(
                        "A non-successful generation termination requires its matching typed host error code.");
                }

                break;
        }
    }
}
