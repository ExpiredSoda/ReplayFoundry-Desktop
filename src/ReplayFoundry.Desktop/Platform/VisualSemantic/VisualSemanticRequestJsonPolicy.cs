using System.Text.Json;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class VisualSemanticRequestJsonPolicy
{
    public static JsonSerializerOptions IndentedCamelCase { get; } =
        CreateIndentedCamelCase();

    private static JsonSerializerOptions CreateIndentedCamelCase()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
