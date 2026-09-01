using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ReplayFoundry.Desktop.Media.Analysis.Audio;

namespace ReplayFoundry.Desktop.Media.Analysis.Summaries;

public sealed class AudioStreamSilenceSummary
{
    private readonly ReadOnlyCollection<SilenceInterval>
        _normalizedSilenceIntervals;

    private readonly ReadOnlyCollection<AudioActivityInterval>
        _activeIntervals;

    public AudioStreamSilenceSummary(
        int audioStreamIndex,
        TimeSpan sourceDuration,
        TimeSpan shortSilenceMaximum,
        TimeSpan longSilenceMinimum,
        int rawIntervalCount,
        IEnumerable<SilenceInterval> normalizedSilenceIntervals,
        IEnumerable<AudioActivityInterval> activeIntervals,
        TimeSpan totalSilentDuration,
        TimeSpan longestSilence,
        TimeSpan meanSilenceDuration,
        TimeSpan medianSilenceDuration,
        int shortSilenceCount,
        int longSilenceCount)
    {
        if (audioStreamIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(audioStreamIndex),
                audioStreamIndex,
                "Audio stream index cannot be negative.");
        }

        if (sourceDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceDuration),
                sourceDuration,
                "Source duration must be greater than zero.");
        }

        if (shortSilenceMaximum <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(shortSilenceMaximum),
                shortSilenceMaximum,
                "Short-silence maximum must be greater than zero.");
        }

        if (longSilenceMinimum <= shortSilenceMaximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(longSilenceMinimum),
                longSilenceMinimum,
                "Long-silence minimum must exceed the short-silence maximum.");
        }

        if (rawIntervalCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rawIntervalCount),
                rawIntervalCount,
                "Raw silence-interval count cannot be negative.");
        }

        ArgumentNullException.ThrowIfNull(normalizedSilenceIntervals);
        ArgumentNullException.ThrowIfNull(activeIntervals);

        SilenceInterval[] silenceSnapshot =
            normalizedSilenceIntervals.ToArray();

        AudioActivityInterval[] activeSnapshot =
            activeIntervals.ToArray();

        if (silenceSnapshot.Any(static item => item is null) ||
            activeSnapshot.Any(static item => item is null))
        {
            throw new ArgumentException(
                "Audio summary collections cannot contain null items.");
        }

        if (silenceSnapshot.Any(
                item => item.AudioStreamIndex != audioStreamIndex))
        {
            throw new ArgumentException(
                "Normalized silence intervals must belong to the summarized stream.",
                nameof(normalizedSilenceIntervals));
        }

        if (rawIntervalCount < silenceSnapshot.Length)
        {
            throw new ArgumentException(
                "Normalized silence count cannot exceed the raw interval count.",
                nameof(rawIntervalCount));
        }

        ValidateDuration(totalSilentDuration, sourceDuration, nameof(totalSilentDuration));
        ValidateDuration(longestSilence, sourceDuration, nameof(longestSilence));
        ValidateDuration(meanSilenceDuration, sourceDuration, nameof(meanSilenceDuration));
        ValidateDuration(medianSilenceDuration, sourceDuration, nameof(medianSilenceDuration));

        if (shortSilenceCount < 0 ||
            longSilenceCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(shortSilenceCount),
                "Silence category counts cannot be negative.");
        }

        AudioStreamIndex = audioStreamIndex;
        SourceDuration = sourceDuration;
        ShortSilenceMaximum = shortSilenceMaximum;
        LongSilenceMinimum = longSilenceMinimum;
        RawIntervalCount = rawIntervalCount;
        TotalSilentDuration = totalSilentDuration;
        LongestSilence = longestSilence;
        MeanSilenceDuration = meanSilenceDuration;
        MedianSilenceDuration = medianSilenceDuration;
        ShortSilenceCount = shortSilenceCount;
        LongSilenceCount = longSilenceCount;
        _normalizedSilenceIntervals = Array.AsReadOnly(silenceSnapshot);
        _activeIntervals = Array.AsReadOnly(activeSnapshot);
    }

    public int AudioStreamIndex { get; }

    public TimeSpan SourceDuration { get; }

    public TimeSpan ShortSilenceMaximum { get; }

    public TimeSpan LongSilenceMinimum { get; }

    public int RawIntervalCount { get; }

    public int NormalizedIntervalCount =>
        _normalizedSilenceIntervals.Count;

    public int MergedIntervalCount =>
        RawIntervalCount - NormalizedIntervalCount;

    public TimeSpan TotalSilentDuration { get; }

    public double SilentPercentage =>
        TotalSilentDuration.TotalSeconds /
        SourceDuration.TotalSeconds *
        100;

    public TimeSpan LongestSilence { get; }

    public TimeSpan MeanSilenceDuration { get; }

    public TimeSpan MedianSilenceDuration { get; }

    public int ShortSilenceCount { get; }

    public int LongSilenceCount { get; }

    public IReadOnlyList<SilenceInterval> NormalizedSilenceIntervals =>
        _normalizedSilenceIntervals;

    public IReadOnlyList<AudioActivityInterval> ActiveIntervals =>
        _activeIntervals;

    private static void ValidateDuration(
        TimeSpan value,
        TimeSpan sourceDuration,
        string parameterName)
    {
        if (value < TimeSpan.Zero ||
            value > sourceDuration)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Audio summary duration must be within the source duration.");
        }
    }
}
