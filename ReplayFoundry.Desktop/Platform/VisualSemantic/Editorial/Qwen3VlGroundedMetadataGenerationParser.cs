using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataGenerator;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataJson;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataGenerationParser
{
    internal static Qwen3VlGroundedMetadataGenerationValidation Parse(
        JsonElement result,
        ClipEditorialMetadataRequest request,
        string outputSchema)
    {
        (JsonElement generation,
            Qwen3VlGroundedMetadataGenerationSchemaProfile profile) =
            Qwen3VlGroundedMetadataGenerationSchemaParser.Parse(
                result,
                outputSchema);
        Qwen3VlGroundedMetadataRecoveryValidation recovery =
            Qwen3VlGroundedMetadataRecoveryParser.Parse(
                generation,
                request,
                outputSchema,
                profile);
        Qwen3VlGroundedMetadataVisualValidation visual =
            Qwen3VlGroundedMetadataVisualParser.Parse(
                generation,
                profile,
                recovery);
        Qwen3VlGroundedMetadataEvidenceValidation evidence =
            Qwen3VlGroundedMetadataEvidenceParser.Parse(
                result,
                generation,
                request,
                outputSchema,
                profile,
                recovery);
        ValidatePassProvenance(profile, recovery, visual, evidence);
        _ = Seconds(result, "elapsedSeconds");
        return BuildValidation(recovery, visual, evidence);
    }

    internal static bool IncludesClipLinkedKnowledgeSelection(
        string outputSchema) =>
        Qwen3VlGroundedMetadataEvidenceParser
            .IncludesClipLinkedKnowledgeSelection(outputSchema);

    private static void ValidatePassProvenance(
        Qwen3VlGroundedMetadataGenerationSchemaProfile profile,
        Qwen3VlGroundedMetadataRecoveryValidation recovery,
        Qwen3VlGroundedMetadataVisualValidation visual,
        Qwen3VlGroundedMetadataEvidenceValidation evidence)
    {
        bool visualEventSelectionApplied = visual.VisualDrafts.Count > 1;
        Qwen3VlGroundedMetadataSelection.ValidateGenerationPassProvenance(
            recovery.GenerationPassCount,
            visual.VisualDrafts.Count,
            visualEventSelectionApplied,
            evidence.KnowledgeSelectionApplied,
            evidence.GroundingReviewApplied,
            evidence.RejectedValidationRules,
            recovery.GroundingPassCount,
            recovery.SynthesisPassCount,
            recovery.GroundingPacketReused,
            recovery.ActorAuthorityAssessmentApplied,
            profile.BoundedDuplicateRefinement,
            recovery.DuplicateSynthesisRecoveryApplied,
            recovery.DuplicateSynthesisRecoverySourcePassOrdinal,
            recovery.DuplicateSynthesisRecoveryRepeatedPassOrdinal,
            recovery.DuplicateSynthesisRecoverySourceRejectedJsonSha256,
            recovery.DuplicateSynthesisRecoveryRepeatedRejectedJsonSha256,
            profile.SampledSynthesis,
            recovery.SampledSynthesisApplied,
            recovery.SampledSynthesisPassOrdinal,
            recovery.SampledSynthesisTrigger,
            recovery.SampledSynthesisSourceRejectedJsonSha256,
            profile.NonRetrospectiveRetryAnchor,
            recovery.NonRetrospectiveRetryAnchorApplied,
            recovery.NonRetrospectiveRetryAnchorSourcePassOrdinal,
            recovery.NonRetrospectiveRetryAnchorSourceRule,
            recovery.NonRetrospectiveRetryAnchorEnvelopeSha256,
            recovery.NonRetrospectiveRetryAnchorAuthoritySha256,
            profile.SynthesisRecoveryPool,
            recovery.SynthesisRecoveryPoolApplied,
            recovery.SynthesisRecoveryPoolSourcePassOrdinal,
            recovery.SynthesisRecoveryPoolSourceRejectedJsonSha256,
            recovery.SynthesisRecoveryPoolAttemptedCandidateCount,
            recovery.SynthesisRecoveryPoolSelectedCandidateOrdinal,
            profile.ConditionalRecoveryPoolSource,
            recovery.SynthesisRecoveryPoolSourceSelectionReason,
            profile.StrictRetryAnchorSourceRule,
            profile.FourDraftEventSelection,
            profile.CreatorAuthorityRetrySourceWithholding,
            profile.SemanticExhaustionRecovery,
            profile.EditorialRephrase,
            recovery.EditorialRephrase?.Attempted == true,
            recovery.EditorialRephrase?.RecoveredRejectedLanguage == true);
        if (!profile.SynthesisRecoveryPool)
        {
            return;
        }
        Qwen3VlGroundedMetadataSelection
            .ValidateSynthesisRecoveryPoolProvenance(
                recovery.SynthesisPassCount!.Value,
                evidence.RejectedValidationRules,
                recovery.DecodedTextSha256,
                recovery.SynthesisRecoveryPoolApplied,
                recovery.SynthesisRecoveryPoolSourcePassOrdinal,
                recovery.SynthesisRecoveryPoolSourceRejectedJsonSha256,
                recovery.SynthesisRecoveryPoolAttemptedCandidateCount,
                recovery.SynthesisRecoveryPoolSelectedCandidateOrdinal,
                recovery.ModuleIdentities,
                recovery.SynthesisPassAttestations,
                recovery.DuplicateSynthesisRecoveryApplied,
                recovery.DuplicateSynthesisRecoverySourceRejectedJsonSha256,
                recovery.DuplicateSynthesisRecoveryRepeatedRejectedJsonSha256,
                recovery.NonRetrospectiveRetryAnchorApplied,
                recovery.NonRetrospectiveRetryAnchorSourcePassOrdinal,
                recovery.NonRetrospectiveRetryAnchorEnvelopeSha256,
                recovery.NonRetrospectiveRetryAnchorAuthoritySha256,
                profile.RetryableContinuationRecoveryPool
                    ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                        .RetryableSemanticRejectionSet
                    : Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                        .LegacyRetryableSemanticRejectionSet,
                recovery.SynthesisRecoveryPoolSourceSelectionReason,
                profile.ConditionalRecoveryPoolSource,
                profile.StrictRetryAnchorSourceRule,
                profile.CrossDraftRetrySourceWithholding,
                profile.CreatorAuthorityRetrySourceWithholding,
                profile.SemanticExhaustionRecovery,
                profile.EditorialRephrase,
                profile.RetrospectiveGrammarRecovery,
                recovery.EditorialRephrase);
    }

    private static Qwen3VlGroundedMetadataGenerationValidation BuildValidation(
        Qwen3VlGroundedMetadataRecoveryValidation recovery,
        Qwen3VlGroundedMetadataVisualValidation visual,
        Qwen3VlGroundedMetadataEvidenceValidation evidence) =>
        new(
            recovery.GenerationPassCount,
            recovery.GroundingPassCount,
            recovery.SynthesisPassCount,
            recovery.DuplicateSynthesisRecoveryApplied,
            recovery.DuplicateSynthesisRecoverySourcePassOrdinal,
            recovery.DuplicateSynthesisRecoveryRepeatedPassOrdinal,
            recovery.DuplicateSynthesisRecoverySourceRejectedJsonSha256,
            recovery.DuplicateSynthesisRecoveryRepeatedRejectedJsonSha256,
            recovery.SampledSynthesisApplied,
            recovery.SampledSynthesisPassOrdinal,
            recovery.SampledSynthesisTrigger,
            recovery.SampledSynthesisSourceRejectedJsonSha256,
            recovery.NonRetrospectiveRetryAnchorApplied,
            recovery.NonRetrospectiveRetryAnchorSourcePassOrdinal,
            recovery.NonRetrospectiveRetryAnchorSourceRule,
            recovery.NonRetrospectiveRetryAnchorEnvelopeSha256,
            recovery.NonRetrospectiveRetryAnchorAuthoritySha256,
            recovery.SynthesisRecoveryPoolApplied,
            recovery.SynthesisRecoveryPoolSourcePassOrdinal,
            recovery.SynthesisRecoveryPoolSourceRejectedJsonSha256,
            recovery.SynthesisRecoveryPoolSourceSelectionReason,
            recovery.SynthesisRecoveryPoolAttemptedCandidateCount,
            recovery.SynthesisRecoveryPoolSelectedCandidateOrdinal,
            recovery.SynthesisRecoveryPoolRetryableSemanticRejections,
            recovery.SynthesisRecoveryPoolRetryableSemanticRejectionsSha256,
            recovery.ModuleIdentities,
            recovery.SynthesisPassAttestations,
            recovery.GroundingPacketRequestSha256,
            recovery.GroundingPacketFactSha256,
            recovery.GroundingPacketSourceAttempt,
            recovery.GroundingPacketReused,
            recovery.PrimaryOnlySynthesisEvidenceApplied,
            visual.VisualDrafts,
            visual.StableReadableText,
            recovery.ActorAuthorityAssessmentApplied,
            visual.PrimaryVisualDraftOrdinal,
            recovery.PrimaryActorAuthority,
            recovery.PrimaryCreatorExperienceRelation,
            visual.VisualEventAssessments,
            evidence.KnowledgeSelectionApplied,
            evidence.SelectedCurrentPassageId,
            evidence.KnowledgeAssessments,
            evidence.GroundingReviewApplied,
            evidence.RejectedValidationRules,
            recovery.PriorAcceptedTitleCount,
            recovery.RerollTitleDiversityCode,
            recovery.RerollTitleTokenJaccardNumerator,
            recovery.RerollTitleTokenJaccardDenominator,
            recovery.MetadataReviewRequired,
            recovery.MetadataReviewIssues,
            recovery.EditorialRephrase);
}
