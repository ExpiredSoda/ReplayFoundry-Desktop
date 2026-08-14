using System;
using System.Collections.Generic;
using System.Linq;
using ReplayFoundry.Desktop.Media.Analysis.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Visual;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Media.Analysis.Summaries;

internal static class AudioStreamSilenceSummaryBuilder
{
    internal static AudioStreamSilenceSummary Build(
        TimeSpan sourceDuration,
        int audioStreamIndex,
        IEnumerable<SilenceInterval> intervals,
        MediaEvidenceSummaryOptions? options = null)
    {
        if (sourceDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceDuration),
                sourceDuration,
                "Source duration must be greater than zero.");
        }

        if (audioStreamIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(audioStreamIndex),
                audioStreamIndex,
                "Audio stream index cannot be negative.");
        }

        ArgumentNullException.ThrowIfNull(intervals);

        options ??=
            MediaEvidenceSummaryOptions.CreateDefault();

        SilenceInterval[] rawIntervals =
            intervals
                .OrderBy(static item => item.Start)
                .ThenBy(static item => item.End)
                .ToArray();

        if (rawIntervals.Any(static item => item is null))
        {
            throw new ArgumentException(
                "Silence intervals cannot contain null items.",
                nameof(intervals));
        }

        if (rawIntervals.Any(
                item => item.AudioStreamIndex != audioStreamIndex))
        {
            throw new ArgumentException(
                "All silence intervals must belong to the requested audio stream.",
                nameof(intervals));
        }

        IReadOnlyList<SilenceInterval> normalized =
            SilenceIntervalSummaryBuilder.Normalize(
                sourceDuration,
                audioStreamIndex,
                rawIntervals,
                options.SilenceMergeTolerance);

        IReadOnlyList<AudioActivityInterval> activeIntervals =
            SilenceIntervalSummaryBuilder.BuildActiveIntervals(
                sourceDuration,
                normalized);

        TimeSpan[] durations =
            normalized
                .Select(static item => item.Duration)
                .OrderBy(static item => item)
                .ToArray();

        TimeSpan totalSilentDuration =
            MediaEvidenceSummaryMath.SumDurations(durations);

        TimeSpan longestSilence =
            durations.Length == 0
                ? TimeSpan.Zero
                : durations[^1];

        TimeSpan meanSilence =
            durations.Length == 0
                ? TimeSpan.Zero
                : TimeSpan.FromTicks(
                    totalSilentDuration.Ticks /
                    durations.Length);

        TimeSpan medianSilence =
            MediaEvidenceSummaryMath.CalculateMedian(durations);

        int shortSilenceCount =
            durations.Count(
                duration =>
                    duration <= options.ShortSilenceMaximum);

        int longSilenceCount =
            durations.Count(
                duration =>
                    duration >= options.LongSilenceMinimum);

        return new AudioStreamSilenceSummary(
            audioStreamIndex,
            sourceDuration,
            options.ShortSilenceMaximum,
            options.LongSilenceMinimum,
            rawIntervals.Length,
            normalized,
            activeIntervals,
            totalSilentDuration,
            longestSilence,
            meanSilence,
            medianSilence,
            shortSilenceCount,
            longSilenceCount);
    }
}
