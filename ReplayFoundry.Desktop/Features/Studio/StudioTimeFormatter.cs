using ReplayFoundry.Desktop.Presentation;

namespace ReplayFoundry.Desktop.Features.Studio;

internal static class StudioTimeFormatter
{
    public static string FormatDuration(TimeSpan duration) =>
        MediaTimeFormatter.Format(duration);

    public static string FormatTime(TimeSpan value) =>
        MediaTimeFormatter.Format(value);

    public static string FormatAdjustment(double seconds)
    {
        long wholeSeconds = (long)Math.Round(
            Math.Abs(seconds),
            MidpointRounding.AwayFromZero);
        string sign = wholeSeconds == 0
            ? string.Empty
            : seconds switch
            {
                > 0 => "+",
                < 0 => "−",
                _ => string.Empty,
            };
        return sign + MediaTimeFormatter.Format(
            TimeSpan.FromSeconds(wholeSeconds));
    }
}
