using System.Globalization;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Platform.Transcription;

internal static class WhisperCppJsonReader
{
    public static JsonElement FindSegments(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new WhisperCppTranscriptionException(
                "whisper.cpp output root must be an object.");
        }

        if (TryGetArray(root, "transcription", out JsonElement transcription))
        {
            return transcription;
        }

        if (TryGetArray(root, "segments", out JsonElement segments))
        {
            return segments;
        }

        throw new WhisperCppTranscriptionException(
            "whisper.cpp output does not contain a transcription or segments array.");
    }

    public static (TimeSpan Start, TimeSpan End) ReadTimes(JsonElement item)
    {
        if (item.TryGetProperty("timestamps", out JsonElement timestamps) &&
            timestamps.ValueKind == JsonValueKind.Object)
        {
            return (
                ParseTimestamp(RequiredString(timestamps, "from")),
                ParseTimestamp(RequiredString(timestamps, "to")));
        }

        if (item.TryGetProperty("start", out JsonElement start) &&
            item.TryGetProperty("end", out JsonElement end) &&
            start.TryGetDouble(out double startSeconds) &&
            end.TryGetDouble(out double endSeconds) &&
            double.IsFinite(startSeconds) &&
            double.IsFinite(endSeconds))
        {
            return (
                TimeSpan.FromSeconds(startSeconds),
                TimeSpan.FromSeconds(endSeconds));
        }

        throw new WhisperCppTranscriptionException(
            "A transcript segment is missing unambiguous timestamps.");
    }

    public static string RequiredString(JsonElement item, string propertyName) =>
        RequiredRawString(item, propertyName).Trim();

    public static string RequiredRawString(
        JsonElement item,
        string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new WhisperCppTranscriptionException(
                $"Transcript property '{propertyName}' must contain nonblank text.");
        }

        return property.GetString()!;
    }

    public static string RequiredRawTokenString(
        JsonElement item,
        string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            throw new WhisperCppTranscriptionException(
                $"Transcript token property '{propertyName}' must be a string.");
        }

        return property.GetString()!;
    }

    public static double? OptionalRatio(
        JsonElement item,
        string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (!property.TryGetDouble(out double value) ||
            !double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new WhisperCppTranscriptionException(
                $"Transcript property '{propertyName}' must be a ratio when supplied.");
        }

        return value;
    }

    public static AudioTranscriptionLanguage? ReadDetectedLanguage(
        JsonElement root)
    {
        AudioTranscriptionLanguage? direct = OptionalLanguage(root, "language");
        if (direct is not null)
        {
            return direct;
        }

        return root.TryGetProperty("result", out JsonElement result) &&
               result.ValueKind == JsonValueKind.Object
            ? OptionalLanguage(result, "language")
            : null;
    }

    public static AudioTranscriptionLanguage? OptionalLanguage(
        JsonElement item,
        string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new WhisperCppTranscriptionException(
                "Reported language must contain a nonblank code.");
        }

        return new AudioTranscriptionLanguage(property.GetString()!);
    }

    private static bool TryGetArray(
        JsonElement item,
        string propertyName,
        out JsonElement value) =>
        item.TryGetProperty(propertyName, out value) &&
        value.ValueKind == JsonValueKind.Array;

    private static TimeSpan ParseTimestamp(string value)
    {
        string normalized = value.Trim().Replace(',', '.');
        string[] formats =
        [
            @"hh\:mm\:ss\.FFFFFFF",
            @"h\:mm\:ss\.FFFFFFF",
            "c",
        ];
        if (TimeSpan.TryParseExact(
                normalized,
                formats,
                CultureInfo.InvariantCulture,
                out TimeSpan result))
        {
            return result;
        }

        throw new WhisperCppTranscriptionException(
            $"Invalid transcript timestamp '{value}'.");
    }
}
