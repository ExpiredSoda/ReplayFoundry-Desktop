namespace ReplayFoundry.Desktop.Features.Generate.Editorial;

internal static class ClipEditorialProfileTags
{
    public static string[] Parse(string? value) =>
        (value ?? string.Empty)
            .Split(
                [',', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .SelectMany(SplitLegacyHashtagList)
            .ToArray();

    private static IEnumerable<string> SplitLegacyHashtagList(string item)
    {
        string[] tokens = item.Split(
            [' ', '\t'],
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
        return tokens.Length > 1 &&
            tokens.All(static token => token.StartsWith('#'))
                ? tokens
                : [item];
    }
}
