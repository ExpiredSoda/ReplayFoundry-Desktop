using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataInterfaceAttributionPolicy
{
    private static readonly Regex ConsumerPlatformIdentity = new(
        @"\b(?:steam(?:\s+client)?|xbox(?:\s+app)?|playstation(?:\s+store)?|" +
        @"epic(?:\s+games)?(?:\s+launcher)?|nintendo(?:\s+eshop)?|" +
        @"gog(?:\s+galaxy)?|battle\.?net|ubisoft\s+connect)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DisplaySourceNoun = new(
        @"\b(?:screen|display|monitor|sign|billboard)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static void Validate(
        string title,
        string description,
        ClipEditorialMetadataRequest request,
        ICollection<string> failures)
    {
        string audienceCopy = title + "\n" + description;
        var authority = new List<string>
        {
            request.Context.GameContext.GameName,
        };
        if (request.Context.GameContext.IsUserGrounded &&
            request.Context.GameContext.ContextNotes is string notes)
        {
            authority.Add(notes);
        }
        authority.AddRange(
            request.Context.Transcripts
                .Where(static transcript =>
                    transcript.MaySupportVerbatimAudienceCopy)
                .Select(static transcript => transcript.Text));
        authority.AddRange(
            request.Context.VisualText?.GroundingAnchors
                .Select(static anchor => anchor.DisplayText) ?? []);
        string normalizedAuthority = string.Join(
            '\n',
            authority.Select(NormalizeWords));
        foreach (Match match in ConsumerPlatformIdentity.Matches(audienceCopy))
        {
            if (!normalizedAuthority.Contains(
                    NormalizeWords(match.Value),
                    StringComparison.Ordinal))
            {
                failures.Add(
                    "unsupported interface platform identity without exact " +
                    "readable-text or user authority");
                break;
            }
        }

        string[] stableText = request.Context.VisualText?.GroundingAnchors
            .Select(static anchor => NormalizeWords(anchor.DisplayText))
            .Where(static value => value.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries).Length >= 2)
            .ToArray() ?? [];
        if (stableText.Length == 0)
        {
            return;
        }
        foreach (string sentence in Regex.Split(
                     audienceCopy,
                     @"(?<=[.!?])\s+|\n",
                     RegexOptions.CultureInvariant))
        {
            string normalizedSentence = NormalizeWords(sentence);
            if (DisplaySourceNoun.IsMatch(sentence) &&
                stableText.Any(value => normalizedSentence.Contains(
                    value,
                    StringComparison.Ordinal)))
            {
                failures.Add(
                    "unsupported interface-text attribution to a physical " +
                    "display source");
                break;
            }
        }
    }

    private static string NormalizeWords(string value) =>
        string.Join(
            ' ',
            Regex.Matches(
                    value,
                    @"[\p{L}\p{Nd}'’_-]+",
                    RegexOptions.CultureInvariant)
                .Select(static match => match.Value.ToLowerInvariant()));
}
