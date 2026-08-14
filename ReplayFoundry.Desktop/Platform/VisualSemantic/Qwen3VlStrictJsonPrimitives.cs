using System.Text.Json;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlStrictJsonPrimitives
{
    internal static JsonDocument Open(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new JsonException("Structured host output is empty.");
        }

        return JsonDocument.Parse(
            json,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64,
            });
    }

    internal static void RequireExactProperties(
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
                .Select(static property => property.Name)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();
        string[] expectedOrdered =
            expected
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();

        if (!actual.SequenceEqual(
                expectedOrdered,
                StringComparer.Ordinal))
        {
            throw Failure(
                $"{path} contains missing, extra, or unsupported properties.");
        }
    }

    internal static JsonElement RequireObject(
        JsonElement parent,
        string name,
        string path)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Object)
        {
            throw Failure($"{path}.{name} must be an object.");
        }

        return value;
    }

    internal static JsonElement[] RequireArray(
        JsonElement parent,
        string name,
        string path)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            throw Failure($"{path}.{name} must be an array.");
        }

        return value.EnumerateArray().ToArray();
    }

    internal static string RequireString(
        JsonElement parent,
        string name,
        string path,
        int maximumLength)
    {
        if (!parent.TryGetProperty(name, out JsonElement value))
        {
            throw Failure($"{path}.{name} is required.");
        }

        return RequireStringValue(
            value,
            $"{path}.{name}",
            maximumLength);
    }

    internal static string RequireStringValue(
        JsonElement value,
        string path,
        int maximumLength)
    {
        if (value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()) ||
            value.GetString()!.Length > maximumLength ||
            !string.Equals(
                value.GetString(),
                value.GetString()!.Trim(),
                StringComparison.Ordinal))
        {
            throw Failure(
                $"{path} must be nonblank text no longer than {maximumLength} characters.");
        }

        return value.GetString()!;
    }

    internal static string RequireUntrimmedString(
        JsonElement parent,
        string name,
        string path,
        int exactOrMaximumLength)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String)
        {
            throw Failure($"{path}.{name} must be text.");
        }

        string? result = value.GetString();

        if (string.IsNullOrEmpty(result) ||
            result.Length > exactOrMaximumLength ||
            !string.Equals(
                result,
                result.Trim(),
                StringComparison.Ordinal))
        {
            throw Failure(
                $"{path}.{name} must be untrimmed nonempty text no longer than {exactOrMaximumLength} characters.");
        }

        return result;
    }

    internal static string? RequireNullableString(
        JsonElement parent,
        string name,
        string path,
        int maximumLength)
    {
        if (!parent.TryGetProperty(name, out JsonElement value))
        {
            throw Failure($"{path}.{name} is required.");
        }

        return value.ValueKind == JsonValueKind.Null
            ? null
            : RequireStringValue(
                value,
                $"{path}.{name}",
                maximumLength);
    }

    internal static Qwen3VlOutputParseException Failure(string message) =>
        new(message);
}
