using System.Text.Json;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataJson
{
    internal static bool Boolean(JsonElement value, string name)
    {
        JsonElement property = Qwen3VlEditorialJson.Property(value, name);
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new Qwen3VlOutputParseException(
                $"Grounded Qwen metadata '{name}' must be Boolean."),
        };
    }

    internal static void RequireText(
        JsonElement value,
        string name,
        string expected)
    {
        if (!Qwen3VlEditorialJson.Text(value, name).Equals(
                expected,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new Qwen3VlOutputParseException(
                $"Grounded Qwen metadata '{name}' changed.");
        }
    }

    internal static TimeSpan Seconds(JsonElement value, string name)
    {
        JsonElement property = Qwen3VlEditorialJson.Property(value, name);
        if (property.ValueKind != JsonValueKind.Number ||
            !property.TryGetDouble(out double seconds) ||
            !double.IsFinite(seconds) || seconds < 0)
        {
            throw new Qwen3VlOutputParseException(
                $"Grounded Qwen metadata '{name}' must be non-negative seconds.");
        }
        return TimeSpan.FromSeconds(seconds);
    }
}
