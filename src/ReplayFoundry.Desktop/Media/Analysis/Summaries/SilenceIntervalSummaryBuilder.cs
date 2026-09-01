using System;
using System.Collections.Generic;
using System.Linq;
using ReplayFoundry.Desktop.Media.Analysis.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Visual;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Media.Analysis.Summaries;

internal static class SilenceIntervalSummaryBuilder
{
    internal static IReadOnlyList<SilenceInterval> Normalize(
        TimeSpan sourceDuration,
        int streamIndex,
        IReadOnlyList<SilenceInterval> intervals,
        TimeSpan mergeTolerance)
    {
        if (intervals.Count == 0)
        {
            return [];
        }

        var normalized =
            new List<SilenceInterval>();

        TimeSpan currentStart =
            MediaEvidenceSummaryMath.Clamp(
                intervals[0].Start,
                sourceDuration);

        TimeSpan currentEnd =
            MediaEvidenceSummaryMath.Clamp(
                intervals[0].End,
                sourceDuration);

        for (int index = 1;
             index < intervals.Count;
             index++)
        {
            SilenceInterval next =
                intervals[index];

            TimeSpan nextStart =
                MediaEvidenceSummaryMath.Clamp(
                    next.Start,
                    sourceDuration);

            TimeSpan nextEnd =
                MediaEvidenceSummaryMath.Clamp(
                    next.End,
                    sourceDuration);

            if (nextStart <= currentEnd + mergeTolerance)
            {
                if (nextEnd > currentEnd)
                {
                    currentEnd = nextEnd;
                }

                continue;
            }

            if (currentEnd > currentStart)
            {
                normalized.Add(
                    new SilenceInterval(
                        streamIndex,
                        currentStart,
                        currentEnd));
            }

            currentStart = nextStart;
            currentEnd = nextEnd;
        }

        if (currentEnd > currentStart)
        {
            normalized.Add(
                new SilenceInterval(
                    streamIndex,
                    currentStart,
                    currentEnd));
        }

        return normalized;
    }

    internal static IReadOnlyList<AudioActivityInterval> BuildActiveIntervals(
        TimeSpan sourceDuration,
        IReadOnlyList<SilenceInterval> silenceIntervals)
    {
        var active =
            new List<AudioActivityInterval>();

        TimeSpan cursor =
            TimeSpan.Zero;

        foreach (SilenceInterval silence in silenceIntervals)
        {
            if (silence.Start > cursor)
            {
                active.Add(
                    new AudioActivityInterval(
                        cursor,
                        silence.Start));
            }

            if (silence.End > cursor)
            {
                cursor = silence.End;
            }
        }

        if (cursor < sourceDuration)
        {
            active.Add(
                new AudioActivityInterval(
                    cursor,
                    sourceDuration));
        }

        return active;
    }
}
