using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Media.Moments;

internal sealed record AttributedGameplaySceneBoundary(
    VisualTargetEvidenceResult Result,
    SceneBoundary Boundary);

internal static class MomentGameplaySceneBoundaryCollector
{
    private static readonly TimeSpan DuplicateTolerance =
        TimeSpan.FromMilliseconds(50);

    public static IReadOnlyList<AttributedGameplaySceneBoundary> Collect(
        MediaEvidenceResult evidence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        AttributedGameplaySceneBoundary[] ordered =
            evidence.RegionVisualResults
                .Where(
                    static result =>
                        result.Target.Role ==
                        CompositionRegionRole.Gameplay)
                .SelectMany(
                    result =>
                        result.SceneBoundaries.Select(
                            boundary =>
                                new AttributedGameplaySceneBoundary(
                                    result,
                                    boundary)))
                .OrderBy(
                    static item =>
                        item.Boundary.Timestamp)
                .ThenBy(
                    static item =>
                        item.Result.Target.TargetKey,
                    StringComparer.Ordinal)
                .ToArray();

        var unique =
            new List<AttributedGameplaySceneBoundary>();

        foreach (AttributedGameplaySceneBoundary item in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (unique.Count == 0 ||
                item.Boundary.Timestamp -
                unique[^1].Boundary.Timestamp >
                DuplicateTolerance)
            {
                unique.Add(item);
                continue;
            }

            if ((item.Boundary.ScorePercent ?? 0) >
                (unique[^1].Boundary.ScorePercent ?? 0))
            {
                unique[^1] = item;
            }
        }

        return Array.AsReadOnly(
            unique.ToArray());
    }
}
