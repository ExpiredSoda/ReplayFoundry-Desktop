using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ReplayFoundry.Desktop.Features.Generate.Guidance;

public static class MomentMarkerImporter
{
    public static IReadOnlyList<UserMomentGuidance> ParseCsv(string text, string sourcePath, TimeSpan sourceDuration)
    {
        var rows = CsvRows(text);
        if (rows.Count == 0) return [];
        string[] header = rows[0].Select(s => s.Trim().ToLowerInvariant()).ToArray();
        int start = Array.FindIndex(header, s => s is "timestamp" or "time" or "start" or "start_time" or "timecode");
        int end = Array.FindIndex(header, s => s is "end" or "end_time");
        int duration = Array.FindIndex(header, s => s is "duration" or "duration_seconds");
        bool hasHeader = start >= 0;
        if (rows.Count - (hasHeader ? 1 : 0) > 1000) throw new FormatException("Import at most 1,000 markers at once.");
        start = Math.Max(0, start);
        var result = new List<UserMomentGuidance>();
        foreach (string[] row in rows.Skip(hasHeader ? 1 : 0))
        {
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            if (row.Length <= start) throw new FormatException("A marker row is missing its timestamp.");
            TimeSpan from = ParseTime(row[start]);
            if (from > sourceDuration) throw new ArgumentOutOfRangeException(nameof(text), "The marker starts beyond the recording.");
            TimeSpan? to = end >= 0 && end < row.Length && !string.IsNullOrWhiteSpace(row[end]) ? ParseTime(row[end]) : null;
            if (to is null && duration >= 0 && duration < row.Length && !string.IsNullOrWhiteSpace(row[duration]))
            {
                TimeSpan length = ParseTime(row[duration]);
                if (length > sourceDuration - from) throw new ArgumentOutOfRangeException(nameof(text), "The marker range ends beyond the recording.");
                to = from + length;
            }
            result.Add(to is { } finish && finish != from ? UserMomentGuidance.CreateRange(sourcePath, sourceDuration, from, finish) :
                UserMomentGuidance.CreatePoint(sourcePath, sourceDuration, from));
        }
        return result.DistinctBy(s => s.Id).OrderBy(s => s.Start).ToArray();
    }
    public static IReadOnlyList<UserMomentGuidance> ParseChapterJson(string json, string sourcePath, TimeSpan sourceDuration)
    {
        if (json.Length > 1_000_000) throw new ArgumentException("Marker files must be smaller than 1 MB.");
        using var document = JsonDocument.Parse(json);
        JsonElement chapters;
        if (document.RootElement.ValueKind == JsonValueKind.Array) chapters = document.RootElement;
        else if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("chapters", out chapters))
            throw new FormatException("Chapter JSON needs a chapters array.");
        if (chapters.ValueKind != JsonValueKind.Array || chapters.GetArrayLength() > 1000) throw new FormatException("Chapter JSON must contain at most 1,000 chapters.");
        return chapters.EnumerateArray().Select(chapter =>
        {
            if (chapter.ValueKind != JsonValueKind.Object) throw new FormatException("Each chapter must be an object with a start time.");
            if (!chapter.TryGetProperty("start_time", out var start) && !chapter.TryGetProperty("timestamp", out start))
                throw new FormatException("Each chapter needs start_time in seconds.");
            return UserMomentGuidance.CreatePoint(sourcePath, sourceDuration, ParseTime(start.ToString()));
        }).DistinctBy(s => s.Id).OrderBy(s => s.Start).ToArray();
    }
    private static TimeSpan ParseTime(string text)
    {
        text = text.Trim();
        double seconds;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
        {
            if (!double.IsFinite(seconds) || seconds < 0 || seconds >= TimeSpan.MaxValue.TotalSeconds)
                throw new FormatException("Marker times must be finite, nonnegative, and within the recording.");
            return TimeSpan.FromSeconds(seconds);
        }
        string[] parts = text.Split(':');
        if (parts.Length is not (2 or 3) || parts.Any(p => !double.TryParse(p, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _)))
            throw new FormatException("Use seconds, MM:SS.mmm, or HH:MM:SS.mmm for marker times.");
        double tail = double.Parse(parts[^1], CultureInfo.InvariantCulture);
        double minutes = double.Parse(parts[^2], CultureInfo.InvariantCulture);
        double hours = parts.Length == 3 ? double.Parse(parts[0], CultureInfo.InvariantCulture) : 0;
        double total = hours * 3600 + minutes * 60 + tail;
        if (!double.IsFinite(total) || total >= TimeSpan.MaxValue.TotalSeconds || tail >= 60 || minutes >= 60 ||
            minutes != Math.Floor(minutes) || hours != Math.Floor(hours))
            throw new FormatException("Invalid marker timecode.");
        return TimeSpan.FromSeconds(total);
    }
    private static IReadOnlyList<string[]> CsvRows(string text)
    {
        if (text.Length > 1_000_000) throw new ArgumentException("Marker files must be smaller than 1 MB.");
        var rows = new List<string[]>(); var cells = new List<string>(); var field = new StringBuilder(); bool quoted = false;
        text = text.TrimStart('\uFEFF');
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"') { if (quoted && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; } else quoted = !quoted; }
            else if (c == ',' && !quoted) { cells.Add(field.ToString()); field.Clear(); }
            else if ((c is '\r' or '\n') && !quoted)
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                cells.Add(field.ToString()); field.Clear(); rows.Add(cells.ToArray()); cells.Clear();
                if (rows.Count > 1001) throw new FormatException("Import at most 1,000 markers at once.");
            }
            else field.Append(c);
        }
        if (quoted) throw new FormatException("A quoted CSV field is not closed.");
        if (field.Length > 0 || cells.Count > 0) { cells.Add(field.ToString()); rows.Add(cells.ToArray()); }
        return rows;
    }
}
