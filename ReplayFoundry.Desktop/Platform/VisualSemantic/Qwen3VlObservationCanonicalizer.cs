using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonPrimitives;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlObservationCanonicalizer
{
    private static readonly string[] CanonicalObservationPropertyOrder =
    [
        "candidateId",
        "caseId",
        "conciseRationale",
        "evidenceIntervals",
        "hasClearBeginning",
        "hasClearOutcome",
        "limitations",
        "menuOrTraversalPresent",
        "observableContentType",
        "reviewCertainty",
        "schemaVersion",
        "spokenContentAppearsRelevant",
        "suggestedWorthReviewing",
        "uncertainties",
        "visibleStateChange",
    ];

    private static readonly string[] ProhibitedReasoningFragments =
    [
        "chain of thought",
        "chain-of-thought",
        "step-by-step reasoning",
        "step by step reasoning",
        "my hidden reasoning",
        "internal reasoning",
    ];

    internal static string ComputeCanonicalObservationSha256(
        JsonElement observation,
        string? caseIdOverride = null,
        string? candidateIdOverride = null)
    {
        var canonical = new StringBuilder(2048);
        canonical.Append('{');

        for (int index = 0;
             index < CanonicalObservationPropertyOrder.Length;
             index++)
        {
            string propertyName =
                CanonicalObservationPropertyOrder[index];

            if (!observation.TryGetProperty(
                    propertyName,
                    out JsonElement value))
            {
                throw Failure(
                    $"The canonical observation is missing '{propertyName}'.");
            }

            if (index > 0)
            {
                canonical.Append(',');
            }

            AppendPythonJsonString(
                canonical,
                propertyName);
            canonical.Append(':');

            if (caseIdOverride is not null &&
                string.Equals(
                    propertyName,
                    "caseId",
                    StringComparison.Ordinal))
            {
                AppendPythonJsonString(
                    canonical,
                    caseIdOverride);
            }
            else if (candidateIdOverride is not null &&
                     string.Equals(
                         propertyName,
                         "candidateId",
                         StringComparison.Ordinal))
            {
                AppendPythonJsonString(
                    canonical,
                    candidateIdOverride);
            }
            else
            {
                AppendCanonicalJson(
                    canonical,
                    value);
            }
        }

        canonical.Append('}');

        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    canonical.ToString())));
    }

    private static void AppendCanonicalJson(
        StringBuilder output,
        JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                output.Append('{');
                JsonProperty[] properties =
                    value.EnumerateObject()
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

                    AppendPythonJsonString(
                        output,
                        properties[index].Name);
                    output.Append(':');
                    AppendCanonicalJson(
                        output,
                        properties[index].Value);
                }

                output.Append('}');
                break;

            case JsonValueKind.Array:
                output.Append('[');
                int arrayIndex = 0;

                foreach (JsonElement item in
                         value.EnumerateArray())
                {
                    if (arrayIndex > 0)
                    {
                        output.Append(',');
                    }

                    AppendCanonicalJson(output, item);
                    arrayIndex++;
                }

                output.Append(']');
                break;

            case JsonValueKind.String:
                AppendPythonJsonString(
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
                throw Failure(
                    "The canonical observation contains an unsupported JSON token.");
        }
    }

    private static void AppendPythonJsonString(
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
                            ((int)character).ToString(
                                "x4",
                                CultureInfo.InvariantCulture));
                    }
                    else if (char.IsHighSurrogate(character))
                    {
                        if (index + 1 >= value.Length ||
                            !char.IsLowSurrogate(
                                value[index + 1]))
                        {
                            throw Failure(
                                "Canonical JSON text contains an unpaired UTF-16 surrogate.");
                        }

                        output.Append(character);
                        output.Append(value[++index]);
                    }
                    else if (char.IsLowSurrogate(character))
                    {
                        throw Failure(
                            "Canonical JSON text contains an unpaired UTF-16 surrogate.");
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

    internal static void RequireCanonicalEvidenceIntervals(
        VisualSemanticEvidenceInterval[] values,
        string path)
    {
        VisualSemanticEvidenceInterval[] canonical =
            values
                .Distinct()
                .OrderBy(static value => value.Start)
                .ThenBy(static value => value.End)
                .ThenBy(
                    static value => value.Description,
                    StringComparer.Ordinal)
                .ToArray();

        if (!values.SequenceEqual(canonical))
        {
            throw Failure(
                $"{path} must be unique and canonically ordered.");
        }
    }

    internal static void RequireCanonicalUncertainties(
        VisualSemanticUncertainty[] values,
        string path)
    {
        VisualSemanticUncertainty[] canonical =
            values
                .Distinct()
                .OrderBy(static value => value.Code)
                .ThenBy(
                    static value => value.Description,
                    StringComparer.Ordinal)
                .ToArray();

        if (!values.SequenceEqual(canonical))
        {
            throw Failure(
                $"{path} must be unique and canonically ordered.");
        }
    }

    internal static void RequireCanonicalLimitations(
        string[] values,
        string path)
    {
        string[] canonical =
            values
                .Distinct(StringComparer.Ordinal)
                .OrderBy(
                    static value => value,
                    StringComparer.Ordinal)
                .ToArray();

        if (!values.SequenceEqual(
                canonical,
                StringComparer.Ordinal))
        {
            throw Failure(
                $"{path} must be unique and canonically ordered.");
        }
    }

    internal static void RejectProhibitedReasoning(
        JsonElement value,
        string path)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                string text = value.GetString()!;

                if (ProhibitedReasoningFragments.Any(
                        fragment =>
                            text.Contains(
                                fragment,
                                StringComparison.OrdinalIgnoreCase)))
                {
                    throw Failure(
                        $"{path} contains prohibited hidden-reasoning content.");
                }

                break;

            case JsonValueKind.Array:
                int index = 0;

                foreach (JsonElement item in
                         value.EnumerateArray())
                {
                    RejectProhibitedReasoning(
                        item,
                        $"{path}[{index}]");
                    index++;
                }

                break;

            case JsonValueKind.Object:
                foreach (JsonProperty property in
                         value.EnumerateObject())
                {
                    RejectProhibitedReasoning(
                        property.Value,
                        $"{path}.{property.Name}");
                }

                break;
        }
    }

}
