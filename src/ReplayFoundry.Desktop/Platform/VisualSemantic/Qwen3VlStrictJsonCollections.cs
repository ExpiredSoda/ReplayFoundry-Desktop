using System.Text.Json;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonPrimitives;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlStrictJsonCollections
{
    internal static T RequireEnum<T>(
        JsonElement parent,
        string name,
        string path)
        where T : struct, Enum
    {
        string value = RequireString(parent, name, path, 128);

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
                $"{path}.{name} contains unsupported {typeof(T).Name} value '{value}'.");
        }

        return result;
    }

    internal static T RequireEnumValue<T>(
        JsonElement value,
        string path)
        where T : struct, Enum
    {
        string? text =
            value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        if (string.IsNullOrWhiteSpace(text) ||
            !string.Equals(
                text,
                text.Trim(),
                StringComparison.Ordinal) ||
            !Enum.TryParse(
                text,
                ignoreCase: false,
                out T result) ||
            !Enum.IsDefined(result) ||
            !string.Equals(
                Enum.GetName(result),
                text,
                StringComparison.Ordinal))
        {
            throw Failure(
                $"{path} contains an unsupported {typeof(T).Name} value.");
        }

        return result;
    }

    internal static string RequireLowerSha256(
        JsonElement parent,
        string name,
        string path)
    {
        string value =
            RequireUntrimmedString(
                parent,
                name,
                path,
                64);

        if (value.Length != 64 ||
            value.Any(
                static character =>
                    !Uri.IsHexDigit(character)) ||
            !string.Equals(
                value,
                value.ToLowerInvariant(),
                StringComparison.Ordinal))
        {
            throw Failure(
                $"{path}.{name} must be a lowercase SHA-256 value.");
        }

        return value;
    }

    internal static int[] RequireInt32Array(
        JsonElement parent,
        string name,
        string path) =>
        RequireArray(parent, name, path)
            .Select(
                (value, index) =>
                {
                    if (value.ValueKind !=
                            JsonValueKind.Number ||
                        !value.TryGetInt32(
                            out int result))
                    {
                        throw Failure(
                            $"{path}.{name}[{index}] must be an integer.");
                    }

                    return result;
                })
            .ToArray();

    internal static double[] RequireDoubleArray(
        JsonElement parent,
        string name,
        string path) =>
        RequireArray(parent, name, path)
            .Select(
                (value, index) =>
                {
                    if (value.ValueKind !=
                            JsonValueKind.Number ||
                        !value.TryGetDouble(
                            out double result) ||
                        !double.IsFinite(result))
                    {
                        throw Failure(
                            $"{path}.{name}[{index}] must be a finite number.");
                    }

                    return result;
                })
            .ToArray();

    internal static string[] RequireSha256Array(
        JsonElement parent,
        string name,
        string path) =>
        RequireArray(parent, name, path)
            .Select(
                (value, index) =>
                {
                    if (value.ValueKind !=
                            JsonValueKind.String ||
                        value.GetString() is not
                        { } text ||
                        text.Length != 64 ||
                        text.Any(
                            static character =>
                                !Uri.IsHexDigit(
                                    character)) ||
                        !string.Equals(
                            text,
                            text.ToLowerInvariant(),
                            StringComparison.Ordinal))
                    {
                        throw Failure(
                            $"{path}.{name}[{index}] must be a lowercase SHA-256 value.");
                    }

                    return text;
                })
            .ToArray();
}
