using System;

namespace ReplayFoundry.Desktop.Media.Analysis.Summaries;

public sealed class SceneBoundaryCluster
{
    public SceneBoundaryCluster(
        TimeSpan start,
        TimeSpan end,
        int boundaryCount,
        double? maximumScorePercent,
        double? meanScorePercent)
    {
        if (start < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(start),
                start,
                "A scene cluster cannot start before the source begins.");
        }

        if (end < start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                end,
                "A scene cluster cannot end before it starts.");
        }

        if (boundaryCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(boundaryCount),
                boundaryCount,
                "A scene cluster requires at least one boundary.");
        }

        ValidateOptionalScore(
            maximumScorePercent,
            nameof(maximumScorePercent));

        ValidateOptionalScore(
            meanScorePercent,
            nameof(meanScorePercent));

        Start = start;
        End = end;
        BoundaryCount = boundaryCount;
        MaximumScorePercent = maximumScorePercent;
        MeanScorePercent = meanScorePercent;
    }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public TimeSpan Duration =>
        End - Start;

    public int BoundaryCount { get; }

    public double? MaximumScorePercent { get; }

    public double? MeanScorePercent { get; }

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
