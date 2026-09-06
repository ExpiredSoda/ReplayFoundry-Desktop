using System.Windows.Media;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed record StudioCaptionFontResolution(string RequestedFamily, string Family, bool IsFallback)
{
    public string? Warning => IsFallback
        ? $"Font ‘{RequestedFamily}’ is unavailable. Preview and export use ‘{Family}’ on this computer; the saved look retains the requested font."
        : null;
}

/// <summary>Resolves one installed family name for WPF measurement, preview, and ASS export.</summary>
public static class StudioCaptionFontResolver
{
    private static readonly Lazy<IReadOnlyList<string>> Installed = new(() => Array.AsReadOnly(
        Fonts.SystemFontFamilies.Select(static family => family.Source)
            .Where(static name => !string.IsNullOrWhiteSpace(name) && name.IndexOfAny([',', '\r', '\n', '{', '}']) < 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray()));

    public static IReadOnlyList<string> InstalledFamilies => Installed.Value;

    public static StudioCaptionFontResolution Resolve(string requestedFamily) => Resolve(requestedFamily, InstalledFamilies);

    internal static StudioCaptionFontResolution Resolve(string requestedFamily, IReadOnlyList<string> installedFamilies)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedFamily);
        ArgumentNullException.ThrowIfNull(installedFamilies);
        string requested = requestedFamily.Trim();
        string? exact = installedFamilies.FirstOrDefault(name => name.Equals(requested, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return new(requested, exact, false);
        string? fallback = new[] { "Segoe UI", "Arial" }.Select(name => installedFamilies.FirstOrDefault(
                installed => installed.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(static name => name is not null) ?? installedFamilies.Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (fallback is null) throw new InvalidOperationException("No installed caption font is available for preview or export.");
        return new(requested, fallback, true);
    }
}
