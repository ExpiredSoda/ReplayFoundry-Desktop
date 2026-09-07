using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataGenerator;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataSchemaCapabilities
{
    private static readonly string[] NewestToOldest =
    [
        OutputSchema,
        PreviousAccelerationOutputSchema,
        PreviousResponsibilitySplitOutputSchema,
        PreviousCompactIsolatedFieldAuthoringOutputSchema,
        PreviousIsolatedFieldAuthoringOutputSchema,
        PreviousSchemaEnforcedBalancedCopyOutputSchema,
        PreviousCompactBalancedCopyOutputSchema,
        PreviousBalancedCopyOutputSchema,
        PreviousCommentaryTimingOutputSchema,
        PreviousCreatorVoiceOutputSchema,
        PreviousEditorialFrameAdherenceOutputSchema,
        PreviousEditorialFramingOutputSchema,
        PreviousWholeBatchOutputSchema,
        PreviousReviewableAudienceCopyOutputSchema,
        PreviousTerminalPeriodNormalizationOutputSchema,
        PreviousOutputLanguageRecoveryOutputSchema,
        PreviousNeutralPersonRecoveryOutputSchema,
        PreviousRetrospectiveGrammarRecoveryOutputSchema,
        PreviousLiteralActionRecoveryOutputSchema,
        PreviousWithheldEmbodimentCopyOutputSchema,
        PreviousCreatorEmbodimentRecoveryOutputSchema,
        PreviousTypedLanguageRecoveryOutputSchema,
        PreviousLanguageRecoveryOutputSchema,
        PreviousEditorialRephraseOutputSchema,
        PreviousInterfaceCorrectionOutputSchema,
        PreviousInterfaceAttributionOutputSchema,
        PreviousVisualDraftPromptOutputSchema,
        PreviousEffectiveVoiceOutputSchema,
        PreviousGroundedJsonWhitespaceOutputSchema,
        PreviousCreatorAuthorityOutputSchema,
        PreviousAudienceCopyWithholdingOutputSchema,
        PreviousCrossDraftRetryOutputSchema,
        PreviousRootPreloadOutputSchema,
        PreviousCudnnAttentionOutputSchema,
        PreviousPositionEmbeddingOutputSchema,
        PreviousAccelerateOffloadOutputSchema,
        PreviousVisionOffloadOutputSchema,
        PreviousLowPeakSamplingOutputSchema,
        PreviousPeakBoundedSamplingOutputSchema,
        PreviousSamplingOutputSchema,
        PreWatchdogOutputSchema,
        PreviousOutputSchema,
        PriorOutputSchema,
        LegacyOutputSchema,
        HistoricalOutputSchema,
        PriorHistoricalOutputSchema,
        EarlierHistoricalOutputSchema,
        InitialOutputSchema,
        OldestOutputSchema,
        EarliestOutputSchema,
        FoundationalOutputSchema,
        OriginalOutputSchema,
        BaselineOutputSchema,
    ];

    internal static bool IsNewerThan(string schema, string boundarySchema)
    {
        int schemaIndex = Array.IndexOf(NewestToOldest, schema);
        int boundaryIndex = Array.IndexOf(NewestToOldest, boundarySchema);
        return schemaIndex >= 0 && boundaryIndex >= 0 && schemaIndex < boundaryIndex;
    }

    internal static bool SupportsLiteralActionPrompt(string schema) =>
        IsNewerThan(schema, PreviousVisualDraftPromptOutputSchema);

    internal static bool SupportsInterfaceAttribution(string schema) =>
        IsNewerThan(schema, PreviousInterfaceAttributionOutputSchema);

    internal static bool SupportsEditorialRephrase(string schema) =>
        IsNewerThan(schema, PreviousEditorialRephraseOutputSchema);

    internal static bool SupportsRejectedLanguageRecovery(string schema) =>
        IsNewerThan(schema, PreviousLanguageRecoveryOutputSchema);

    internal static bool SupportsTypedLanguageRecovery(string schema) =>
        IsNewerThan(schema, PreviousTypedLanguageRecoveryOutputSchema);

    internal static bool SupportsCreatorEmbodimentRecovery(string schema) =>
        IsNewerThan(schema, PreviousCreatorEmbodimentRecoveryOutputSchema);

    internal static bool SupportsWithheldEmbodimentCopy(string schema) =>
        IsNewerThan(schema, PreviousWithheldEmbodimentCopyOutputSchema);

    internal static bool SupportsLiteralActionRecovery(string schema) =>
        IsNewerThan(schema, PreviousLiteralActionRecoveryOutputSchema);

    internal static bool SupportsRetrospectiveGrammarRecovery(string schema) =>
        IsNewerThan(schema, PreviousRetrospectiveGrammarRecoveryOutputSchema);

    internal static bool SupportsNeutralPersonRecovery(string schema) =>
        IsNewerThan(schema, PreviousNeutralPersonRecoveryOutputSchema);

    internal static bool SupportsOutputLanguageRecovery(string schema) =>
        IsNewerThan(schema, PreviousOutputLanguageRecoveryOutputSchema);

    internal static bool SupportsTerminalPeriodNormalization(string schema) =>
        IsNewerThan(schema, PreviousTerminalPeriodNormalizationOutputSchema);

    internal static bool SupportsReviewableAudienceCopy(string schema) =>
        IsNewerThan(schema, PreviousReviewableAudienceCopyOutputSchema);

    internal static bool SupportsPerCaseFailure(string schema) =>
        IsNewerThan(schema, PreviousWholeBatchOutputSchema);

    internal static bool SupportsEditorialFraming(string schema) =>
        IsNewerThan(schema, PreviousEditorialFramingOutputSchema);

    internal static bool SupportsEditorialFrameAdherence(string schema) =>
        IsNewerThan(schema, PreviousEditorialFrameAdherenceOutputSchema);

    internal static bool SupportsSynthesisSanitizationModule(string schema) =>
        IsNewerThan(schema, PreviousEditorialFrameAdherenceOutputSchema);

    internal static bool SupportsRecoveryCandidateSelectionModule(
        string schema) =>
        IsNewerThan(schema, PreviousCreatorVoiceOutputSchema);

    internal static bool SupportsEditorialRephraseEligibilitySkip(
        string schema) =>
        IsNewerThan(schema, PreviousEditorialFrameAdherenceOutputSchema);

    internal static bool SupportsBestAvailableVisualEvidence(string schema) =>
        IsNewerThan(schema, PreviousEditorialFrameAdherenceOutputSchema);

    internal static bool SupportsSeparatedTitleTags(string schema) =>
        IsNewerThan(schema, PreviousCreatorVoiceOutputSchema);

    internal static bool SupportsCommentaryTiming(string schema) =>
        IsNewerThan(schema, PreviousCommentaryTimingOutputSchema);

    internal static bool SupportsBalancedCopy(string schema) =>
        IsNewerThan(schema, PreviousBalancedCopyOutputSchema);

    internal static bool SupportsCompactBalancedCopy(string schema) =>
        IsNewerThan(schema, PreviousCompactBalancedCopyOutputSchema);

    internal static bool SupportsSchemaEnforcedBalancedCopy(string schema) =>
        IsNewerThan(schema, PreviousSchemaEnforcedBalancedCopyOutputSchema);

    internal static bool SupportsIsolatedFieldAuthoring(string schema) =>
        IsNewerThan(schema, PreviousIsolatedFieldAuthoringOutputSchema);

    internal static bool SupportsCompactIsolatedFieldAuthoring(string schema) =>
        IsNewerThan(schema, PreviousCompactIsolatedFieldAuthoringOutputSchema);

    internal static bool SupportsEditorialResponsibilityModules(string schema) =>
        IsNewerThan(schema, PreviousResponsibilitySplitOutputSchema);
}
