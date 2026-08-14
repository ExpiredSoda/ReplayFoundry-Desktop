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

internal static class FfmpegAudioIntervalPairing
{
    internal static void AddAudioIntervalEvent(
        FfmpegMetadataRecord record,
        string key,
        bool isStart,
        ICollection<IntervalEvent> events,
        ICollection<MediaEvidenceWarning> warnings,
        int streamIndex)
    {
        if (!record.Tags.TryGetValue(
                key,
                out string? value))
        {
            return;
        }

        if (!TryParseSeconds(
                value,
                out TimeSpan timestamp))
        {
            warnings.Add(
                InvalidValueWarning(
                    key,
                    value,
                    streamIndex:
                        streamIndex));
            return;
        }

        events.Add(
            new IntervalEvent(
                timestamp,
                isStart));
    }

    internal static IReadOnlyList<SilenceInterval>
        PairAudioIntervals(
            ICollection<IntervalEvent> events,
            int streamIndex,
            TimeSpan sourceDuration,
            ICollection<MediaEvidenceWarning> warnings)
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
            new List<SilenceInterval>();

        TimeSpan? openStart = null;

        foreach (IntervalEvent intervalEvent in ordered)
        {
            if (intervalEvent.Timestamp >
                sourceDuration)
            {
                warnings.Add(
                    new MediaEvidenceWarning(
                        MediaEvidenceWarningCode
                            .EvidenceOutsideSourceDuration,
                        $"Silence metadata at {intervalEvent.Timestamp} exceeds the source duration.",
                        streamIndex));
                continue;
            }

            if (intervalEvent.IsStart)
            {
                if (openStart is not null)
                {
                    warnings.Add(
                        new MediaEvidenceWarning(
                            MediaEvidenceWarningCode
                                .OverlappingIntervalStart,
                            "A second silence interval started before the previous one ended.",
                            streamIndex));
                    continue;
                }

                openStart =
                    intervalEvent.Timestamp;
                continue;
            }

            if (openStart is null)
            {
                warnings.Add(
                    new MediaEvidenceWarning(
                        MediaEvidenceWarningCode
                            .UnmatchedIntervalEnd,
                        $"Silence ended at {intervalEvent.Timestamp} without a matching start.",
                        streamIndex));
                continue;
            }

            if (intervalEvent.Timestamp <=
                openStart.Value)
            {
                warnings.Add(
                    new MediaEvidenceWarning(
                        MediaEvidenceWarningCode
                            .InvalidMetadataValue,
                        "Silence ended before or at its start.",
                        streamIndex));
                openStart = null;
                continue;
            }

            intervals.Add(
                new SilenceInterval(
                    streamIndex,
                    openStart.Value,
                    intervalEvent.Timestamp));
            openStart = null;
        }

        if (openStart is not null)
        {
            if (sourceDuration >
                openStart.Value)
            {
                intervals.Add(
                    new SilenceInterval(
                        streamIndex,
                        openStart.Value,
                        sourceDuration));

                warnings.Add(
                    new MediaEvidenceWarning(
                        MediaEvidenceWarningCode
                            .OpenIntervalClosedAtSourceEnd,
                        "An open silence interval was closed at the known source duration.",
                        streamIndex));
            }
            else
            {
                warnings.Add(
                    new MediaEvidenceWarning(
                        MediaEvidenceWarningCode
                            .UnmatchedIntervalStart,
                        "An open silence interval could not be closed.",
                        streamIndex));
            }
        }

        return intervals;
    }
}
