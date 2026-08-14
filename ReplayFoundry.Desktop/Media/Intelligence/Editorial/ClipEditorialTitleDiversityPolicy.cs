using System.Collections.ObjectModel;
using System.Text;

namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial;

internal enum ClipEditorialTitleDiversityCode
{
    NoComparablePrior,
    ExactCanonicalTitle,
    HighTokenOverlap,
    MateriallyDistinct,
}

internal sealed record ClipEditorialTitleDiversityResult(
    ClipEditorialTitleDiversityCode Code,
    IReadOnlyList<string> CanonicalTokens,
    int ComparablePriorCount,
    string? MatchedPriorTitle,
    IReadOnlyList<string> MatchedPriorTokens,
    int TokenJaccardNumerator,
    int TokenJaccardDenominator)
{
    internal bool IsMateriallyDistinct => Code is
        ClipEditorialTitleDiversityCode.NoComparablePrior or
        ClipEditorialTitleDiversityCode.MateriallyDistinct;
}

/// <summary>
/// Compares audience titles for one exact candidate cut without treating any
/// prior title as evidence. This provider-neutral policy intentionally ignores
/// punctuation and a small closed-class vocabulary so cosmetic rewrites do not
/// masquerade as a new editorial angle.
/// </summary>
internal static class ClipEditorialTitleDiversityPolicy
{
    internal const int SimilarityThresholdNumerator = 85;
    internal const int SimilarityThresholdDenominator = 100;

    private static readonly HashSet<string> ClosedClassWords = new(
        [
            "a", "an", "and", "at", "but", "by", "for", "from",
            "i", "in", "into", "me", "my", "of", "on", "or",
            "our", "the", "to", "us", "we", "with",
        ],
        StringComparer.Ordinal);

    internal static ClipEditorialTitleDiversityResult Evaluate(
        string candidateTitle,
        string gameHashtag,
        IEnumerable<string> priorTitles)
    {
        ArgumentNullException.ThrowIfNull(priorTitles);
        string[] candidateTokens = CanonicalTitleTokens(
            candidateTitle,
            gameHashtag);
        (string Title, string[] Tokens)[] comparable = priorTitles
            .Select(title => (
                title,
                CanonicalTitleTokens(title, gameHashtag)))
            .ToArray();
        if (comparable.Length == 0)
        {
            return Result(
                ClipEditorialTitleDiversityCode.NoComparablePrior,
                candidateTokens,
                0,
                null,
                [],
                0,
                1);
        }

        foreach ((string priorTitle, string[] priorTokens) in comparable)
        {
            if (candidateTokens.SequenceEqual(
                    priorTokens,
                    StringComparer.Ordinal))
            {
                return Result(
                    ClipEditorialTitleDiversityCode.ExactCanonicalTitle,
                    candidateTokens,
                    comparable.Length,
                    priorTitle,
                    priorTokens,
                    1,
                    1);
            }
        }

        (int firstNumerator, int firstDenominator) = Jaccard(
            candidateTokens,
            comparable[0].Tokens);
        (string Title, string[] Tokens, int Numerator, int Denominator) best =
            (comparable[0].Title, comparable[0].Tokens,
                firstNumerator, firstDenominator);
        foreach ((string priorTitle, string[] priorTokens) in
                 comparable.Skip(1))
        {
            (int numerator, int denominator) = Jaccard(
                candidateTokens,
                priorTokens);
            if ((long)numerator * best.Denominator >
                    (long)best.Numerator * denominator)
            {
                best = (priorTitle, priorTokens, numerator, denominator);
            }
        }

        bool tooSimilar =
            (long)best.Numerator * SimilarityThresholdDenominator >=
            (long)SimilarityThresholdNumerator * best.Denominator;
        return Result(
            tooSimilar
                ? ClipEditorialTitleDiversityCode.HighTokenOverlap
                : ClipEditorialTitleDiversityCode.MateriallyDistinct,
            candidateTokens,
            comparable.Length,
            best.Title,
            best.Tokens,
            best.Numerator,
            best.Denominator);
    }

    internal static string[] CanonicalTitleTokens(
        string title,
        string gameHashtag)
    {
        string body = TitleBody(title, gameHashtag);
        string folded = body
            .Normalize(NormalizationForm.FormKC)
            .Replace("\u0130", "i\u0307", StringComparison.Ordinal)
            .ToLowerInvariant()
            .Replace("ß", "ss", StringComparison.Ordinal);
        var tokens = new List<string>();
        var token = new StringBuilder();
        void Flush()
        {
            if (token.Length == 0)
            {
                return;
            }
            string value = token.ToString();
            token.Clear();
            int runeCount = value.EnumerateRunes().Count();
            bool decimalOnly = value.EnumerateRunes().All(Rune.IsDigit);
            if (!ClosedClassWords.Contains(value) &&
                (runeCount > 1 || decimalOnly))
            {
                tokens.Add(value);
            }
        }

        foreach (Rune rune in folded.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                token.Append(rune.ToString());
            }
            else
            {
                Flush();
            }
        }
        Flush();
        if (tokens.Count == 0)
        {
            throw new ArgumentException(
                "An editorial title must retain a content-bearing token.",
                nameof(title));
        }
        return tokens.ToArray();
    }

    private static ClipEditorialTitleDiversityResult Result(
        ClipEditorialTitleDiversityCode code,
        string[] tokens,
        int comparablePriorCount,
        string? matchedPriorTitle,
        string[] matchedPriorTokens,
        int numerator,
        int denominator) =>
        new(
            code,
            new ReadOnlyCollection<string>(tokens),
            comparablePriorCount,
            matchedPriorTitle,
            new ReadOnlyCollection<string>(matchedPriorTokens),
            numerator,
            denominator);

    private static string TitleBody(string title, string gameHashtag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameHashtag);
        string suffix = " " + gameHashtag;
        if (!title.EndsWith(suffix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "An editorial title must end with one space and the exact game hashtag.",
                nameof(title));
        }
        string body = title[..^suffix.Length].TrimEnd();
        if (body.Length == 0)
        {
            throw new ArgumentException(
                "An editorial title requires audience copy before its hashtag.",
                nameof(title));
        }
        return body;
    }

    private static (int Numerator, int Denominator) Jaccard(
        IEnumerable<string> left,
        IEnumerable<string> right)
    {
        var leftSet = new HashSet<string>(left, StringComparer.Ordinal);
        var rightSet = new HashSet<string>(right, StringComparer.Ordinal);
        int intersection = leftSet.Count(rightSet.Contains);
        leftSet.UnionWith(rightSet);
        return (intersection, leftSet.Count);
    }
}
