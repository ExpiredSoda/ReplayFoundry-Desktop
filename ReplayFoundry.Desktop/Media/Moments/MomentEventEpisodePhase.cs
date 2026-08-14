using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class MomentEventEpisodePhase
{
    public MomentEventEpisodePhase(
        MomentEventEpisodePhaseKind kind,
        TimeSpan start,
        TimeSpan end,
        bool isObserved)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (start < TimeSpan.Zero || end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(end));
        }

        Kind = kind;
        Start = start;
        End = end;
        IsObserved = isObserved;
    }

    public MomentEventEpisodePhaseKind Kind { get; }
    public TimeSpan Start { get; }
    public TimeSpan End { get; }
    public TimeSpan Duration => End - Start;
    public bool IsObserved { get; }
}
