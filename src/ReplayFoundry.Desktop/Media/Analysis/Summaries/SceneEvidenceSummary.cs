using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ReplayFoundry.Desktop.Media.Analysis.Summaries;

public sealed class SceneEvidenceSummary
{
    private readonly ReadOnlyCollection<SceneBoundaryCluster> _clusters;
    private readonly ReadOnlyCollection<SceneDensityBucket> _densityBuckets;

    public SceneEvidenceSummary(
        int boundaryCount,
        TimeSpan? firstBoundary,
        TimeSpan? lastBoundary,
        double? maximumScorePercent,
        double? meanScorePercent,
        double? medianScorePercent,
        TimeSpan longestGapWithoutBoundary,
        IEnumerable<SceneBoundaryCluster> clusters,
        IEnumerable<SceneDensityBucket> densityBuckets)
    {
        if (boundaryCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(boundaryCount),
                boundaryCount,
                "Scene-boundary count cannot be negative.");
        }

        if (firstBoundary is TimeSpan first && first < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(firstBoundary),
                firstBoundary,
                "First scene boundary cannot be negative.");
        }

        if (lastBoundary is TimeSpan last && last < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastBoundary),
                lastBoundary,
                "Last scene boundary cannot be negative.");
        }

        if (firstBoundary is not null &&
            lastBoundary is not null &&
            lastBoundary < firstBoundary)
        {
            throw new ArgumentException(
                "Last scene boundary cannot precede the first boundary.",
                nameof(lastBoundary));
        }

        if (boundaryCount == 0 &&
            (firstBoundary is not null || lastBoundary is not null))
        {
            throw new ArgumentException(
                "An empty scene summary cannot report first or last boundaries.");
        }

        ValidateOptionalScore(maximumScorePercent, nameof(maximumScorePercent));
        ValidateOptionalScore(meanScorePercent, nameof(meanScorePercent));
        ValidateOptionalScore(medianScorePercent, nameof(medianScorePercent));

        if (longestGapWithoutBoundary < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(longestGapWithoutBoundary),
                longestGapWithoutBoundary,
                "Longest scene gap cannot be negative.");
        }

        ArgumentNullException.ThrowIfNull(clusters);
        ArgumentNullException.ThrowIfNull(densityBuckets);

        SceneBoundaryCluster[] clusterSnapshot =
            clusters.ToArray();

        SceneDensityBucket[] bucketSnapshot =
            densityBuckets.ToArray();

        if (clusterSnapshot.Any(static item => item is null) ||
            bucketSnapshot.Any(static item => item is null))
        {
            throw new ArgumentException(
                "Scene summary collections cannot contain null items.");
        }

        BoundaryCount = boundaryCount;
        FirstBoundary = firstBoundary;
        LastBoundary = lastBoundary;
        MaximumScorePercent = maximumScorePercent;
        MeanScorePercent = meanScorePercent;
        MedianScorePercent = medianScorePercent;
        LongestGapWithoutBoundary = longestGapWithoutBoundary;
        _clusters = Array.AsReadOnly(clusterSnapshot);
        _densityBuckets = Array.AsReadOnly(bucketSnapshot);
    }

    public int BoundaryCount { get; }

    public TimeSpan? FirstBoundary { get; }

    public TimeSpan? LastBoundary { get; }

    public double? MaximumScorePercent { get; }

    public double? MeanScorePercent { get; }

    public double? MedianScorePercent { get; }

    public TimeSpan LongestGapWithoutBoundary { get; }

    public IReadOnlyList<SceneBoundaryCluster> Clusters =>
        _clusters;

    public IReadOnlyList<SceneDensityBucket> DensityBuckets =>
        _densityBuckets;

    private static void ValidateOptionalScore(
        double? value,
        string parameterName)
    {
        if (value is double actual &&
            (!double.IsFinite(actual) ||
             actual is < 0 or > 100))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Scene score must be between zero and 100 when supplied.");
        }
    }
}
