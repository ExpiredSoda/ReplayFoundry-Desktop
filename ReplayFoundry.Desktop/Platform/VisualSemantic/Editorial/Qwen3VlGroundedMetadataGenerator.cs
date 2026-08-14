using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

public sealed class Qwen3VlGroundedMetadataGenerator :
    IClipEditorialVisualMetadataGenerator,
    IDisposable
{
    internal const int MaximumCases = 30;
    internal const int MaximumNewTokens = 768;
    internal const double VideoFramesPerSecond =
        Qwen3VlGroundedMetadataSamplingPolicy.CoreFramesPerSecond;
    internal const int MinimumVideoFrames =
        Qwen3VlGroundedMetadataSamplingPolicy.CoreMinimumFrames;
    internal const int MaximumVideoFrames =
        Qwen3VlGroundedMetadataSamplingPolicy.CoreMaximumFrames;
    internal const int MaximumPixelsPerFrame =
        Qwen3VlGroundedMetadataSamplingPolicy.CoreMaximumPixelsPerFrame;
    internal const int MaximumTotalVideoPixels =
        Qwen3VlGroundedMetadataSamplingPolicy.CoreMaximumTotalVideoPixels;
    internal const string InputSchema =
        "grounded-editorial-metadata-input-batch-1.8";
    internal const string OutputSchema =
        "grounded-editorial-metadata-output-batch-1.50";
    internal const string PreviousReviewableAudienceCopyOutputSchema =
        "grounded-editorial-metadata-output-batch-1.49";
    internal const string PreviousTerminalPeriodNormalizationOutputSchema =
        "grounded-editorial-metadata-output-batch-1.48";
    internal const string PreviousOutputLanguageRecoveryOutputSchema =
        "grounded-editorial-metadata-output-batch-1.47";
    internal const string PreviousNeutralPersonRecoveryOutputSchema =
        "grounded-editorial-metadata-output-batch-1.46";
    internal const string PreviousRetrospectiveGrammarRecoveryOutputSchema =
        "grounded-editorial-metadata-output-batch-1.45";
    internal const string PreviousLiteralActionRecoveryOutputSchema =
        "grounded-editorial-metadata-output-batch-1.44";
    internal const string PreviousWithheldEmbodimentCopyOutputSchema =
        "grounded-editorial-metadata-output-batch-1.43";
    internal const string PreviousCreatorEmbodimentRecoveryOutputSchema =
        "grounded-editorial-metadata-output-batch-1.42";
    internal const string PreviousTypedLanguageRecoveryOutputSchema =
        "grounded-editorial-metadata-output-batch-1.41";
    internal const string PreviousLanguageRecoveryOutputSchema =
        "grounded-editorial-metadata-output-batch-1.40";
    internal const string PreviousEditorialRephraseOutputSchema =
        "grounded-editorial-metadata-output-batch-1.39";
    internal const string PreviousInterfaceCorrectionOutputSchema =
        "grounded-editorial-metadata-output-batch-1.38";
    internal const string PreviousInterfaceAttributionOutputSchema =
        "grounded-editorial-metadata-output-batch-1.37";
    internal const string PreviousVisualDraftPromptOutputSchema =
        "grounded-editorial-metadata-output-batch-1.36";
    internal const string PreviousEffectiveVoiceOutputSchema =
        "grounded-editorial-metadata-output-batch-1.35";
    internal const string PreviousGroundedJsonWhitespaceOutputSchema =
        "grounded-editorial-metadata-output-batch-1.34";
    internal const string PreviousCreatorAuthorityOutputSchema =
        "grounded-editorial-metadata-output-batch-1.33";
    internal const string PreviousAudienceCopyWithholdingOutputSchema =
        "grounded-editorial-metadata-output-batch-1.32";
    internal const string PreviousCrossDraftRetryOutputSchema =
        "grounded-editorial-metadata-output-batch-1.31";
    internal const string PreviousRootPreloadOutputSchema =
        "grounded-editorial-metadata-output-batch-1.30";
    internal const string PreviousCudnnAttentionOutputSchema =
        "grounded-editorial-metadata-output-batch-1.29";
    internal const string PreviousPositionEmbeddingOutputSchema =
        "grounded-editorial-metadata-output-batch-1.28";
    internal const string PreviousAccelerateOffloadOutputSchema =
        "grounded-editorial-metadata-output-batch-1.27";
    internal const string PreviousVisionOffloadOutputSchema =
        "grounded-editorial-metadata-output-batch-1.26";
    internal const string PreviousLowPeakSamplingOutputSchema =
        "grounded-editorial-metadata-output-batch-1.25";
    internal const string PreviousPeakBoundedSamplingOutputSchema =
        "grounded-editorial-metadata-output-batch-1.24";
    internal const string PreviousSamplingOutputSchema =
        "grounded-editorial-metadata-output-batch-1.23";
    internal const string PreWatchdogOutputSchema =
        "grounded-editorial-metadata-output-batch-1.22";
    internal const string PreviousOutputSchema =
        "grounded-editorial-metadata-output-batch-1.21";
    internal const string PriorOutputSchema =
        "grounded-editorial-metadata-output-batch-1.20";
    internal const string LegacyOutputSchema =
        "grounded-editorial-metadata-output-batch-1.19";
    internal const string HistoricalOutputSchema =
        "grounded-editorial-metadata-output-batch-1.18";
    internal const string PriorHistoricalOutputSchema =
        "grounded-editorial-metadata-output-batch-1.17";
    internal const string EarlierHistoricalOutputSchema =
        "grounded-editorial-metadata-output-batch-1.16";
    internal const string InitialOutputSchema =
        "grounded-editorial-metadata-output-batch-1.15";
    internal const string OldestOutputSchema =
        "grounded-editorial-metadata-output-batch-1.14";
    internal const string EarliestOutputSchema =
        "grounded-editorial-metadata-output-batch-1.13";
    internal const string FoundationalOutputSchema =
        "grounded-editorial-metadata-output-batch-1.12";
    internal const string OriginalOutputSchema =
        "grounded-editorial-metadata-output-batch-1.11";
    internal const string BaselineOutputSchema =
        "grounded-editorial-metadata-output-batch-1.10";
    internal const string PromptName =
        "ReplayFoundry Grounded Editorial Metadata";
    internal const string PromptVersion = "1.37";
    internal const string PromptSha256 =
        "f7952e452ef7d8ac2b586cd96fcd21b779bb06c891656fa47687644be6310dbf";
    internal const string PreviousPromptVersion = "1.36";
    internal const string PreviousPromptSha256 =
        "5315f5a2571e879bd8c8e3c73668b848b56d02cc8ba02beb0f9607534b767a96";
    internal const string EarlierPromptVersion = "1.35";
    internal const string EarlierPromptSha256 =
        "75582cb94e01a440dd139cacc4025a15ae82878252f84720c96b427e579d65aa";
    internal const string PriorPromptVersion = "1.34";
    internal const string PriorPromptSha256 =
        "1e0c0e2ed9413a890714c815d80ba6e161f08eea20130e742f3e50a666394a1a";
    internal const string InitialPromptVersion = "1.33";
    internal const string InitialPromptSha256 =
        "732371e8f16101fb07a4afda058c7fca98819f0bba7828b0f77236be1c1fe34c";
    internal const string MetadataSchemaVersion =
        "grounded-editorial-metadata-json-schema-1.8";
    internal const string PreviousMetadataSchemaVersion =
        "grounded-editorial-metadata-json-schema-1.7";
    internal const string VisualDraftSchemaVersion =
        "grounded-editorial-visual-draft-json-schema-1.1";
    internal const string VisualDraftPromptVersion = "1.4";
    internal const string VisualDraftPromptSha256 =
        "e07bb76961c9764c12fdbf13b60963928d319af15f5da55cca76bd660754f77b";
    internal const string PreviousVisualDraftPromptVersion = "1.3";
    internal const string PreviousVisualDraftPromptSha256 =
        "62d3c92f0fbc863ea83d4a869632f291872e8eaa0ac2f99b45f23b846e1c6c56";
    internal const string EarlierVisualDraftPromptVersion = "1.2";
    internal const string EarlierVisualDraftPromptSha256 =
        "573cdf04dfc0358a057af1a134e81e33f9ba6e272fc0477878bf6036793e3771";
    internal const string VisualEventSelectionSchemaVersion =
        "grounded-editorial-visual-event-selection-json-schema-1.2";
    internal const string PreviousVisualEventSelectionSchemaVersion =
        "grounded-editorial-visual-event-selection-json-schema-1.1";
    internal const string InitialVisualEventSelectionSchemaVersion =
        "grounded-editorial-visual-event-selection-json-schema-1.0";
    internal const string VisualEventSelectionPromptVersion = "1.1";
    internal const string VisualEventSelectionPromptSha256 =
        "26a6529193c9093dea13001ab9f4b5b2051e3b2aaad1328c78ea80444a39df30";
    internal const string PreviousVisualEventSelectionPromptVersion = "1.0";
    internal const string PreviousVisualEventSelectionPromptSha256 =
        "cadc0e83ad25ed74c8ec1ed4703366c7389984d3e166b589281a7b5b7dae8b43";
    internal const string KnowledgeSelectionSchemaVersion =
        "grounded-editorial-knowledge-selection-json-schema-1.1";
    internal const string StableReadableTextPolicyVersion = "1.0";
    internal const string GroundingPacketSchemaVersion =
        "grounded-editorial-grounding-packet-1.0";
    internal const string SynthesisEvidencePolicyVersion =
        "grounded-editorial-synthesis-evidence-1.0";
    internal const string RerollDiversityPolicyVersion =
        Qwen3VlGroundedMetadataRerollDiversityPolicy.Version;
    internal const string KnowledgeSelectionPromptVersion = "1.4";
    internal const string KnowledgeSelectionPromptSha256 =
        "723d0892c5e74d75671bc61854f2717b19f852133c8b994ceed4bd595ec0001a";
    internal const string ProviderVersion = "1.98.0";

    private readonly Qwen3VlQualifiedEditorialRuntime _runtime;
    private readonly Qwen3VlGroundedMetadataExecutor _executor;

    public Qwen3VlGroundedMetadataGenerator(
        Qwen3VlQualifiedEditorialRuntime runtime)
        : this(
            runtime,
            new WindowsProcessRunner(),
            new SystemQwen3VlBatchWorkspaceFactory(),
            new SystemQwen3VlGroundedFailureArchive())
    {
    }

    internal Qwen3VlGroundedMetadataGenerator(
        Qwen3VlQualifiedEditorialRuntime runtime,
        IProcessRunner processRunner,
        IQwen3VlBatchWorkspaceFactory workspaceFactory,
        IQwen3VlGroundedFailureArchive? failureArchive = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _executor = new Qwen3VlGroundedMetadataExecutor(
            _runtime,
            processRunner,
            workspaceFactory,
            failureArchive ?? NullQwen3VlGroundedFailureArchive.Instance);
    }

    public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
        new("Qwen3-VL grounded editorial metadata", ProviderVersion);

    public bool IsAvailable => true;

    public async Task<ClipEditorialMetadataDraft> GenerateAsync(
        ClipEditorialMetadataRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        IReadOnlyList<ClipEditorialMetadataDraft> result =
            await GenerateBatchAsync([request], cancellationToken);
        return result[0];
    }

    public Task<IReadOnlyList<ClipEditorialMetadataDraft>> GenerateBatchAsync(
        IReadOnlyList<ClipEditorialMetadataRequest> requests,
        CancellationToken cancellationToken) =>
        _executor.GenerateBatchAsync(requests, Parse, cancellationToken);

    internal IReadOnlyList<ClipEditorialMetadataDraft> Parse(
        string json,
        IReadOnlyList<ClipEditorialMetadataRequest> requests) =>
        Qwen3VlGroundedMetadataResultParser.Parse(
            json,
            requests,
            _runtime,
            Identity);

    internal static void ValidateMetadata(
        string title,
        string description,
        IReadOnlyList<string> tags,
        ClipEditorialMetadataRequest request,
        bool requireInterfaceAttributionAuthority = false) =>
        Qwen3VlGroundedMetadataAudienceValidator.ValidateMetadata(
            title,
            description,
            tags,
            request,
            requireInterfaceAttributionAuthority:
                requireInterfaceAttributionAuthority);

    internal static void ValidateGrounding(
        JsonElement metadata,
        ClipEditorialMetadataRequest request,
        string title,
        string description) =>
        Qwen3VlGroundedMetadataAudienceValidator.ValidateGrounding(
            metadata,
            request,
            title,
            description);

    internal static void ValidateGenerationPassProvenance(
        int generationPassCount,
        int visualDraftCount,
        bool visualEventSelectionApplied,
        bool knowledgeSelectionApplied,
        bool groundingReviewApplied,
        IReadOnlyList<string> rejectedValidationRules,
        int? groundingPassCount = null,
        int? synthesisPassCount = null,
        bool? groundingPacketReused = null,
        bool actorAuthorityAssessmentApplied = false,
        bool duplicateSynthesisRecoveryApplied = false,
        int? duplicateSynthesisRecoverySourcePassOrdinal = null,
        int? duplicateSynthesisRecoveryRepeatedPassOrdinal = null,
        string? duplicateSynthesisRecoverySourceRejectedJsonSha256 = null,
        string? duplicateSynthesisRecoveryRepeatedRejectedJsonSha256 = null,
        bool sampledSynthesisApplied = false,
        int? sampledSynthesisPassOrdinal = null,
        string? sampledSynthesisTrigger = null,
        string? sampledSynthesisSourceRejectedJsonSha256 = null,
        bool nonRetrospectiveRetryAnchorSupported = true,
        bool nonRetrospectiveRetryAnchorApplied = false,
        int? nonRetrospectiveRetryAnchorSourcePassOrdinal = null,
        string? nonRetrospectiveRetryAnchorSourceRule = null,
        string? nonRetrospectiveRetryAnchorEnvelopeSha256 = null,
        string? nonRetrospectiveRetryAnchorAuthoritySha256 = null,
        bool synthesisRecoveryPoolApplied = false,
        int? synthesisRecoveryPoolSourcePassOrdinal = null,
        string? synthesisRecoveryPoolSourceRejectedJsonSha256 = null,
        int synthesisRecoveryPoolAttemptedCandidateCount = 0,
        int? synthesisRecoveryPoolSelectedCandidateOrdinal = null,
        bool conditionalRecoveryPoolSourceSupported = false,
        string? synthesisRecoveryPoolSourceSelectionReason = null,
        bool strictRetryAnchorSourceRuleSupported = false,
        bool fourDraftEventSelectionSupported = true,
        bool creatorAuthorityRetrySourceWithholdingSupported = true,
        bool semanticExhaustionRecoverySupported = true,
        bool editorialRephraseSupported = false,
        bool editorialRephraseAttempted = false,
        bool rejectedLanguageRecovered = false) =>
        Qwen3VlGroundedMetadataSelection.ValidateGenerationPassProvenance(
            generationPassCount,
            visualDraftCount,
            visualEventSelectionApplied,
            knowledgeSelectionApplied,
            groundingReviewApplied,
            rejectedValidationRules,
            groundingPassCount,
            synthesisPassCount,
            groundingPacketReused,
            actorAuthorityAssessmentApplied,
            duplicateSynthesisRecoverySupported: true,
            duplicateSynthesisRecoveryApplied,
            duplicateSynthesisRecoverySourcePassOrdinal,
            duplicateSynthesisRecoveryRepeatedPassOrdinal,
            duplicateSynthesisRecoverySourceRejectedJsonSha256,
            duplicateSynthesisRecoveryRepeatedRejectedJsonSha256,
            sampledSynthesisSupported: true,
            sampledSynthesisApplied,
            sampledSynthesisPassOrdinal,
            sampledSynthesisTrigger,
            sampledSynthesisSourceRejectedJsonSha256,
            nonRetrospectiveRetryAnchorSupported,
            nonRetrospectiveRetryAnchorApplied,
            nonRetrospectiveRetryAnchorSourcePassOrdinal,
            nonRetrospectiveRetryAnchorSourceRule,
            nonRetrospectiveRetryAnchorEnvelopeSha256,
            nonRetrospectiveRetryAnchorAuthoritySha256,
            synthesisRecoveryPoolSupported: true,
            synthesisRecoveryPoolApplied,
            synthesisRecoveryPoolSourcePassOrdinal,
            synthesisRecoveryPoolSourceRejectedJsonSha256,
            synthesisRecoveryPoolAttemptedCandidateCount,
            synthesisRecoveryPoolSelectedCandidateOrdinal,
            conditionalRecoveryPoolSourceSupported,
            synthesisRecoveryPoolSourceSelectionReason,
            strictRetryAnchorSourceRuleSupported,
            fourDraftEventSelectionSupported,
            creatorAuthorityRetrySourceWithholdingSupported,
            semanticExhaustionRecoverySupported,
            editorialRephraseSupported,
            editorialRephraseAttempted,
            rejectedLanguageRecovered);

    public void Dispose() => _executor.Dispose();
}
