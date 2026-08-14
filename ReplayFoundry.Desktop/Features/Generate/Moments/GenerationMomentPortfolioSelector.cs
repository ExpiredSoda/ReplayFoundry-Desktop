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
        => SelectCore(request, sourceResults, null, cancellationToken);

    public IReadOnlyList<GenerationMomentCandidate> Select(
        GenerationMomentFindingRequest request,
        IReadOnlyList<GenerationSourceMomentResult> sourceResults,
        IReadOnlyDictionary<MomentCandidate, GenerationCandidateRefinement>
            refinements,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refinements);
        return SelectCore(request, sourceResults, refinements, cancellationToken);
    }

    private static IReadOnlyList<GenerationMomentCandidate> SelectCore(
        GenerationMomentFindingRequest request,
        IReadOnlyList<GenerationSourceMomentResult> sourceResults,
        IReadOnlyDictionary<MomentCandidate, GenerationCandidateRefinement>?
            refinements,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceResults);

        var ranked =
            sourceResults
                .SelectMany(
                    (source, sourceOrder) =>
                        source.Moments.Proposals
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
                        entry.Refinement?.FinalScore ??
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
                    entry.Refinement?.FinalScore ??
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
                entry => IsQualityQualified(entry, request)),
            GenerationCandidateSelectionReason.QualityQualified,
            requireDiversity: true);

        if (request.Setup.ClipFulfillmentPreference ==
            ClipFulfillmentPreference.FillRequestedCount)
        {
            SelectPass(
                ranked.Where(
                    entry => IsBelowQualityTarget(entry, request)),
                GenerationCandidateSelectionReason
                    .CountFillBelowQualityTarget,
                requireDiversity: true);

            SelectPass(
                ranked,
                GenerationCandidateSelectionReason
                    .CountFillRelaxedDiversity,
                requireDiversity: false);
        }

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
            foreach (PortfolioEntry entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

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
            candidate.Candidate.EpisodeId is not null &&
            sameSource.Any(
                existing =>
                    string.Equals(
                        existing.Candidate.EpisodeId,
                        candidate.Candidate.EpisodeId,
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
}
