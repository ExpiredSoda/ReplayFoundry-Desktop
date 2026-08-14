using System.Collections.ObjectModel;
using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Moments;

public enum GenerationHiddenMomentReason
{
    BelowQualityTarget,
    RequestedCountReached,
    OverlapSuppressed,
    SameEventSuppressed,
    CooldownSuppressed,
    PortfolioNotSelected,
}

public sealed class GenerationHiddenMoment
{
    private readonly AnalyzedGenerationSource? _analyzedSource;
    private readonly MomentCandidate? _candidate;

    public GenerationHiddenMoment(
        string id,
        int reviewOrder,
        int sourceOrder,
        AnalyzedGenerationSource analyzedSource,
        MomentCandidate candidate,
        double finalScore,
        double qualityTarget,
        GenerationHiddenMomentReason reason,
        string explanation,
        ClipPreferenceFeatureVector preferenceFeatures,
        ClipEditorialGenerationPreference editorialPreference =
            ClipEditorialGenerationPreference.HeuristicOnly,
        ClipEditorialContext? editorialContext = null,
        ClipEditorialMetadataDraft? editorialMetadata = null,
        GenerationCaptionSourceSelection? captionSourceSelection = null,
        GenerationCaptionStylePreset? captionStyle = null)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            reviewOrder <= 0 ||
            !double.IsFinite(finalScore) ||
            finalScore is < 0 or > 100 ||
            !double.IsFinite(qualityTarget) ||
            qualityTarget is < 0 or > 100 ||
            !Enum.IsDefined(reason) ||
            !Enum.IsDefined(editorialPreference) ||
            string.IsNullOrWhiteSpace(explanation))
        {
            throw new ArgumentException(
                "A hidden moment requires stable, bounded, and explained provenance.");
        }

        ArgumentNullException.ThrowIfNull(analyzedSource);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(preferenceFeatures);
        if (sourceOrder < 0 ||
            (captionSourceSelection is null) != (captionStyle is null))
        {
            throw new ArgumentException(
                "A hidden moment requires a source order and a complete optional caption request.");
        }
        if ((editorialContext is null) != (editorialMetadata is null) ||
            editorialContext is not null &&
            !editorialContext.CandidateId.Equals(id, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Hidden-moment editorial metadata must be complete and belong to the same candidate.",
                nameof(editorialContext));
        }
        if (analyzedSource.PreparedSource.Media.Duration !=
            candidate.Window.SourceDuration)
        {
            throw new ArgumentException(
                "A hidden moment must preserve the retained source identity.",
                nameof(candidate));
        }
        if (candidate.Disposition is
            MomentCandidateDisposition.RejectedBlack or
            MomentCandidateDisposition.RejectedFreeze)
        {
            throw new ArgumentException(
                "Hard integrity rejections cannot enter Hidden Moments.",
                nameof(candidate));
        }

        Id = id.Trim();
        ReviewOrder = reviewOrder;
        SourceOrder = sourceOrder;
        _analyzedSource = analyzedSource;
        _candidate = candidate;
        SourceMedia = analyzedSource.PreparedSource.Media;
        SourceStart = candidate.Window.Start;
        SourceEnd = candidate.Window.End;
        FinalScore = finalScore;
        QualityTarget = qualityTarget;
        Reason = reason;
        Explanation = explanation.Trim();
        PreferenceFeatures = preferenceFeatures;
        EditorialPreference = editorialPreference;
        EditorialContext = editorialContext;
        EditorialMetadata = editorialMetadata;
        CaptionSourceSelection = captionSourceSelection;
        CaptionStyle = captionStyle;
    }

    public string Id { get; }
    public int ReviewOrder { get; }
    public int SourceOrder { get; }
    public AnalyzedGenerationSource AnalyzedSource => _analyzedSource ??
        throw new InvalidOperationException(
            "The retained Studio hidden moment no longer owns the Generate analysis source.");
    public MediaProbeResult SourceMedia { get; }
    public string SourceFullPath => SourceMedia.FullPath;
    public MomentCandidate Candidate => _candidate ??
        throw new InvalidOperationException(
            "The retained Studio hidden moment no longer owns the deterministic proposal graph.");
    public TimeSpan SourceStart { get; }
    public TimeSpan SourceEnd { get; }
    public TimeSpan Duration => SourceEnd - SourceStart;
    public double FinalScore { get; }
    public double QualityTarget { get; }
    public bool MeetsQualityTarget => FinalScore >= QualityTarget;
    public GenerationHiddenMomentReason Reason { get; }
    public string Explanation { get; }
    public ClipPreferenceFeatureVector PreferenceFeatures { get; }
    public ClipEditorialGenerationPreference EditorialPreference { get; }
    public ClipEditorialContext? EditorialContext { get; }
    public ClipEditorialMetadataDraft? EditorialMetadata { get; }
    public GenerationCaptionSourceSelection? CaptionSourceSelection { get; }
    public GenerationCaptionStylePreset? CaptionStyle { get; }
    public bool CaptionsRequested => CaptionSourceSelection is not null;
    public bool HasGenerationProvenance =>
        _analyzedSource is not null && _candidate is not null;
    public string SourceName => Path.GetFileName(SourceFullPath);
    public string ReviewReason => Reason switch
    {
        GenerationHiddenMomentReason.BelowQualityTarget =>
            "This safe moment landed just below your quality target.",
        GenerationHiddenMomentReason.RequestedCountReached =>
            "It passed the quality checks, but your requested clip count was already filled.",
        GenerationHiddenMomentReason.OverlapSuppressed =>
            "This moment overlapped a stronger selection but may offer a better cut for you.",
        GenerationHiddenMomentReason.SameEventSuppressed =>
            "Another segment represented the same event more strongly.",
        GenerationHiddenMomentReason.CooldownSuppressed =>
            "This moment was held back to keep the main results varied.",
        GenerationHiddenMomentReason.PortfolioNotSelected =>
            "This safe proposal did not enter the final ranked portfolio.",
        _ => throw new InvalidOperationException(
            "The hidden-moment reason is unsupported."),
    };

    public GenerationHiddenMoment WithEditorialMetadata(
        ClipEditorialContext context,
        ClipEditorialMetadataDraft metadata) =>
        HasGenerationProvenance
            ? new(
            Id,
            ReviewOrder,
            SourceOrder,
            AnalyzedSource,
            Candidate,
            FinalScore,
            QualityTarget,
            Reason,
            Explanation,
            PreferenceFeatures,
            EditorialPreference,
            context,
            metadata,
            CaptionSourceSelection,
            CaptionStyle)
            : RestoreStudioHandoff(
                Id,
                ReviewOrder,
                SourceOrder,
                SourceMedia,
                SourceStart,
                SourceEnd,
                FinalScore,
                QualityTarget,
                Reason,
                Explanation,
                PreferenceFeatures,
                EditorialPreference,
                context,
                metadata,
                CaptionSourceSelection,
                CaptionStyle);

    internal bool TryGetGenerationProvenance(
        out AnalyzedGenerationSource? analyzedSource,
        out MomentCandidate? candidate)
    {
        analyzedSource = _analyzedSource;
        candidate = _candidate;
        return analyzedSource is not null && candidate is not null;
    }

    internal GenerationHiddenMoment ToStudioHandoff() =>
        RestoreStudioHandoff(
            Id,
            ReviewOrder,
            SourceOrder,
            SourceMedia,
            SourceStart,
            SourceEnd,
            FinalScore,
            QualityTarget,
            Reason,
            Explanation,
            PreferenceFeatures,
            EditorialPreference,
            EditorialContext,
            EditorialMetadata,
            CaptionSourceSelection,
            CaptionStyle);

    internal static GenerationHiddenMoment RestoreStudioHandoff(
        string id,
        int reviewOrder,
        int sourceOrder,
        MediaProbeResult sourceMedia,
        TimeSpan sourceStart,
        TimeSpan sourceEnd,
        double finalScore,
        double qualityTarget,
        GenerationHiddenMomentReason reason,
        string explanation,
        ClipPreferenceFeatureVector preferenceFeatures,
        ClipEditorialGenerationPreference editorialPreference,
        ClipEditorialContext? editorialContext,
        ClipEditorialMetadataDraft? editorialMetadata,
        GenerationCaptionSourceSelection? captionSourceSelection,
        GenerationCaptionStylePreset? captionStyle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(sourceMedia);
        ArgumentNullException.ThrowIfNull(preferenceFeatures);
        if (reviewOrder <= 0 || sourceOrder < 0 ||
            sourceStart < TimeSpan.Zero || sourceEnd <= sourceStart ||
            sourceEnd > sourceMedia.Duration ||
            !double.IsFinite(finalScore) || finalScore is < 0 or > 100 ||
            !double.IsFinite(qualityTarget) || qualityTarget is < 0 or > 100 ||
            !Enum.IsDefined(reason) || !Enum.IsDefined(editorialPreference) ||
            string.IsNullOrWhiteSpace(explanation) ||
            (editorialContext is null) != (editorialMetadata is null) ||
            editorialContext is not null &&
            !editorialContext.CandidateId.Equals(id, StringComparison.Ordinal) ||
            (captionSourceSelection is null) != (captionStyle is null))
        {
            throw new ArgumentException(
                "A retained Studio hidden moment requires bounded source, score, and optional metadata values.");
        }

        return new GenerationHiddenMoment(
            id.Trim(),
            reviewOrder,
            sourceOrder,
            sourceMedia,
            sourceStart,
            sourceEnd,
            finalScore,
            qualityTarget,
            reason,
            explanation.Trim(),
            preferenceFeatures,
            editorialPreference,
            editorialContext,
            editorialMetadata,
            captionSourceSelection,
            captionStyle);
    }

    private GenerationHiddenMoment(
        string id,
        int reviewOrder,
        int sourceOrder,
        MediaProbeResult sourceMedia,
        TimeSpan sourceStart,
        TimeSpan sourceEnd,
        double finalScore,
        double qualityTarget,
        GenerationHiddenMomentReason reason,
        string explanation,
        ClipPreferenceFeatureVector preferenceFeatures,
        ClipEditorialGenerationPreference editorialPreference,
        ClipEditorialContext? editorialContext,
        ClipEditorialMetadataDraft? editorialMetadata,
        GenerationCaptionSourceSelection? captionSourceSelection,
        GenerationCaptionStylePreset? captionStyle)
    {
        Id = id;
        ReviewOrder = reviewOrder;
        SourceOrder = sourceOrder;
        SourceMedia = sourceMedia;
        SourceStart = sourceStart;
        SourceEnd = sourceEnd;
        FinalScore = finalScore;
        QualityTarget = qualityTarget;
        Reason = reason;
        Explanation = explanation;
        PreferenceFeatures = preferenceFeatures;
        EditorialPreference = editorialPreference;
        EditorialContext = editorialContext;
        EditorialMetadata = editorialMetadata;
        CaptionSourceSelection = captionSourceSelection;
        CaptionStyle = captionStyle;
    }
}

public sealed class GenerationHiddenMomentDeck
{
    private readonly ReadOnlyCollection<GenerationHiddenMoment> _moments;

    public GenerationHiddenMomentDeck(
        GenerationMomentFindingResult selectedMoments,
        IEnumerable<GenerationHiddenMoment>? moments = null)
    {
        SelectedMoments = selectedMoments ??
            throw new ArgumentNullException(nameof(selectedMoments));
        GenerationHiddenMoment[] snapshot = moments?.ToArray() ?? [];
        if (snapshot.Any(static value => value is null) ||
            snapshot.Select(static value => value.Id)
                .Distinct(StringComparer.Ordinal).Count() != snapshot.Length ||
            !snapshot.Select(static value => value.ReviewOrder)
                .SequenceEqual(Enumerable.Range(1, snapshot.Length)) ||
            snapshot.Any(value => selectedMoments.SelectedCandidates.Any(
                selected => selected.Id.Equals(value.Id, StringComparison.Ordinal))))
        {
            throw new ArgumentException(
                "A Hidden Moments deck must be unique, ordered, and disjoint from selected clips.",
                nameof(moments));
        }

        _moments = Array.AsReadOnly(snapshot);
    }

    public GenerationMomentFindingResult SelectedMoments { get; }
    public IReadOnlyList<GenerationHiddenMoment> Moments => _moments;
    public int Count => _moments.Count;

    public GenerationHiddenMomentDeck WithEditorialMetadata(
        IEnumerable<GenerationHiddenMoment> moments) =>
        new(SelectedMoments, moments);
}

public static class GenerationHiddenMomentPlanner
{
    public const string PolicyVersion = "generation-hidden-moments-1.0";

    public static GenerationHiddenMomentDeck Create(
        GenerationMomentFindingResult moments,
        GenerationCandidateIntelligenceResult? intelligence = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(moments);
        if (intelligence is not null &&
            !ReferenceEquals(intelligence.RefinedMoments, moments))
        {
            throw new ArgumentException(
                "Hidden Moments intelligence must belong to the final selected result.",
                nameof(intelligence));
        }

        var selected = moments.SelectedCandidates
            .Select(static value => value.Candidate)
            .ToHashSet(ReferenceEqualityComparer.Instance);
        var refinementMap =
            new Dictionary<MomentCandidate, GenerationCandidateRefinement>(
                ReferenceEqualityComparer.Instance);
        if (intelligence is not null)
        {
            foreach (GenerationCandidateRefinement refinement in
                     intelligence.Refinements)
            {
                refinementMap.Add(refinement.Candidate, refinement);
            }
        }
        IReadOnlyDictionary<MomentCandidate, GenerationCandidateRefinement>
            refinements = refinementMap;

        CandidateEntry[] eligible = moments.Sources
            .SelectMany((source, sourceOrder) => source.Moments.Proposals.Select(
                candidate => new CandidateEntry(
                    source.AnalyzedSource,
                    sourceOrder,
                    candidate,
                    refinements.GetValueOrDefault(candidate))))
            .Where(entry =>
                !selected.Contains(entry.Candidate) &&
                entry.Candidate.Disposition is not
                    (MomentCandidateDisposition.RejectedBlack or
                     MomentCandidateDisposition.RejectedFreeze))
            .OrderBy(entry => SameSelectedEpisode(entry, moments))
            .ThenByDescending(static entry => entry.FinalScore)
            .ThenByDescending(static entry => entry.Candidate.HeuristicScore)
            .ThenBy(static entry => entry.SourceOrder)
            .ThenBy(static entry => entry.Candidate.Window.Start)
            .ThenBy(static entry => entry.Candidate.Id, StringComparer.Ordinal)
            .ToArray();

        var ordered = new List<CandidateEntry>(eligible.Length);
        var remaining = new List<CandidateEntry>(eligible);
        CandidateEntry? previous = null;
        while (remaining.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = previous is null
                ? 0
                : remaining.FindIndex(candidate =>
                    candidate.SourceOrder != previous.SourceOrder ||
                    !string.Equals(
                        candidate.Candidate.EpisodeCohesionIdentity ??
                            candidate.Candidate.EventNeighborhoodId,
                        previous.Candidate.EpisodeCohesionIdentity ??
                            previous.Candidate.EventNeighborhoodId,
                        StringComparison.Ordinal));
            if (index < 0)
            {
                index = 0;
            }
            previous = remaining[index];
            ordered.Add(previous);
            remaining.RemoveAt(index);
        }

        GenerationHiddenMoment[] hidden = ordered.Select((entry, index) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            string id = MomentStableId.Create(
                "g",
                Path.GetFullPath(entry.Source.PreparedSource.Media.FullPath)
                    .ToUpperInvariant(),
                entry.Candidate.Id);
            return new GenerationHiddenMoment(
                id,
                index + 1,
                entry.SourceOrder,
                entry.Source,
                entry.Candidate,
                entry.FinalScore,
                moments.Request.Setup.QualityThreshold,
                MapReason(entry.Candidate, entry.FinalScore,
                    moments.Request.Setup.QualityThreshold,
                    moments.Request.Setup.ResultCountMode ==
                        GenerationResultCountMode.Exact &&
                    moments.SelectedCandidates.Count >=
                        moments.Request.Setup.DesiredResultCount),
                Explain(entry),
                GenerationClipPreferenceFeatureExtractor.Create(
                    entry.Candidate,
                    entry.Refinement),
                editorialPreference:
                    moments.Request.Setup.AnalysisDepth ==
                    GenerationAnalysisDepth.Thorough
                        ? ClipEditorialGenerationPreference.AiRequired
                        : ClipEditorialGenerationPreference.HeuristicOnly,
                captionSourceSelection:
                    moments.Request.Setup.CaptionSettings.IsEnabled
                        ? moments.Request.Setup.CaptionSettings.FindForSource(
                            entry.Source.PreparedSource.Media.FullPath)
                        : null,
                captionStyle:
                    moments.Request.Setup.CaptionSettings.IsEnabled
                        ? moments.Request.Setup.CaptionSettings.Style
                        : null);
        }).ToArray();

        return new GenerationHiddenMomentDeck(moments, hidden);
    }

    private static bool SameSelectedEpisode(
        CandidateEntry entry,
        GenerationMomentFindingResult moments)
    {
        string? identity = entry.Candidate.EpisodeCohesionIdentity;
        return identity is not null && moments.SelectedCandidates.Any(selected =>
            ReferenceEquals(selected.AnalyzedSource, entry.Source) &&
            selected.Candidate.EpisodeCohesionIdentity == identity);
    }

    private static GenerationHiddenMomentReason MapReason(
        MomentCandidate candidate,
        double finalScore,
        double qualityTarget,
        bool requestedCountReached) => candidate.Disposition switch
        {
            MomentCandidateDisposition.SuppressedOverlap =>
                GenerationHiddenMomentReason.OverlapSuppressed,
            MomentCandidateDisposition.SuppressedEpisode or
            MomentCandidateDisposition.SuppressedSubepisode or
            MomentCandidateDisposition.SuppressedNeighborhood =>
                GenerationHiddenMomentReason.SameEventSuppressed,
            MomentCandidateDisposition.SuppressedCooldown =>
                GenerationHiddenMomentReason.CooldownSuppressed,
            _ when finalScore < qualityTarget =>
                GenerationHiddenMomentReason.BelowQualityTarget,
            _ when requestedCountReached =>
                GenerationHiddenMomentReason.RequestedCountReached,
            _ => GenerationHiddenMomentReason.PortfolioNotSelected,
        };

    private static string Explain(CandidateEntry entry) =>
        entry.Refinement?.Components
            .OrderByDescending(static value => value.SignedContribution)
            .Select(static value => value.Explanation)
            .FirstOrDefault() ??
        entry.Candidate.Score.Components
            .OrderByDescending(static value => value.SignedContribution)
            .Select(static value => value.Explanation)
            .FirstOrDefault() ??
        "Retained as a safe alternate from deterministic evidence.";

    private sealed record CandidateEntry(
        AnalyzedGenerationSource Source,
        int SourceOrder,
        MomentCandidate Candidate,
        GenerationCandidateRefinement? Refinement)
    {
        public double FinalScore =>
            Refinement?.FinalScore ?? Candidate.HeuristicScore;
    }
}
