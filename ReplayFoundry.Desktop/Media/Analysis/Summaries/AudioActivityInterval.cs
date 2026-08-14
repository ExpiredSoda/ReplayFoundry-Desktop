using System;

namespace ReplayFoundry.Desktop.Media.Analysis.Summaries;

public sealed class AudioActivityInterval
{
    public AudioActivityInterval(
        TimeSpan start,
        TimeSpan end)
    {
        if (start < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(start),
                start,
                "An audio-activity interval cannot start before the source begins.");
        }

        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                end,
                "An audio-activity interval must end after it starts.");
        }

        Start = start;
        End = end;
    }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public TimeSpan Duration =>
        End - Start;
}
