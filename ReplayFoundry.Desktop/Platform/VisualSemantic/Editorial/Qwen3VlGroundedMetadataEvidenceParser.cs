using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataGenerator;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataJson;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataSelection;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataEvidenceParser
{
    internal static Qwen3VlGroundedMetadataEvidenceValidation Parse(
        JsonElement result,
        JsonElement generation,
        ClipEditorialMetadataRequest request,
        string outputSchema,
        Qwen3VlGroundedMetadataGenerationSchemaProfile profile,
        Qwen3VlGroundedMetadataRecoveryValidation recovery)
    {
        bool selectionApplied = Boolean(generation, "knowledgeSelectionApplied");
        string selectedPassageId = Qwen3VlEditorialJson.Text(
            generation,
            "selectedCurrentPassageId");
        RequireText(
            generation,
            "knowledgeSelectionPromptVersion",
            KnowledgeSelectionPromptVersion);
        RequireText(
            generation,
            "knowledgeSelectionPromptSha256",
            KnowledgeSelectionPromptSha256);
        RequireText(
            generation,
            "knowledgeSelectionSchemaVersion",
            KnowledgeSelectionSchemaVersion);
        bool includeClipLinked = IncludesClipLinkedKnowledgeSelection(
            outputSchema);
        string[] candidateIds = (request.Context.GameKnowledge?.Matches ?? [])
            .Where(match => IsCurrentKnowledgeCandidate(
                match.Strength,
                match.TemporalRelation,
                includeClipLinked))
            .Select(static match => match.Passage.Id)
            .ToArray();
        int assessmentCount = Qwen3VlEditorialJson.Integer(
            generation,
            "knowledgeSelectionAssessmentCount");
        JsonElement[] assessments = Qwen3VlEditorialJson.Array(
            generation,
            "knowledgeSelectionAssessments");
        if (assessmentCount != assessments.Length ||
            assessments.Length != candidateIds.Length)
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen knowledge-assessment count is invalid.");
        }
        var validatedAssessments =
            new List<Qwen3VlGroundedMetadataKnowledgeAssessment>(
                assessments.Length);
        for (int index = 0; index < assessments.Length; index++)
        {
            JsonElement assessment = assessments[index];
            Qwen3VlEditorialJson.Exact(
                assessment,
                "passageId",
                "settingSupport",
                "entityIdentitySupport",
                "distinctiveObjectSupport",
                "centralActionSupport",
                "chronologySupport",
                "materialContradiction");
            string passageId = Qwen3VlEditorialJson.Text(
                assessment,
                "passageId");
            if (!passageId.Equals(candidateIds[index], StringComparison.Ordinal))
            {
                throw new Qwen3VlOutputParseException(
                    "Grounded Qwen knowledge-assessment ordering is invalid.");
            }
            validatedAssessments.Add(
                new Qwen3VlGroundedMetadataKnowledgeAssessment(
                    passageId,
                    Boolean(assessment, "settingSupport"),
                    Boolean(assessment, "entityIdentitySupport"),
                    Boolean(assessment, "distinctiveObjectSupport"),
                    Boolean(assessment, "centralActionSupport"),
                    Boolean(assessment, "chronologySupport"),
                    Boolean(assessment, "materialContradiction")));
        }
        string expectedPassageId = SelectKnowledgePassage(validatedAssessments);
        if (selectionApplied != (candidateIds.Length > 0) ||
            !selectionApplied && selectedPassageId != "None" ||
            !selectedPassageId.Equals(expectedPassageId, StringComparison.Ordinal))
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen knowledge-selection provenance is invalid.");
        }
        bool groundingReviewApplied = Boolean(
            generation,
            "groundingReviewApplied");
        string[] rejectedRules = Qwen3VlEditorialJson.Array(
                generation,
                "rejectedValidationRules")
            .Select(static value => value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!
                : throw new Qwen3VlOutputParseException(
                    "Grounded Qwen metadata validation-rule identities must be text."))
            .ToArray();
        if (profile.EvidenceIsolation)
        {
            RequireText(
                generation,
                "synthesisEvidencePolicyVersion",
                SynthesisEvidencePolicyVersion);
            bool rejectedCrossDraft = rejectedRules.Contains(
                "CrossDraftTitleContamination",
                StringComparer.Ordinal);
            if (recovery.PrimaryOnlySynthesisEvidenceApplied != rejectedCrossDraft)
            {
                throw new Qwen3VlOutputParseException(
                    "Grounded Qwen synthesis-evidence retry provenance is invalid.");
            }
        }
        Qwen3VlGroundedMetadataSamplingPolicy.ValidateSummary(
            generation,
            profile.AdaptiveSampling,
            profile.PeakBoundedSampling,
            profile.LowPeakSampling);
        ValidateStructuredDecodingAudit(
            result,
            recovery.GeneratedTokenCount,
            profile.GroundedTagShapeConstrained);
        return new(
            selectionApplied,
            selectedPassageId,
            validatedAssessments.AsReadOnly(),
            groundingReviewApplied,
            rejectedRules);
    }

    internal static bool IncludesClipLinkedKnowledgeSelection(
        string outputSchema) =>
        outputSchema.Equals(OutputSchema, StringComparison.Ordinal) ||
        outputSchema.Equals(
            PreviousReviewableAudienceCopyOutputSchema,
            StringComparison.Ordinal) ||
        outputSchema.Equals(
            PreviousTerminalPeriodNormalizationOutputSchema,
            StringComparison.Ordinal) ||
        outputSchema.Equals(
            PreviousOutputLanguageRecoveryOutputSchema,
            StringComparison.Ordinal) ||
        outputSchema.Equals(PreviousNeutralPersonRecoveryOutputSchema, StringComparison.Ordinal) || outputSchema.Equals(PreviousRetrospectiveGrammarRecoveryOutputSchema, StringComparison.Ordinal) ||
        outputSchema.Equals(
            PreviousLiteralActionRecoveryOutputSchema,
            StringComparison.Ordinal) ||
        outputSchema.Equals(
            PreviousWithheldEmbodimentCopyOutputSchema,
            StringComparison.Ordinal) ||
        outputSchema.Equals(
            PreviousCreatorEmbodimentRecoveryOutputSchema,
            StringComparison.Ordinal) ||
        outputSchema.Equals(
            PreviousTypedLanguageRecoveryOutputSchema,
            StringComparison.Ordinal) ||
        outputSchema.Equals(
            PreviousLanguageRecoveryOutputSchema,
            StringComparison.Ordinal) ||
        outputSchema.Equals(
            PreviousEditorialRephraseOutputSchema,
            StringComparison.Ordinal) ||
        outputSchema.Equals(
            PreviousInterfaceCorrectionOutputSchema,
            StringComparison.Ordinal) ||
        outputSchema.Equals(
            PreviousInterfaceAttributionOutputSchema,
            StringComparison.Ordinal) ||
        outputSchema.Equals(
            PreviousVisualDraftPromptOutputSchema,
            StringComparison.Ordinal) ||
        outputSchema.Equals(
            PreviousEffectiveVoiceOutputSchema,
            StringComparison.Ordinal) ||
        outputSchema.Equals(
            PreviousCreatorAuthorityOutputSchema,
            StringComparison.Ordinal) ||
        outputSchema.Equals(
            PreviousAudienceCopyWithholdingOutputSchema,
            StringComparison.Ordinal) ||
        outputSchema.Equals(
            PreviousCrossDraftRetryOutputSchema,
            StringComparison.Ordinal) ||
        outputSchema.Equals(
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
            StringComparison.Ordinal) ||
        outputSchema.Equals(
            PreviousLowPeakSamplingOutputSchema,
            StringComparison.Ordinal) ||
        outputSchema.Equals(
            PreviousPeakBoundedSamplingOutputSchema,
            StringComparison.Ordinal) ||
        outputSchema.Equals(
            PreviousSamplingOutputSchema,
            StringComparison.Ordinal) ||
        outputSchema.Equals(PreviousOutputSchema, StringComparison.Ordinal) ||
        outputSchema.Equals(PriorOutputSchema, StringComparison.Ordinal) ||
        outputSchema.Equals(LegacyOutputSchema, StringComparison.Ordinal) ||
        outputSchema.Equals(HistoricalOutputSchema, StringComparison.Ordinal) ||
        outputSchema.Equals(
            PriorHistoricalOutputSchema,
            StringComparison.Ordinal) ||
        outputSchema.Equals(
            EarlierHistoricalOutputSchema,
            StringComparison.Ordinal) ||
        outputSchema.Equals(InitialOutputSchema, StringComparison.Ordinal) ||
        outputSchema.Equals(OldestOutputSchema, StringComparison.Ordinal);

    private static void ValidateStructuredDecodingAudit(
        JsonElement result,
        int generatedTokenCount,
        bool groundedTagShapeConstrained)
    {
        JsonElement audit = Qwen3VlEditorialJson.Object(
            result,
            "structuredDecodingAudit");
        Qwen3VlEditorialJson.Exact(
            audit,
            "policyVersion",
            "backendName",
            "backendVersion",
            "schemaVersion",
            "schemaSha256",
            "representation",
            "cudaMaskBackend",
            "compileElapsedSeconds",
            "generatedTokenCount",
            "grammarTerminationState",
            "strictParserAccepted",
            "unconstrainedFallbackUsed",
            "semanticRepairApplied");
        RequireText(
            audit,
            "policyVersion",
            Qwen3VlEditorialStructuredDecodingPolicy.Version);
        RequireText(
            audit,
            "backendName",
            Qwen3VlEditorialStructuredDecodingPolicy.BackendName);
        RequireText(
            audit,
            "backendVersion",
            Qwen3VlEditorialStructuredDecodingPolicy.BackendVersion);
        RequireText(
            audit,
            "schemaVersion",
            groundedTagShapeConstrained
                ? MetadataSchemaVersion
                : PreviousMetadataSchemaVersion);
        RequireText(
            audit,
            "representation",
            Qwen3VlEditorialStructuredDecodingPolicy.Representation.ToString());
        RequireText(
            audit,
            "cudaMaskBackend",
            Qwen3VlEditorialStructuredDecodingPolicy.CudaMaskBackend);
        _ = Qwen3VlEditorialJson.Sha256(audit, "schemaSha256");
        _ = Qwen3VlEditorialJson.Finite(audit, "compileElapsedSeconds");
        if (Qwen3VlEditorialJson.Integer(
                audit,
                "generatedTokenCount") != generatedTokenCount ||
            !Qwen3VlEditorialJson.Text(audit, "grammarTerminationState")
                .Equals("EndOfSequence", StringComparison.Ordinal) ||
            !Boolean(audit, "strictParserAccepted") ||
            Boolean(audit, "unconstrainedFallbackUsed") ||
            Boolean(audit, "semanticRepairApplied"))
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen metadata did not use strict constrained decoding.");
        }
    }
}
