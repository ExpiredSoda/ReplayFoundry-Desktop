using System.Text.Json;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataGenerator;
namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataGenerationSchemaParser
{
    internal static (
        JsonElement Generation,
        Qwen3VlGroundedMetadataGenerationSchemaProfile Profile) Parse(
            JsonElement result,
            string outputSchema)
    {
        string[] groundedJsonSchemas =
        [
            OutputSchema,
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
        ];
        bool groundedJsonCanonicalWhitespace = groundedJsonSchemas.Contains(
            outputSchema,
            StringComparer.Ordinal);
        bool creatorAuthorityRetrySourceWithholding = outputSchema.Equals(
            PreviousGroundedJsonWhitespaceOutputSchema,
            StringComparison.Ordinal) || groundedJsonCanonicalWhitespace;
        bool semanticExhaustionRecovery =
            creatorAuthorityRetrySourceWithholding || outputSchema.Equals(
                PreviousCreatorAuthorityOutputSchema,
                StringComparison.Ordinal);
        bool crossDraftRetrySourceWithholding = semanticExhaustionRecovery ||
            outputSchema.Equals(
                PreviousAudienceCopyWithholdingOutputSchema,
                StringComparison.Ordinal);
        bool lowPeakSampling = crossDraftRetrySourceWithholding ||
            outputSchema.Equals(
                PreviousCrossDraftRetryOutputSchema,
                StringComparison.Ordinal) || outputSchema.Equals(
                PreviousRootPreloadOutputSchema,
                StringComparison.Ordinal) ||
            outputSchema.Equals(
                PreviousCudnnAttentionOutputSchema,
                StringComparison.Ordinal) ||
            outputSchema.Equals(
                PreviousPositionEmbeddingOutputSchema,
                StringComparison.Ordinal) ||
            outputSchema.Equals(
                PreviousAccelerateOffloadOutputSchema,
                StringComparison.Ordinal) ||
            outputSchema.Equals(
                PreviousVisionOffloadOutputSchema,
                StringComparison.Ordinal);
        bool fourDraftEventSelection = lowPeakSampling ||
            outputSchema.Equals(
                PreviousLowPeakSamplingOutputSchema,
                StringComparison.Ordinal);
        bool peakBoundedSampling = fourDraftEventSelection ||
            outputSchema.Equals(
                PreviousPeakBoundedSamplingOutputSchema,
                StringComparison.Ordinal);
        bool strictRetryAnchorSourceRule = peakBoundedSampling ||
            outputSchema.Equals(
                PreviousSamplingOutputSchema,
                StringComparison.Ordinal) ||
            outputSchema.Equals(PreWatchdogOutputSchema, StringComparison.Ordinal);
        bool conditionalRecoveryPoolSource = strictRetryAnchorSourceRule ||
            outputSchema.Equals(PreviousOutputSchema, StringComparison.Ordinal);
        bool retryableContinuationRecoveryPool =
            conditionalRecoveryPoolSource ||
            outputSchema.Equals(PriorOutputSchema, StringComparison.Ordinal);
        bool synthesisRecoveryPool = retryableContinuationRecoveryPool ||
            outputSchema.Equals(LegacyOutputSchema, StringComparison.Ordinal);
        bool nonRetrospectiveRetryAnchor = synthesisRecoveryPool ||
            outputSchema.Equals(HistoricalOutputSchema, StringComparison.Ordinal);
        bool sampledSynthesis = outputSchema.Equals(
                HistoricalOutputSchema,
                StringComparison.Ordinal) ||
            outputSchema.Equals(PriorHistoricalOutputSchema, StringComparison.Ordinal);
        bool boundedDuplicateRefinement = synthesisRecoveryPool ||
            sampledSynthesis ||
            outputSchema.Equals(
                EarlierHistoricalOutputSchema,
                StringComparison.Ordinal) ||
            outputSchema.Equals(InitialOutputSchema, StringComparison.Ordinal);
        bool actorAuthority = boundedDuplicateRefinement ||
            outputSchema.Equals(OldestOutputSchema, StringComparison.Ordinal);
        bool evidenceIsolation = actorAuthority || outputSchema.Equals(
            EarliestOutputSchema,
            StringComparison.Ordinal);
        bool packetReuse = evidenceIsolation || outputSchema.Equals(
            FoundationalOutputSchema,
            StringComparison.Ordinal);
        bool adaptiveSampling = packetReuse || outputSchema.Equals(
            OriginalOutputSchema,
            StringComparison.Ordinal);
        var profile = new Qwen3VlGroundedMetadataGenerationSchemaProfile(
            groundedJsonCanonicalWhitespace,
            groundedJsonCanonicalWhitespace,
            strictRetryAnchorSourceRule,
            conditionalRecoveryPoolSource,
            retryableContinuationRecoveryPool,
            synthesisRecoveryPool,
            nonRetrospectiveRetryAnchor,
            sampledSynthesis,
            boundedDuplicateRefinement,
            actorAuthority,
            actorAuthority,
            evidenceIsolation,
            packetReuse,
            adaptiveSampling,
            lowPeakSampling,
            peakBoundedSampling,
            fourDraftEventSelection,
            creatorAuthorityRetrySourceWithholding,
            crossDraftRetrySourceWithholding,
            semanticExhaustionRecovery,
            Qwen3VlGroundedMetadataSchemaCapabilities
                .SupportsLiteralActionPrompt(outputSchema),
            Qwen3VlGroundedMetadataSchemaCapabilities
                .SupportsInterfaceAttribution(outputSchema),
            Qwen3VlGroundedMetadataSchemaCapabilities
                .SupportsEditorialRephrase(outputSchema),
            Qwen3VlGroundedMetadataSchemaCapabilities
                .SupportsRejectedLanguageRecovery(outputSchema),
            Qwen3VlGroundedMetadataSchemaCapabilities
                .SupportsTypedLanguageRecovery(outputSchema),
            Qwen3VlGroundedMetadataSchemaCapabilities
                .SupportsCreatorEmbodimentRecovery(outputSchema),
            Qwen3VlGroundedMetadataSchemaCapabilities
                .SupportsWithheldEmbodimentCopy(outputSchema),
            Qwen3VlGroundedMetadataSchemaCapabilities
                .SupportsLiteralActionRecovery(outputSchema),
            Qwen3VlGroundedMetadataSchemaCapabilities
                .SupportsRetrospectiveGrammarRecovery(outputSchema),
            Qwen3VlGroundedMetadataSchemaCapabilities
                .SupportsNeutralPersonRecovery(outputSchema),
            Qwen3VlGroundedMetadataSchemaCapabilities
                .SupportsOutputLanguageRecovery(outputSchema),
            Qwen3VlGroundedMetadataSchemaCapabilities
                .SupportsTerminalPeriodNormalization(outputSchema),
            Qwen3VlGroundedMetadataSchemaCapabilities
                .SupportsReviewableAudienceCopy(outputSchema));
        JsonElement generation = Qwen3VlEditorialJson.Object(result, "generation");
        RequireExactFields(generation, profile);
        return (generation, profile);
    }
    private static void RequireExactFields(
        JsonElement generation,
        Qwen3VlGroundedMetadataGenerationSchemaProfile profile)
    {
        string[] generationFields =
        [
            "generatedTokenCount",
            "maximumNewTokens",
            "terminationReason",
            "firstEndOfSequenceGeneratedIndex",
            "decodedTextSha256",
            .. (profile.ReviewableAudienceCopy
                ? new[] { "metadataReviewRequired", "metadataReviewIssues" }
                : Array.Empty<string>()),
            "generationPassCount",
            "visualDraftCount",
            "visualDrafts",
            "stableReadableText",
            "stableReadableTextPolicyVersion",
            "visualDraftPromptVersion",
            "visualDraftPromptSha256",
            "visualDraftSchemaVersion",
            "visualEventSelectionApplied",
            "primaryVisualDraftOrdinal",
            "visualEventSelectionAssessmentCount",
            "visualEventSelectionAssessments",
            "visualEventSelectionPromptVersion",
            "visualEventSelectionPromptSha256",
            "visualEventSelectionSchemaVersion",
            "knowledgeSelectionApplied",
            "selectedCurrentPassageId",
            "knowledgeSelectionAssessmentCount",
            "knowledgeSelectionAssessments",
            "knowledgeSelectionPromptVersion",
            "knowledgeSelectionPromptSha256",
            "knowledgeSelectionSchemaVersion",
            "groundingReviewApplied",
            "rejectedValidationRules",
            "videoFramesPerSecond",
            "minimumVideoFrames",
            "maximumVideoFrames",
            "maximumPixelsPerFrame",
            "maximumTotalVideoPixels",
        ];
        string[] actorAuthorityFields =
        [
            "samplingPolicyVersion",
            "groundingPassCount",
            "synthesisPassCount",
            "groundingPacketSchemaVersion",
            "groundingPacketRequestSha256",
            "groundingPacketFactSha256",
            "groundingPacketSourceAttempt",
            "groundingPacketReused",
            "synthesisEvidencePolicyVersion",
            "primaryOnlySynthesisEvidenceApplied",
            "actorAuthorityAssessmentApplied",
            "primaryActorAuthority",
            "primaryCreatorExperienceRelation",
            "rerollDiversityPolicyVersion",
            "priorAcceptedTitleCount",
            "rerollTitleDiversityCode",
            "rerollTitleTokenJaccardNumerator",
            "rerollTitleTokenJaccardDenominator",
        ];
        string[] retryAnchorFields = profile.NonRetrospectiveRetryAnchor
            ? Qwen3VlGroundedMetadataGenerationFields.RetryAnchorFields
            : [];
        string[] duplicateRecoveryFields =
            Qwen3VlGroundedMetadataGenerationFields.DuplicateRecoveryFields;
        string[] sampledSynthesisFields =
            Qwen3VlGroundedMetadataGenerationFields.SampledSynthesisFields;
        string[] recoveryPoolFields =
            Qwen3VlGroundedMetadataGenerationFields.RecoveryPoolFields;
        string[] currentRecoveryPoolFields =
        [
            "synthesisRecoveryPoolRetryableSemanticRejections",
            "synthesisRecoveryPoolRetryableSemanticRejectionsSha256",
        ];
        string[] conditionalRecoveryPoolSourceFields =
        [
            "synthesisRecoveryPoolSourceSelectionReason",
        ];
        string[] groundedJsonWhitespaceFields =
        [
            "groundedJsonWhitespacePolicyVersion",
            "groundedJsonWhitespacePolicySha256",
            "groundedJsonAnyWhitespace",
        ];
        string[] editorialRephraseFields =
        [
            "editorialRephrasePolicyVersion",
            "editorialRephrasePolicySha256",
            "editorialRephraseAttempted",
            "editorialRephraseApplied",
            "editorialRephraseOutcome",
            "editorialRephraseSourceJsonSha256",
            "editorialRephraseOutputJsonSha256",
            "editorialRephraseRejectionCode",
            "editorialRephraseCanonicalMessagesSha256",
            "editorialRephraseRenderedPromptSha256",
            "editorialRephraseRenderedPromptUtf8ByteCount",
            "editorialRephraseInputTokenIdsSha256",
            "editorialRephraseInputTokenCount",
            "editorialRephraseRawOutputSha256",
        ];
        Qwen3VlEditorialJson.Exact(
            generation,
            profile.SynthesisRecoveryPool
                ? [
                    .. generationFields,
                    .. actorAuthorityFields,
                    .. duplicateRecoveryFields,
                    .. recoveryPoolFields,
                    .. (profile.RetryableContinuationRecoveryPool
                        ? currentRecoveryPoolFields
                        : []),
                    .. (profile.ConditionalRecoveryPoolSource
                        ? conditionalRecoveryPoolSourceFields
                        : []),
                    .. (profile.GroundedJsonCanonicalWhitespace
                        ? groundedJsonWhitespaceFields
                        : []),
                    .. retryAnchorFields,
                    .. (profile.EditorialRephrase
                        ? editorialRephraseFields
                        : []),
                ]
                : profile.SampledSynthesis
                ? [
                    .. generationFields,
                    .. actorAuthorityFields,
                    .. duplicateRecoveryFields,
                    .. sampledSynthesisFields,
                    .. retryAnchorFields,
                ]
                : profile.BoundedDuplicateRefinement
                ? [
                    .. generationFields,
                    .. actorAuthorityFields,
                    .. duplicateRecoveryFields,
                ]
                : profile.ActorAuthority
                ? [.. generationFields, .. actorAuthorityFields]
                : profile.EvidenceIsolation
                ? [
                    .. generationFields,
                    "samplingPolicyVersion",
                    "groundingPassCount",
                    "synthesisPassCount",
                    "groundingPacketSchemaVersion",
                    "groundingPacketRequestSha256",
                    "groundingPacketFactSha256",
                    "groundingPacketSourceAttempt",
                    "groundingPacketReused",
                    "synthesisEvidencePolicyVersion",
                    "primaryOnlySynthesisEvidenceApplied",
                ]
                : profile.PacketReuse
                ? [
                    .. generationFields,
                    "samplingPolicyVersion",
                    "groundingPassCount",
                    "synthesisPassCount",
                    "groundingPacketSchemaVersion",
                    "groundingPacketRequestSha256",
                    "groundingPacketFactSha256",
                    "groundingPacketSourceAttempt",
                    "groundingPacketReused",
                ]
                : profile.AdaptiveSampling
                ? [.. generationFields, "samplingPolicyVersion"]
                : generationFields);
        if (profile.GroundedJsonCanonicalWhitespace &&
            (!Qwen3VlEditorialJson.Text(
                    generation,
                    "groundedJsonWhitespacePolicyVersion").Equals(
                        Qwen3VlGroundedMetadataJsonWhitespacePolicy.Version,
                        StringComparison.Ordinal) ||
             !Qwen3VlEditorialJson.Text(
                    generation,
                    "groundedJsonWhitespacePolicySha256").Equals(
                        Qwen3VlGroundedMetadataJsonWhitespacePolicy.Sha256,
                        StringComparison.OrdinalIgnoreCase) ||
             Qwen3VlGroundedMetadataJson.Boolean(
                 generation,
                 "groundedJsonAnyWhitespace") !=
                 Qwen3VlGroundedMetadataJsonWhitespacePolicy.AnyWhitespace))
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen JSON-whitespace policy changed.");
        }
    }
}
