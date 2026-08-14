using System;
using System.Collections.Generic;
using System.Linq;
using ReplayFoundry.Desktop.Media.Analysis.Visual;

namespace ReplayFoundry.Desktop.Media.Analysis.Summaries;

/// <summary>
/// Builds deterministic scene-boundary clusters for both evidence summaries
/// and downstream deterministic moment finding.
/// </summary>
public static class SceneBoundaryClusterBuilder
{
    public static IReadOnlyList<SceneBoundaryCluster> Build(
        IEnumerable<SceneBoundary> boundaries,
        TimeSpan maximumGap)
    {
        ArgumentNullException.ThrowIfNull(boundaries);

        if (maximumGap < TimeSpan.Zero ||
            maximumGap > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumGap),
                maximumGap,
                "Scene-cluster gap must be between zero and ten minutes.");
        }

        SceneBoundary[] snapshot =
            boundaries.ToArray();

        if (snapshot.Any(static boundary => boundary is null))
        {
            throw new ArgumentException(
                "Scene boundaries cannot contain null entries.",
                nameof(boundaries));
        }

        SceneBoundary[] ordered =
            snapshot
                .OrderBy(
                    static boundary =>
                        boundary.Timestamp)
                .ToArray();

        if (ordered.Length == 0)
        {
            return [];
        }

        var clusters =
            new List<SceneBoundaryCluster>();

        var current =
            new List<SceneBoundary>
            {
                ordered[0],
            };

        for (int index = 1;
             index < ordered.Length;
             index++)
        {
            SceneBoundary boundary =
                ordered[index];

            TimeSpan gap =
                boundary.Timestamp -
                ordered[index - 1].Timestamp;

            if (gap <= maximumGap)
            {
                current.Add(boundary);
                continue;
            }

            clusters.Add(
                CreateCluster(current));

            current.Clear();
            current.Add(boundary);
        }

        clusters.Add(
            CreateCluster(current));

        return Array.AsReadOnly(
            clusters.ToArray());
    }

    private static SceneBoundaryCluster CreateCluster(
        IReadOnlyList<SceneBoundary> boundaries)
    {
        double[] scores =
            boundaries
                .Where(
                    static boundary =>
                        boundary.ScorePercent is not null)
                .Select(
                    static boundary =>
                        boundary.ScorePercent!.Value)
                .ToArray();

        return new SceneBoundaryCluster(
            boundaries[0].Timestamp,
            boundaries[^1].Timestamp,
            boundaries.Count,
            scores.Length == 0
                ? null
                : scores.Max(),
            scores.Length == 0
                ? null
                : scores.Average());
    }
}
