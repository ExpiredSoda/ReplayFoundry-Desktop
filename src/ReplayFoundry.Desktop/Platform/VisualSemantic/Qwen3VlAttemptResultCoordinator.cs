using System.IO;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlAttemptResultCoordinator
{
    internal static void RequireAttemptMatchesCompleted(
        Qwen3VlProviderAttemptBatch attempt,
        Qwen3VlParsedBatchResult completed)
    {
        if (!attempt.IsCompleteSuccess ||
            attempt.Cases.Count != completed.Results.Count ||
            attempt.Cases.Count != completed.Generation.Cases.Count ||
            attempt.Cases.Count !=
                completed.ExecutionTiming.Cases.Count ||
            !string.Equals(
                attempt.Device,
                completed.Device,
                StringComparison.Ordinal) ||
            !string.Equals(
                attempt.Backend,
                completed.Backend,
                StringComparison.Ordinal) ||
            attempt.PeakAllocatedGpuBytes !=
                completed.PeakAllocatedGpuBytes ||
            attempt.TotalElapsed != completed.TotalElapsed)
        {
            throw new Qwen3VlOutputParseException(
                "The completed observation batch does not match the all-success provider-attempt batch.");
        }

        for (int index = 0;
             index < attempt.Cases.Count;
             index++)
        {
            Qwen3VlProviderCaseAttempt attempted =
                attempt.Cases[index];
            Qwen3VlParsedCaseResult parsed =
                completed.Results[index];
            VisualSemanticCaseGenerationManifest generation =
                completed.Generation.Cases[index];
            VisualSemanticCaseExecutionTiming timing =
                completed.ExecutionTiming.Cases[index];
            string attemptedObservationSha256 =
                attempted.NormalizationAudit
                    ?.CanonicalOutputSha256 ??
                attempted.IdentityBindingAudit!
                    .TrustedBoundPayloadSha256;
            string completedObservationSha256 =
                parsed.NormalizationAudit
                    ?.CanonicalOutputSha256 ??
                parsed.IdentityBindingAudit
                    .TrustedBoundPayloadSha256;

            if (attempted.Status !=
                    Qwen3VlProviderCaseAttemptStatus.Succeeded ||
                attempted.Observation is null ||
                attempted.IdentityBindingAudit is null ||
                attempted.Generation is null ||
                attempted.ExecutionTiming is null ||
                attempted.Failure is not null ||
                attempted.Elapsed != parsed.Elapsed ||
                !string.Equals(
                    attempted.CaseId,
                    parsed.Observation.CaseId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    attempted.CandidateId,
                    parsed.Observation.CandidateId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    attemptedObservationSha256,
                    completedObservationSha256,
                    StringComparison.Ordinal) ||
                !IdentityAuditsMatch(
                    attempted.IdentityBindingAudit,
                    parsed.IdentityBindingAudit) ||
                !NormalizationAuditsMatch(
                    attempted.NormalizationAudit,
                    parsed.NormalizationAudit) ||
                !GenerationCasesMatch(
                    attempted.Generation,
                    generation) ||
                !string.Equals(
                    attempted.ExecutionTiming
                        .CanonicalCaseTimingSha256,
                    timing.CanonicalCaseTimingSha256,
                    StringComparison.Ordinal))
            {
                throw new Qwen3VlOutputParseException(
                    $"The completed observation for '{attempted.CaseId}' differs from its successful provider attempt.");
            }
        }
    }

    internal static void RequireAttemptExecutionMatchesInitialization(
        Qwen3VlProviderAttemptBatch attempt,
        Qwen3VlInitialization initialization)
    {
        if (!string.Equals(
                attempt.Backend,
                Qwen3VlRuntimeContract.ExecutionBackend,
                StringComparison.Ordinal))
        {
            throw new Qwen3VlOutputParseException(
                $"The provider-attempt batch must use the exact '{Qwen3VlRuntimeContract.ExecutionBackend}' backend; fallback is not permitted.");
        }

        if (!string.Equals(
                attempt.Device,
                initialization.Probe.Device,
                StringComparison.Ordinal) ||
            !string.Equals(
                attempt.Backend,
                initialization.Probe.Backend,
                StringComparison.Ordinal))
        {
            throw new Qwen3VlOutputParseException(
                "The provider-attempt execution backend changed after capability probing.");
        }
    }

    private static bool IdentityAuditsMatch(
        VisualSemanticIdentityBindingAudit first,
        VisualSemanticIdentityBindingAudit second) =>
        string.Equals(
            first.TrustedCaseId,
            second.TrustedCaseId,
            StringComparison.Ordinal) &&
        string.Equals(
            first.TrustedCandidateId,
            second.TrustedCandidateId,
            StringComparison.Ordinal) &&
        first.CaseOrdinal == second.CaseOrdinal &&
        string.Equals(
            first.ProviderEchoCaseId,
            second.ProviderEchoCaseId,
            StringComparison.Ordinal) &&
        string.Equals(
            first.ProviderEchoCandidateId,
            second.ProviderEchoCandidateId,
            StringComparison.Ordinal) &&
        first.CaseEchoMatched == second.CaseEchoMatched &&
        first.CandidateEchoMatched == second.CandidateEchoMatched &&
        first.Source == second.Source &&
        string.Equals(
            first.ProviderPayloadSha256,
            second.ProviderPayloadSha256,
            StringComparison.Ordinal) &&
        string.Equals(
            first.TrustedBoundPayloadSha256,
            second.TrustedBoundPayloadSha256,
            StringComparison.Ordinal) &&
        first.BoundAtUtc == second.BoundAtUtc;

    private static bool NormalizationAuditsMatch(
        VisualSemanticOutputNormalizationAudit? first,
        VisualSemanticOutputNormalizationAudit? second)
    {
        if (first is null ||
            second is null)
        {
            return first is null &&
                second is null;
        }

        return string.Equals(
                   first.CaseId,
                   second.CaseId,
                   StringComparison.Ordinal) &&
               string.Equals(
                   first.RawGeneratedTextSha256,
                   second.RawGeneratedTextSha256,
                   StringComparison.Ordinal) &&
               string.Equals(
                   first.RawOutputSha256,
                   second.RawOutputSha256,
                   StringComparison.Ordinal) &&
               string.Equals(
                   first.CanonicalOutputSha256,
                   second.CanonicalOutputSha256,
                   StringComparison.Ordinal) &&
               string.Equals(
                   first.NormalizationPolicyVersion,
                   second.NormalizationPolicyVersion,
                   StringComparison.Ordinal) &&
               first.NormalizationKinds.SequenceEqual(
                   second.NormalizationKinds) &&
               first.RawEvidenceIntervalCount ==
                   second.RawEvidenceIntervalCount &&
               first.CanonicalEvidenceIntervalCount ==
                   second.CanonicalEvidenceIntervalCount &&
               first.ExactDuplicateEvidenceIntervalCount ==
                   second.ExactDuplicateEvidenceIntervalCount &&
               first.EvidenceIntervalOrderChanged ==
                   second.EvidenceIntervalOrderChanged &&
               first.RawLimitationCount ==
                   second.RawLimitationCount &&
               first.CanonicalLimitationCount ==
                   second.CanonicalLimitationCount &&
               first.ExactDuplicateLimitationCount ==
                   second.ExactDuplicateLimitationCount &&
               first.LimitationOrderChanged ==
                   second.LimitationOrderChanged &&
               first.RawUncertaintyCount ==
                   second.RawUncertaintyCount &&
               first.CanonicalUncertaintyCount ==
                   second.CanonicalUncertaintyCount &&
               first.ExactDuplicateUncertaintyCount ==
                   second.ExactDuplicateUncertaintyCount &&
               first.UncertaintyOrderChanged ==
                   second.UncertaintyOrderChanged &&
               first.SemanticTextChanged ==
                   second.SemanticTextChanged &&
               first.NormalizedAtUtc == second.NormalizedAtUtc;
    }

    private static bool GenerationCasesMatch(
        VisualSemanticCaseGenerationManifest first,
        VisualSemanticCaseGenerationManifest second) =>
        string.Equals(
            first.CaseId,
            second.CaseId,
            StringComparison.Ordinal) &&
        string.Equals(
            first.CandidateId,
            second.CandidateId,
            StringComparison.Ordinal) &&
        first.CaseOrdinal == second.CaseOrdinal &&
        first.InputTokenCount == second.InputTokenCount &&
        first.GeneratedTokenCount == second.GeneratedTokenCount &&
        first.MaximumNewTokens == second.MaximumNewTokens &&
        first.EndOfSequenceTokenIds.SequenceEqual(
            second.EndOfSequenceTokenIds) &&
        first.FirstEndOfSequenceGeneratedIndex ==
            second.FirstEndOfSequenceGeneratedIndex &&
        first.TerminalTokenId == second.TerminalTokenId &&
        first.TerminationReason == second.TerminationReason &&
        string.Equals(
            first.GeneratedTokenIdsSha256,
            second.GeneratedTokenIdsSha256,
            StringComparison.Ordinal) &&
        first.LegacyPrefixTokenCount ==
            second.LegacyPrefixTokenCount &&
        string.Equals(
            first.LegacyPrefixTokenIdsSha256,
            second.LegacyPrefixTokenIdsSha256,
            StringComparison.Ordinal) &&
        string.Equals(
            first.DecodedTextSha256,
            second.DecodedTextSha256,
            StringComparison.Ordinal) &&
        first.DecodedTextUtf8ByteCount ==
            second.DecodedTextUtf8ByteCount;

}
