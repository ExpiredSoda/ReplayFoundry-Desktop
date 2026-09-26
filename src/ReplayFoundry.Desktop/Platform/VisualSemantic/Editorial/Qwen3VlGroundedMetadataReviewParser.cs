using System.Text.Json;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataJson;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataReviewParser
{
    internal static (bool Required, string[] Issues) Parse(
        JsonElement generation,
        bool reviewableAudienceCopySupported)
    {
        if (!reviewableAudienceCopySupported)
        {
            return (false, []);
        }
        bool required = Boolean(generation, "metadataReviewRequired");
        string[] issues = TextArray(generation, "metadataReviewIssues", 8);
        if (required != (issues.Length > 0) ||
            issues.Any(code => !Qwen3VlGroundedMetadataSelection.IsKnownValidationRule(code)))
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen metadata-review provenance is invalid.");
        }
        return (required, issues);
    }

    private static string[] TextArray(
        JsonElement value,
        string name,
        int maximum)
    {
        JsonElement[] array = Qwen3VlEditorialJson.Array(value, name);
        string[] results = array.Select(item =>
                item.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(item.GetString())
                    ? item.GetString()!
                    : throw new Qwen3VlOutputParseException(
                        $"Grounded Qwen '{name}' entries must be text."))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (results.Length != array.Length || results.Length > maximum)
        {
            throw new Qwen3VlOutputParseException(
                $"Grounded Qwen '{name}' is invalid.");
        }
        return results;
    }
}
