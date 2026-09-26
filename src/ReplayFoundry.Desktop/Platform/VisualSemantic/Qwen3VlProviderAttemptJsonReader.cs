using System.Globalization;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlProviderAttemptJsonReader
{
    internal static JsonDocument Open(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw Failure(
                "The provider-attempt batch is empty.");
        }

        return JsonDocument.Parse(
            json,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 128,
            });
    }

    internal static void Exact(
        JsonElement value,
        string path,
        params string[] expected)
    {
        RequireObject(value, path);
        string[] actual =
            value.EnumerateObject()
                .Select(static property => property.Name)
                .ToArray();

        if (actual.Length != expected.Length ||
            actual.Distinct(StringComparer.Ordinal).Count() !=
                actual.Length ||
            expected.Any(
                name =>
                    !actual.Contains(
                        name,
                        StringComparer.Ordinal)))
        {
            throw Failure(
                $"{path} must contain exactly: {string.Join(", ", expected)}.");
        }
    }

    internal static JsonElement Property(
        JsonElement value,
        string property,
        string path)
    {
        if (!value.TryGetProperty(
                property,
                out JsonElement result))
        {
            throw Failure(
                $"{path}.{property} is required.");
        }

        return result;
    }

    internal static JsonElement[] Array(
        JsonElement value,
        string property,
        string path)
    {
        JsonElement result = Property(value, property, path);

        if (result.ValueKind != JsonValueKind.Array)
        {
            throw Failure(
                $"{path}.{property} must be an array.");
        }

        return result.EnumerateArray().ToArray();
    }

    internal static string Text(
        JsonElement value,
        string property,
        string path,
        int maximumLength)
    {
        JsonElement result = Property(value, property, path);

        if (result.ValueKind != JsonValueKind.String)
        {
            throw Failure(
                $"{path}.{property} must be a string.");
        }

        string text = result.GetString()!;

        if (string.IsNullOrWhiteSpace(text) ||
            text.Length > maximumLength ||
            !string.Equals(
                text,
                text.Trim(),
                StringComparison.Ordinal))
        {
            throw Failure(
                $"{path}.{property} is blank, untrimmed, or too long.");
        }

        return text;
    }

    internal static string? NullableText(
        JsonElement value,
        string property,
        string path,
        int maximumLength)
    {
        JsonElement result = Property(value, property, path);
        return result.ValueKind == JsonValueKind.Null
            ? null
            : Text(value, property, path, maximumLength);
    }

    internal static int Integer(
        JsonElement value,
        string property,
        string path)
    {
        JsonElement result = Property(value, property, path);

        if (result.ValueKind != JsonValueKind.Number ||
            !result.TryGetInt32(out int number))
        {
            throw Failure(
                $"{path}.{property} must be an Int32.");
        }

        return number;
    }

    internal static long? NullableInt64(
        JsonElement value,
        string property,
        string path)
    {
        JsonElement result = Property(value, property, path);

        if (result.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (result.ValueKind != JsonValueKind.Number ||
            !result.TryGetInt64(out long number))
        {
            throw Failure(
                $"{path}.{property} must be null or an Int64.");
        }

        return number;
    }

    internal static double FiniteDouble(
        JsonElement value,
        string property,
        string path)
    {
        JsonElement result = Property(value, property, path);

        if (result.ValueKind != JsonValueKind.Number ||
            !result.TryGetDouble(out double number) ||
            !double.IsFinite(number))
        {
            throw Failure(
                $"{path}.{property} must be finite.");
        }

        return number;
    }

    internal static TimeSpan? NullableSeconds(
        JsonElement value,
        string property,
        string path)
    {
        JsonElement result = Property(value, property, path);
        return result.ValueKind == JsonValueKind.Null
            ? null
            : Seconds(
                FiniteDouble(value, property, path),
                $"{path}.{property}");
    }

    internal static TimeSpan Seconds(
        double value,
        string path)
    {
        if (value < 0)
        {
            throw Failure(
                $"{path} cannot be negative.");
        }

        return TimeSpan.FromSeconds(value);
    }

    internal static T EnumValue<T>(
        JsonElement value,
        string property,
        string path)
        where T : struct, Enum
    {
        string text = Text(value, property, path, 128);

        if (!Enum.TryParse(text, ignoreCase: false, out T result) ||
            !Enum.IsDefined(result))
        {
            throw Failure(
                $"{path}.{property} contains an unsupported value.");
        }

        return result;
    }

    internal static string LowerSha256(
        JsonElement value,
        string property,
        string path)
    {
        string result = Text(value, property, path, 64);

        if (result.Length != 64 ||
            result.Any(
                static character =>
                    !char.IsDigit(character) &&
                    character is not (
                        >= 'a' and <= 'f'
                    )))
        {
            throw Failure(
                $"{path}.{property} must be a lowercase SHA-256 value.");
        }

        return result;
    }

    internal static string? NullableLowerSha256(
        JsonElement value,
        string property,
        string path)
    {
        JsonElement result = Property(value, property, path);
        return result.ValueKind == JsonValueKind.Null
            ? null
            : LowerSha256(value, property, path);
    }

    internal static void RequireEqual(
        string actual,
        string expected,
        string path)
    {
        if (!string.Equals(
                actual,
                expected,
                StringComparison.Ordinal))
        {
            throw Failure(
                $"{path} does not match the trusted request or protocol.");
        }
    }

    internal static void RequireObject(
        JsonElement value,
        string path)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Failure($"{path} must be an object.");
        }
    }

    internal static void RequireObjectOrNull(
        JsonElement value,
        string path)
    {
        if (value.ValueKind is not (
                JsonValueKind.Object or
                JsonValueKind.Null
            ))
        {
            throw Failure(
                $"{path} must be an object or null.");
        }
    }

    internal static void RequireNull(
        JsonElement value,
        string path)
    {
        if (value.ValueKind != JsonValueKind.Null)
        {
            throw Failure($"{path} must be null.");
        }
    }

    internal static Qwen3VlOutputParseException Failure(
        string message) =>
        new(message);
}
