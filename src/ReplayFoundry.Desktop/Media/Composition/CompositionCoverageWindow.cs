using System;

namespace ReplayFoundry.Desktop.Media.Composition;

/// <summary>
/// A timeline interval that received denser composition verification.
/// </summary>
public sealed class CompositionCoverageWindow
{
    public CompositionCoverageWindow(
        TimeSpan start,
        TimeSpan end)
    {
        if (start < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(start),
                start,
                "A composition coverage window cannot start before the source begins.");
        }

        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                end,
                "A composition coverage window must end after it starts.");
        }

        Start = start;
        End = end;
    }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public TimeSpan Duration =>
        End - Start;
}
