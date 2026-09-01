using System;
using ReplayFoundry.Desktop.Media.Analysis.Visual;

namespace ReplayFoundry.Desktop.Media.Analysis.Summaries;

public sealed class VisualTargetEvidenceSummary
{
    public VisualTargetEvidenceSummary(
        VisualEvidenceTarget target,
        int sceneBoundaryCount,
        TimeSpan? firstSceneBoundary,
        TimeSpan? lastSceneBoundary,
        double? maximumSceneScorePercent,
        double? meanSceneScorePercent,
        double? medianSceneScorePercent,
        int blackIntervalCount,
        TimeSpan totalBlackDuration,
        TimeSpan longestBlackInterval,
        int freezeIntervalCount,
        TimeSpan totalFreezeDuration,
        TimeSpan longestFreezeInterval,
        VisualTargetSignalSummary signals)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(signals);

        if (!ReferenceEquals(
                target,
                signals.Target))
        {
            throw new ArgumentException(
                "Target signal summary must preserve visual-target identity.",
                nameof(signals));
        }

        if (sceneBoundaryCount < 0 ||
            blackIntervalCount < 0 ||
            freezeIntervalCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sceneBoundaryCount),
                "Visual target evidence counts cannot be negative.");
        }

        if (sceneBoundaryCount == 0 &&
            (firstSceneBoundary is not null ||
             lastSceneBoundary is not null))
        {
            throw new ArgumentException(
                "An empty target scene summary cannot report first or last boundaries.");
        }

        if (firstSceneBoundary is TimeSpan first &&
            (first < target.Start ||
             first > target.End))
        {
            throw new ArgumentOutOfRangeException(
                nameof(firstSceneBoundary),
                firstSceneBoundary,
                "First scene boundary must remain inside the target.");
        }

        if (lastSceneBoundary is TimeSpan last &&
            (last < target.Start ||
             last > target.End))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastSceneBoundary),
                lastSceneBoundary,
                "Last scene boundary must remain inside the target.");
        }

        if (firstSceneBoundary is not null &&
            lastSceneBoundary <
            firstSceneBoundary)
        {
            throw new ArgumentException(
                "Last target scene boundary cannot precede the first.");
        }

        ValidateScore(
            maximumSceneScorePercent,
            nameof(maximumSceneScorePercent));
        ValidateScore(
            meanSceneScorePercent,
            nameof(meanSceneScorePercent));
        ValidateScore(
            medianSceneScorePercent,
            nameof(medianSceneScorePercent));

        ValidateDuration(
            totalBlackDuration,
            target.Duration,
            nameof(totalBlackDuration));
        ValidateDuration(
            longestBlackInterval,
            target.Duration,
            nameof(longestBlackInterval));
        ValidateDuration(
            totalFreezeDuration,
            target.Duration,
            nameof(totalFreezeDuration));
        ValidateDuration(
            longestFreezeInterval,
            target.Duration,
            nameof(longestFreezeInterval));

        Target = target;
        SceneBoundaryCount = sceneBoundaryCount;
        FirstSceneBoundary = firstSceneBoundary;
        LastSceneBoundary = lastSceneBoundary;
        MaximumSceneScorePercent =
            maximumSceneScorePercent;
        MeanSceneScorePercent =
            meanSceneScorePercent;
        MedianSceneScorePercent =
            medianSceneScorePercent;
        BlackIntervalCount = blackIntervalCount;
        TotalBlackDuration = totalBlackDuration;
        LongestBlackInterval =
            longestBlackInterval;
        FreezeIntervalCount = freezeIntervalCount;
        TotalFreezeDuration = totalFreezeDuration;
        LongestFreezeInterval =
            longestFreezeInterval;
        Signals = signals;
    }

    public VisualEvidenceTarget Target { get; }

    public int SceneBoundaryCount { get; }

    public TimeSpan? FirstSceneBoundary { get; }

    public TimeSpan? LastSceneBoundary { get; }

    public double? MaximumSceneScorePercent { get; }

    public double? MeanSceneScorePercent { get; }

    public double? MedianSceneScorePercent { get; }

    public double SceneBoundariesPerMinute =>
        SceneBoundaryCount /
        Target.Duration.TotalMinutes;

    public int BlackIntervalCount { get; }

    public TimeSpan TotalBlackDuration { get; }

    public double BlackPercentage =>
        TotalBlackDuration.TotalSeconds /
        Target.Duration.TotalSeconds *
        100;

    public TimeSpan LongestBlackInterval { get; }

    public int FreezeIntervalCount { get; }

    public TimeSpan TotalFreezeDuration { get; }

    public double FreezePercentage =>
        TotalFreezeDuration.TotalSeconds /
        Target.Duration.TotalSeconds *
        100;

    public TimeSpan LongestFreezeInterval { get; }

    public VisualTargetSignalSummary Signals { get; }

    private static void ValidateScore(
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
                "Target scene scores must be between zero and 100.");
        }
    }

    private static void ValidateDuration(
        TimeSpan value,
        TimeSpan targetDuration,
        string parameterName)
    {
        if (value < TimeSpan.Zero ||
            value > targetDuration)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Target summary durations must remain within target duration.");
        }
    }
}
