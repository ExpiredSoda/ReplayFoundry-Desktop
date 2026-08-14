using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataGenerator;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataJson;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataResultPolicyParser
{
    internal static bool UsesGenerationWatchdog(string outputSchema) =>
        outputSchema is
            OutputSchema or
            PreviousReviewableAudienceCopyOutputSchema or
            PreviousTerminalPeriodNormalizationOutputSchema or
            PreviousOutputLanguageRecoveryOutputSchema or
            PreviousNeutralPersonRecoveryOutputSchema or
            PreviousRetrospectiveGrammarRecoveryOutputSchema or
            PreviousLiteralActionRecoveryOutputSchema or
            PreviousWithheldEmbodimentCopyOutputSchema or
            PreviousCreatorEmbodimentRecoveryOutputSchema or
            PreviousTypedLanguageRecoveryOutputSchema or
            PreviousLanguageRecoveryOutputSchema or
            PreviousEditorialRephraseOutputSchema or
            PreviousInterfaceCorrectionOutputSchema or
            PreviousInterfaceAttributionOutputSchema or
            PreviousVisualDraftPromptOutputSchema or
            PreviousEffectiveVoiceOutputSchema or
            PreviousGroundedJsonWhitespaceOutputSchema or
            PreviousCreatorAuthorityOutputSchema or
            PreviousAudienceCopyWithholdingOutputSchema or
            PreviousCrossDraftRetryOutputSchema or
            PreviousRootPreloadOutputSchema or
            PreviousCudnnAttentionOutputSchema or
            PreviousPositionEmbeddingOutputSchema or
            PreviousAccelerateOffloadOutputSchema or
            PreviousVisionOffloadOutputSchema or
            PreviousLowPeakSamplingOutputSchema or
            PreviousPeakBoundedSamplingOutputSchema or
            PreviousSamplingOutputSchema;

    internal static (string Version, string Sha256) PromptIdentityFor(
        string outputSchema) =>
        outputSchema switch
        {
            OutputSchema => (PromptVersion, PromptSha256),
            PreviousReviewableAudienceCopyOutputSchema =>
                (PromptVersion, PromptSha256),
            PreviousTerminalPeriodNormalizationOutputSchema =>
                (PromptVersion, PromptSha256),
            PreviousOutputLanguageRecoveryOutputSchema =>
                (PromptVersion, PromptSha256),
            PreviousNeutralPersonRecoveryOutputSchema =>
                (PreviousPromptVersion, PreviousPromptSha256),
            PreviousRetrospectiveGrammarRecoveryOutputSchema =>
                (PreviousPromptVersion, PreviousPromptSha256),
            PreviousLiteralActionRecoveryOutputSchema =>
                (PreviousPromptVersion, PreviousPromptSha256),
            PreviousWithheldEmbodimentCopyOutputSchema =>
                (PreviousPromptVersion, PreviousPromptSha256),
            PreviousCreatorEmbodimentRecoveryOutputSchema =>
                (PreviousPromptVersion, PreviousPromptSha256),
            PreviousTypedLanguageRecoveryOutputSchema =>
                (PreviousPromptVersion, PreviousPromptSha256),
            PreviousLanguageRecoveryOutputSchema =>
                (PreviousPromptVersion, PreviousPromptSha256),
            PreviousEditorialRephraseOutputSchema =>
                (PreviousPromptVersion, PreviousPromptSha256),
            PreviousInterfaceCorrectionOutputSchema =>
                (PreviousPromptVersion, PreviousPromptSha256),
            PreviousInterfaceAttributionOutputSchema =>
                (PreviousPromptVersion, PreviousPromptSha256),
            PreviousVisualDraftPromptOutputSchema =>
                (PreviousPromptVersion, PreviousPromptSha256),
            PreviousEffectiveVoiceOutputSchema =>
                (PreviousPromptVersion, PreviousPromptSha256),
            PreviousGroundedJsonWhitespaceOutputSchema =>
                (PreviousPromptVersion, PreviousPromptSha256),
            PreviousCreatorAuthorityOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousAudienceCopyWithholdingOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousCrossDraftRetryOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousRootPreloadOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousCudnnAttentionOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousPositionEmbeddingOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousAccelerateOffloadOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousVisionOffloadOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousLowPeakSamplingOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousPeakBoundedSamplingOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousSamplingOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreWatchdogOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PriorOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            LegacyOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            HistoricalOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PriorHistoricalOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            EarlierHistoricalOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            InitialOutputSchema => (PriorPromptVersion, PriorPromptSha256),
            OldestOutputSchema => (PriorPromptVersion, PriorPromptSha256),
            EarliestOutputSchema => (PriorPromptVersion, PriorPromptSha256),
            FoundationalOutputSchema =>
                (PriorPromptVersion, PriorPromptSha256),
            OriginalOutputSchema => (PriorPromptVersion, PriorPromptSha256),
            BaselineOutputSchema => (InitialPromptVersion, InitialPromptSha256),
            _ => throw new Qwen3VlOutputParseException(
                "Grounded Qwen metadata output schema is unsupported."),
        };

    internal static void ValidateGenerationWatchdogPolicy(JsonElement value)
    {
        Qwen3VlEditorialJson.Exact(
            value,
            "policyVersion",
            "policySha256",
            "maximumGenerationWallClockSeconds",
            "maximumGroundedCaseWallClockSeconds",
            "timeoutBehavior");
        ValidateGenerationWatchdogPolicyFields(value);
    }

    internal static void ValidateGenerationWatchdogSuccess(
        JsonElement value,
        int generationPassCount)
    {
        Qwen3VlEditorialJson.Exact(
            value,
            "policyVersion",
            "policySha256",
            "maximumGenerationWallClockSeconds",
            "maximumGroundedCaseWallClockSeconds",
            "timeoutBehavior",
            "generationInvocationCount",
            "elapsedCaseWallClockSeconds",
            "triggered",
            "timeoutReason");
        ValidateGenerationWatchdogPolicyFields(value);
        int invocationCount = Qwen3VlEditorialJson.Integer(
            value,
            "generationInvocationCount");
        TimeSpan elapsed = Seconds(value, "elapsedCaseWallClockSeconds");
        JsonElement reason = Qwen3VlEditorialJson.Property(value, "timeoutReason");
        if (invocationCount != generationPassCount ||
            elapsed.TotalSeconds >
                Qwen3VlGenerationWatchdogPolicy
                    .MaximumGroundedCaseWallClockSeconds ||
            Boolean(value, "triggered") ||
            reason.ValueKind != JsonValueKind.Null)
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen success watchdog provenance is invalid.");
        }
    }

    internal static void ValidateGroundingPacketReuse(
        JsonElement result,
        ClipEditorialMetadataRequest request,
        Qwen3VlGroundedMetadataGenerationValidation validation,
        IDictionary<string, (string RequestSha256, int SourceAttempt,
            string CandidateId, string FactWitness)> packets)
    {
        if (validation.GroundingPacketFactSha256 is not string factSha256)
        {
            return;
        }
        if (validation.GroundingPacketRequestSha256 is not string requestSha256 ||
            validation.GroundingPacketSourceAttempt is not int sourceAttempt ||
            validation.GroundingPacketReused is not bool reused)
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen packet provenance is incomplete.");
        }

        JsonElement generation = Qwen3VlEditorialJson.Object(result, "generation");
        string factWitness = string.Join(
            "\n",
            Qwen3VlEditorialJson.Property(generation, "visualDrafts").GetRawText(),
            Qwen3VlEditorialJson.Property(
                generation,
                "stableReadableText").GetRawText(),
            Qwen3VlEditorialJson.Property(
                generation,
                "primaryVisualDraftOrdinal").GetRawText(),
            Qwen3VlEditorialJson.Property(
                generation,
                "visualEventSelectionAssessments").GetRawText(),
            Qwen3VlEditorialJson.Property(
                generation,
                "selectedCurrentPassageId").GetRawText(),
            Qwen3VlEditorialJson.Property(
                generation,
                "knowledgeSelectionAssessments").GetRawText());

        if (!reused)
        {
            if (packets.ContainsKey(factSha256))
            {
                throw new Qwen3VlOutputParseException(
                    "Grounded Qwen rebuilt an already reported grounding packet.");
            }
            packets.Add(
                factSha256,
                (requestSha256, sourceAttempt, request.Context.CandidateId,
                    factWitness));
            return;
        }

        if (!packets.TryGetValue(factSha256, out var source) ||
            !source.RequestSha256.Equals(
                requestSha256,
                StringComparison.OrdinalIgnoreCase) ||
            source.SourceAttempt != sourceAttempt ||
            !source.CandidateId.Equals(
                request.Context.CandidateId,
                StringComparison.Ordinal) ||
            !source.FactWitness.Equals(factWitness, StringComparison.Ordinal))
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen reused a packet without identical prior facts.");
        }
    }

    private static void ValidateGenerationWatchdogPolicyFields(
        JsonElement value)
    {
        RequireText(
            value,
            "policyVersion",
            Qwen3VlGenerationWatchdogPolicy.Version);
        RequireText(
            value,
            "policySha256",
            Qwen3VlGenerationWatchdogPolicy.Sha256);
        RequireExactSeconds(
            value,
            "maximumGenerationWallClockSeconds",
            Qwen3VlGenerationWatchdogPolicy
                .MaximumGenerationWallClockSeconds);
        RequireExactSeconds(
            value,
            "maximumGroundedCaseWallClockSeconds",
            Qwen3VlGenerationWatchdogPolicy
                .MaximumGroundedCaseWallClockSeconds);
        RequireText(
            value,
            "timeoutBehavior",
            Qwen3VlGenerationWatchdogPolicy.TimeoutBehavior);
    }

    private static void RequireExactSeconds(
        JsonElement value,
        string name,
        double expected)
    {
        double actual = Seconds(value, name).TotalSeconds;
        if (Math.Abs(actual - expected) > 0.000001)
        {
            throw new Qwen3VlOutputParseException(
                $"Grounded Qwen metadata '{name}' changed.");
        }
    }
}
