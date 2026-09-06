using System.Text.RegularExpressions;

namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial;

/// <summary>
/// A deliberately narrow mechanical check for an unfinished trailing modifier.
/// An -ing ending alone is valid. Require a complement-bearing with-clause and
/// a matching continuation in the description; never supply missing words or
/// treat that description as independent factual grounding.
/// </summary>
internal static partial class ClipEditorialTitleCompletenessPolicy
{
    internal static bool HasDanglingPunctuation(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        string headline = HashtagRegex().Replace(title, string.Empty).Trim();
        if (headline.Length == 0) return false;
        if (headline[^1] is ',' or ';' or ':') return true;

        int openCurlyQuotes = 0;
        int straightQuoteCount = 0;
        for (int index = 0; index < headline.Length; index++)
        {
            char character = headline[index];
            if (character == '“') openCurlyQuotes++;
            else if (character == '”')
            {
                if (openCurlyQuotes == 0) return true;
                openCurlyQuotes--;
            }
            if (character == '"' && !(straightQuoteCount % 2 == 0 &&
                    index > 0 && char.IsDigit(headline[index - 1])))
                straightQuoteCount++;
        }

        // A bare closing straight double quote is distinguishable at the end
        // of a word. Leave inch marks after numbers and apostrophes alone;
        // a single quote can legitimately be a possessive or contraction.
        return headline.Length > 1 && headline[^1] == '"' &&
            !char.IsDigit(headline[^2]) && straightQuoteCount % 2 != 0;
    }

    internal static bool HasUnfinishedModifier(string title, string description)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(description);
        string headline = HashtagRegex().Replace(title, string.Empty).Trim();
        Match clause = TrailingModifierRegex().Match(headline);
        if (!clause.Success) return false;

        string action = clause.Groups["action"].Value;
        if (action.Equals("cutting", StringComparison.OrdinalIgnoreCase))
        {
            // A person can simply be cutting; a light/beam cutting through a
            // medium needs that continuation to complete this modifier.
            return LightSubjectRegex().IsMatch(clause.Groups["subject"].Value) &&
                CuttingContinuationRegex().IsMatch(description);
        }

        return Regex.IsMatch(description,
            @"\b" + Regex.Escape(action) +
            @"\s+(?!(?:and|or|but|while|when|before|after|as)\b)[\p{L}\p{N}'’_-]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    [GeneratedRegex(@"#[\p{L}\p{N}_]+", RegexOptions.CultureInvariant)]
    private static partial Regex HashtagRegex();

    [GeneratedRegex(@"\bwith\s+(?<subject>(?:[\p{L}\p{N}'’_-]+\s+){1,6})(?<action>cutting|illuminating|revealing|depicting|containing)\s*[.!?…]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingModifierRegex();

    [GeneratedRegex(@"\b(?:beam|light|headlamp|spotlight|sunlight)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LightSubjectRegex();

    [GeneratedRegex(@"\bcutting\s+(?:through|into|across)\s+[\p{L}\p{N}'’_-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CuttingContinuationRegex();
}
