namespace ReplayFoundry.Desktop.Media.Moments;

internal static class MomentIntervalMath
{
    public static double OverlapRatio(
        MomentCandidateWindow window,
        IEnumerable<(TimeSpan Start, TimeSpan End)> intervals)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(intervals);

        (TimeSpan Start, TimeSpan End)[] clipped =
            intervals
                .Select(
                    interval =>
                        (
                            Start: interval.Start < window.Start
                                ? window.Start
                                : interval.Start,
                            End: interval.End > window.End
                                ? window.End
                                : interval.End))
                .Where(static interval => interval.End > interval.Start)
                .OrderBy(static interval => interval.Start)
                .ThenBy(static interval => interval.End)
                .ToArray();

        if (clipped.Length == 0)
        {
            return 0;
        }

        long coveredTicks = 0;
        TimeSpan currentStart =
            clipped[0].Start;
        TimeSpan currentEnd =
            clipped[0].End;

        foreach ((TimeSpan start, TimeSpan end) in
                 clipped.Skip(1))
        {
            if (start <= currentEnd)
            {
                if (end > currentEnd)
                {
                    currentEnd = end;
                }

                continue;
            }

            coveredTicks +=
                (currentEnd - currentStart).Ticks;
            currentStart = start;
            currentEnd = end;
        }

        coveredTicks +=
            (currentEnd - currentStart).Ticks;

        return Math.Clamp(
            coveredTicks /
            (double)window.Duration.Ticks,
            0,
            1);
    }

    public static double PairOverlapRatio(
        MomentCandidateWindow left,
        MomentCandidateWindow right)
    {
        TimeSpan start =
            left.Start > right.Start
                ? left.Start
                : right.Start;
        TimeSpan end =
            left.End < right.End
                ? left.End
                : right.End;

        if (end <= start)
        {
            return 0;
        }

        long denominator =
            Math.Min(
                left.Duration.Ticks,
                right.Duration.Ticks);

        return (end - start).Ticks /
            (double)denominator;
    }
}
