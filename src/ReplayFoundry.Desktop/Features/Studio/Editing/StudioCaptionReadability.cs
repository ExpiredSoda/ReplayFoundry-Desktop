using System.Globalization;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed record StudioCaptionReadabilityReport(int Characters, double CharactersPerSecond, int EstimatedLines, string? Warning)
{
    public string Summary => $"{Characters} characters · {CharactersPerSecond:0.#} chars/sec · about {EstimatedLines} line{(EstimatedLines == 1 ? "" : "s")}";
}
public static class StudioCaptionReadability
{
    // Product review targets, not timing changes. Speech synchronization is never
    // stretched to hide an over-dense caption.
    public const int TargetCharactersPerLine = 42;
    public const double TargetCharactersPerSecond = 20;
    public static StudioCaptionReadabilityReport Assess(string text, double durationSeconds)
    {
        ArgumentNullException.ThrowIfNull(text);
        int characters = new StringInfo(text).LengthInTextElements;
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
            return new(characters, 0, 0, "Set a positive caption duration before reviewing readability.");
        int lines = text.Replace("\r\n", "\n").Split('\n').Sum(line => Math.Max(1,
            (int)Math.Ceiling(new StringInfo(line).LengthInTextElements / (double)TargetCharactersPerLine)));
        double rate = characters / durationSeconds;
        var warnings = new List<string>();
        if (rate > TargetCharactersPerSecond) warnings.Add($"Fast reading ({rate:0.#} characters/sec). Review phrase breaks or shorten the displayed wording.");
        if (lines > 2) warnings.Add("More than two estimated lines. Split the phrase or widen the caption area; actual wrapping depends on font and width.");
        return new(characters, rate, lines, warnings.Count == 0 ? null : string.Join(" ", warnings));
    }
}
