using System.Text.RegularExpressions;

namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial;

internal static partial class ClipEditorialGeneratedTags
{
    private const int MaximumGeneratedTagCount = 8;
    private const int MaximumCombinedTagCount = 15;

    internal static string[] Build(
        ClipEditorialContext context,
        IEnumerable<string> explicitDefaultTags,
        IEnumerable<string>? additionalGroundedTags = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(explicitDefaultTags);

        var generated = new List<string>
        {
            context.GameContext.GameName,
            "gaming",
            "gameplay",
        };
        // A retained gameplay crop is typed evidence for a play-through
        // presentation. Full-frame footage can legitimately omit that
        // crop, so it still receives the broader product-grounded categories
        // above without manufacturing the more specific format claim.
        if (context.GameplayRegion is not null)
        {
            generated.Add("playthrough");
        }
        if (additionalGroundedTags is not null)
        {
            string compactGameIdentity = ClipEditorialProfile.NormalizeTag(
                context.GameContext.GameHashtag);
            generated.AddRange(additionalGroundedTags.Where(tag =>
                !ClipEditorialProfile.NormalizeTag(tag).Equals(
                    compactGameIdentity,
                    StringComparison.OrdinalIgnoreCase)));
        }

        string[] generatedSnapshot = Normalize(generated)
            .Take(MaximumGeneratedTagCount)
            .ToArray();
        string[] explicitSnapshot = Normalize(explicitDefaultTags)
            .ToArray();

        // Keep the canonical game identity first and every explicitly saved
        // user default ahead of optional generated terms. The remaining
        // generated terms are broad, typed facts; platform and release claims
        // are never inferred here.
        return Normalize(
                generatedSnapshot.Take(1)
                    .Concat(explicitSnapshot)
                    .Concat(generatedSnapshot.Skip(1)))
            .Take(MaximumCombinedTagCount)
            .ToArray();
    }

    internal static bool ContainsUnsupportedGeneratedClaim(
        string tag,
        string gameName,
        string gameHashtag,
        IEnumerable<string>? explicitUserTags = null)
    {
        string normalized = ClipEditorialProfile.NormalizeTag(tag);
        if (normalized.Length == 0 ||
            normalized.Equals(gameName, StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals(
                ClipEditorialProfile.NormalizeTag(gameHashtag),
                StringComparison.OrdinalIgnoreCase) ||
            explicitUserTags?.Any(value => normalized.Equals(
                ClipEditorialProfile.NormalizeTag(value),
                StringComparison.OrdinalIgnoreCase)) == true)
        {
            return false;
        }

        return ReleaseYearRegex().IsMatch(normalized) ||
            ReleaseMarketingRegex().IsMatch(normalized) ||
            UnsupportedPlatformRegex().IsMatch(normalized);
    }

    private static IEnumerable<string> Normalize(
        IEnumerable<string> tags) =>
        tags
            .Select(ClipEditorialProfile.NormalizeTag)
            .Where(static tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(
        @"(?<!\d)(?:19|20)\d{2}(?!\d)",
        RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseYearRegex();

    [GeneratedRegex(
        @"\b(?:brand\s+new|new|newly\s+released|latest|released|release\s+(?:date|year))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseMarketingRegex();

    [GeneratedRegex(
        @"^(?:(?:(?:pc|windows(?:\s+\d{1,2})?|xbox(?:\s+(?:one|series\s+[sx]))?|playstation(?:\s*[345])?|ps[345]|nintendo(?:\s+switch)?|steam(?:\s+deck)?|console|mobile|ios|android|mac(?:os)?)(?:\s+(?:gaming|gameplay|version|edition))?)|switch\s+(?:gaming|gameplay|version|edition))$|\b(?:pc|xbox(?:\s+(?:one|series\s+[sx]))?|playstation\s*[345]|ps[345]|nintendo\s+switch|steam\s+deck|(?:windows|console|mobile)\s+(?:gaming|gameplay|version|edition))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnsupportedPlatformRegex();
}
