using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class MomentCandidate
{
    private readonly ReadOnlyCollection<MomentAnchor> _anchors;
    private readonly ReadOnlyCollection<MomentEvidenceReference>
        _integrityEvidenceReferences;

    public MomentCandidate(
        string id,
        MomentCandidateWindow window,
        MomentCandidateConstructionReason constructionReason,
        MomentEventNeighborhood eventNeighborhood,
        IEnumerable<MomentAnchor> anchors,
        MomentScore score,
        MomentCandidateDisposition disposition,
        double fullFrameBlackOverlapRatio,
        double fullFrameFreezeOverlapRatio,
        double gameplayBlackOverlapRatio,
        double gameplayFreezeOverlapRatio,
        IEnumerable<MomentEvidenceReference>? integrityEvidenceReferences = null,
        MomentEventEpisode? episode = null,
        MomentWindowContextAllocation? contextAllocation = null,
        MontageSegmentObjective? montageObjective = null,
        MontageSegmentSelectionReason? montageSelectionReason = null,
        MomentEpisodeFeatureVector? episodeFeatures = null,
        StandaloneEpisodeFeatureVector? standaloneFeatures = null,
        MontageSegmentFeatureVector? montageFeatures = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "A moment candidate requires a stable identifier.",
                nameof(id));
        }

        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(eventNeighborhood);
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(score);

        if (!Enum.IsDefined(constructionReason))
        {
            throw new ArgumentOutOfRangeException(nameof(constructionReason));
        }

        if (!Enum.IsDefined(disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }

        if (montageSelectionReason is not null &&
            !Enum.IsDefined(montageSelectionReason.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(montageSelectionReason));
        }

        if (episode is not null &&
            (episode.Start > window.End || episode.End < window.Start))
        {
            throw new ArgumentException(
                "A candidate window must intersect its event episode.",
                nameof(episode));
        }

        if ((montageObjective is null) != (montageSelectionReason is null))
        {
            throw new ArgumentException(
                "Montage objective and selection reason must be supplied together.");
        }

        if (episode is null &&
            (
                episodeFeatures is not null ||
                standaloneFeatures is not null ||
                montageFeatures is not null
            ))
        {
            throw new ArgumentException(
                "Mode-specific feature vectors require an event episode.");
        }

        if (standaloneFeatures is not null &&
            montageFeatures is not null)
        {
            throw new ArgumentException(
                "A candidate cannot carry both Standalone and Montage ranking features.");
        }

        MomentAnchor[] anchorSnapshot =
            anchors
                .OrderBy(static anchor => anchor.Timestamp)
                .ThenBy(static anchor => anchor.Kind)
                .ThenBy(static anchor => anchor.Id, StringComparer.Ordinal)
                .ToArray();

        if (anchorSnapshot.Length == 0 ||
            anchorSnapshot.Any(static anchor => anchor is null))
        {
            throw new ArgumentException(
                "A moment candidate requires at least one anchor.",
                nameof(anchors));
        }

        if (anchorSnapshot.Any(
                anchor =>
                    !window.Contains(anchor.Timestamp)))
        {
            throw new ArgumentException(
                "Every candidate anchor must remain inside its window.",
                nameof(anchors));
        }

        if (eventNeighborhood.Start > window.End ||
            eventNeighborhood.End < window.Start ||
            !anchorSnapshot
                .Select(static anchor => anchor.Id)
                .SequenceEqual(
                    eventNeighborhood.Anchors.Select(static anchor => anchor.Id),
                    StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "A candidate must preserve its intersecting event neighborhood and anchors.",
                nameof(eventNeighborhood));
        }

        MomentEvidenceReference[] integritySnapshot =
            integrityEvidenceReferences?.ToArray() ??
            [];

        if (integritySnapshot.Any(static reference => reference is null))
        {
            throw new ArgumentException(
                "Integrity evidence references cannot contain null entries.",
                nameof(integrityEvidenceReferences));
        }

        ValidateRatio(fullFrameBlackOverlapRatio, nameof(fullFrameBlackOverlapRatio));
        ValidateRatio(fullFrameFreezeOverlapRatio, nameof(fullFrameFreezeOverlapRatio));
        ValidateRatio(gameplayBlackOverlapRatio, nameof(gameplayBlackOverlapRatio));
        ValidateRatio(gameplayFreezeOverlapRatio, nameof(gameplayFreezeOverlapRatio));

        Id = id.Trim();
        Window = window;
        ConstructionReason = constructionReason;
        EventNeighborhood = eventNeighborhood;
        Score = score;
        Disposition = disposition;
        FullFrameBlackOverlapRatio = fullFrameBlackOverlapRatio;
        FullFrameFreezeOverlapRatio = fullFrameFreezeOverlapRatio;
        GameplayBlackOverlapRatio = gameplayBlackOverlapRatio;
        GameplayFreezeOverlapRatio = gameplayFreezeOverlapRatio;
        _anchors = Array.AsReadOnly(anchorSnapshot);
        _integrityEvidenceReferences =
            Array.AsReadOnly(integritySnapshot);
        Episode = episode;
        ContextAllocation = contextAllocation;
        MontageObjective = montageObjective;
        MontageSelectionReason = montageSelectionReason;
        EpisodeFeatures = episodeFeatures;
        StandaloneFeatures = standaloneFeatures;
        MontageFeatures = montageFeatures;
    }

    public string Id { get; }
    public MomentCandidateWindow Window { get; }
    public MomentCandidateConstructionReason ConstructionReason { get; }
    public MomentEventNeighborhood EventNeighborhood { get; }
    public string EventNeighborhoodId => EventNeighborhood.Id;
    public MomentEventEpisode? Episode { get; }
    public string? EpisodeId => Episode?.Id;
    public string? EpisodeCohesionIdentity => Episode?.CohesionIdentity;
    public MomentWindowContextAllocation? ContextAllocation { get; }
    public MontageSegmentObjective? MontageObjective { get; }
    public MontageSegmentSelectionReason? MontageSelectionReason { get; }
    public MomentEpisodeFeatureVector? EpisodeFeatures { get; }
    public StandaloneEpisodeFeatureVector? StandaloneFeatures { get; }
    public MontageSegmentFeatureVector? MontageFeatures { get; }
    public IReadOnlyList<MomentAnchor> Anchors => _anchors;
    public MomentScore Score { get; }
    public double HeuristicScore => Score.HeuristicScore;
    public MomentCandidateDisposition Disposition { get; }
    public double FullFrameBlackOverlapRatio { get; }
    public double FullFrameFreezeOverlapRatio { get; }
    public double GameplayBlackOverlapRatio { get; }
    public double GameplayFreezeOverlapRatio { get; }
    public IReadOnlyList<MomentEvidenceReference>
        IntegrityEvidenceReferences =>
        _integrityEvidenceReferences;

    internal MomentCandidate WithDisposition(
        MomentCandidateDisposition disposition) =>
        new(
            Id,
            Window,
            ConstructionReason,
            EventNeighborhood,
            Anchors,
            Score,
            disposition,
            FullFrameBlackOverlapRatio,
            FullFrameFreezeOverlapRatio,
            GameplayBlackOverlapRatio,
            GameplayFreezeOverlapRatio,
            IntegrityEvidenceReferences,
            Episode,
            ContextAllocation,
            MontageObjective,
            MontageSelectionReason,
            EpisodeFeatures,
            StandaloneFeatures,
            MontageFeatures);

    private static void ValidateRatio(double value, string parameterName)
    {
        if (!double.IsFinite(value) ||
            value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
