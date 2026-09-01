using System.Text.Json;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlHostFailureJsonReader;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlHostFailureParserValidation;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlHostFailureGenerationWatchdogParser
{
    private const double ToleranceSeconds = 0.050001;

    internal static Qwen3VlHostFailureGenerationWatchdog? Parse(
        JsonElement root,
        Qwen3VlHostFailureCase? failureCase)
    {
        JsonElement value = Property(root, "generationWatchdog", "$");
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Failure("$.generationWatchdog must be an object or null.");
        }
        Exact(
            value,
            "$.generationWatchdog",
            "policyVersion",
            "policySha256",
            "maximumGenerationWallClockSeconds",
            "maximumGroundedCaseWallClockSeconds",
            "timeoutBehavior",
            "caseId",
            "candidateId",
            "caseOrdinal",
            "generationInvocationOrdinal",
            "effectiveMaximumGenerationWallClockSeconds",
            "elapsedGenerationWallClockSeconds",
            "elapsedCaseWallClockSeconds",
            "triggered",
            "timeoutReason");

        string policyVersion = Text(
            value,
            "policyVersion",
            "$.generationWatchdog",
            128);
        string policySha256 = Hash(
            value,
            "policySha256",
            "$.generationWatchdog");
        double maximumGeneration = Number(
            value,
            "maximumGenerationWallClockSeconds",
            "$.generationWatchdog");
        double maximumCase = Number(
            value,
            "maximumGroundedCaseWallClockSeconds",
            "$.generationWatchdog");
        string timeoutBehavior = Text(
            value,
            "timeoutBehavior",
            "$.generationWatchdog",
            64);
        string? caseId = NullableText(
            value,
            "caseId",
            "$.generationWatchdog",
            128);
        string? candidateId = NullableText(
            value,
            "candidateId",
            "$.generationWatchdog",
            128);
        int? caseOrdinal = NullableInteger(
            value,
            "caseOrdinal",
            "$.generationWatchdog");
        int invocationOrdinal = Integer(
            value,
            "generationInvocationOrdinal",
            "$.generationWatchdog");
        double? effectiveMaximum = NullableNumber(
            value,
            "effectiveMaximumGenerationWallClockSeconds",
            "$.generationWatchdog");
        double? elapsedGeneration = NullableNumber(
            value,
            "elapsedGenerationWallClockSeconds",
            "$.generationWatchdog");
        double? elapsedCase = NullableNumber(
            value,
            "elapsedCaseWallClockSeconds",
            "$.generationWatchdog");
        bool triggered = Boolean(
            value,
            "triggered",
            "$.generationWatchdog");
        string? timeoutReason = NullableText(
            value,
            "timeoutReason",
            "$.generationWatchdog",
            128);

        bool identityAbsent =
            caseId is null && candidateId is null && caseOrdinal is null;
        bool identityComplete =
            caseId is not null && candidateId is not null && caseOrdinal > 0;
        bool identityMatches = identityAbsent
            ? failureCase is null
            : failureCase is not null &&
                string.Equals(
                    caseId,
                    failureCase.CaseId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    candidateId,
                    failureCase.CandidateId,
                    StringComparison.Ordinal) &&
                caseOrdinal == failureCase.CaseOrdinal;
        bool validTimeoutReason = timeoutReason is null ||
            timeoutReason.Equals(
                Qwen3VlGenerationWatchdogPolicy.GenerationTimeoutReason,
                StringComparison.Ordinal) ||
            timeoutReason.Equals(
                Qwen3VlGenerationWatchdogPolicy.CaseTimeoutReason,
                StringComparison.Ordinal);
        if (!policyVersion.Equals(
                Qwen3VlGenerationWatchdogPolicy.Version,
                StringComparison.Ordinal) ||
            !policySha256.Equals(
                Qwen3VlGenerationWatchdogPolicy.Sha256,
                StringComparison.OrdinalIgnoreCase) ||
            Math.Abs(
                maximumGeneration -
                Qwen3VlGenerationWatchdogPolicy
                    .MaximumGenerationWallClockSeconds) > 0.000001 ||
            Math.Abs(
                maximumCase -
                Qwen3VlGenerationWatchdogPolicy
                    .MaximumGroundedCaseWallClockSeconds) > 0.000001 ||
            !timeoutBehavior.Equals(
                Qwen3VlGenerationWatchdogPolicy.TimeoutBehavior,
                StringComparison.Ordinal) ||
            (!identityAbsent && !identityComplete) ||
            !identityMatches ||
            invocationOrdinal < 0 ||
            effectiveMaximum is < 0 or >
                Qwen3VlGenerationWatchdogPolicy
                    .MaximumGenerationWallClockSeconds ||
            elapsedGeneration < 0 ||
            elapsedCase < 0 ||
            triggered != (timeoutReason is not null) ||
            !validTimeoutReason)
        {
            throw Failure(
                "$.generationWatchdog is not canonical policy-bound telemetry.");
        }

        if (triggered)
        {
            if (invocationOrdinal <= 0 || effectiveMaximum is null)
            {
                throw Failure(
                    "Triggered generation watchdog telemetry is incomplete.");
            }
            bool generationExpired = timeoutReason!.Equals(
                    Qwen3VlGenerationWatchdogPolicy.GenerationTimeoutReason,
                    StringComparison.Ordinal) &&
                effectiveMaximum > 0 &&
                elapsedGeneration is not null &&
                elapsedGeneration.Value + ToleranceSeconds >=
                    effectiveMaximum.Value;
            bool caseExpired = timeoutReason.Equals(
                    Qwen3VlGenerationWatchdogPolicy.CaseTimeoutReason,
                    StringComparison.Ordinal) &&
                elapsedCase is not null &&
                (elapsedCase.Value + ToleranceSeconds >=
                    Qwen3VlGenerationWatchdogPolicy
                        .MaximumGroundedCaseWallClockSeconds ||
                 effectiveMaximum > 0 &&
                 elapsedGeneration is not null &&
                 elapsedGeneration.Value + ToleranceSeconds >=
                    effectiveMaximum.Value);
            if (!generationExpired && !caseExpired)
            {
                throw Failure(
                    "Triggered generation watchdog telemetry does not prove its wall-clock boundary.");
            }
        }

        return new Qwen3VlHostFailureGenerationWatchdog(
            policyVersion,
            policySha256,
            maximumGeneration,
            maximumCase,
            timeoutBehavior,
            caseId,
            candidateId,
            caseOrdinal,
            invocationOrdinal,
            effectiveMaximum,
            elapsedGeneration,
            elapsedCase,
            triggered,
            timeoutReason);
    }
}
