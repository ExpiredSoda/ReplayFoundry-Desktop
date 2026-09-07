using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using System.IO;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.VisualText;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;
using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;

namespace ReplayFoundry.Desktop.Features.Studio.Projects;

public static class StudioProjectDocumentMapper
{
    public static StudioProjectDocument Capture(
        GenerationOutputProject project,
        long revision,
        DateTimeOffset updatedAtUtc,
        StudioProjectRecoveryState? recovery = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (revision <= 0 || updatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        StudioProjectSourceSnapshot[] sources = project.SourceMedia
            .Select(static source => source.FullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(CaptureSource)
            .ToArray();

        return new StudioProjectDocument(
            StudioProjectDocument.CurrentSchemaVersion,
            project.Id,
            revision,
            project.Mode,
            project.OutputDirectory,
            project.RequestedCount,
            project.FulfillmentPreference,
            project.FulfillmentOutcome,
            project.ResultCountMode,
            project.CandidateSetFingerprint,
            project.CreatedAtUtc,
            updatedAtUtc,
            project.FinalizedAtUtc,
            sources,
            project.Assets.Select(MapAsset).ToArray(),
            project.HiddenMoments.Select(MapHiddenMoment).ToArray(),
            recovery,
            project.SourceMedia.Select(MapMedia).ToArray());
    }

    public static GenerationOutputProject Restore(
        StudioProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        GenerationOutputAsset[] assets = document.Assets
            .Select(RestoreAsset)
            .ToArray();
        GenerationHiddenMoment[] hidden = document.HiddenMoments
            .Select(RestoreHiddenMoment)
            .ToArray();
        return new GenerationOutputProject(
            document.ProjectId,
            document.Mode,
            document.OutputDirectory,
            document.RequestedCount,
            document.FulfillmentPreference,
            document.FulfillmentOutcome,
            assets,
            document.CreatedAtUtc,
            document.FinalizedAtUtc,
            document.ResultCountMode,
            hidden,
            document.CandidateSetFingerprint,
            document.SourceMedia.Select(RestoreMedia));
    }

    private static StudioProjectSourceSnapshot CaptureSource(string fullPath)
    {
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException(
                "A Studio project source disappeared before it could be saved.",
                fullPath);
        }
        return new StudioProjectSourceSnapshot(
            info.FullName,
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
    }

    private static StudioProjectAssetDocument MapAsset(
        GenerationOutputAsset asset) =>
        new(
            asset.Id,
            asset.Rank,
            MapMedia(asset.SourceMedia),
            asset.OutputFullPath,
            asset.ThumbnailFullPath,
            asset.SourceStart,
            asset.SourceEnd,
            asset.OriginalSourceStart,
            asset.OriginalSourceEnd,
            asset.Score,
            asset.QualityTarget,
            asset.SelectionReason,
            asset.Explanation,
            asset.Captions is null ? null : MapCaption(asset.Captions),
            MapAppearance(asset.Appearance),
            asset.EditorialContext is null
                ? null
                : MapEditorialContext(asset.EditorialContext),
            asset.EditorialMetadata is null
                ? null
                : MapEditorialMetadata(asset.EditorialMetadata),
            asset.PreferenceFeatures is null
                ? null
                : MapPreferenceVector(asset.PreferenceFeatures),
            asset.Disposition,
            asset.RenderSettings,
            asset.EditorialAuthoredContextRevision);

    private static StudioCaptionTrackDocument MapCaption(
        GenerationCandidateCaptionTrack track) =>
        new(
            track.CandidateId,
            track.NeighborhoodId,
            track.SourceSelection,
            track.RequestedStyle,
            track.SourceWindowStart,
            track.SourceWindowDuration,
            track.SourceDuration,
            track.Segments.Select(MapTranscriptionSegment).ToArray(),
            track.IsUserEdited,
            track.SuppressionReason);

    private static StudioClipAppearanceDocument MapAppearance(
        StudioClipAppearance appearance) =>
        new(
            appearance.CaptionStyle,
            appearance.CaptionVerticalPositionPercent,
            appearance.CaptionWordLimit,
            appearance.CaptionMaximumWidthPercent,
            appearance.CaptionFontScalePercent,
            appearance.VideoEffect,
            appearance.VideoEffectIntensityPercent,
            appearance.GraphicOverlays.Select(static overlay =>
                new StudioGraphicOverlayDocument(
                    overlay.Id,
                    overlay.ImageFullPath,
                    overlay.CenterXPercent,
                    overlay.CenterYPercent,
                    overlay.WidthPercent)).ToArray(), appearance.CaptionTypography);

    private static StudioHiddenMomentDocument MapHiddenMoment(
        GenerationHiddenMoment hidden) =>
        new(
            hidden.Id,
            hidden.ReviewOrder,
            hidden.SourceOrder,
            MapMedia(hidden.SourceMedia),
            hidden.SourceStart,
            hidden.SourceEnd,
            hidden.FinalScore,
            hidden.QualityTarget,
            hidden.Reason,
            hidden.Explanation,
            MapPreferenceVector(hidden.PreferenceFeatures),
            hidden.EditorialPreference,
            hidden.EditorialContext is null
                ? null
                : MapEditorialContext(hidden.EditorialContext),
            hidden.EditorialMetadata is null
                ? null
                : MapEditorialMetadata(hidden.EditorialMetadata),
            hidden.CaptionSourceSelection,
            hidden.CaptionStyle);

    private static GenerationOutputAsset RestoreAsset(
        StudioProjectAssetDocument asset) =>
        GenerationOutputAsset.RestoreStudioHandoff(
            asset.Id,
            asset.Rank,
            RestoreMedia(asset.SourceMedia),
            asset.OutputFullPath,
            asset.ThumbnailFullPath,
            asset.SourceStart,
            asset.SourceEnd,
            asset.OriginalSourceStart,
            asset.OriginalSourceEnd,
            asset.Score,
            asset.QualityTarget,
            asset.SelectionReason,
            asset.Explanation,
            asset.Captions is null
                ? null
                : RestoreCaption(asset.Captions),
            RestoreAppearance(asset.Appearance),
            asset.EditorialContext is null
                ? null
                : RestoreEditorialContext(asset.EditorialContext),
            asset.EditorialMetadata is null
                ? null
                : RestoreEditorialMetadata(asset.EditorialMetadata),
            asset.PreferenceFeatures is null
                ? null
                : RestorePreferenceVector(asset.PreferenceFeatures),
            asset.Disposition,
            asset.RenderSettings,
            asset.EditorialAuthoredContextRevision);

    private static GenerationCandidateCaptionTrack RestoreCaption(
        StudioCaptionTrackDocument caption) =>
        GenerationCandidateCaptionTrack.RestoreStudioHandoff(
            caption.CandidateId,
            caption.NeighborhoodId,
            caption.SourceSelection,
            caption.RequestedStyle,
            caption.SourceWindowStart,
            caption.SourceWindowDuration,
            caption.SourceDuration,
            caption.Segments.Select(RestoreTranscriptionSegment),
            caption.IsUserEdited,
            caption.SuppressionReason);

    private static StudioClipAppearance RestoreAppearance(
        StudioClipAppearanceDocument appearance) =>
        new(
            appearance.CaptionStyle,
            appearance.CaptionVerticalPositionPercent,
            appearance.VideoEffect,
            appearance.VideoEffectIntensityPercent,
            appearance.GraphicOverlays.Select(static overlay =>
                StudioGraphicOverlay.Restore(
                    overlay.Id,
                    overlay.ImageFullPath,
                    overlay.CenterXPercent,
                    overlay.CenterYPercent,
                    overlay.WidthPercent)),
            appearance.CaptionWordLimit,
            appearance.CaptionMaximumWidthPercent,
            appearance.CaptionFontScalePercent, appearance.CaptionTypography);

    private static GenerationHiddenMoment RestoreHiddenMoment(
        StudioHiddenMomentDocument hidden) =>
        GenerationHiddenMoment.RestoreStudioHandoff(
            hidden.Id,
            hidden.ReviewOrder,
            hidden.SourceOrder,
            RestoreMedia(hidden.SourceMedia),
            hidden.SourceStart,
            hidden.SourceEnd,
            hidden.FinalScore,
            hidden.QualityTarget,
            hidden.Reason,
            hidden.Explanation,
            RestorePreferenceVector(hidden.PreferenceFeatures),
            hidden.EditorialPreference ??
                ClipEditorialGenerationPreference.AiRequired,
            hidden.EditorialContext is null
                ? null
                : RestoreEditorialContext(hidden.EditorialContext),
            hidden.EditorialMetadata is null
                ? null
                : RestoreEditorialMetadata(hidden.EditorialMetadata),
            hidden.CaptionSourceSelection,
            hidden.CaptionStyle);

    internal static StudioEditorialContextDocument MapEditorialContext(
        ClipEditorialContext context) =>
        new(
            context.CandidateId,
            context.SourceFullPath,
            context.SourceLabel,
            context.SourceStart,
            context.SourceEnd,
            context.SourceDuration,
            context.DeterministicScore,
            context.DeterministicReason,
            context.Transcripts.ToArray(),
            context.Evidence.ToArray(),
            context.GameContext,
            MapSelectedGameKnowledge(context.GameKnowledge),
            context.GameplayRegion,
            context.VisualText is null
                ? null
                : new StudioVisualTextDocument(
                    context.VisualText.CandidateId,
                    context.VisualText.SourceFullPath,
                    context.VisualText.ContentRegion,
                    context.VisualText.Anchors.ToArray(),
                    context.VisualText.Warnings.ToArray()),
            context.EditorialBrief);

    private static ClipEditorialContext RestoreEditorialContext(
        StudioEditorialContextDocument context)
    {
        ClipVisualTextContext? visualText = context.VisualText is null
            ? null
            : new ClipVisualTextContext(
                context.VisualText.CandidateId,
                context.VisualText.SourceFullPath,
                context.VisualText.ContentRegion,
                frames: [],
                context.VisualText.Anchors,
                context.VisualText.Warnings);
        ClipGameKnowledgeContext? gameKnowledge =
            RestoreSelectedGameKnowledge(context.SelectedGameKnowledge);
        return new ClipEditorialContext(
            context.CandidateId,
            context.SourceFullPath,
            context.SourceLabel,
            context.SourceStart,
            context.SourceEnd,
            context.SourceDuration,
            context.DeterministicScore,
            context.DeterministicReason,
            context.Transcripts,
            context.Evidence,
            context.GameContext,
            gameKnowledge,
            context.GameplayRegion,
            visualText,
            context.EditorialBrief);
    }

    private static StudioSelectedGameKnowledgeDocument?
        MapSelectedGameKnowledge(ClipGameKnowledgeContext? knowledge)
    {
        if (knowledge?.Snapshot is not GameKnowledgeSnapshot snapshot)
        {
            return null;
        }
        GameKnowledgeMatch[] matches = knowledge.Matches.Take(4).ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        string[] passageIds = matches.Select(static match => match.Passage.Id)
            .Distinct(StringComparer.Ordinal).ToArray();
        GameKnowledgePassage[] passages = snapshot.Passages
            .Where(passage => passageIds.Contains(
                passage.Id,
                StringComparer.Ordinal))
            .Take(4)
            .ToArray();
        string[] sourceIds = passages.Select(static passage => passage.SourceId)
            .Distinct(StringComparer.Ordinal).ToArray();
        GameKnowledgeSource[] sources = snapshot.Sources
            .Where(source => sourceIds.Contains(
                source.Id,
                StringComparer.Ordinal))
            .Take(4)
            .ToArray();
        return new StudioSelectedGameKnowledgeDocument(
            knowledge.GameName,
            snapshot.Provider.Name,
            snapshot.Provider.Version,
            snapshot.RetrievedAtUtc,
            sources.Select(MapKnowledgeSource).ToArray(),
            passages.Select(static passage =>
                new StudioGameKnowledgePassageDocument(
                    passage.Id,
                    passage.SourceId,
                    passage.Section,
                    passage.Text,
                    passage.ContentSha256)).ToArray(),
            matches.Select(static match =>
                new StudioGameKnowledgeMatchDocument(
                    match.Passage.Id,
                    match.Strength,
                    match.TemporalRelation,
                    match.Relevance,
                    match.MatchedTerms.ToArray(),
                    match.ClipEvidenceIds.ToArray())).ToArray());
    }

    private static StudioGameKnowledgeSourceDocument MapKnowledgeSource(
        GameKnowledgeSource source) =>
        new(
            source.Id,
            source.Kind,
            source.Role,
            source.Title,
            source.PageUri.AbsoluteUri,
            source.RevisionId,
            source.RevisionTimestampUtc,
            source.LicenseIdentifier,
            source.LicenseUri.AbsoluteUri,
            source.Attribution,
            source.ContentSha256);

    private static ClipGameKnowledgeContext? RestoreSelectedGameKnowledge(
        StudioSelectedGameKnowledgeDocument? document)
    {
        if (document is null)
        {
            return null;
        }
        GameKnowledgeSnapshot snapshot = RestoreSelectedSnapshot(document);
        return new ClipGameKnowledgeContext(
            document.GameName,
            snapshot,
            RestoreKnowledgeMatches(document.Matches, snapshot));
    }

    private static GameKnowledgeSnapshot RestoreSelectedSnapshot(
        StudioSelectedGameKnowledgeDocument document) =>
        new(
            document.GameName,
            new GameKnowledgeProviderIdentity(
                document.ProviderName,
                document.ProviderVersion),
            document.RetrievedAtUtc,
            document.Sources.Select(RestoreKnowledgeSource),
            document.Passages.Select(static passage =>
                new GameKnowledgePassage(
                    passage.Id,
                    passage.SourceId,
                    passage.Section,
                    passage.Text,
                    passage.ContentSha256)));

    private static GameKnowledgeSource RestoreKnowledgeSource(
        StudioGameKnowledgeSourceDocument source) =>
        new(
            source.Id,
            source.Kind,
            source.Title,
            new Uri(source.PageUri, UriKind.Absolute),
            source.RevisionId,
            source.RevisionTimestampUtc,
            source.LicenseIdentifier,
            new Uri(source.LicenseUri, UriKind.Absolute),
            source.Attribution,
            source.ContentSha256,
            source.Role);

    private static IEnumerable<GameKnowledgeMatch> RestoreKnowledgeMatches(
        IReadOnlyList<StudioGameKnowledgeMatchDocument> matches,
        GameKnowledgeSnapshot snapshot)
    {
        IReadOnlyDictionary<string, GameKnowledgePassage> passages =
            snapshot.Passages.ToDictionary(
                static passage => passage.Id,
                StringComparer.Ordinal);
        return matches.Take(4).Select(match => new GameKnowledgeMatch(
            passages[match.PassageId],
            match.Strength,
            match.Relevance,
            match.MatchedTerms,
            match.ClipEvidenceIds,
            match.TemporalRelation));
    }

    private static StudioMediaDocument MapMedia(MediaProbeResult media) =>
        new(
            media.FullPath,
            new StudioMediaContainerDocument(
                media.Container.FormatName,
                media.Container.FormatLongName,
                media.Container.Duration,
                media.Container.StartTime,
                media.Container.SizeBytes,
                media.Container.BitRate,
                media.Container.ProbeScore,
                new Dictionary<string, string>(media.Container.Tags)),
            media.VideoStreams.Select(static stream =>
                new StudioVideoStreamDocument(
                    stream.Index,
                    stream.CodecName,
                    stream.CodecLongName,
                    stream.Profile,
                    stream.Width,
                    stream.Height,
                    stream.CodedWidth,
                    stream.CodedHeight,
                    MapRational(stream.AverageFrameRateExact),
                    MapRational(stream.RealFrameRateExact),
                    stream.PixelFormat,
                    stream.BitDepth,
                    stream.BitDepthSource,
                    MapRational(stream.SampleAspectRatioExact),
                    stream.SampleAspectRatioSource,
                    MapRational(stream.DisplayAspectRatioExact),
                    stream.DisplayAspectRatioSource,
                    stream.RotationDegrees,
                    stream.FieldOrder,
                    stream.ColorRange,
                    stream.ColorPrimaries,
                    stream.ColorTransfer,
                    stream.ColorMatrix,
                    stream.ChromaLocation,
                    stream.BitRate,
                    stream.Duration,
                    stream.IsDefault)).ToArray(),
            media.AudioStreams.Select(static stream =>
                new StudioAudioStreamDocument(
                    stream.Index,
                    stream.CodecName,
                    stream.CodecLongName,
                    stream.Profile,
                    stream.SampleRate,
                    stream.Channels,
                    stream.ChannelLayout,
                    stream.BitDepth,
                    stream.BitRate,
                    stream.Duration,
                    stream.Language,
                    stream.Title,
                    stream.IsDefault)).ToArray(),
            new StudioMediaInspectionManifestDocument(
                media.Manifest.InspectorName,
                media.Manifest.InspectorVersion,
                media.Manifest.ToolName,
                media.Manifest.ToolVersion,
                media.Manifest.ToolPath,
                media.Manifest.InspectedAtUtc),
            media.Warnings.Select(static warning =>
                new StudioMediaInspectionWarningDocument(
                    warning.Code,
                    warning.Message,
                    warning.StreamIndex)).ToArray());

    private static MediaProbeResult RestoreMedia(StudioMediaDocument media) =>
        new(
            media.FullPath,
            new MediaContainerInfo(
                media.Container.FormatName,
                media.Container.FormatLongName,
                media.Container.Duration,
                media.Container.StartTime,
                media.Container.SizeBytes,
                media.Container.BitRate,
                media.Container.ProbeScore,
                media.Container.Tags),
            media.VideoStreams.Select(static stream =>
                new VideoStreamInfo(
                    stream.Index,
                    stream.CodecName,
                    stream.CodecLongName,
                    stream.Profile,
                    stream.Width,
                    stream.Height,
                    stream.CodedWidth,
                    stream.CodedHeight,
                    RestoreRational(stream.AverageFrameRate),
                    RestoreRational(stream.RealFrameRate),
                    stream.PixelFormat,
                    stream.BitDepth,
                    stream.BitDepthSource,
                    RestoreRational(stream.SampleAspectRatio),
                    stream.SampleAspectRatioSource,
                    RestoreRational(stream.DisplayAspectRatio),
                    stream.DisplayAspectRatioSource,
                    stream.RotationDegrees,
                    stream.FieldOrder,
                    stream.ColorRange,
                    stream.ColorPrimaries,
                    stream.ColorTransfer,
                    stream.ColorMatrix,
                    stream.ChromaLocation,
                    stream.BitRate,
                    stream.Duration,
                    stream.IsDefault)),
            media.AudioStreams.Select(static stream =>
                new AudioStreamInfo(
                    stream.Index,
                    stream.CodecName,
                    stream.CodecLongName,
                    stream.Profile,
                    stream.SampleRate,
                    stream.Channels,
                    stream.ChannelLayout,
                    stream.BitDepth,
                    stream.BitRate,
                    stream.Duration,
                    stream.Language,
                    stream.Title,
                    stream.IsDefault)),
            new MediaInspectionManifest(
                media.Manifest.InspectorName,
                media.Manifest.InspectorVersion,
                media.Manifest.ToolName,
                media.Manifest.ToolVersion,
                media.Manifest.ToolPath,
                media.Manifest.InspectedAtUtc),
            media.Warnings.Select(static warning =>
                new MediaInspectionWarning(
                    warning.Code,
                    warning.Message,
                    warning.StreamIndex)));

    private static StudioMediaRationalDocument? MapRational(
        MediaRational? value) =>
        value is null
            ? null
            : new StudioMediaRationalDocument(
                value.Value.Numerator,
                value.Value.Denominator);

    private static MediaRational? RestoreRational(
        StudioMediaRationalDocument? value) =>
        value is null
            ? null
            : new MediaRational(value.Numerator, value.Denominator);

    private static StudioTranscriptionSegmentDocument MapTranscriptionSegment(
        AudioTranscriptionSegment segment) =>
        new(
            segment.Id,
            segment.NeighborhoodId,
            segment.Text,
            segment.RelativeStart,
            segment.RelativeEnd,
            segment.AbsoluteSourceStart,
            segment.AbsoluteSourceEnd,
            segment.Words.Select(static word =>
                new StudioTranscriptionWordDocument(
                    word.Text,
                    word.RelativeStart,
                    word.RelativeEnd,
                    word.AbsoluteSourceStart,
                    word.AbsoluteSourceEnd,
                    word.ProviderReportedProbability, word.IsEmphasized)).ToArray(),
            segment.ProviderReportedConfidence,
            segment.Language is null
                ? null
                : new StudioTranscriptionLanguageDocument(
                    segment.Language.Code,
                    segment.Language.DisplayName),
            segment.Warnings.Select(static warning =>
                new StudioTranscriptionWarningDocument(
                    warning.Code,
                    warning.Message,
                    warning.SegmentId)).ToArray(), segment.Speaker, segment.SecondaryText);

    private static AudioTranscriptionSegment RestoreTranscriptionSegment(
        StudioTranscriptionSegmentDocument segment) =>
        new(
            segment.Id,
            segment.NeighborhoodId,
            segment.Text,
            segment.RelativeStart,
            segment.RelativeEnd,
            segment.AbsoluteSourceStart,
            segment.AbsoluteSourceEnd,
            segment.Words.Select(static word =>
                new AudioTranscriptionWord(
                    word.Text,
                    word.RelativeStart,
                    word.RelativeEnd,
                    word.AbsoluteSourceStart,
                    word.AbsoluteSourceEnd,
                    word.ProviderReportedProbability, word.IsEmphasized)),
            segment.ProviderReportedConfidence,
            segment.Language is null
                ? null
                : new AudioTranscriptionLanguage(
                    segment.Language.Code,
                    segment.Language.DisplayName),
            segment.Warnings.Select(static warning =>
                new AudioTranscriptionWarning(
                    warning.Code,
                    warning.Message,
                    warning.SegmentId)), segment.Speaker, segment.SecondaryText);

    private static StudioEditorialMetadataDocument MapEditorialMetadata(
        ClipEditorialMetadataDraft metadata) =>
        new(
            metadata.Title,
            metadata.Description,
            metadata.Tags.ToArray(),
            metadata.Origin,
            new StudioEditorialGeneratorDocument(
                metadata.Generator.Name,
                metadata.Generator.Version),
            metadata.Attempt,
            metadata.Evidence.Select(static value =>
                new StudioEditorialEvidenceDocument(
                    value.Id,
                    value.Kind,
                    value.Description)).ToArray(),
            metadata.Warnings.Select(static value =>
                new StudioEditorialWarningDocument(
                    value.Code,
                    value.Message)).ToArray(),
            metadata.AiProvenance is null
                ? null
                : new StudioEditorialAiProvenanceDocument(
                    metadata.AiProvenance.ProviderName,
                    metadata.AiProvenance.ProviderVersion,
                    metadata.AiProvenance.RuntimeVersion,
                    metadata.AiProvenance.ModelRepositoryId,
                    metadata.AiProvenance.ModelRevision,
                    metadata.AiProvenance.ModelManifestSha256,
                    metadata.AiProvenance.PromptName,
                    metadata.AiProvenance.PromptVersion,
                    metadata.AiProvenance.PromptSha256,
                    metadata.AiProvenance.BatchElapsed,
                    metadata.AiProvenance.PeakAllocatedGpuBytes,
                    metadata.AiProvenance.WritingAttempts.ToArray()),
            metadata.Readiness,
            metadata.QualityIssues.Select(static value =>
                new StudioEditorialQualityIssueDocument(
                    value.Code,
                    value.Message,
                    value.SourceRuleCode)).ToArray(),
            metadata.PriorAcceptedTitles.ToArray(),
            metadata.GroundingAudit,
            metadata.CopyVersions.ToArray());

    private static ClipEditorialMetadataDraft RestoreEditorialMetadata(
        StudioEditorialMetadataDocument metadata) =>
        new(
            metadata.Title,
            metadata.Description,
            metadata.Tags,
            metadata.Origin,
            new ClipEditorialMetadataGeneratorIdentity(
                metadata.Generator.Name,
                metadata.Generator.Version),
            metadata.Attempt,
            metadata.Evidence.Select(static value =>
                new ClipEditorialEvidenceReference(
                    value.Id,
                    value.Kind,
                    value.Description)),
            metadata.Warnings.Select(static value =>
                new ClipEditorialWarning(value.Code, value.Message)),
            metadata.AiProvenance is null
                ? null
                : new ClipEditorialAiProvenance(
                    metadata.AiProvenance.ProviderName,
                    metadata.AiProvenance.ProviderVersion,
                    metadata.AiProvenance.RuntimeVersion,
                    metadata.AiProvenance.ModelRepositoryId,
                    metadata.AiProvenance.ModelRevision,
                    metadata.AiProvenance.ModelManifestSha256,
                    metadata.AiProvenance.PromptName,
                    metadata.AiProvenance.PromptVersion,
                    metadata.AiProvenance.PromptSha256,
                    metadata.AiProvenance.BatchElapsed,
                    metadata.AiProvenance.PeakAllocatedGpuBytes)
                {
                    WritingAttempts = metadata.AiProvenance.WritingAttempts?.ToArray() ?? []
                },
            metadata.Readiness,
            metadata.QualityIssues.Select(static value =>
                new ClipEditorialMetadataQualityIssue(
                    value.Code,
                    value.Message,
                    value.SourceRuleCode)),
            metadata.PriorAcceptedTitles ?? [],
            metadata.GroundingAudit,
            metadata.CopyVersions ?? []);

    private static StudioPreferenceVectorDocument MapPreferenceVector(
        ClipPreferenceFeatureVector vector) =>
        new(
            vector.Features.Select(static value =>
                new StudioPreferenceFeatureDocument(
                    value.Code,
                    value.NormalizedValue)).ToArray(), vector.Context, vector.DetectedContent);

    private static ClipPreferenceFeatureVector RestorePreferenceVector(
        StudioPreferenceVectorDocument vector) =>
        new(
            vector.Features.Select(static value =>
                new ClipPreferenceFeature(
                    value.Code,
                    value.NormalizedValue)), vector.Context, vector.DetectedContent);
}
