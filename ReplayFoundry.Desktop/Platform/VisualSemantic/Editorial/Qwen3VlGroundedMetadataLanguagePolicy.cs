using System.Text;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataLanguagePolicy
{
    internal static bool ContainsUnapprovedNonLatinAudienceCopy(
        string value,
        ClipEditorialMetadataRequest request)
    {
        string audienceCopy = value
            .Replace(
                request.Context.GameContext.GameName,
                string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                request.Context.GameContext.GameHashtag,
                string.Empty,
                StringComparison.OrdinalIgnoreCase);
        Rune[] letters = audienceCopy
            .EnumerateRunes()
            .Where(static rune => Rune.IsLetter(rune))
            .ToArray();
        return letters.Any(static rune =>
            rune.Value is not (>= 0x0041 and <= 0x024F or
                >= 0x1E00 and <= 0x1EFF));
    }
}
