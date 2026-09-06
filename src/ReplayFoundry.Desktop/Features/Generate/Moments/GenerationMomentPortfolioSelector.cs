using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Guidance;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using System.IO;

namespace ReplayFoundry.Desktop.Features.Generate.Moments;

public sealed class GenerationMomentPortfolioSelector
{
    public IReadOnlyList<GenerationMomentCandidate> Select(
        GenerationMomentFindingRequest request,
        IReadOnlyList<GenerationSourceMomentResult> sourceResults,
        CancellationToken cancellationToken = default)
        => SelectCore(request, sourceResults, null, null, null, cancellationToken);

    public IReadOnlyList<GenerationMomentCandidate> Select(
        GenerationMomentFindingRequest request,
        IReadOnlyList<GenerationSourceMomentResult> sourceResults,
        IReadOnlyDictionary<MomentCandidate, GenerationCandidateRefinement>
            refinements,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refinements);
        return SelectCore(request, sourceResults, refinements, null, null, cancellationToken);
    }

    internal IReadOnlyList<GenerationMomentCandidate> SelectEligible(
        GenerationMomentFindingRequest request, IReadOnlyList<GenerationSourceMomentResult> sourceResults,
        IReadOnlyDictionary<MomentCandidate, GenerationCandidateRefinement> refinements,
        IReadOnlySet<MomentCandidate> eligibleCandidates,
        IReadOnlyDictionary<MomentCandidate, double> selectionPreferences, CancellationToken cancellationToken) =>
        SelectCore(request, sourceResults, refinements, eligibleCandidates, selectionPreferences, cancellationToken);

    private static IReadOnlyList<GenerationMomentCandidate> SelectCore(
        GenerationMomentFindingRequest request,
        IReadOnlyList<GenerationSourceMomentResult> sourceResults,
        IReadOnlyDictionary<MomentCandidate, GenerationCandidateRefinement>?
            refinements,
        IReadOnlySet<MomentCandidate>? eligibleCandidates,
        IReadOnlyDictionary<MomentCandidate, double>? selectionPreferences,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceResults);

        var ranked =
            sourceResults
                .SelectMany(
                    (source, sourceOrder) =>
                        source.Moments.Proposals
                            .Where(candidate => eligibleCandidates is null || eligibleCandidates.Contains(candidate))
                            .Where(
                                static candidate =>
                                    candidate.Disposition is not
                                        (MomentCandidateDisposition.RejectedBlack or
                                         MomentCandidateDisposition.RejectedFreeze))
                            .Select(
                                candidate =>
                                    new PortfolioEntry(
                                        source,
                                        sourceOrder,
                                        candidate,
                                        refinements is not null &&
                                        refinements.TryGetValue(candidate, out GenerationCandidateRefinement? refinement)
                                            ? refinement
                                            : null)))
                .OrderByDescending(
                    static entry =>
                        entry.Refinement?.RankingScore ??
                        entry.Candidate.Score.RawComponentTotal)
                .ThenByDescending(
                    static entry =>
                        entry.Candidate.Score.RawComponentTotal)
                .ThenByDescending(
                    static entry =>
                        entry.Candidate.HeuristicScore)
                .ThenBy(
                    static entry =>
                        entry.SourceOrder)
                .ThenBy(
                    static entry =>
                        entry.Candidate.Window.Start)
                .ThenBy(
                    static entry =>
                        entry.Candidate.Window.End)
                .ThenBy(
                    static entry =>
                        entry.Candidate.Id,
                    StringComparer.Ordinal)
                .ToArray();

        var selected =
            new List<SelectedPortfolioEntry>();

        foreach (UserMomentGuidance guidance in request.Setup.MomentGuidance.Items
                     .OrderByDescending(static item => item.ReservesCandidateSearch)
                     .ThenBy(static item => item.SourceFullPath, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static item => item.Start)
                     .ThenBy(static item => item.Id, StringComparer.Ordinal))
        {
            IEnumerable<PortfolioEntry> matches = ranked
                .Where(entry => MatchesGuidance(entry, guidance))
                .OrderByDescending(entry => GuidanceMatchStrength(entry, guidance))
                .ThenByDescending(static entry =>
                    entry.Refinement?.RankingScore ??
                    entry.Candidate.Score.RawComponentTotal)
                .ThenByDescending(static entry => entry.Candidate.Score.RawComponentTotal)
                .ThenByDescending(static entry => entry.Candidate.HeuristicScore)
                .ThenBy(static entry => entry.SourceOrder)
                .ThenBy(static entry => entry.Candidate.Window.Start)
                .ThenBy(static entry => entry.Candidate.Id, StringComparer.Ordinal);
            SelectPass(
                matches,
                guidance.ReservesCandidateSearch
                    ? GenerationCandidateSelectionReason.UserReservedRange
                    : GenerationCandidateSelectionReason.UserPriority,
                requireDiversity: false,
                maximumSelections: 1);
        }

        SelectPass(
            ranked.Where(
                entry => IsAutomaticEndingSafe(entry) &&
                         IsQualityQualified(entry, request)),
            GenerationCandidateSelectionReason.QualityQualified,
            requireDiversity: true);

        if (request.Setup.ClipFulfillmentPreference ==
            ClipFulfillmentPreference.FillRequestedCount)
        {
            SelectPass(
                ranked.Where(
                    entry => IsAutomaticEndingSafe(entry) &&
                             IsBelowQualityTarget(entry, request)),
                GenerationCandidateSelectionReason
                    .CountFillBelowQualityTarget,
                requireDiversity: true);

            SelectPass(
                ranked.Where(IsAutomaticEndingSafe),
                GenerationCandidateSelectionReason
                    .CountFillRelaxedDiversity,
                requireDiversity: false);
        }

        EnsureGameplayEventCoverage();

        return selected
            .Select(
                (entry, index) =>
                    new GenerationMomentCandidate(
                        MomentStableId.Create(
                            "g",
                            Path.GetFullPath(
                                entry.Entry.Source.AnalyzedSource
                                    .PreparedSource.Media.FullPath)
                                .ToUpperInvariant(),
                            entry.Entry.Candidate.Id),
                        entry.Entry.Source.AnalyzedSource,
                        entry.Entry.Candidate,
                        entry.Entry.SourceOrder,
                        index + 1,
                        entry.Reason,
                        entry.Entry.Refinement))
            .ToArray();

        void SelectPass(
            IEnumerable<PortfolioEntry> entries,
            GenerationCandidateSelectionReason reason,
            bool requireDiversity,
            int maximumSelections = int.MaxValue)
        {
            int added = 0;
            var remaining = entries.ToList();
            while (remaining.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // A candidate contributes only the part of its footage that
                // the current portfolio has not already shown. Recompute
                // after each pick so two nearly identical cuts cannot beat a
                // comparably strong, independent moment just below a hard
                // overlap threshold.
                PortfolioEntry entry = requireDiversity
                    ? remaining.OrderByDescending(value => MarginalScore(value, selected,
                        reason == GenerationCandidateSelectionReason.QualityQualified &&
                        selectionPreferences is not null && selectionPreferences.TryGetValue(value.Candidate, out double preference) ? preference : 0))
                        .ThenBy(value => remaining.IndexOf(value)).First()
                    : remaining[0];
                remaining.Remove(entry);

                if (selected.Count >=
                    request.Setup.DesiredResultCount)
                {
                    return;
                }

                if (selected.Any(
                        item =>
                            item.Entry.SourceOrder == entry.SourceOrder &&
                            string.Equals(
                                item.Entry.Candidate.Id,
                                entry.Candidate.Id,
                                StringComparison.Ordinal)))
                {
                    continue;
                }

                if (requireDiversity &&
                    !IsDiverse(
                        entry,
                        selected.Select(static item => item.Entry),
                        request.Settings.Options))
                {
                    continue;
                }

                selected.Add(
                    new SelectedPortfolioEntry(
                        entry,
                        reason));
                added++;
                if (added >= maximumSelections)
                {
                    return;
                }
            }
        }

        void EnsureGameplayEventCoverage()
        {
            if (request.Setup.DesiredResultCount < 3 ||
                selected.Count < 3 ||
                request.Setup.ContentEmphasis ==
                    ContentEmphasis.CommentaryFocused)
            {
                return;
            }

            bool IsGameplayEvent(PortfolioEntry entry) =>
                GenerationGameplayEventCoveragePolicy
                    .HasQualifiedVisualAction(entry.Refinement) ||
                (!GenerationGameplayEventCoveragePolicy.HasVisualReview(
                     entry.Refinement) &&
                 GenerationGameplayEventCoveragePolicy
                     .IsDeterministicGameplayEvent(entry.Candidate));

            if (selected.Any(item => IsGameplayEvent(item.Entry)))
            {
                return;
            }

            SelectedPortfolioEntry? replaceable = selected
                .Where(static item => item.Reason is not
                    (GenerationCandidateSelectionReason.UserReservedRange or
                     GenerationCandidateSelectionReason.UserPriority))
                .OrderBy(static item =>
                    item.Entry.Refinement?.RankingScore ??
                    item.Entry.Candidate.Score.RawComponentTotal)
                .ThenByDescending(static item => item.Entry.SourceOrder)
                .ThenByDescending(static item => item.Entry.Candidate.Window.Start)
                .FirstOrDefault();
            if (replaceable is null)
            {
                return;
            }

            double replaceableScore =
                replaceable.Entry.Refinement?.RankingScore ??
                replaceable.Entry.Candidate.Score.RawComponentTotal;
            PortfolioEntry[] remaining = selected
                .Where(item => !ReferenceEquals(item, replaceable))
                .Select(static item => item.Entry)
                .ToArray();
            PortfolioEntry? challenger = ranked
                .Where(entry =>
                    !selected.Any(item =>
                        item.Entry.SourceOrder == entry.SourceOrder &&
                        string.Equals(
                            item.Entry.Candidate.Id,
                            entry.Candidate.Id,
                            StringComparison.Ordinal)) &&
                    IsAutomaticEndingSafe(entry) &&
                    IsQualityQualified(entry, request) &&
                    IsGameplayEvent(entry) &&
                    (entry.Refinement?.RankingScore ??
                        entry.Candidate.Score.RawComponentTotal) >=
                        replaceableScore -
                        GenerationGameplayEventCoveragePolicy
                            .MaximumRankingTradeoff &&
                    IsDiverse(
                        entry,
                        remaining,
                        request.Settings.Options))
                .OrderByDescending(entry =>
                    GenerationGameplayEventCoveragePolicy
                        .HasQualifiedVisualAction(entry.Refinement))
                .ThenByDescending(entry =>
                    GenerationGameplayEventCoveragePolicy.Strength(
                        entry.Candidate))
                .ThenByDescending(static entry =>
                    entry.Refinement?.RankingScore ??
                    entry.Candidate.Score.RawComponentTotal)
                .FirstOrDefault();
            if (challenger is null)
            {
                return;
            }

            int replacementIndex = selected.IndexOf(replaceable);
            selected[replacementIndex] = new SelectedPortfolioEntry(
                challenger,
                GenerationCandidateSelectionReason
                    .QualityQualifiedGameplayEventCoverage);
        }
    }

    private sealed record PortfolioEntry(
        GenerationSourceMomentResult Source,
        int SourceOrder,
        MomentCandidate Candidate,
        GenerationCandidateRefinement? Refinement);

    private sealed record SelectedPortfolioEntry(
        PortfolioEntry Entry,
        GenerationCandidateSelectionReason Reason);

    private static bool MatchesGuidance(
        PortfolioEntry entry,
        UserMomentGuidance guidance)
    {
        if (!string.Equals(
                entry.Source.AnalyzedSource.PreparedSource.Media.FullPath,
                guidance.SourceFullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        MomentCandidateWindow window = entry.Candidate.Window;
        return guidance.Kind == UserMomentGuidanceKind.PriorityPoint
            ? window.Contains(guidance.Timestamp)
            : window.Start < guidance.End && window.End > guidance.Start;
    }

    private static bool IsQualityQualified(
        PortfolioEntry entry,
        GenerationMomentFindingRequest request) =>
        entry.Refinement is null
            ? entry.Candidate.Disposition is
                MomentCandidateDisposition.Eligible or
                MomentCandidateDisposition.Selected
            : entry.Refinement.FinalScore >= request.Setup.QualityThreshold;

    private static bool IsBelowQualityTarget(
        PortfolioEntry entry,
        GenerationMomentFindingRequest request) =>
        entry.Refinement is null
            ? entry.Candidate.Disposition == MomentCandidateDisposition.BelowThreshold
            : entry.Refinement.FinalScore < request.Setup.QualityThreshold;

    private static bool IsAutomaticEndingSafe(PortfolioEntry entry) =>
        GenerationAutomaticCandidateEligibility.IsEligible(entry.Candidate, entry.Refinement);

    private static double GuidanceMatchStrength(
        PortfolioEntry entry,
        UserMomentGuidance guidance)
    {
        MomentCandidateWindow window = entry.Candidate.Window;
        if (guidance.Kind == UserMomentGuidanceKind.PriorityPoint)
        {
            double distance = Math.Abs(
                (Midpoint(window) - guidance.Timestamp).TotalSeconds);
            return 2 + 1 / (1 + distance);
        }
        TimeSpan overlapStart = window.Start > guidance.Start
            ? window.Start
            : guidance.Start;
        TimeSpan overlapEnd = window.End < guidance.End
            ? window.End
            : guidance.End;
        double overlap = Math.Max(0, (overlapEnd - overlapStart).TotalSeconds);
        double ratio = overlap / Math.Max(0.001, window.Duration.TotalSeconds);
        bool contained = window.Start >= guidance.Start && window.End <= guidance.End;
        return (contained ? 2 : 1) + ratio;
    }

    private static bool IsDiverse(
        PortfolioEntry candidate,
        IEnumerable<PortfolioEntry> selected,
        MediaMomentFindingOptions options)
    {
        PortfolioEntry[] sameSource =
            selected
                .Where(
                    existing =>
                        existing.SourceOrder ==
                        candidate.SourceOrder)
                .ToArray();

        bool overlaps =
            sameSource.Any(
                existing =>
                    MomentIntervalMath.PairOverlapRatio(
                        existing.Candidate.Window,
                        candidate.Candidate.Window) >=
                    options.CandidateOverlapSuppressionRatio);
        bool sameEpisode =
            EpisodeDiversityIdentity(candidate.Candidate, options.OutputKind) is not null &&
            sameSource.Any(
                existing =>
                    string.Equals(
                        EpisodeDiversityIdentity(existing.Candidate, options.OutputKind),
                        EpisodeDiversityIdentity(candidate.Candidate, options.OutputKind),
                        StringComparison.Ordinal));
        bool montageCooldown =
            options.OutputKind ==
                MomentOutputKind.MontageSegment &&
            sameSource.Any(
                existing =>
                    (Midpoint(existing.Candidate.Window) -
                     Midpoint(candidate.Candidate.Window)).Duration() <
                    options.CalibrationPolicy.MontageMinimumCooldown &&
                    !(
                        existing.Candidate.Episode?.ParentEpisodeId is not null &&
                        string.Equals(
                            existing.Candidate.Episode.ParentEpisodeId,
                            candidate.Candidate.Episode?.ParentEpisodeId,
                            StringComparison.Ordinal)));

        return !overlaps &&
               !sameEpisode &&
               !montageCooldown;
    }

    private static TimeSpan Midpoint(MomentCandidateWindow window) =>
        TimeSpan.FromTicks(
            window.Start.Ticks +
            window.Duration.Ticks / 2);

    private static double MarginalScore(PortfolioEntry candidate, IEnumerable<SelectedPortfolioEntry> selected, double selectionPreference = 0)
    {
        var overlap = selected.Where(value => value.Entry.SourceOrder == candidate.SourceOrder)
            .Select(value => (Start: Math.Max(candidate.Candidate.Window.Start.Ticks, value.Entry.Candidate.Window.Start.Ticks),
                End: Math.Min(candidate.Candidate.Window.End.Ticks, value.Entry.Candidate.Window.End.Ticks)))
            .Where(static range => range.End > range.Start).OrderBy(static range => range.Start).ToArray();
        long covered = 0;
        long lastEnd = 0;
        foreach (var range in overlap)
        {
            covered += Math.Max(0, range.End - Math.Max(lastEnd, range.Start));
            lastEnd = Math.Max(lastEnd, range.End);
        }
        double novelty = 1 - Math.Clamp(covered / (double)candidate.Candidate.Window.Duration.Ticks, 0, 1);
        return (Math.Max(0, candidate.Refinement?.RankingScore ?? candidate.Candidate.Score.RawComponentTotal) + selectionPreference) * novelty;
    }

    private static string? EpisodeDiversityIdentity(MomentCandidate candidate, MomentOutputKind kind) =>
        kind == MomentOutputKind.StandaloneClip
            ? candidate.EpisodeCohesionIdentity ?? candidate.EpisodeId
            : candidate.EpisodeId;
}
