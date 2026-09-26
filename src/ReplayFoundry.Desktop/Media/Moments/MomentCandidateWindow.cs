namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class MomentCandidateWindow
{
    public MomentCandidateWindow(
        TimeSpan start,
        TimeSpan end,
        TimeSpan sourceDuration)
    {
        if (sourceDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceDuration));
        }

        if (start < TimeSpan.Zero ||
            end <= start ||
            end > sourceDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                end,
                "A candidate window must be positive and remain inside the source.");
        }

        Start = start;
        End = end;
        SourceDuration = sourceDuration;
    }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public TimeSpan Duration => End - Start;

    public TimeSpan SourceDuration { get; }

    public bool Contains(TimeSpan timestamp) =>
        timestamp >= Start &&
        timestamp <= End;
}
