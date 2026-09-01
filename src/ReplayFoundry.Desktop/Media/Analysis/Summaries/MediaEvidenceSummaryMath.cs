using System;
using System.Collections.Generic;
using System.Linq;
using ReplayFoundry.Desktop.Media.Analysis.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Visual;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Media.Analysis.Summaries;

internal static class MediaEvidenceSummaryMath
{
    internal static TimeSpan Clamp(
        TimeSpan value,
        TimeSpan sourceDuration)
    {
        if (value < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return value > sourceDuration
            ? sourceDuration
            : value;
    }

    internal static TimeSpan SumDurations(
        IEnumerable<TimeSpan> durations)
    {
        long ticks = 0;

        foreach (TimeSpan duration in durations)
        {
            ticks =
                checked(ticks + duration.Ticks);
        }
        return TimeSpan.FromTicks(ticks);
    }

    internal static TimeSpan CalculateMedian(
        IReadOnlyList<TimeSpan> sortedDurations)
    {
        if (sortedDurations.Count == 0)
        {
            return TimeSpan.Zero;
        }

        int middle =
            sortedDurations.Count / 2;

        if (sortedDurations.Count % 2 != 0)
        {
            return sortedDurations[middle];
        }

        long leftTicks =
            sortedDurations[middle - 1].Ticks;

        long rightTicks =
            sortedDurations[middle].Ticks;

        return TimeSpan.FromTicks(
            leftTicks +
            ((rightTicks - leftTicks) / 2));
    }

    internal static double CalculateMedian(
        IReadOnlyList<double> sortedValues)
    {
        int middle =
            sortedValues.Count / 2;

        return sortedValues.Count % 2 != 0
            ? sortedValues[middle]
            : sortedValues[middle - 1] +
              ((sortedValues[middle] - sortedValues[middle - 1]) / 2);
    }

    internal static double CalculatePercentile(
        IReadOnlyList<double> sortedValues,
        double percentile)
    {
        double position =
            (sortedValues.Count - 1) *
            percentile;

        int lower =
            (int)Math.Floor(position);

        int upper =
            (int)Math.Ceiling(position);

        if (lower == upper)
        {
            return sortedValues[lower];
        }

        double fraction =
            position -
            lower;

        return sortedValues[lower] +
               ((sortedValues[upper] -
                 sortedValues[lower]) *
                fraction);
    }
}
