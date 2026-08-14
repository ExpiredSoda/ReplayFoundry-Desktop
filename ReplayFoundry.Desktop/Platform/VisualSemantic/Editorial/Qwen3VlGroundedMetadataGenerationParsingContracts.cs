namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal sealed record Qwen3VlGroundedMetadataGenerationSchemaProfile(
    bool GroundedTagShapeConstrained,
    bool GroundedJsonCanonicalWhitespace,
    bool StrictRetryAnchorSourceRule,
    bool ConditionalRecoveryPoolSource,
    bool RetryableContinuationRecoveryPool,
    bool SynthesisRecoveryPool,
    bool NonRetrospectiveRetryAnchor,
    bool SampledSynthesis,
    bool BoundedDuplicateRefinement,
    bool ActorAuthority,
    bool RerollDiversity,
    bool EvidenceIsolation,
    bool PacketReuse,
    bool AdaptiveSampling,
    bool LowPeakSampling,
    bool PeakBoundedSampling,
    bool FourDraftEventSelection,
    bool CreatorAuthorityRetrySourceWithholding,
    bool CrossDraftRetrySourceWithholding,
    bool SemanticExhaustionRecovery,
    bool LiteralActionVisualDraftPrompt,
    bool InterfaceAttributionVisualDraftPrompt,
    bool EditorialRephrase,
    bool RejectedLanguageRecovery,
    bool TypedLanguageRecovery,
    bool CreatorEmbodimentRecovery,
    bool WithheldEmbodimentCopyRecovery,
    bool LiteralActionRecovery,
    bool RetrospectiveGrammarRecovery,
    bool NeutralPersonRecovery,
    bool OutputLanguageRecovery,
    bool TerminalPeriodNormalization,
    bool ReviewableAudienceCopy);

internal sealed record Qwen3VlGroundedMetadataEditorialRephraseValidation(
    bool Attempted,
    bool Applied,
    string Outcome,
    string SourceJsonSha256,
    string OutputJsonSha256,
    string RawOutputSha256,
    string? RejectionCode,
    bool RecoveredRejectedLanguage);

internal sealed record Qwen3VlGroundedMetadataRecoveryValidation(
    int GeneratedTokenCount,
    string DecodedTextSha256,
    int GenerationPassCount,
    int? GroundingPassCount,
    int? SynthesisPassCount,
    bool DuplicateSynthesisRecoveryApplied,
    int? DuplicateSynthesisRecoverySourcePassOrdinal,
    int? DuplicateSynthesisRecoveryRepeatedPassOrdinal,
    string? DuplicateSynthesisRecoverySourceRejectedJsonSha256,
    string? DuplicateSynthesisRecoveryRepeatedRejectedJsonSha256,
    bool SampledSynthesisApplied,
    int? SampledSynthesisPassOrdinal,
    string? SampledSynthesisTrigger,
    string? SampledSynthesisSourceRejectedJsonSha256,
    bool NonRetrospectiveRetryAnchorApplied,
    int? NonRetrospectiveRetryAnchorSourcePassOrdinal,
    string? NonRetrospectiveRetryAnchorSourceRule,
    string? NonRetrospectiveRetryAnchorEnvelopeSha256,
    string? NonRetrospectiveRetryAnchorAuthoritySha256,
    bool SynthesisRecoveryPoolApplied,
    int? SynthesisRecoveryPoolSourcePassOrdinal,
    string? SynthesisRecoveryPoolSourceRejectedJsonSha256,
    string? SynthesisRecoveryPoolSourceSelectionReason,
    int SynthesisRecoveryPoolAttemptedCandidateCount,
    int? SynthesisRecoveryPoolSelectedCandidateOrdinal,
    IReadOnlyList<string> SynthesisRecoveryPoolRetryableSemanticRejections,
    string? SynthesisRecoveryPoolRetryableSemanticRejectionsSha256,
    IReadOnlyList<Qwen3VlGroundedMetadataModuleIdentity> ModuleIdentities,
    IReadOnlyList<Qwen3VlGroundedMetadataSynthesisPassAttestation>
        SynthesisPassAttestations,
    string? GroundingPacketRequestSha256,
    string? GroundingPacketFactSha256,
    int? GroundingPacketSourceAttempt,
    bool? GroundingPacketReused,
    bool? PrimaryOnlySynthesisEvidenceApplied,
    bool ActorAuthorityAssessmentApplied,
    Qwen3VlGroundedMetadataActorAuthority PrimaryActorAuthority,
    Qwen3VlGroundedMetadataCreatorExperienceRelation
        PrimaryCreatorExperienceRelation,
    int? PriorAcceptedTitleCount,
    Qwen3VlGroundedMetadataRerollTitleDiversityCode?
        RerollTitleDiversityCode,
    int? RerollTitleTokenJaccardNumerator,
    int? RerollTitleTokenJaccardDenominator,
    bool MetadataReviewRequired,
    IReadOnlyList<string> MetadataReviewIssues,
    Qwen3VlGroundedMetadataEditorialRephraseValidation?
        EditorialRephrase);

internal sealed record Qwen3VlGroundedMetadataVisualValidation(
    IReadOnlyList<Qwen3VlGroundedMetadataVisualDraft> VisualDrafts,
    IReadOnlyList<string> StableReadableText,
    int PrimaryVisualDraftOrdinal,
    IReadOnlyList<Qwen3VlGroundedMetadataVisualEventAssessment>
        VisualEventAssessments);

internal sealed record Qwen3VlGroundedMetadataEvidenceValidation(
    bool KnowledgeSelectionApplied,
    string SelectedCurrentPassageId,
    IReadOnlyList<Qwen3VlGroundedMetadataKnowledgeAssessment>
        KnowledgeAssessments,
    bool GroundingReviewApplied,
    IReadOnlyList<string> RejectedValidationRules);
