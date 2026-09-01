using System;

namespace ReplayFoundry.Desktop.Media.Analysis.Audio;

public sealed class SilenceInterval
{
    public SilenceInterval(
        int audioStreamIndex,
        TimeSpan start,
        TimeSpan end)
    {
        if (audioStreamIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(audioStreamIndex),
                audioStreamIndex,
                "Audio stream index cannot be negative.");
        }

        if (start < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(start),
                start,
                "Silence interval cannot start before the source begins.");
        }

        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                end,
                "Silence interval must end after it starts.");
        }

        AudioStreamIndex = audioStreamIndex;
        Start = start;
        End = end;
    }

    public int AudioStreamIndex { get; }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public TimeSpan Duration =>
        End - Start;
}
