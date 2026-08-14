using System;

namespace ReplayFoundry.Desktop.Media.Analysis.Signals.Audio;

public sealed class AudioSignalSample
{
    public AudioSignalSample(
        int audioStreamIndex,
        TimeSpan start,
        TimeSpan end,
        TimeSpan sourceDuration,
        long? actualSourceSampleCount,
        double? rmsLevelDbfs,
        double? peakLevelDbfs,
        bool isDigitalSilence)
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
                "Audio signal evidence requires a positive source duration.");
        }

        if (start < TimeSpan.Zero ||
            end <= start ||
            end > sourceDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                end,
                "Audio signal intervals must be positive and remain within the source duration.");
        }

        if (actualSourceSampleCount is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(actualSourceSampleCount),
                actualSourceSampleCount,
                "Reported audio sample counts must be positive.");
        }

        ValidateDbfs(
            rmsLevelDbfs,
            nameof(rmsLevelDbfs));
        ValidateDbfs(
            peakLevelDbfs,
            nameof(peakLevelDbfs));

        if (isDigitalSilence &&
            (rmsLevelDbfs is not null ||
             peakLevelDbfs is not null))
        {
            throw new ArgumentException(
                "Digital silence must not be represented by an arbitrary finite dBFS floor.");
        }

        AudioStreamIndex = audioStreamIndex;
        Start = start;
        End = end;
        ActualSourceSampleCount =
            actualSourceSampleCount;
        RmsLevelDbfs = rmsLevelDbfs;
        PeakLevelDbfs = peakLevelDbfs;
        IsDigitalSilence = isDigitalSilence;
    }

    public int AudioStreamIndex { get; }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public TimeSpan Duration =>
        End -
        Start;

    public long? ActualSourceSampleCount { get; }

    public double? RmsLevelDbfs { get; }

    public double? PeakLevelDbfs { get; }

    public bool IsDigitalSilence { get; }

    private static void ValidateDbfs(
        double? value,
        string parameterName)
    {
        if (value is double actual &&
            (!double.IsFinite(actual) ||
             actual > 0))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Finite audio levels must be at or below 0 dBFS.");
        }
    }
}
