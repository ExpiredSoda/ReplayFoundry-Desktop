using System;
using System.Collections.Generic;
using System.Linq;
using ReplayFoundry.Desktop.Media.Analysis.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Visual;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Media.Analysis.Summaries;

internal static class SceneEvidenceSummaryBuilder
{
    internal static SceneEvidenceSummary Build(
        TimeSpan sourceDuration,
        IReadOnlyList<SceneBoundary> boundaries,
        MediaEvidenceSummaryOptions options)
    {
        SceneBoundary[] ordered =
            boundaries
                .OrderBy(static item => item.Timestamp)
                .ToArray();

        double[] scores =
            ordered
                .Where(static item => item.ScorePercent is not null)
                .Select(static item => item.ScorePercent!.Value)
                .OrderBy(static value => value)
                .ToArray();

        IReadOnlyList<SceneBoundaryCluster> clusters =
            SceneBoundaryClusterBuilder.Build(
                ordered,
                options.SceneClusterMaximumGap);

        IReadOnlyList<SceneDensityBucket> buckets =
            BuildDensityBuckets(
                sourceDuration,
                ordered,
                options.SceneDensityBucketDuration);

        return new SceneEvidenceSummary(
            ordered.Length,
            ordered.Length == 0 ? null : ordered[0].Timestamp,
            ordered.Length == 0 ? null : ordered[^1].Timestamp,
            scores.Length == 0 ? null : scores[^1],
            scores.Length == 0 ? null : scores.Average(),
            scores.Length == 0
                ? null
                : MediaEvidenceSummaryMath.CalculateMedian(scores),
            CalculateLongestSceneGap(sourceDuration, ordered),
            clusters,
            buckets);
    }

    private static IReadOnlyList<SceneDensityBucket> BuildDensityBuckets(
        TimeSpan sourceDuration,
        IReadOnlyList<SceneBoundary> boundaries,
        TimeSpan bucketDuration)
    {
        int bucketCount =
            checked(
                (int)Math.Ceiling(
                    sourceDuration.Ticks /
                    (double)bucketDuration.Ticks));

        bucketCount =
            Math.Max(bucketCount, 1);

        int[] counts =
            new int[bucketCount];

        foreach (SceneBoundary boundary in boundaries)
        {
            int index =
                (int)Math.Min(
                    boundary.Timestamp.Ticks /
                    bucketDuration.Ticks,
                    bucketCount - 1L);

            counts[index]++;
        }

        var buckets =
            new List<SceneDensityBucket>(bucketCount);

        for (int index = 0;
             index < bucketCount;
             index++)
        {
            TimeSpan start =
                TimeSpan.FromTicks(
                    checked(
                        bucketDuration.Ticks *
                        (long)index));

            TimeSpan proposedEnd =
                TimeSpan.FromTicks(
                    checked(
                        bucketDuration.Ticks *
                        (long)(index + 1)));

            TimeSpan end =
                proposedEnd < sourceDuration
                    ? proposedEnd
                    : sourceDuration;

            buckets.Add(
                new SceneDensityBucket(
                    start,
                    end,
                    counts[index]));
        }

        return buckets;
    }

    private static TimeSpan CalculateLongestSceneGap(
        TimeSpan sourceDuration,
        IReadOnlyList<SceneBoundary> boundaries)
    {
        if (boundaries.Count == 0)
        {
            return sourceDuration;
        }

        TimeSpan longest =
            boundaries[0].Timestamp;

        for (int index = 1;
             index < boundaries.Count;
             index++)
        {
            TimeSpan gap =
                boundaries[index].Timestamp -
                boundaries[index - 1].Timestamp;

            if (gap > longest)
            {
                longest = gap;
            }
        }

        TimeSpan trailingGap =
            sourceDuration -
            boundaries[^1].Timestamp;

        return trailingGap > longest
            ? trailingGap
            : longest;
    }
}
