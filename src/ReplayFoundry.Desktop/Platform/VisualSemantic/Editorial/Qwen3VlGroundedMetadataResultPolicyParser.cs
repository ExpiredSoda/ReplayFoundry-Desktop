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
            PreviousCreatorVoiceOutputSchema or
            PreviousEditorialFrameAdherenceOutputSchema or
            PreviousEditorialFramingOutputSchema or
            PreviousWholeBatchOutputSchema or
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
            PreviousCreatorVoiceOutputSchema =>
                (PreviousCreatorVoicePromptVersion,
                    PreviousCreatorVoicePromptSha256),
            PreviousEditorialFrameAdherenceOutputSchema =>
                (PreviousCreatorVoicePromptVersion,
                    PreviousCreatorVoicePromptSha256),
            PreviousEditorialFramingOutputSchema =>
                (PreviousEditorialFramingPromptVersion,
                    PreviousEditorialFramingPromptSha256),
            PreviousWholeBatchOutputSchema =>
                (PreviousPromptVersion, PreviousPromptSha256),
            PreviousReviewableAudienceCopyOutputSchema =>
                (PreviousPromptVersion, PreviousPromptSha256),
            PreviousTerminalPeriodNormalizationOutputSchema =>
                (PreviousPromptVersion, PreviousPromptSha256),
            PreviousOutputLanguageRecoveryOutputSchema =>
                (PreviousPromptVersion, PreviousPromptSha256),
            PreviousNeutralPersonRecoveryOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousRetrospectiveGrammarRecoveryOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousLiteralActionRecoveryOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousWithheldEmbodimentCopyOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousCreatorEmbodimentRecoveryOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousTypedLanguageRecoveryOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousLanguageRecoveryOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousEditorialRephraseOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousInterfaceCorrectionOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousInterfaceAttributionOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousVisualDraftPromptOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousEffectiveVoiceOutputSchema =>
                (EarlierPromptVersion, EarlierPromptSha256),
            PreviousGroundedJsonWhitespaceOutputSchema =>
                (PriorPromptVersion, PriorPromptSha256),
            PreviousCreatorAuthorityOutputSchema =>
                (PriorPromptVersion, PriorPromptSha256),
            PreviousAudienceCopyWithholdingOutputSchema =>
                (PriorPromptVersion, PriorPromptSha256),
            PreviousCrossDraftRetryOutputSchema =>
                (PriorPromptVersion, PriorPromptSha256),
            PreviousRootPreloadOutputSchema =>
                (PriorPromptVersion, PriorPromptSha256),
            PreviousCudnnAttentionOutputSchema =>
                (PriorPromptVersion, PriorPromptSha256),
            PreviousPositionEmbeddingOutputSchema =>
                (PriorPromptVersion, PriorPromptSha256),
            PreviousAccelerateOffloadOutputSchema =>
                (PriorPromptVersion, PriorPromptSha256),
            PreviousVisionOffloadOutputSchema =>
                (PriorPromptVersion, PriorPromptSha256),
            PreviousLowPeakSamplingOutputSchema =>
                (PriorPromptVersion, PriorPromptSha256),
            PreviousPeakBoundedSamplingOutputSchema =>
                (PriorPromptVersion, PriorPromptSha256),
            PreviousSamplingOutputSchema =>
                (PriorPromptVersion, PriorPromptSha256),
            PreWatchdogOutputSchema =>
                (PriorPromptVersion, PriorPromptSha256),
            PreviousOutputSchema =>
                (PriorPromptVersion, PriorPromptSha256),
            PriorOutputSchema =>
                (PriorPromptVersion, PriorPromptSha256),
            LegacyOutputSchema =>
                (PriorPromptVersion, PriorPromptSha256),
            HistoricalOutputSchema =>
                (PriorPromptVersion, PriorPromptSha256),
            PriorHistoricalOutputSchema =>
                (PriorPromptVersion, PriorPromptSha256),
            EarlierHistoricalOutputSchema =>
                (PriorPromptVersion, PriorPromptSha256),
            InitialOutputSchema => (InitialPromptVersion, InitialPromptSha256),
            OldestOutputSchema => (InitialPromptVersion, InitialPromptSha256),
            EarliestOutputSchema => (InitialPromptVersion, InitialPromptSha256),
            FoundationalOutputSchema =>
                (InitialPromptVersion, InitialPromptSha256),
            OriginalOutputSchema => (InitialPromptVersion, InitialPromptSha256),
            BaselineOutputSchema =>
                (BaselinePromptVersion, BaselinePromptSha256),
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
            generation.TryGetProperty("editorialFrame", out JsonElement frame)
                ? frame.GetRawText()
                : string.Empty,
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
