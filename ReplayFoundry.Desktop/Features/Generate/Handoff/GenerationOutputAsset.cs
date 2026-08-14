using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;

namespace ReplayFoundry.Desktop.Features.Generate.Handoff;

public enum GenerationOutputAssetDisposition
{
    IncludeInFinalRender,
    ExcludeFromFinalRender,
}

public sealed class GenerationOutputAsset
{
    public GenerationOutputAsset(
        string id,
        int rank,
        MediaProbeResult sourceMedia,
        string? outputFullPath,
        TimeSpan sourceStart,
        TimeSpan sourceEnd,
        double score,
        double qualityTarget,
        GenerationCandidateSelectionReason selectionReason,
        string explanation,
        GenerationCandidateCaptionTrack? captions = null,
        StudioClipAppearance? appearance = null,
        ClipEditorialContext? editorialContext = null,
        ClipEditorialMetadataDraft? editorialMetadata = null,
        ClipPreferenceFeatureVector? preferenceFeatures = null,
        string? thumbnailFullPath = null,
        GenerationOutputAssetDisposition disposition =
            GenerationOutputAssetDisposition.IncludeInFinalRender)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(explanation))
        {
            throw new ArgumentException(
                "A generated output asset requires identity and explanation.");
        }
        ArgumentNullException.ThrowIfNull(sourceMedia);
        if (rank <= 0 ||
            sourceEnd <= sourceStart ||
            sourceStart < TimeSpan.Zero ||
            sourceEnd > sourceMedia.Duration ||
            score is < 0 or > 100 ||
            qualityTarget is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(rank));
        }
        if (outputFullPath is not null &&
            !Path.IsPathFullyQualified(outputFullPath))
        {
            throw new ArgumentException(
                "Generated output paths must be fully qualified.");
        }
        if (thumbnailFullPath is not null &&
            (!Path.IsPathFullyQualified(thumbnailFullPath) ||
             outputFullPath is null))
        {
            throw new ArgumentException(
                "A generated thumbnail requires a rendered output and a fully qualified path.");
        }
        if (!Enum.IsDefined(selectionReason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(selectionReason));
        }
        if (!Enum.IsDefined(disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }
        if (disposition == GenerationOutputAssetDisposition.ExcludeFromFinalRender &&
            outputFullPath is not null)
        {
            throw new ArgumentException(
                "An excluded candidate must retain metadata without a rendered output.",
                nameof(disposition));
        }

        Id = id.Trim();
        Rank = rank;
        SourceMedia = sourceMedia;
        OutputFullPath = outputFullPath is null
            ? null
            : Path.GetFullPath(outputFullPath);
        ThumbnailFullPath = thumbnailFullPath is null
            ? null
            : Path.GetFullPath(thumbnailFullPath);
        SourceStart = sourceStart;
        SourceEnd = sourceEnd;
        OriginalSourceStart = sourceStart;
        OriginalSourceEnd = sourceEnd;
        Score = score;
        QualityTarget = qualityTarget;
        SelectionReason = selectionReason;
        Explanation = explanation.Trim();
        if (captions is not null &&
            !captions.CandidateId.Equals(Id, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Retained captions must belong to the generated asset.",
                nameof(captions));
        }
        if ((editorialContext is null) !=
                (editorialMetadata is null) ||
            editorialContext is not null &&
            !editorialContext.CandidateId.Equals(
                Id,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Editorial context and metadata must be supplied together for the generated asset.",
                nameof(editorialContext));
        }
        Captions = captions;
        Appearance = appearance ??
            StudioClipAppearance.CreateDefault(
                captions?.RequestedStyle ??
                GenerationCaptionStylePreset.Clean);
        if (captions is not null &&
            Appearance.CaptionStyle != captions.RequestedStyle)
        {
            throw new ArgumentException(
                "The retained caption track and Studio appearance must use the same caption style.",
                nameof(appearance));
        }
        EditorialContext = editorialContext;
        EditorialMetadata = editorialMetadata;
        PreferenceFeatures = preferenceFeatures;
        Disposition = disposition;
    }

    public string Id { get; }
    public int Rank { get; }
    public string DisplayName =>
        $"Clip {Rank:00} · {Path.GetFileName(SourceFullPath)}";

    public override string ToString() => DisplayName;
    public MediaProbeResult SourceMedia { get; }
    public string SourceFullPath => SourceMedia.FullPath;
    public TimeSpan SourceDuration => SourceMedia.Duration;
    public string? OutputFullPath { get; }
    public string? ThumbnailFullPath { get; }
    public bool HasThumbnail => ThumbnailFullPath is not null;
    public bool IsRendered => OutputFullPath is not null;
    public TimeSpan SourceStart { get; }
    public TimeSpan SourceEnd { get; }
    public TimeSpan OriginalSourceStart { get; private init; }
    public TimeSpan OriginalSourceEnd { get; private init; }
    public TimeSpan Duration => SourceEnd - SourceStart;
    public double Score { get; }
    public double QualityTarget { get; }
    public bool MeetsQualityTarget => Score >= QualityTarget;
    public GenerationCandidateSelectionReason SelectionReason { get; }
    public bool RequiredDiversityRelaxation =>
        SelectionReason ==
        GenerationCandidateSelectionReason.CountFillRelaxedDiversity;
    public string Explanation { get; }
    public GenerationCandidateCaptionTrack? Captions { get; }
    public bool HasCaptions => Captions is not null;
    public StudioClipAppearance Appearance { get; }
    public ClipEditorialContext? EditorialContext { get; }
    public ClipEditorialMetadataDraft? EditorialMetadata { get; }
    public bool HasEditorialMetadata => EditorialMetadata is not null;
    public bool IsEditorialMetadataCurrentForCut =>
        EditorialContext is not null &&
        EditorialMetadata is not null &&
        EditorialContext.SourceStart == SourceStart &&
        EditorialContext.SourceEnd == SourceEnd;
    public ClipPreferenceFeatureVector? PreferenceFeatures { get; }
    public GenerationOutputAssetDisposition Disposition { get; }
    public bool IsIncludedInFinalRender =>
        Disposition == GenerationOutputAssetDisposition.IncludeInFinalRender;

    internal GenerationOutputAsset WithStudioEdits(
        TimeSpan sourceStart,
        TimeSpan sourceEnd,
        StudioClipAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        GenerationCandidateCaptionTrack? captions = Captions is null
            ? null
            : Captions.WithRequestedStyle(
                appearance.CaptionStyle);
        return new GenerationOutputAsset(
            Id,
            Rank,
            SourceMedia,
            outputFullPath: null,
            sourceStart,
            sourceEnd,
            Score,
            QualityTarget,
            SelectionReason,
            Explanation,
            captions,
            appearance,
            EditorialContext,
            EditorialMetadata,
            PreferenceFeatures,
            thumbnailFullPath: null,
            Disposition)
        {
            OriginalSourceStart = OriginalSourceStart,
            OriginalSourceEnd = OriginalSourceEnd,
        };
    }

    internal static GenerationOutputAsset RestoreStudioHandoff(
        string id,
        int rank,
        MediaProbeResult sourceMedia,
        string? outputFullPath,
        string? thumbnailFullPath,
        TimeSpan sourceStart,
        TimeSpan sourceEnd,
        TimeSpan originalSourceStart,
        TimeSpan originalSourceEnd,
        double score,
        double qualityTarget,
        GenerationCandidateSelectionReason selectionReason,
        string explanation,
        GenerationCandidateCaptionTrack? captions,
        StudioClipAppearance appearance,
        ClipEditorialContext? editorialContext,
        ClipEditorialMetadataDraft? editorialMetadata,
        ClipPreferenceFeatureVector? preferenceFeatures,
        GenerationOutputAssetDisposition disposition)
    {
        if (originalSourceStart < TimeSpan.Zero ||
            originalSourceEnd <= originalSourceStart ||
            originalSourceEnd > sourceMedia.Duration)
        {
            throw new ArgumentOutOfRangeException(nameof(originalSourceStart));
        }

        return new GenerationOutputAsset(
            id,
            rank,
            sourceMedia,
            outputFullPath,
            sourceStart,
            sourceEnd,
            score,
            qualityTarget,
            selectionReason,
            explanation,
            captions,
            appearance,
            editorialContext,
            editorialMetadata,
            preferenceFeatures,
            thumbnailFullPath,
            disposition)
        {
            OriginalSourceStart = originalSourceStart,
            OriginalSourceEnd = originalSourceEnd,
        };
    }

    internal GenerationOutputAsset WithRenderedOutput(
        string outputFullPath,
        string? thumbnailFullPath = null)
    {
        return new GenerationOutputAsset(
            Id,
            Rank,
            SourceMedia,
            outputFullPath,
            SourceStart,
            SourceEnd,
            Score,
            QualityTarget,
            SelectionReason,
            Explanation,
            Captions,
            Appearance,
            EditorialContext,
            EditorialMetadata,
            PreferenceFeatures,
            thumbnailFullPath,
            Disposition)
        {
            OriginalSourceStart = OriginalSourceStart,
            OriginalSourceEnd = OriginalSourceEnd,
        };
    }

    internal GenerationOutputAsset WithCurrentCutEditorialMetadata(
        ClipEditorialContext editorialContext,
        ClipEditorialMetadataDraft editorialMetadata)
    {
        ArgumentNullException.ThrowIfNull(editorialContext);
        ArgumentNullException.ThrowIfNull(editorialMetadata);
        if (!editorialContext.CandidateId.Equals(Id, StringComparison.Ordinal) ||
            !editorialContext.SourceFullPath.Equals(
                SourceFullPath,
                StringComparison.OrdinalIgnoreCase) ||
            editorialContext.SourceDuration != SourceDuration ||
            editorialContext.SourceStart != SourceStart ||
            editorialContext.SourceEnd != SourceEnd)
        {
            throw new ArgumentException(
                "Current-cut editorial context must match the asset identity, source, duration, and exact Studio boundaries.",
                nameof(editorialContext));
        }

        return new GenerationOutputAsset(
            Id,
            Rank,
            SourceMedia,
            outputFullPath: null,
            SourceStart,
            SourceEnd,
            Score,
            QualityTarget,
            SelectionReason,
            Explanation,
            Captions,
            Appearance,
            editorialContext,
            editorialMetadata,
            PreferenceFeatures,
            thumbnailFullPath: null,
            Disposition)
        {
            OriginalSourceStart = OriginalSourceStart,
            OriginalSourceEnd = OriginalSourceEnd,
        };
    }

    internal ClipEditorialContext CreateCurrentCutEditorialContext()
    {
        if (EditorialContext is null)
        {
            throw new InvalidOperationException(
                "The generated asset has no retained editorial context.");
        }
        if (IsEditorialMetadataCurrentForCut)
        {
            return EditorialContext;
        }

        ClipEditorialTranscriptContext[] transcripts =
            BuildCurrentCutTranscripts();
        return EditorialContext
            .WithSourceRange(SourceStart, SourceEnd)
            .WithTranscripts(transcripts)
            // Retained OCR observations were sampled for the Generate-time
            // window. A changed cut receives a fresh bounded visual review,
            // so stale OCR must not be presented as current-cut evidence.
            .WithVisualText(visualText: null);
    }

    internal GenerationOutputAsset WithCaptionTrack(
        GenerationCandidateCaptionTrack captions)
    {
        ArgumentNullException.ThrowIfNull(captions);
        GenerationCandidateCaptionTrack retainedCaptions =
            captions.ToStudioHandoff();
        ClipEditorialContext? editorialContext = EditorialContext;
        if (editorialContext is not null)
        {
            string transcriptText = string.Join(
                " ",
                retainedCaptions.Segments.Select(static segment => segment.Text));
            ClipEditorialTranscriptContext[] transcripts =
                string.IsNullOrWhiteSpace(transcriptText)
                    ? []
                    :
                    [
                        new ClipEditorialTranscriptContext(
                            retainedCaptions.SourceSelection.AbsoluteAudioStreamIndex,
                            MapEditorialRole(
                                retainedCaptions.SourceSelection.ContentRole),
                            transcriptText,
                            retainedCaptions.IsUserEdited
                                ? ClipEditorialTranscriptAuthority.UserCorrected
                                : ClipEditorialTranscriptAuthority.AutomaticUnreviewed),
                    ];
            editorialContext = editorialContext.WithTranscripts(transcripts);
        }
        return new GenerationOutputAsset(
            Id,
            Rank,
            SourceMedia,
            outputFullPath: null,
            SourceStart,
            SourceEnd,
            Score,
            QualityTarget,
            SelectionReason,
            Explanation,
            retainedCaptions,
            new StudioClipAppearance(
                retainedCaptions.RequestedStyle,
                Appearance.CaptionVerticalPositionPercent,
                Appearance.VideoEffect,
                Appearance.VideoEffectIntensityPercent,
                Appearance.GraphicOverlays,
                Appearance.CaptionWordLimit,
                Appearance.CaptionMaximumWidthPercent,
                Appearance.CaptionFontScalePercent),
            editorialContext,
            EditorialMetadata,
            PreferenceFeatures,
            thumbnailFullPath: null,
            Disposition)
        {
            OriginalSourceStart = OriginalSourceStart,
            OriginalSourceEnd = OriginalSourceEnd,
        };
    }

    private static AudioContentRoleAssignment
        MapEditorialRole(CaptionAudioContentRole role) =>
        role switch
        {
            CaptionAudioContentRole.CreatorCommentary =>
                new AudioContentRoleAssignment(
                    AudioContentRole.CreatorSpeech,
                    AudioContentRoleSource.UserConfirmed),
            CaptionAudioContentRole.GameDialogue =>
                new AudioContentRoleAssignment(
                    AudioContentRole.GameDialogue,
                    AudioContentRoleSource.UserConfirmed),
            CaptionAudioContentRole.MixedSpeech =>
                new AudioContentRoleAssignment(
                    AudioContentRole.MixedSpeech,
                    AudioContentRoleSource.UserConfirmed),
            _ =>
                new AudioContentRoleAssignment(
                    AudioContentRole.Unknown,
                    AudioContentRoleSource.NotAvailable),
        };

    private ClipEditorialTranscriptContext[] BuildCurrentCutTranscripts()
    {
        if (Captions is null)
        {
            // Untimed transcript summaries cannot be safely clipped to a
            // changed Studio window.
            return [];
        }

        string text = string.Join(
            " ",
            Captions.Segments
                .Select(segment =>
                {
                    if (segment.Words.Count > 0)
                    {
                        return string.Join(
                            " ",
                            segment.Words
                                .Where(word =>
                                    word.AbsoluteSourceEnd > SourceStart &&
                                    word.AbsoluteSourceStart < SourceEnd)
                                .Select(static word => word.Text));
                    }

                    return segment.AbsoluteSourceStart >= SourceStart &&
                           segment.AbsoluteSourceEnd <= SourceEnd
                        ? segment.Text
                        : string.Empty;
                })
                .Where(static value => !string.IsNullOrWhiteSpace(value)))
            .Trim();
        if (text.Length == 0)
        {
            return [];
        }
        if (text.Length > ClipEditorialTranscriptContext.MaximumTextLength)
        {
            text = text[..ClipEditorialTranscriptContext.MaximumTextLength]
                .TrimEnd();
        }

        return
        [
            new ClipEditorialTranscriptContext(
                Captions.SourceSelection.AbsoluteAudioStreamIndex,
                MapEditorialRole(Captions.SourceSelection.ContentRole),
                text,
                Captions.IsUserEdited
                    ? ClipEditorialTranscriptAuthority.UserCorrected
                    : ClipEditorialTranscriptAuthority.AutomaticUnreviewed),
        ];
    }

    internal GenerationOutputAsset WithDisposition(
        GenerationOutputAssetDisposition disposition)
    {
        if (!Enum.IsDefined(disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }

        return new GenerationOutputAsset(
            Id,
            Rank,
            SourceMedia,
            outputFullPath: null,
            SourceStart,
            SourceEnd,
            Score,
            QualityTarget,
            SelectionReason,
            Explanation,
            Captions,
            Appearance,
            EditorialContext,
            EditorialMetadata,
            PreferenceFeatures,
            thumbnailFullPath: null,
            disposition)
        {
            OriginalSourceStart = OriginalSourceStart,
            OriginalSourceEnd = OriginalSourceEnd,
        };
    }
}
