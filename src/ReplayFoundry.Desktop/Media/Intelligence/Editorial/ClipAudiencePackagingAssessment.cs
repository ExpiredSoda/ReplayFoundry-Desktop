using System.Text.RegularExpressions;

namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial;

/// <summary>Readable-copy checks, separate from factual grounding and audience performance.</summary>
public sealed partial record ClipAudiencePackagingAssessment(
    int Penalty,
    IReadOnlyList<string> Suggestions)
{
    public string Summary => Suggestions.Count == 0
        ? "No wording problems were detected. Check names and facts against the clip before sharing."
        : string.Join("\n", Suggestions);

    public static ClipAudiencePackagingAssessment Evaluate(string? title, string? description)
    {
        string headline = HashtagRegex().Replace(title ?? string.Empty, "").Trim();
        string body = (description ?? string.Empty).Trim();
        var suggestions = new List<string>();
        int penalty = 0;
        if (headline.Length == 0)
        {
            suggestions.Add("Write a title that names the clip's specific action or detail.");
            penalty += 100;
        }
        else if (headline.Length > 70)
        {
            suggestions.Add("Put the clearest action or detail first; aim for a title under 70 characters before hashtags.");
            penalty += 5 + (headline.Length - 70) / 10;
        }
        if (ClipEditorialTitleCompletenessPolicy.HasUnfinishedModifier(headline, body))
        {
            suggestions.Add("The title ends inside a modifier clause. Shorten it to a complete supported thought; do not invent a missing ending.");
            penalty += 20;
        }
        else if (ClipEditorialTitleCompletenessPolicy.HasDanglingPunctuation(headline))
        {
            suggestions.Add("The title has dangling punctuation or an unmatched closing quote. Review it as one complete supported thought; do not invent a missing ending.");
            penalty += 20;
        }
        if (VagueHookRegex().IsMatch(headline) || GenericGameplayLabelRegex().IsMatch(headline))
        {
            suggestions.Add("Replace the generic hook with a concrete detail supported by this clip.");
            penalty += 12;
        }
        if (body.Length == 0)
        {
            suggestions.Add("Add a description with context beyond the title.");
            penalty += 100;
        }
        else
        {
            HashSet<string> titleTerms = Terms(headline);
            HashSet<string> descriptionTerms = Terms(body);
            if (titleTerms.Count >= 2 &&
                titleTerms.Count(descriptionTerms.Contains) * 4 >= titleTerms.Count * 3 &&
                descriptionTerms.Count(term => !titleTerms.Contains(term)) < 2)
            {
                suggestions.Add("Add one supported detail to the description instead of repeating the title.");
                penalty += 10;
            }
            if (body.Length > ClipEditorialMetadataQuality.PreferredMaximumDescriptionLength)
            {
                suggestions.Add("Keep the opening description to one or two useful sentences; move optional details below it.");
                penalty += 5;
            }
        }
        return new(penalty, suggestions.AsReadOnly());
    }

    private static HashSet<string> Terms(string value) => WordRegex().Matches(value.ToLowerInvariant())
        .Select(match => match.Value)
        .Where(word => word.Length > 2 && word is not ("the" or "and" or "with" or "that" or "this" or "was" or "were" or "for" or "from" or "into" or "then"))
        .ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"#[\p{L}\p{N}_]+", RegexOptions.CultureInvariant)]
    private static partial Regex HashtagRegex();
    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
    [GeneratedRegex(@"(?i)^\s*(?:you won't believe|you won’t believe|wait for it|this changes everything|things got (?:crazy|wild|intense)|an? (?:epic|insane|crazy) moment)\b", RegexOptions.CultureInvariant)]
    private static partial Regex VagueHookRegex();
    [GeneratedRegex(@"(?i)^\s*(?:(?:a|an|one|another|the|complete|continuous|uncut|more|uninterrupted|part|of|from)\s+)*(?:gameplay\s+(?:sequence|moment)|playthrough|run)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex GenericGameplayLabelRegex();
}
