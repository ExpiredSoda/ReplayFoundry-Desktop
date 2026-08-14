using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Analysis.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Visual;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Inspection;
using static ReplayFoundry.Desktop.Platform.Media.FfmpegEvidenceParseAccumulators;
using static ReplayFoundry.Desktop.Platform.Media.FfmpegEvidenceValueParser;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfmpegVisualIntervalPairing
{
    internal static IReadOnlyList<TInterval>
        PairTargetIntervals<TInterval>(
            ICollection<IntervalEvent> events,
            TargetAccumulator accumulator,
            string intervalName,
            Func<TimeSpan, TimeSpan, TInterval> factory)
    {
        IntervalEvent[] ordered =
            events
                .OrderBy(
                    static item =>
                        item.Timestamp)
                .ThenBy(
                    static item =>
                        item.IsStart)
                .ToArray();

        var intervals =
            new List<TInterval>();

        TimeSpan? openStart = null;

        foreach (IntervalEvent intervalEvent in ordered)
        {
            if (intervalEvent.IsStart)
            {
                if (openStart is not null)
                {
                    accumulator.Warnings.Add(
                        TargetWarning(
                            accumulator.Target,
                            MediaEvidenceWarningCode
                                .OverlappingIntervalStart,
                            $"A second {intervalName} interval started before the previous one ended."));
                    continue;
                }

                openStart =
                    intervalEvent.Timestamp;
                continue;
            }

            if (openStart is null)
            {
                accumulator.Warnings.Add(
                    TargetWarning(
                        accumulator.Target,
                        MediaEvidenceWarningCode
                            .UnmatchedIntervalEnd,
                        $"{intervalName} metadata ended at {intervalEvent.Timestamp} without a matching start."));
                continue;
            }

            if (intervalEvent.Timestamp <=
                openStart.Value)
            {
                accumulator.Warnings.Add(
                    TargetWarning(
                        accumulator.Target,
                        MediaEvidenceWarningCode
                            .InvalidMetadataValue,
                        $"{intervalName} metadata ended before or at its start."));
                openStart = null;
                continue;
            }

            intervals.Add(
                factory(
                    openStart.Value,
                    intervalEvent.Timestamp));
            openStart = null;
        }

        if (openStart is not null)
        {
            if (accumulator.Target.End >
                openStart.Value)
            {
                intervals.Add(
                    factory(
                        openStart.Value,
                        accumulator.Target.End));

                accumulator.Warnings.Add(
                    TargetWarning(
                        accumulator.Target,
                        MediaEvidenceWarningCode
                            .OpenIntervalClosedAtTargetEnd,
                        $"An open {intervalName} interval was closed at target end " +
                        $"{accumulator.Target.End}."));
            }
            else
            {
                accumulator.Warnings.Add(
                    TargetWarning(
                        accumulator.Target,
                        MediaEvidenceWarningCode
                            .UnmatchedIntervalStart,
                        $"An open {intervalName} interval could not be closed."));
            }
        }

        return intervals;
    }
}
