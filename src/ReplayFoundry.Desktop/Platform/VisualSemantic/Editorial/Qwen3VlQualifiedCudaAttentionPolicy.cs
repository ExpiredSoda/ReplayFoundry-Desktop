using System.Text.Json;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlQualifiedCudaAttentionPolicy
{
    public const string Version = "qualified-editorial-cuda-attention-1.0";
    public const string Sha256 =
        "B0747A0ED7D160315C6FCA9FD869A9AFEC50221E97CCCB0BFF74B87B92A6C90D";

    public static void Validate(JsonElement value)
    {
        Qwen3VlEditorialJson.Exact(
            value,
            "policyVersion",
            "policySha256",
            "attentionImplementation",
            "sdpaBackend",
            "sdpaBackendForced",
            "attentionFallbackPermitted",
            "cacheImplementation");
        RequireText(value, "policyVersion", Version);
        RequireText(value, "policySha256", Sha256);
        RequireText(value, "attentionImplementation", "sdpa");
        RequireText(value, "sdpaBackend", "CudnnAttention");
        RequireText(value, "cacheImplementation", "offloaded");
        if (!Boolean(value, "sdpaBackendForced") ||
            Boolean(value, "attentionFallbackPermitted"))
        {
            throw new Qwen3VlOutputParseException(
                "Qualified Qwen CUDA attention was not forced or reported fallback.");
        }
    }

    private static void RequireText(
        JsonElement value,
        string name,
        string expected)
    {
        string actual = Qwen3VlEditorialJson.Text(value, name);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new Qwen3VlOutputParseException(
                $"Qualified Qwen CUDA-attention '{name}' changed.");
        }
    }

    private static bool Boolean(JsonElement value, string name)
    {
        JsonElement property = Qwen3VlEditorialJson.Property(value, name);
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new Qwen3VlOutputParseException(
                $"Qualified Qwen CUDA-attention '{name}' must be Boolean."),
        };
    }
}
