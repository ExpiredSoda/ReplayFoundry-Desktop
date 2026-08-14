using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlHostFailureParserValidation;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlHostFailureJsonReader
{
    internal static Qwen3VlHostCommand ParseCommand(string value) =>
        value switch
        {
            "probe" => Qwen3VlHostCommand.Probe,
            "run" => Qwen3VlHostCommand.Run,
            "run-qualified-editorial-batch" =>
                Qwen3VlHostCommand.Run,
            "run-grounded-editorial-metadata-batch" =>
                Qwen3VlHostCommand.Run,
            "audit-video-sampling" =>
                Qwen3VlHostCommand.AuditVideoSampling,
            _ => throw Failure(
                $"$.command contains unsupported value '{value}'."),
        };

    internal static JsonDocument Open(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw Failure(
                "The structured host failure envelope is empty.");
        }

        try
        {
            return JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling =
                        JsonCommentHandling.Disallow,
                    MaxDepth = 32,
                });
        }
        catch (JsonException exception)
        {
            throw Failure(
                "The structured host failure envelope is not valid JSON.",
                exception);
        }
    }

    internal static void Exact(
        JsonElement value,
        string path,
        params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Failure($"{path} must be an object.");
        }

        string[] actual =
            value.EnumerateObject()
                .Select(static item => item.Name)
                .OrderBy(static item => item, StringComparer.Ordinal)
                .ToArray();
        string[] canonical =
            expected.OrderBy(
                    static item => item,
                    StringComparer.Ordinal)
                .ToArray();

        if (!actual.SequenceEqual(
                canonical,
                StringComparer.Ordinal))
        {
            throw Failure(
                $"{path} contains missing, extra, or unsupported properties.");
        }
    }

    internal static JsonElement Property(
        JsonElement parent,
        string name,
        string path)
    {
        if (!parent.TryGetProperty(
                name,
                out JsonElement value))
        {
            throw Failure($"{path}.{name} is required.");
        }

        return value;
    }

    internal static JsonElement Object(
        JsonElement parent,
        string name,
        string path)
    {
        JsonElement value =
            Property(parent, name, path);

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Failure($"{path}.{name} must be an object.");
        }

        return value;
    }

    internal static string Text(
        JsonElement parent,
        string name,
        string path,
        int maximumLength)
    {
        JsonElement value =
            Property(parent, name, path);

        if (value.ValueKind != JsonValueKind.String)
        {
            throw Failure($"{path}.{name} must be text.");
        }

        string? text = value.GetString();

        if (string.IsNullOrWhiteSpace(text) ||
            text.Length > maximumLength ||
            !string.Equals(
                text,
                text.Trim(),
                StringComparison.Ordinal))
        {
            throw Failure(
                $"{path}.{name} must be bounded, nonblank, untrimmed text.");
        }

        return text;
    }

    internal static string? NullableText(
        JsonElement parent,
        string name,
        string path,
        int maximumLength)
    {
        JsonElement value =
            Property(parent, name, path);

        return value.ValueKind == JsonValueKind.Null
            ? null
            : Text(parent, name, path, maximumLength);
    }

    internal static int Integer(
        JsonElement parent,
        string name,
        string path)
    {
        JsonElement value =
            Property(parent, name, path);

        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out int result))
        {
            throw Failure($"{path}.{name} must be an integer.");
        }

        return result;
    }

    internal static int? NullableInteger(
        JsonElement parent,
        string name,
        string path)
    {
        JsonElement value =
            Property(parent, name, path);

        return value.ValueKind == JsonValueKind.Null
            ? null
            : Integer(parent, name, path);
    }

    internal static bool Boolean(
        JsonElement parent,
        string name,
        string path)
    {
        JsonElement value =
            Property(parent, name, path);

        if (value.ValueKind is not (
                JsonValueKind.True or
                JsonValueKind.False))
        {
            throw Failure(
                $"{path}.{name} must be a boolean.");
        }

        return value.GetBoolean();
    }

    internal static long Int64(
        JsonElement parent,
        string name,
        string path)
    {
        JsonElement value =
            Property(parent, name, path);

        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out long result))
        {
            throw Failure($"{path}.{name} must be an integer.");
        }

        return result;
    }

    internal static double Number(
        JsonElement parent,
        string name,
        string path)
    {
        JsonElement value =
            Property(parent, name, path);

        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out double result) ||
            !double.IsFinite(result))
        {
            throw Failure(
                $"{path}.{name} must be a finite number.");
        }

        return result;
    }

    internal static double? NullableNumber(
        JsonElement parent,
        string name,
        string path)
    {
        JsonElement value =
            Property(parent, name, path);

        return value.ValueKind == JsonValueKind.Null
            ? null
            : Number(parent, name, path);
    }

    internal static TimeSpan Time(
        JsonElement parent,
        string name,
        string path) =>
        Seconds(
            SecondsValue(parent, name, path),
            $"{path}.{name}");

    internal static double SecondsValue(
        JsonElement parent,
        string name,
        string path)
    {
        double value = Number(parent, name, path);

        if (value < 0 ||
            value > TimeSpan.MaxValue.TotalSeconds)
        {
            throw Failure(
                $"{path}.{name} must be finite, non-negative seconds.");
        }

        return value;
    }

    internal static TimeSpan Seconds(
        double value,
        string path)
    {
        if (value < 0 ||
            value > TimeSpan.MaxValue.TotalSeconds)
        {
            throw Failure(
                $"{path} must be finite, non-negative seconds.");
        }

        return TimeSpan.FromSeconds(value);
    }

    internal static string Hash(
        JsonElement parent,
        string name,
        string path)
    {
        string value =
            Text(parent, name, path, 64);

        if (value.Length != 64 ||
            value.Any(
                static character =>
                    !Uri.IsHexDigit(character)))
        {
            throw Failure(
                $"{path}.{name} must be a SHA-256 value.");
        }

        return value;
    }

    internal static string? NullableHash(
        JsonElement parent,
        string name,
        string path)
    {
        JsonElement value =
            Property(parent, name, path);

        return value.ValueKind == JsonValueKind.Null
            ? null
            : Hash(parent, name, path);
    }

    internal static T EnumValue<T>(
        JsonElement parent,
        string name,
        string path)
        where T : struct, Enum
    {
        string value =
            Text(parent, name, path, 128);

        if (!Enum.TryParse(
                value,
                ignoreCase: false,
                out T result) ||
            !Enum.IsDefined(result) ||
            !string.Equals(
                Enum.GetName(result),
                value,
                StringComparison.Ordinal))
        {
            throw Failure(
                $"{path}.{name} contains unsupported {typeof(T).Name} value.");
        }

        return result;
    }
}
