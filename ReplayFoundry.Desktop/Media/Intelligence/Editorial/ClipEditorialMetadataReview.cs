namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial;

/// <summary>
/// Maps provider diagnostics to one stable user-review contract. Provider rule
/// names remain provenance; the public draft model does not need a duplicate
/// enum member for every model-specific copy heuristic.
/// </summary>
public static class ClipEditorialMetadataReview
{
    public static IReadOnlyList<ClipEditorialMetadataQualityIssue> BuildIssues(
        IEnumerable<string> providerRuleCodes)
    {
        ArgumentNullException.ThrowIfNull(providerRuleCodes);
        return providerRuleCodes
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Select(code => new ClipEditorialMetadataQualityIssue(
                ClipEditorialMetadataQualityIssueCode.AudienceCopyReview,
                Message(code)))
            .ToArray();
    }

    private static string Message(string code) => code switch
    {
        "RerollTitleTooSimilar" =>
            "This reroll stayed too close to earlier copy. Try another reroll for a different narrative angle.",
        "UnsupportedKnowledgeGrounding" or "UncoupledKnowledgeReference" =>
            "A game-context claim could not be tied cleanly to this clip. Review or reroll the copy before publishing.",
        _ =>
            $"The AI draft is usable, but its audience copy needs review ({code}). Edit it or reroll for a new structure.",
    };
}
