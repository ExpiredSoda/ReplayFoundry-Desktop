using System.Globalization;

namespace ReplayFoundry.Desktop.Presentation;

/// <summary>
/// Formats media positions for human-facing UI without exposing sub-second precision.
/// Exact timestamps remain unchanged in the underlying media models.
/// </summary>
public static class MediaTimeFormatter
{
    public static string Format(TimeSpan value)
    {
        long wholeSeconds = value <= TimeSpan.Zero
            ? 0
            : value.Ticks / TimeSpan.TicksPerSecond;
        long hours = wholeSeconds / 3600;
        long minutes = wholeSeconds / 60 % 60;
        long seconds = wholeSeconds % 60;

        return hours > 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{hours}:{minutes:00}:{seconds:00}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{minutes}:{seconds:00}");
    }
}
