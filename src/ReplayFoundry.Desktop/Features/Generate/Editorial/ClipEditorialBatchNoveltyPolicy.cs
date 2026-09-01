using System.Text.RegularExpressions;

namespace ReplayFoundry.Desktop.Features.Generate.Editorial;

internal static class ClipEditorialBatchNoveltyPolicy
{
    private const double CollisionThreshold = 0.70;

    private static readonly Regex TokenPattern = new(
        @"[\p{L}\p{N}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BannedAbstractFamilyPattern = new(
        @"(?ix)\b(?:" +
        @"piece\s+of\s+the\s+story|" +
        @"new\s+piece\b.{0,48}\bemerged|" +
        @"next\s+beat|" +
        @"beat\s+landed|" +
        @"revealed\s+more|" +
        @"added(?:\s+more)?\s+context|" +
        @"filled\s+in(?:\s+more)?\s+details|" +
        @"more\s+details\s+came\s+into\s+focus|" +
        @"moment\s+took\s+shape" +
        @")\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> StopWords = new(
        [
            "a", "an", "and", "as", "at", "before", "but", "by",
            "during", "for", "from", "in", "into", "it", "of", "on",
            "or", "our", "over", "that", "the", "their", "then", "this",
            "through", "to", "under", "up", "was", "we", "when", "while",
            "with", "you", "your",
        ],
        StringComparer.Ordinal);

    internal static bool Rejects(
        string title,
        IEnumerable<string> acceptedOrRejectedTitles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(acceptedOrRejectedTitles);

        if (IsBannedAbstractFamily(title))
        {
            return true;
        }

        return acceptedOrRejectedTitles.Any(existing =>
            Collides(title, existing));
    }

    internal static bool IsBannedAbstractFamily(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return BannedAbstractFamilyPattern.IsMatch(Canonicalize(title));
    }

    internal static bool Collides(string first, string second)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(first);
        ArgumentException.ThrowIfNullOrWhiteSpace(second);

        string firstCanonical = Canonicalize(first);
        string secondCanonical = Canonicalize(second);
        if (firstCanonical.Equals(
                secondCanonical,
                StringComparison.Ordinal))
        {
            return true;
        }

        string[] firstTokens = ContentTokens(firstCanonical);
        string[] secondTokens = ContentTokens(secondCanonical);
        if (firstTokens.Length == 0 || secondTokens.Length == 0)
        {
            return false;
        }

        var firstSet = firstTokens.ToHashSet(StringComparer.Ordinal);
        var secondSet = secondTokens.ToHashSet(StringComparer.Ordinal);
        int unionCount = firstSet.Union(secondSet).Count();
        double jaccard = unionCount == 0
            ? 0
            : (double)firstSet.Intersect(secondSet).Count() / unionCount;
        if (jaccard >= CollisionThreshold)
        {
            return true;
        }

        return (firstTokens.Length <= 5 || secondTokens.Length <= 5) &&
            firstTokens.Length >= 2 &&
            secondTokens.Length >= 2 &&
            firstTokens[0].Equals(secondTokens[0], StringComparison.Ordinal) &&
            firstTokens[1].Equals(secondTokens[1], StringComparison.Ordinal);
    }

    private static string Canonicalize(string title) =>
        string.Join(
            ' ',
            TokenPattern.Matches(title.ToLowerInvariant())
                .Select(static match => match.Value));

    private static string[] ContentTokens(string canonicalTitle) =>
        canonicalTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeToken)
            .Where(static token =>
                token.Length > 0 && !StopWords.Contains(token))
            .ToArray();

    private static string NormalizeToken(string token)
    {
        if (token.Length > 5 && token.EndsWith("ing", StringComparison.Ordinal))
        {
            return token[..^3];
        }
        if (token.Length > 4 && token.EndsWith("ied", StringComparison.Ordinal))
        {
            return token[..^3] + "y";
        }
        if (token.Length > 4 && token.EndsWith("ed", StringComparison.Ordinal))
        {
            return token[..^2];
        }
        if (token.Length > 4 && token.EndsWith('s'))
        {
            return token[..^1];
        }
        return token;
    }
}
