using System.Diagnostics;
using System.IO;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Media.Moments;

internal static class DeterministicMomentSelection
{
    internal static MomentCandidate[] Select(
        List<MomentCandidate> candidates,
        MediaMomentFindingOptions options,
        CancellationToken cancellationToken)
    {
        MomentCandidate[] ordered =
            candidates
                .Where(
                    static candidate =>
                        candidate.Disposition ==
                        MomentCandidateDisposition.Eligible)
                .OrderByDescending(
                    static candidate =>
                        candidate.Score.RawComponentTotal)
                .ThenByDescending(
                    static candidate =>
                        candidate.HeuristicScore)
                .ThenBy(
                    static candidate =>
                        candidate.Window.Start)
                .ThenBy(
                    static candidate =>
                        candidate.Window.End)
                .ThenBy(
                    static candidate =>
                        candidate.Id,
                    StringComparer.Ordinal)
                .ToArray();

        var selected =
            new List<MomentCandidate>();

        foreach (MomentCandidate candidate in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int candidateIndex =
                candidates.FindIndex(
                    item =>
                        item.Id == candidate.Id);

            MomentCandidate[] overlappingSelected =
                selected
                    .Where(
                        existing =>
                            MomentIntervalMath.PairOverlapRatio(
                                existing.Window,
                                candidate.Window) >=
                            options.CandidateOverlapSuppressionRatio)
                    .ToArray();
            bool overlaps =
                overlappingSelected.Length > 0;

            bool sameEpisode =
                selected.Any(
                    existing =>
                        candidate.EpisodeId is not null &&
                        string.Equals(
                            existing.EpisodeId,
                            candidate.EpisodeId,
                            StringComparison.Ordinal));

            bool montageCooldown =
                options.OutputKind ==
                    MomentOutputKind.MontageSegment &&
                selected.Any(
                    existing =>
                        (Midpoint(existing.Window) -
                         Midpoint(candidate.Window)).Duration() <
                            options.CalibrationPolicy.MontageMinimumCooldown &&
                        !(
                            existing.Episode?.ParentEpisodeId is not null &&
                            string.Equals(
                                existing.Episode.ParentEpisodeId,
                                candidate.Episode?.ParentEpisodeId,
                                StringComparison.Ordinal)) &&
                        (existing.EpisodeId is not null &&
                         candidate.EpisodeId is not null &&
                         SharedAnchorRatio(existing, candidate) > 0));

            if (sameEpisode)
            {
                candidates[candidateIndex] =
                    candidate.WithDisposition(
                        MomentCandidateDisposition.SuppressedEpisode);
                continue;
            }

            if (montageCooldown)
            {
                candidates[candidateIndex] =
                    candidate.WithDisposition(
                        MomentCandidateDisposition.SuppressedCooldown);
                continue;
            }

            if (overlaps)
            {
                if (options.OutputKind ==
                        MomentOutputKind.StandaloneClip &&
                    overlappingSelected.All(
                        existing =>
                            PreferStandaloneOverlapRepresentative(
                                candidate,
                                existing)))
                {
                    int insertionIndex =
                        overlappingSelected
                            .Select(
                                existing =>
                                    selected.IndexOf(existing))
                            .Min();

                    foreach (MomentCandidate existing in
                             overlappingSelected)
                    {
                        int existingIndex =
                            candidates.FindIndex(
                                item =>
                                    item.Id == existing.Id);
                        candidates[existingIndex] =
                            existing.WithDisposition(
                                MomentCandidateDisposition
                                    .SuppressedOverlap);
                        selected.Remove(existing);
                    }

                    MomentCandidate replacement =
                        candidate.WithDisposition(
                            MomentCandidateDisposition.Selected);
                    candidates[candidateIndex] = replacement;
                    selected.Insert(
                        Math.Min(insertionIndex, selected.Count),
                        replacement);
                    continue;
                }

                candidates[candidateIndex] =
                    candidate.WithDisposition(
                        MomentCandidateDisposition.SuppressedOverlap);
                continue;
            }

            if (selected.Count >=
                options.DesiredCandidateCount)
            {
                continue;
            }

            MomentCandidate chosen =
                candidate.WithDisposition(
                    MomentCandidateDisposition.Selected);
            candidates[candidateIndex] = chosen;
            selected.Add(chosen);
        }

        return selected.ToArray();
    }

    private static bool PreferStandaloneOverlapRepresentative(
        MomentCandidate challenger,
        MomentCandidate incumbent)
    {
        double challengerRanking =
            challenger.StandaloneFeatures?.RankingValue ?? 0;
        double incumbentRanking =
            incumbent.StandaloneFeatures?.RankingValue ?? 0;
        int comparison =
            challengerRanking.CompareTo(incumbentRanking);
        if (comparison != 0)
        {
            return comparison > 0;
        }

        comparison =
            (challenger.EpisodeFeatures?.Distinctiveness ?? 0)
            .CompareTo(
                incumbent.EpisodeFeatures?.Distinctiveness ?? 0);
        if (comparison != 0)
        {
            return comparison > 0;
        }

        comparison =
            (challenger.Episode?.PeakActivation ?? 0)
            .CompareTo(
                incumbent.Episode?.PeakActivation ?? 0);
        if (comparison != 0)
        {
            return comparison > 0;
        }

        comparison =
            challenger.Score.RawComponentTotal.CompareTo(
                incumbent.Score.RawComponentTotal);
        if (comparison != 0)
        {
            return comparison > 0;
        }

        comparison =
            challenger.HeuristicScore.CompareTo(
                incumbent.HeuristicScore);
        if (comparison != 0)
        {
            return comparison > 0;
        }

        return string.CompareOrdinal(
                   challenger.Id,
                   incumbent.Id) < 0;
    }

    private static TimeSpan Midpoint(
        MomentCandidateWindow window) =>
        TimeSpan.FromTicks(
            window.Start.Ticks +
            (window.Duration.Ticks / 2));

    private static double SharedAnchorRatio(
        MomentCandidate left,
        MomentCandidate right)
    {
        int denominator =
            Math.Max(left.Anchors.Count, right.Anchors.Count);
        if (denominator == 0)
        {
            return 0;
        }

        int shared =
            left.Anchors
                .Select(static anchor => anchor.Id)
                .Intersect(
                    right.Anchors.Select(static anchor => anchor.Id),
                    StringComparer.Ordinal)
                .Count();
        return shared / (double)denominator;
    }
}
