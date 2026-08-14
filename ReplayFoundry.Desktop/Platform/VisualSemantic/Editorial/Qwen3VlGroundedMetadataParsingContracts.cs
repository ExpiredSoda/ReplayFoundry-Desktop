namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal sealed record Qwen3VlGroundedMetadataGenerationValidation(
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
    IReadOnlyList<string>
        SynthesisRecoveryPoolRetryableSemanticRejections,
    string? SynthesisRecoveryPoolRetryableSemanticRejectionsSha256,
    IReadOnlyList<Qwen3VlGroundedMetadataModuleIdentity>
        GroundedMetadataModuleIdentities,
    IReadOnlyList<Qwen3VlGroundedMetadataSynthesisPassAttestation>
        SynthesisPassAttestations,
    string? GroundingPacketRequestSha256,
    string? GroundingPacketFactSha256,
    int? GroundingPacketSourceAttempt,
    bool? GroundingPacketReused,
    bool? PrimaryOnlySynthesisEvidenceApplied,
    IReadOnlyList<Qwen3VlGroundedMetadataVisualDraft> VisualDrafts,
    IReadOnlyList<string> StableReadableText,
    bool ActorAuthorityAssessmentApplied,
    int PrimaryVisualDraftOrdinal,
    Qwen3VlGroundedMetadataActorAuthority PrimaryActorAuthority,
    Qwen3VlGroundedMetadataCreatorExperienceRelation
        PrimaryCreatorExperienceRelation,
    IReadOnlyList<Qwen3VlGroundedMetadataVisualEventAssessment>
        VisualEventSelectionAssessments,
    bool KnowledgeSelectionApplied,
    string SelectedCurrentPassageId,
    IReadOnlyList<Qwen3VlGroundedMetadataKnowledgeAssessment>
        KnowledgeSelectionAssessments,
    bool GroundingReviewApplied,
    IReadOnlyList<string> RejectedRules,
    int? PriorAcceptedTitleCount,
    Qwen3VlGroundedMetadataRerollTitleDiversityCode?
        RerollTitleDiversityCode,
    int? RerollTitleTokenJaccardNumerator,
    int? RerollTitleTokenJaccardDenominator,
    bool MetadataReviewRequired,
    IReadOnlyList<string> MetadataReviewIssues,
    Qwen3VlGroundedMetadataEditorialRephraseValidation?
        EditorialRephrase = null);

internal sealed record Qwen3VlGroundedMetadataModuleIdentity(
    string ModuleName,
    string FileName,
    string Sha256);

internal enum Qwen3VlGroundedMetadataSynthesisDecoding
{
    Greedy,
    RecoveryPool,
}

internal sealed record Qwen3VlGroundedMetadataSynthesisPassAttestation(
    int LogicalPassOrdinal,
    int? CandidateOrdinal,
    Qwen3VlGroundedMetadataSynthesisDecoding Decoding,
    int Seed,
    int? SourcePassOrdinal,
    string? SourceRejectedJsonSha256,
    string? SourceSelectionReason,
    string CanonicalMessagesSha256,
    string RenderedPromptSha256,
    int RenderedPromptUtf8ByteCount,
    string InputTokenIdsSha256,
    int InputTokenCount,
    string OutputSha256,
    string CompletedJsonSha256,
    string? RejectionCode,
    bool Accepted,
    bool RetryAnchorCaptured,
    bool RetryAnchorApplied,
    string? RetryAnchorDisabledReason,
    string? RetryAnchorEnvelopeSha256,
    string? RetryAnchorAuthoritySha256);

internal sealed record Qwen3VlGroundedMetadataVisualDraft(
    int Ordinal,
    double StartSeconds,
    double EndSeconds,
    string Environment,
    bool EnvironmentUncertain,
    IReadOnlyList<string> SubjectsAndObjects,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> ReadableText,
    IReadOnlyList<string> Uncertainties);

internal sealed record Qwen3VlGroundedMetadataVisualEventAssessment(
    int Ordinal,
    bool DistinctAction,
    bool ObjectInteraction,
    bool VisibleOutcome,
    bool ReadableInterfaceChange,
    bool RoutineOnly,
    bool Uncertain,
    Qwen3VlGroundedMetadataActorAuthority ActorAuthority =
        Qwen3VlGroundedMetadataActorAuthority.Unknown,
    Qwen3VlGroundedMetadataCreatorExperienceRelation CreatorExperienceRelation =
        Qwen3VlGroundedMetadataCreatorExperienceRelation.Unestablished)
{
    public int Score =>
        3 * Convert.ToInt32(DistinctAction) +
        2 * Convert.ToInt32(ObjectInteraction) +
        2 * Convert.ToInt32(VisibleOutcome) +
        Convert.ToInt32(ReadableInterfaceChange) -
        3 * Convert.ToInt32(RoutineOnly) -
        Convert.ToInt32(Uncertain);

    public bool HasDistinctEventSupport =>
        DistinctAction ||
        ObjectInteraction ||
        VisibleOutcome ||
        ReadableInterfaceChange;
}

internal enum Qwen3VlGroundedMetadataActorAuthority
{
    CreatorControlled,
    OtherPerson,
    Unknown,
}

internal enum Qwen3VlGroundedMetadataCreatorExperienceRelation
{
    CreatorActed,
    CreatorAffected,
    CreatorEncountered,
    Unestablished,
}

internal enum Qwen3VlGroundedMetadataVisualEventSelectionOutcomeCode
{
    SelectedDistinctPrimaryEvent,
    NoDistinctPrimaryEvent,
}

internal sealed record Qwen3VlGroundedMetadataVisualEventSelectionOutcome(
    Qwen3VlGroundedMetadataVisualEventSelectionOutcomeCode Code,
    int? PrimaryVisualDraftOrdinal);

internal sealed record Qwen3VlGroundedMetadataKnowledgeAssessment(
    string PassageId,
    bool SettingSupport,
    bool EntityIdentitySupport,
    bool DistinctiveObjectSupport,
    bool CentralActionSupport,
    bool ChronologySupport,
    bool MaterialContradiction)
{
    public int SupportCount =>
        Convert.ToInt32(SettingSupport) +
        Convert.ToInt32(EntityIdentitySupport) +
        Convert.ToInt32(DistinctiveObjectSupport) +
        Convert.ToInt32(CentralActionSupport) +
        Convert.ToInt32(ChronologySupport);
}

internal sealed record Qwen3VlGroundedMetadataGroundingReference(
    string AudienceField,
    IReadOnlyList<string> KnowledgeReferenceIds,
    IReadOnlyList<string> ClipEvidenceReferenceIds);
