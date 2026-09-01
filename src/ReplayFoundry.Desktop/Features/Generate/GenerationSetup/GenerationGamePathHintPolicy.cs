using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

internal static partial class GenerationGamePathHintPolicy
{
    internal const string UnconfirmedGameName = "Gameplay";

    private static readonly HashSet<string> GenericPathSegments = new(
        [
            "capture",
            "captures",
            "clip",
            "clips",
            "desktop",
            "documents",
            "downloads",
            "dropbox",
            "edited",
            "export",
            "exports",
            "game capture",
            "game captures",
            "gameplay",
            "gameplay capture",
            "gameplay captures",
            "horizontal",
            "landscape",
            "media",
            "obs",
            "obs studio",
            "onedrive",
            "output",
            "outputs",
            "portrait",
            "processed",
            "recording",
            "recording files",
            "recording video files",
            "recordings",
            "render",
            "renders",
            "replay",
            "replays",
            "screen recordings",
            "shadowplay",
            "short form",
            "shorts",
            "streamlabs",
            "tiktok",
            "vertical",
            "vertical video",
            "video",
            "video files",
            "videos",
            "google drive",
            "youtube shorts",
        ],
        StringComparer.Ordinal);

    internal static string Suggest(string sourceFullPath)
    {
        DirectoryInfo? directory = Directory.GetParent(sourceFullPath);
        for (int depth = 0; directory is not null && depth < 5; depth++)
        {
            if (IsProfileBoundary(directory))
            {
                break;
            }
            if (IsMeaningfulSuggestion(directory.Name))
            {
                return directory.Name.Trim();
            }
            directory = directory.Parent;
        }
        return UnconfirmedGameName;
    }

    internal static bool IsMeaningfulSuggestion(string value)
    {
        string normalized = Normalize(value);
        return normalized.Length >= 2 &&
            !GenericPathSegments.Contains(normalized) &&
            !DateOrCaptureStampRegex().IsMatch(normalized);
    }

    private static bool IsProfileBoundary(DirectoryInfo directory) =>
        directory.Parent?.Name.Equals(
            "Users",
            StringComparison.OrdinalIgnoreCase) == true &&
        directory.Parent.Parent?.Parent is null;

    private static string Normalize(string value)
    {
        var result = new StringBuilder(value.Length);
        bool pendingSpace = false;
        foreach (char character in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && result.Length > 0)
                {
                    result.Append(' ');
                }
                result.Append(char.ToLowerInvariant(character));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }
        return result.ToString();
    }

    [GeneratedRegex(
        @"^(?:\d{4}[ -]\d{1,2}[ -]\d{1,2})(?:[ t_-]+\d{1,2}(?:[ ._-]\d{1,2}){1,2})?(?:[ _-]*(?:vertical|horizontal|portrait|landscape))?$|^\d{8}(?:[ _-]?\d{6})?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DateOrCaptureStampRegex();
}
