using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Studio.Editing;

namespace ReplayFoundry.Desktop.Media.Subtitles;

public enum SubtitleSidecarFormat { Srt, WebVtt }
public sealed record SubtitleCue(TimeSpan Start, TimeSpan End, string Text, string? Speaker = null);

public static class SubtitleSidecarSerializer
{
    public static string Build(GenerationCandidateCaptionTrack track, TimeSpan clipSourceStart,
        TimeSpan clipDuration, SubtitleSidecarFormat format) => Build(Project(track, clipSourceStart, clipDuration), format);

    public static IReadOnlyList<SubtitleCue> Project(GenerationCandidateCaptionTrack track,
        TimeSpan clipSourceStart, TimeSpan clipDuration)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (clipSourceStart < TimeSpan.Zero || clipDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(clipDuration));
        track = StudioCaptionCutProjection.Project(track, clipSourceStart, clipSourceStart + clipDuration).Track;
        return track.Segments.Select(s => new SubtitleCue(
                Max(TimeSpan.Zero, s.AbsoluteSourceStart - clipSourceStart),
                Min(clipDuration, s.AbsoluteSourceEnd - clipSourceStart), s.SecondaryText is null ? s.Text : s.Text + "\n" + s.SecondaryText, s.Speaker))
            .Where(c => c.End > c.Start).ToArray();
    }

    public static string Build(IEnumerable<SubtitleCue> cues, SubtitleSidecarFormat format)
    {
        if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        var text = new StringBuilder();
        if (format == SubtitleSidecarFormat.WebVtt) text.Append("WEBVTT\n\n");
        int index = 0;
        foreach (SubtitleCue cue in cues)
        {
            if (cue.Start < TimeSpan.Zero || cue.End <= cue.Start || string.IsNullOrWhiteSpace(cue.Text))
                throw new ArgumentException("Subtitle cues require text and positive intervals.");
            text.Append(++index).Append('\n').Append(Timestamp(cue.Start, format)).Append(" --> ")
                .Append(Timestamp(cue.End, format)).Append('\n');
            if (!string.IsNullOrWhiteSpace(cue.Speaker))
                text.Append(format == SubtitleSidecarFormat.WebVtt
                    ? "<v " + EscapeText(cue.Speaker) + ">"
                    : EscapeText(cue.Speaker) + ": ");
            text.Append(EscapeText(cue.Text.Replace("\r\n", "\n").Replace('\r', '\n')));
            if (!string.IsNullOrWhiteSpace(cue.Speaker) && format == SubtitleSidecarFormat.WebVtt) text.Append("</v>");
            text.Append("\n\n");
        }
        return text.ToString();
    }

    public static IReadOnlyList<SubtitleCue> Parse(string text, SubtitleSidecarFormat format)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        if (text.Length > 10_000_000) throw new ArgumentException("Subtitle file is too large.");
        string normalized = text.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n');
        if (format == SubtitleSidecarFormat.WebVtt && !normalized.StartsWith("WEBVTT", StringComparison.Ordinal))
            throw new FormatException("A WebVTT file must start with WEBVTT.");
        var cues = new List<SubtitleCue>();
        foreach (string block in Regex.Split(normalized.Trim(), @"\n[ \t]*\n"))
        {
            string[] lines = block.Split('\n');
            if (lines[0].StartsWith("WEBVTT", StringComparison.Ordinal) ||
                lines[0].StartsWith("NOTE", StringComparison.Ordinal) || lines[0] is "STYLE" or "REGION") continue;
            int timing = Array.FindIndex(lines, line => line.Contains("-->", StringComparison.Ordinal));
            if (timing < 0) throw new FormatException("A subtitle cue has no timestamp interval.");
            Match match = Regex.Match(lines[timing], @"^(?<start>(?:\d+:)?\d{2}:\d{2}[.,]\d{3})\s+-->\s+(?<end>(?:\d+:)?\d{2}:\d{2}[.,]\d{3})(?:\s+.*)?$");
            if (!match.Success) throw new FormatException("Invalid subtitle timestamp.");
            TimeSpan start = ParseTimestamp(match.Groups["start"].Value);
            TimeSpan end = ParseTimestamp(match.Groups["end"].Value);
            string content = string.Join('\n', lines.Skip(timing + 1));
            Match voice = Regex.Match(content, @"^<v(?:\.[^ >]+)?\s+(?<speaker>[^>]+)>");
            string? speaker = voice.Success ? WebUtility.HtmlDecode(voice.Groups["speaker"].Value) : null;
            content = WebUtility.HtmlDecode(Regex.Replace(content, @"<[^>]*>", ""));
            if (end <= start || string.IsNullOrWhiteSpace(content)) throw new FormatException("Subtitle cues need text and a positive duration.");
            if (cues.Count > 0 && start < cues[^1].End) throw new FormatException("Overlapping subtitle cues must be resolved before import.");
            cues.Add(new SubtitleCue(start, end, content, speaker));
        }
        return cues;
    }

    private static TimeSpan ParseTimestamp(string value)
    {
        string[] parts = value.Replace(',', '.').Split(':');
        double seconds = double.Parse(parts[^1], CultureInfo.InvariantCulture);
        int minutes = int.Parse(parts[^2], CultureInfo.InvariantCulture);
        int hours = 0;
        if (parts.Length == 3 && !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hours))
            throw new FormatException("Subtitle hours are outside the supported time range.");
        if (seconds >= 60 || minutes >= 60) throw new FormatException("Minutes and seconds must be below 60.");
        double totalSeconds = hours * 3600d + minutes * 60 + seconds;
        if (totalSeconds >= TimeSpan.MaxValue.TotalSeconds) throw new FormatException("Subtitle timestamp is outside the supported time range.");
        return TimeSpan.FromSeconds(totalSeconds);
    }
    private static string EscapeText(string value) => value.Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
    private static string Timestamp(TimeSpan time, SubtitleSidecarFormat format)
    {
        long milliseconds = (long)Math.Round(time.TotalMilliseconds, MidpointRounding.AwayFromZero);
        string separator = format == SubtitleSidecarFormat.Srt ? "," : ".";
        return string.Create(CultureInfo.InvariantCulture, $"{milliseconds / 3600000:00}:{milliseconds / 60000 % 60:00}:{milliseconds / 1000 % 60:00}{separator}{milliseconds % 1000:000}");
    }
    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
