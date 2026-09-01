using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlCanonicalJson
{
    public static string ComputeObjectSha256(
        JsonElement value,
        string excludedTopLevelProperty)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new Qwen3VlOutputParseException(
                "A canonical Qwen identity must be calculated from an object.");
        }

        var canonical = new StringBuilder(16 * 1024);
        AppendObject(
            canonical,
            value,
            excludedTopLevelProperty);

        return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        canonical.ToString())))
            .ToLowerInvariant();
    }

    private static void Append(
        StringBuilder output,
        JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                AppendObject(
                    output,
                    value,
                    excludedProperty: null);
                break;

            case JsonValueKind.Array:
                output.Append('[');
                int index = 0;

                foreach (JsonElement item in
                         value.EnumerateArray())
                {
                    if (index++ > 0)
                    {
                        output.Append(',');
                    }

                    Append(output, item);
                }

                output.Append(']');
                break;

            case JsonValueKind.String:
                AppendString(
                    output,
                    value.GetString()!);
                break;

            case JsonValueKind.Number:
                output.Append(value.GetRawText());
                break;

            case JsonValueKind.True:
                output.Append("true");
                break;

            case JsonValueKind.False:
                output.Append("false");
                break;

            case JsonValueKind.Null:
                output.Append("null");
                break;

            default:
                throw new Qwen3VlOutputParseException(
                    "Canonical Qwen JSON contains an unsupported token.");
        }
    }

    private static void AppendObject(
        StringBuilder output,
        JsonElement value,
        string? excludedProperty)
    {
        output.Append('{');
        JsonProperty[] properties =
            value.EnumerateObject()
                .Where(
                    property =>
                        !string.Equals(
                            property.Name,
                            excludedProperty,
                            StringComparison.Ordinal))
                .OrderBy(
                    static property =>
                        property.Name,
                    StringComparer.Ordinal)
                .ToArray();

        for (int index = 0;
             index < properties.Length;
             index++)
        {
            if (index > 0)
            {
                output.Append(',');
            }

            AppendString(
                output,
                properties[index].Name);
            output.Append(':');
            Append(
                output,
                properties[index].Value);
        }

        output.Append('}');
    }

    private static void AppendString(
        StringBuilder output,
        string value)
    {
        output.Append('"');

        for (int index = 0;
             index < value.Length;
             index++)
        {
            char character = value[index];

            switch (character)
            {
                case '"':
                    output.Append("\\\"");
                    break;
                case '\\':
                    output.Append("\\\\");
                    break;
                case '\b':
                    output.Append("\\b");
                    break;
                case '\t':
                    output.Append("\\t");
                    break;
                case '\n':
                    output.Append("\\n");
                    break;
                case '\f':
                    output.Append("\\f");
                    break;
                case '\r':
                    output.Append("\\r");
                    break;
                default:
                    if (character < ' ')
                    {
                        output.Append("\\u");
                        output.Append(
                            ((int)character).ToString("x4"));
                    }
                    else if (char.IsHighSurrogate(character))
                    {
                        if (index + 1 >= value.Length ||
                            !char.IsLowSurrogate(
                                value[index + 1]))
                        {
                            throw new Qwen3VlOutputParseException(
                                "Canonical Qwen JSON contains an unpaired UTF-16 surrogate.");
                        }

                        output.Append(character);
                        output.Append(value[++index]);
                    }
                    else if (char.IsLowSurrogate(character))
                    {
                        throw new Qwen3VlOutputParseException(
                            "Canonical Qwen JSON contains an unpaired UTF-16 surrogate.");
                    }
                    else
                    {
                        output.Append(character);
                    }

                    break;
            }
        }

        output.Append('"');
    }
}
