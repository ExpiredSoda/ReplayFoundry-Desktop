using System;

namespace ReplayFoundry.Desktop.Media.Analysis.Visual;

public sealed class FreezeInterval
{
    public FreezeInterval(
        TimeSpan start,
        TimeSpan end)
    {
        if (start < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(start),
                start,
                "Freeze interval cannot start before the source begins.");
        }

        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                end,
                "Freeze interval must end after it starts.");
        }

        Start = start;
        End = end;
    }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public TimeSpan Duration =>
        End - Start;
}
