using System.Collections.ObjectModel;
using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;
using ReplayFoundry.Desktop.Media.Intelligence.VisualText;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Studio.Projects;

public enum StudioProjectLoadOutcome
{
    Loaded,
    RecoveredPreviousSave,
    NotFound,
    MissingSource,
    ChangedSource,
    Corrupt,
    UnsupportedSchema,
}

public enum StudioPersistedRenderState
{
    Ready,
    Interrupted,
    Completed,
}

public sealed record StudioProjectSourceSnapshot
{
    public StudioProjectSourceSnapshot(
        string fullPath,
        long length,
        DateTimeOffset lastWriteUtc)
    {
        if (string.IsNullOrWhiteSpace(fullPath) ||
            !Path.IsPathFullyQualified(fullPath) ||
            length < 0 ||
            lastWriteUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A Studio source snapshot requires a full path, nonnegative length, and UTC write time.");
        }

        FullPath = Path.GetFullPath(fullPath);
        Length = length;
        LastWriteUtc = lastWriteUtc;
    }

    public string FullPath { get; }
    public long Length { get; }
    public DateTimeOffset LastWriteUtc { get; }
}

public sealed record StudioGraphicOverlayDocument(
    string Id,
    string ImageFullPath,
    double CenterXPercent,
    double CenterYPercent,
    double WidthPercent);

public sealed record StudioClipAppearanceDocument(
    GenerationCaptionStylePreset CaptionStyle,
    double CaptionVerticalPositionPercent,
    StudioCaptionWordLimitPreset CaptionWordLimit,
    double CaptionMaximumWidthPercent,
    double CaptionFontScalePercent,
    StudioVideoEffectPreset VideoEffect,
    double VideoEffectIntensityPercent,
    IReadOnlyList<StudioGraphicOverlayDocument> GraphicOverlays);

public sealed record StudioTranscriptionWordDocument(
    string Text,
    TimeSpan RelativeStart,
    TimeSpan RelativeEnd,
    TimeSpan AbsoluteSourceStart,
    TimeSpan AbsoluteSourceEnd,
    double? ProviderReportedProbability);

public sealed record StudioTranscriptionWarningDocument(
    AudioTranscriptionWarningCode Code,
    string Message,
    string? SegmentId);

public sealed record StudioTranscriptionLanguageDocument(
    string Code,
    string? DisplayName);

public sealed record StudioTranscriptionSegmentDocument(
    string Id,
    string NeighborhoodId,
    string Text,
    TimeSpan RelativeStart,
    TimeSpan RelativeEnd,
    TimeSpan AbsoluteSourceStart,
    TimeSpan AbsoluteSourceEnd,
    IReadOnlyList<StudioTranscriptionWordDocument> Words,
    double? ProviderReportedConfidence,
    StudioTranscriptionLanguageDocument? Language,
    IReadOnlyList<StudioTranscriptionWarningDocument> Warnings);

public sealed record StudioCaptionTrackDocument(
    string CandidateId,
    string NeighborhoodId,
    GenerationCaptionSourceSelection SourceSelection,
    GenerationCaptionStylePreset RequestedStyle,
    TimeSpan SourceWindowStart,
    TimeSpan SourceWindowDuration,
    TimeSpan SourceDuration,
    IReadOnlyList<StudioTranscriptionSegmentDocument> Segments,
    bool IsUserEdited,
    GenerationCaptionSuppressionReason SuppressionReason);

public sealed record StudioVisualTextDocument(
    string CandidateId,
    string SourceFullPath,
    NormalizedRectangle ContentRegion,
    IReadOnlyList<VisualTextAnchor> Anchors,
    IReadOnlyList<VisualTextWarning> Warnings);

public sealed record StudioEditorialContextDocument(
    string CandidateId,
    string SourceFullPath,
    string SourceLabel,
    TimeSpan SourceStart,
    TimeSpan SourceEnd,
    TimeSpan SourceDuration,
    double DeterministicScore,
    string DeterministicReason,
    IReadOnlyList<ClipEditorialTranscriptContext> Transcripts,
    IReadOnlyList<ClipEditorialEvidenceReference> Evidence,
    ClipEditorialGameContext GameContext,
    ClipGameKnowledgeContext? GameKnowledge,
    NormalizedRectangle? GameplayRegion,
    StudioVisualTextDocument? VisualText);

public sealed record StudioEditorialEvidenceDocument(
    string Id,
    ClipEditorialEvidenceKind Kind,
    string Description);

public sealed record StudioEditorialWarningDocument(
    ClipEditorialWarningCode Code,
    string Message);

public sealed record StudioEditorialQualityIssueDocument(
    ClipEditorialMetadataQualityIssueCode Code,
    string Message);

public sealed record StudioEditorialGeneratorDocument(
    string Name,
    string Version);

public sealed record StudioEditorialAiProvenanceDocument(
    string ProviderName,
    string ProviderVersion,
    string RuntimeVersion,
    string ModelRepositoryId,
    string ModelRevision,
    string ModelManifestSha256,
    string PromptName,
    string PromptVersion,
    string PromptSha256,
    TimeSpan BatchElapsed,
    long? PeakAllocatedGpuBytes);

public sealed record StudioEditorialMetadataDocument(
    string Title,
    string Description,
    IReadOnlyList<string> Tags,
    ClipEditorialMetadataOrigin Origin,
    StudioEditorialGeneratorDocument Generator,
    int Attempt,
    IReadOnlyList<StudioEditorialEvidenceDocument> Evidence,
    IReadOnlyList<StudioEditorialWarningDocument> Warnings,
    StudioEditorialAiProvenanceDocument? AiProvenance,
    ClipEditorialMetadataReadiness Readiness,
    IReadOnlyList<StudioEditorialQualityIssueDocument> QualityIssues,
    IReadOnlyList<string>? PriorAcceptedTitles = null);

public sealed record StudioPreferenceFeatureDocument(
    ClipPreferenceFeatureCode Code,
    double NormalizedValue);

public sealed record StudioPreferenceVectorDocument(
    IReadOnlyList<StudioPreferenceFeatureDocument> Features);

public sealed record StudioMediaRationalDocument(
    long Numerator,
    long Denominator);

public sealed record StudioMediaContainerDocument(
    string FormatName,
    string FormatLongName,
    TimeSpan Duration,
    TimeSpan? StartTime,
    long? SizeBytes,
    long? BitRate,
    int? ProbeScore,
    IReadOnlyDictionary<string, string> Tags);

public sealed record StudioVideoStreamDocument(
    int Index,
    string CodecName,
    string CodecLongName,
    string? Profile,
    int Width,
    int Height,
    int? CodedWidth,
    int? CodedHeight,
    StudioMediaRationalDocument? AverageFrameRate,
    StudioMediaRationalDocument? RealFrameRate,
    string? PixelFormat,
    int? BitDepth,
    MediaValueSource BitDepthSource,
    StudioMediaRationalDocument? SampleAspectRatio,
    MediaValueSource SampleAspectRatioSource,
    StudioMediaRationalDocument? DisplayAspectRatio,
    MediaValueSource DisplayAspectRatioSource,
    double? RotationDegrees,
    string? FieldOrder,
    string? ColorRange,
    string? ColorPrimaries,
    string? ColorTransfer,
    string? ColorMatrix,
    string? ChromaLocation,
    long? BitRate,
    TimeSpan? Duration,
    bool IsDefault);

public sealed record StudioAudioStreamDocument(
    int Index,
    string CodecName,
    string CodecLongName,
    string? Profile,
    int? SampleRate,
    int? Channels,
    string? ChannelLayout,
    int? BitDepth,
    long? BitRate,
    TimeSpan? Duration,
    string? Language,
    string? Title,
    bool IsDefault);

public sealed record StudioMediaInspectionManifestDocument(
    string InspectorName,
    string InspectorVersion,
    string ToolName,
    string ToolVersion,
    string ToolPath,
    DateTimeOffset InspectedAtUtc);

public sealed record StudioMediaInspectionWarningDocument(
    MediaInspectionWarningCode Code,
    string Message,
    int? StreamIndex);

public sealed record StudioMediaDocument(
    string FullPath,
    StudioMediaContainerDocument Container,
    IReadOnlyList<StudioVideoStreamDocument> VideoStreams,
    IReadOnlyList<StudioAudioStreamDocument> AudioStreams,
    StudioMediaInspectionManifestDocument Manifest,
    IReadOnlyList<StudioMediaInspectionWarningDocument> Warnings);

public sealed record StudioProjectAssetDocument(
    string Id,
    int Rank,
    StudioMediaDocument SourceMedia,
    string? OutputFullPath,
    string? ThumbnailFullPath,
    TimeSpan SourceStart,
    TimeSpan SourceEnd,
    TimeSpan OriginalSourceStart,
    TimeSpan OriginalSourceEnd,
    double Score,
    double QualityTarget,
    GenerationCandidateSelectionReason SelectionReason,
    string Explanation,
    StudioCaptionTrackDocument? Captions,
    StudioClipAppearanceDocument Appearance,
    StudioEditorialContextDocument? EditorialContext,
    StudioEditorialMetadataDocument? EditorialMetadata,
    StudioPreferenceVectorDocument? PreferenceFeatures,
    GenerationOutputAssetDisposition Disposition);

public sealed record StudioHiddenMomentDocument(
    string Id,
    int ReviewOrder,
    int SourceOrder,
    StudioMediaDocument SourceMedia,
    TimeSpan SourceStart,
    TimeSpan SourceEnd,
    double FinalScore,
    double QualityTarget,
    GenerationHiddenMomentReason Reason,
    string Explanation,
    StudioPreferenceVectorDocument PreferenceFeatures,
    ClipEditorialGenerationPreference EditorialPreference,
    StudioEditorialContextDocument? EditorialContext,
    StudioEditorialMetadataDocument? EditorialMetadata,
    GenerationCaptionSourceSelection? CaptionSourceSelection,
    GenerationCaptionStylePreset? CaptionStyle);

public sealed record StudioRenderQueueEntryDocument(
    string AssetId,
    StudioPersistedRenderState State);

public sealed class StudioProjectRecoveryState
{
    private readonly ReadOnlyCollection<StudioRenderQueueEntryDocument>
        _renderQueue;

    public StudioProjectRecoveryState(
        string? selectedAssetId = null,
        IReadOnlyList<StudioRenderQueueEntryDocument>? renderQueue = null,
        TimeSpan? previewPosition = null)
    {
        StudioRenderQueueEntryDocument[] queue =
            renderQueue?.ToArray() ?? [];
        if (queue.Any(static entry => entry is null) ||
            queue.Select(static entry => entry.AssetId)
                .Distinct(StringComparer.Ordinal).Count() != queue.Length ||
            queue.Any(static entry =>
                string.IsNullOrWhiteSpace(entry.AssetId) ||
                !Enum.IsDefined(entry.State)) ||
            previewPosition.HasValue && previewPosition.Value < TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Studio recovery state requires unique queue identities and a valid preview position.");
        }

        SelectedAssetId = string.IsNullOrWhiteSpace(selectedAssetId)
            ? null
            : selectedAssetId.Trim();
        PreviewPosition = previewPosition;
        _renderQueue = Array.AsReadOnly(queue);
    }

    public string? SelectedAssetId { get; }
    public IReadOnlyList<StudioRenderQueueEntryDocument> RenderQueue =>
        _renderQueue;
    public TimeSpan? PreviewPosition { get; }
}

public sealed class StudioProjectDocument
{
    public const string CurrentSchemaVersion = "studio-project-1.1";
    public const string PreviousSchemaVersion = "studio-project-1.0";

    private readonly ReadOnlyCollection<StudioProjectSourceSnapshot> _sources;
    private readonly ReadOnlyCollection<StudioProjectAssetDocument> _assets;
    private readonly ReadOnlyCollection<StudioHiddenMomentDocument>
        _hiddenMoments;

    public StudioProjectDocument(
        string schemaVersion,
        string projectId,
        long revision,
        GenerationMode mode,
        string outputDirectory,
        int requestedCount,
        ClipFulfillmentPreference fulfillmentPreference,
        GenerationClipFulfillmentOutcome fulfillmentOutcome,
        GenerationResultCountMode resultCountMode,
        string candidateSetFingerprint,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset? finalizedAtUtc,
        IReadOnlyList<StudioProjectSourceSnapshot> sources,
        IReadOnlyList<StudioProjectAssetDocument> assets,
        IReadOnlyList<StudioHiddenMomentDocument> hiddenMoments,
        StudioProjectRecoveryState? recovery = null)
    {
        if (schemaVersion is not CurrentSchemaVersion and
                not PreviousSchemaVersion ||
            string.IsNullOrWhiteSpace(projectId) ||
            revision <= 0 ||
            !Enum.IsDefined(mode) ||
            string.IsNullOrWhiteSpace(outputDirectory) ||
            !Path.IsPathFullyQualified(outputDirectory) ||
            requestedCount <= 0 ||
            !Enum.IsDefined(fulfillmentPreference) ||
            !Enum.IsDefined(fulfillmentOutcome) ||
            !Enum.IsDefined(resultCountMode) ||
            string.IsNullOrWhiteSpace(candidateSetFingerprint) ||
            createdAtUtc.Offset != TimeSpan.Zero ||
            updatedAtUtc.Offset != TimeSpan.Zero ||
            updatedAtUtc < createdAtUtc ||
            finalizedAtUtc.HasValue &&
            finalizedAtUtc.Value.Offset != TimeSpan.Zero ||
            finalizedAtUtc.HasValue && finalizedAtUtc < createdAtUtc)
        {
            throw new ArgumentException(
                "A Studio project document requires a supported schema and bounded immutable identity.");
        }
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(hiddenMoments);
        StudioProjectSourceSnapshot[] sourceSnapshot = sources.ToArray();
        StudioProjectAssetDocument[] assetSnapshot = assets.ToArray();
        StudioHiddenMomentDocument[] hiddenSnapshot = hiddenMoments.ToArray();
        if (sourceSnapshot.Length == 0 || assetSnapshot.Length == 0 ||
            sourceSnapshot.Any(static value => value is null) ||
            assetSnapshot.Any(static value => value is null) ||
            hiddenSnapshot.Any(static value => value is null) ||
            sourceSnapshot.Select(static value => value.FullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                sourceSnapshot.Length ||
            !assetSnapshot.Select(static value => value.Rank)
                .SequenceEqual(Enumerable.Range(1, assetSnapshot.Length)) ||
            assetSnapshot.Select(static value => value.Id)
                .Concat(hiddenSnapshot.Select(static value => value.Id))
                .Distinct(StringComparer.Ordinal).Count() !=
                assetSnapshot.Length + hiddenSnapshot.Length)
        {
            throw new ArgumentException(
                "A Studio project document requires unique sources and ordered, disjoint candidates.");
        }

        SchemaVersion = schemaVersion;
        ProjectId = projectId.Trim();
        Revision = revision;
        Mode = mode;
        OutputDirectory = Path.GetFullPath(outputDirectory);
        RequestedCount = requestedCount;
        FulfillmentPreference = fulfillmentPreference;
        FulfillmentOutcome = fulfillmentOutcome;
        ResultCountMode = resultCountMode;
        CandidateSetFingerprint = candidateSetFingerprint.Trim();
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        FinalizedAtUtc = finalizedAtUtc;
        _sources = Array.AsReadOnly(sourceSnapshot);
        _assets = Array.AsReadOnly(assetSnapshot);
        _hiddenMoments = Array.AsReadOnly(hiddenSnapshot);
        Recovery = recovery;
    }

    public string SchemaVersion { get; }
    public string ProjectId { get; }
    public long Revision { get; }
    public GenerationMode Mode { get; }
    public string OutputDirectory { get; }
    public int RequestedCount { get; }
    public ClipFulfillmentPreference FulfillmentPreference { get; }
    public GenerationClipFulfillmentOutcome FulfillmentOutcome { get; }
    public GenerationResultCountMode ResultCountMode { get; }
    public string CandidateSetFingerprint { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; }
    public DateTimeOffset? FinalizedAtUtc { get; }
    public IReadOnlyList<StudioProjectSourceSnapshot> Sources => _sources;
    public IReadOnlyList<StudioProjectAssetDocument> Assets => _assets;
    public IReadOnlyList<StudioHiddenMomentDocument> HiddenMoments =>
        _hiddenMoments;
    public StudioProjectRecoveryState? Recovery { get; }
}

public sealed record StudioProjectLoadResult
{
    public StudioProjectLoadResult(
        StudioProjectLoadOutcome outcome,
        string message,
        GenerationOutputProject? project = null,
        StudioProjectDocument? document = null,
        IReadOnlyList<string>? affectedSources = null)
    {
        if (!Enum.IsDefined(outcome) || string.IsNullOrWhiteSpace(message) ||
            (project is null) != (document is null))
        {
            throw new ArgumentException(
                "A Studio project load result requires a typed outcome and consistent retained state.");
        }
        Outcome = outcome;
        Message = message.Trim();
        Project = project;
        Document = document;
        AffectedSources = Array.AsReadOnly(
            affectedSources?.Select(Path.GetFullPath).ToArray() ?? []);
    }

    public StudioProjectLoadOutcome Outcome { get; }
    public string Message { get; }
    public GenerationOutputProject? Project { get; }
    public StudioProjectDocument? Document { get; }
    public IReadOnlyList<string> AffectedSources { get; }
    public bool CanOpen => Outcome is
        StudioProjectLoadOutcome.Loaded or
        StudioProjectLoadOutcome.RecoveredPreviousSave;
    public bool HasRecoverableProject => Project is not null;
}

public interface IStudioProjectStore
{
    void Save(
        GenerationOutputProject project,
        long revision,
        StudioProjectRecoveryState? recovery = null);

    StudioProjectLoadResult Load(string projectId);

    bool Exists(string projectId);

    void Delete(string projectId);
}
