using System;

namespace ReplayFoundry.Desktop.Media.Analysis.Summaries;

public sealed class SceneDensityBucket
{
    public SceneDensityBucket(
        TimeSpan start,
        TimeSpan end,
        int boundaryCount)
    {
        if (start < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(start),
                start,
                "A scene-density bucket cannot start before the source begins.");
        }

        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                end,
                "A scene-density bucket must end after it starts.");
        }

        if (boundaryCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(boundaryCount),
                boundaryCount,
                "Scene-boundary count cannot be negative.");
        }

        Start = start;
        End = end;
        BoundaryCount = boundaryCount;
    }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public TimeSpan Duration =>
        End - Start;

    public int BoundaryCount { get; }

    public double BoundariesPerMinute =>
        Duration.TotalMinutes > 0
            ? BoundaryCount / Duration.TotalMinutes
            : 0;
}
