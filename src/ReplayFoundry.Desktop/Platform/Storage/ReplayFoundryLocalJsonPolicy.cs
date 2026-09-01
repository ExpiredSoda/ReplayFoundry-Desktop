using System.Text.Json;

namespace ReplayFoundry.Desktop.Platform.Storage;

internal static class ReplayFoundryLocalJsonPolicy
{
    public static JsonSerializerOptions IndentedCamelCase { get; } =
        CreateIndented(camelCase: true);

    public static JsonSerializerOptions IndentedDeclaredPropertyNames { get; } =
        CreateIndented(camelCase: false);

    private static JsonSerializerOptions CreateIndented(bool camelCase)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = camelCase
                ? JsonNamingPolicy.CamelCase
                : null,
            WriteIndented = true,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
