using System.Globalization;
using System.Text;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Studio.Editing;

namespace ReplayFoundry.Desktop.Media.Subtitles;

internal static class StudioTimedTextScript
{
    internal static string Build(IReadOnlyList<StudioTimedTextOverlay> overlays, GenerationClipOutputProfile profile,
        TimeSpan sourceStart, TimeSpan sourceEnd)
    {
        var result = new StringBuilder();
        result.AppendLine("[Script Info]").AppendLine("ScriptType: v4.00+")
            .AppendLine($"PlayResX: {profile.Width}").AppendLine($"PlayResY: {profile.Height}")
            .AppendLine("WrapStyle: 0").AppendLine("ScaledBorderAndShadow: yes")
            .AppendLine("[V4+ Styles]")
            .AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding")
            .AppendLine("Style: Text,Arial,48,&H00FFFFFF,&H00FFFFFF,&H00000000,&H80000000,-1,0,0,0,100,100,0,0,1,3,1,5,48,48,48,1")
            .AppendLine("[Events]")
            .AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
        foreach (StudioTimedTextOverlay overlay in overlays)
        {
            TimeSpan start = overlay.SourceStart > sourceStart ? overlay.SourceStart : sourceStart;
            TimeSpan end = overlay.SourceEnd < sourceEnd ? overlay.SourceEnd : sourceEnd;
            if (end <= start) continue;
            int x = (int)Math.Round(profile.Width * overlay.CenterXPercent / 100);
            int y = (int)Math.Round(profile.Height * overlay.CenterYPercent / 100);
            int size = Math.Max(8, (int)Math.Round(Math.Min(profile.Width, profile.Height) * overlay.FontSizePercent / 100));
            string text = overlay.Text.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("{", "\\{", StringComparison.Ordinal).Replace("}", "\\}", StringComparison.Ordinal)
                .ReplaceLineEndings("\\N").Replace("\t", " ", StringComparison.Ordinal);
            result.AppendLine($"Dialogue: 0,{Time(start - sourceStart)},{Time(end - sourceStart)},Text,,0,0,0,,{{\\an5\\pos({x},{y})\\fs{size}}}{text}");
        }
        return result.ToString();
    }
    private static string Time(TimeSpan time) => string.Create(CultureInfo.InvariantCulture,
        $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds / 10:00}");
}
