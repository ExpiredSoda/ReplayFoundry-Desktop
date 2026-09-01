namespace ReplayFoundry.Desktop.Features.Generate.CompositionReview;

public static class CompositionPreviewTimestampPolicy
{
    public static TimeSpan GetInitialTimestamp(
        TimeSpan sourceDuration)
    {
        if (sourceDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceDuration),
                sourceDuration,
                "A preview timestamp requires a positive source duration.");
        }

        long timestampTicks =
            sourceDuration.Ticks / 10;

        if (timestampTicks >= sourceDuration.Ticks)
        {
            timestampTicks =
                sourceDuration.Ticks - 1;
        }

        return TimeSpan.FromTicks(
            Math.Max(
                0,
                timestampTicks));
    }
}
