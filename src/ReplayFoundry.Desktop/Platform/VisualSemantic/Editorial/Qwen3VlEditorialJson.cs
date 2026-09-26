using System.Globalization;
using System.Text.Json;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlEditorialJson
{
    public static void RequireCanonicalHash(JsonElement value)
    {
        string expected = Text(value, "canonicalHash");
        string actual = Qwen3VlCanonicalJson.ComputeObjectSha256(
            value,
            "canonicalHash");
        if (!string.Equals(
                expected,
                actual,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Failure("Prompt 2.0 canonical hash is invalid.");
        }
    }

    public static void Exact(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Failure("Prompt 2.0 JSON must be an object.");
        }
        string[] actual = value.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        string[] expected = names
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw Failure(
                "Prompt 2.0 JSON contains missing, duplicate, or extra properties.");
        }
    }

    public static JsonElement Property(
        JsonElement parent,
        string name) =>
        parent.TryGetProperty(name, out JsonElement value)
            ? value
            : throw Failure($"Prompt 2.0 property '{name}' is missing.");

    public static JsonElement Object(
        JsonElement parent,
        string name)
    {
        JsonElement value = Property(parent, name);
        return value.ValueKind == JsonValueKind.Object
            ? value
            : throw Failure($"Prompt 2.0 '{name}' must be an object.");
    }

    public static JsonElement[] Array(
        JsonElement parent,
        string name)
    {
        JsonElement value = Property(parent, name);
        return value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray()
            : throw Failure($"Prompt 2.0 '{name}' must be an array.");
    }

    public static string Text(JsonElement parent, string name)
    {
        JsonElement value = Property(parent, name);
        string? result = value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
        return !string.IsNullOrWhiteSpace(result) &&
               result == result.Trim()
            ? result
            : throw Failure($"Prompt 2.0 '{name}' must be trimmed text.");
    }

    public static string? NullableText(
        JsonElement parent,
        string name) =>
        Property(parent, name).ValueKind == JsonValueKind.Null
            ? null
            : Text(parent, name);

    public static int Integer(JsonElement parent, string name) =>
        Property(parent, name).TryGetInt32(out int value)
            ? value
            : throw Failure($"Prompt 2.0 '{name}' must be an integer.");

    public static long? NullableInt64(
        JsonElement parent,
        string name)
    {
        JsonElement value = Property(parent, name);
        return value.ValueKind == JsonValueKind.Null
            ? null
            : value.TryGetInt64(out long result)
                ? result
                : throw Failure(
                    $"Prompt 2.0 '{name}' must be an integer or null.");
    }

    public static double Finite(JsonElement parent, string name)
    {
        JsonElement value = Property(parent, name);
        return value.TryGetDouble(out double result) &&
               double.IsFinite(result) &&
               result >= 0
            ? result
            : throw Failure(
                $"Prompt 2.0 '{name}' must be finite non-negative seconds.");
    }

    public static TimeSpan Seconds(JsonElement parent, string name) =>
        TimeSpan.FromSeconds(Finite(parent, name));

    public static TimeSpan FiniteSeconds(
        JsonElement value,
        string path) =>
        value.TryGetDouble(out double result) &&
        double.IsFinite(result) &&
        result >= 0
            ? TimeSpan.FromSeconds(result)
            : throw Failure($"{path} must be finite non-negative seconds.");

    public static string Sha256(JsonElement parent, string name)
    {
        string value = Text(parent, name);
        return value.Length == 64 &&
               value.All(Uri.IsHexDigit)
            ? value
            : throw Failure($"Prompt 2.0 '{name}' must be SHA-256.");
    }

    public static string? NullableSha256(
        JsonElement parent,
        string name) =>
        Property(parent, name).ValueKind == JsonValueKind.Null
            ? null
            : Sha256(parent, name);

    public static DateTimeOffset Utc(
        JsonElement parent,
        string name) =>
        DateTimeOffset.TryParse(
            Text(parent, name),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset value) &&
        value.Offset == TimeSpan.Zero
            ? value
            : throw Failure($"Prompt 2.0 '{name}' must be UTC.");

    public static T EnumValue<T>(JsonElement parent, string name)
        where T : struct, Enum =>
        Enum.TryParse(
            Text(parent, name),
            ignoreCase: false,
            out T value) &&
        Enum.IsDefined(value)
            ? value
            : throw Failure($"Prompt 2.0 '{name}' is undefined.");

    public static void Required(
        JsonElement parent,
        string name,
        string expected,
        bool ignoreCase = false)
    {
        if (!string.Equals(
                Text(parent, name),
                expected,
                ignoreCase
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            throw Failure(
                $"Prompt 2.0 '{name}' differs from the frozen identity.");
        }
    }

    public static void RequireNull(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Null)
        {
            throw Failure(
                $"Prompt 2.0 '{name}' must be null for this outcome.");
        }
    }

    public static Qwen3VlOutputParseException Failure(string message) =>
        new(message);
}
