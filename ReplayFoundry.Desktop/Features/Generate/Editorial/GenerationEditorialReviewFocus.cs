using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Editorial;

internal static class GenerationEditorialReviewFocus
{
    public static TimeSpan Resolve(
        MomentCandidate candidate,
        GenerationVisualSemanticCandidateObservation? observation)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        TimeSpan deterministic = DeterministicFocus(candidate);
        if (observation is null ||
            observation.Observation.EvidenceIntervals.Count == 0)
        {
            return deterministic;
        }

        HashSet<string> observedChangeIntervals = observation.Observation
            .ObservedChanges
            .SelectMany(static change => change.EvidenceIntervalIds)
            .ToHashSet(StringComparer.Ordinal);
        IEnumerable<VisualSemanticEditorialEvidenceInterval> intervals =
            observation.Observation.EvidenceIntervals.Where(interval =>
                interval.EvidenceBasis is
                    VisualSemanticEvidenceBasis.Visual or
                    VisualSemanticEvidenceBasis.Both &&
                (observedChangeIntervals.Count == 0 ||
                 observedChangeIntervals.Contains(interval.Id)));
        TimeSpan? selected = intervals
            .Select(interval => SourceMidpoint(observation, interval))
            .Where(value =>
                value >= candidate.Window.Start &&
                value < candidate.Window.End)
            .OrderBy(value => Math.Abs((value - deterministic).Ticks))
            .ThenBy(static value => value)
            .Cast<TimeSpan?>()
            .FirstOrDefault();
        return selected ?? deterministic;
    }

    private static TimeSpan DeterministicFocus(MomentCandidate candidate)
    {
        TimeSpan focus = candidate.Episode?.PrimaryPeakTimestamp ??
            candidate.EventNeighborhood.PeakTimestamp;
        return focus >= candidate.Window.Start && focus < candidate.Window.End
            ? focus
            : TimeSpan.FromTicks(
                candidate.Window.Start.Ticks +
                candidate.Window.Duration.Ticks / 2);
    }

    private static TimeSpan SourceMidpoint(
        GenerationVisualSemanticCandidateObservation observation,
        VisualSemanticEditorialEvidenceInterval interval) =>
        observation.ReviewedSourceStart + TimeSpan.FromTicks(
            interval.Start.Ticks + (interval.End - interval.Start).Ticks / 2);
}
