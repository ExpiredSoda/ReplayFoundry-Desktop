using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal enum Qwen3VlGroundedMetadataRerollTitleDiversityCode
{
    NoComparablePrior,
    ExactCanonicalTitle,
    HighTokenOverlap,
    MateriallyDistinct,
}

internal readonly record struct Qwen3VlGroundedMetadataRerollTitleScope(
    string CandidateId,
    TimeSpan SourceStart,
    TimeSpan SourceEnd);

internal sealed record Qwen3VlGroundedMetadataRerollTitleReference(
    Qwen3VlGroundedMetadataRerollTitleScope Scope,
    string Title,
    string GameHashtag);

internal sealed record Qwen3VlGroundedMetadataRerollTitleDiversityResult(
    Qwen3VlGroundedMetadataRerollTitleDiversityCode Code,
    IReadOnlyList<string> CanonicalTokens,
    int ComparablePriorCount,
    string? MatchedPriorTitle,
    IReadOnlyList<string> MatchedPriorTokens,
    int TokenJaccardNumerator,
    int TokenJaccardDenominator)
{
    internal bool IsMateriallyDistinct => Code is
        Qwen3VlGroundedMetadataRerollTitleDiversityCode.NoComparablePrior or
        Qwen3VlGroundedMetadataRerollTitleDiversityCode.MateriallyDistinct;
}

internal static class Qwen3VlGroundedMetadataRerollDiversityPolicy
{
    internal const string Version =
        "grounded-editorial-reroll-diversity-1.0";
    internal const int SimilarityThresholdNumerator =
        ClipEditorialTitleDiversityPolicy.SimilarityThresholdNumerator;
    internal const int SimilarityThresholdDenominator =
        ClipEditorialTitleDiversityPolicy.SimilarityThresholdDenominator;

    internal static Qwen3VlGroundedMetadataRerollTitleReference Reference(
        ClipEditorialMetadataRequest request,
        string title)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Reference(
            request.Context.CandidateId,
            request.Context.SourceStart,
            request.Context.SourceEnd,
            title,
            request.Context.GameContext.GameHashtag);
    }

    internal static Qwen3VlGroundedMetadataRerollTitleReference Reference(
        string candidateId,
        TimeSpan sourceStart,
        TimeSpan sourceEnd,
        string title,
        string gameHashtag)
    {
        if (string.IsNullOrWhiteSpace(candidateId) ||
            candidateId.Trim().Length > 256 ||
            sourceStart < TimeSpan.Zero ||
            sourceEnd <= sourceStart)
        {
            throw new ArgumentException(
                "Reroll title scope requires one candidate and exact source cut.");
        }
        if (string.IsNullOrWhiteSpace(title) ||
            title.Trim().Length > ClipEditorialMetadataDraft.MaximumTitleLength ||
            string.IsNullOrWhiteSpace(gameHashtag) ||
            !gameHashtag.StartsWith('#') ||
            gameHashtag.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "Reroll title reference is outside the bounded title policy.");
        }

        string normalizedTitle = title.Trim();
        string normalizedHashtag = gameHashtag.Trim();
        _ = CanonicalTitleTokens(normalizedTitle, normalizedHashtag);
        return new(
            new(candidateId.Trim(), sourceStart, sourceEnd),
            normalizedTitle,
            normalizedHashtag);
    }

    internal static Qwen3VlGroundedMetadataRerollTitleDiversityResult Evaluate(
        Qwen3VlGroundedMetadataRerollTitleReference candidate,
        IEnumerable<Qwen3VlGroundedMetadataRerollTitleReference> priorTitles)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(priorTitles);
        Qwen3VlGroundedMetadataRerollTitleReference[] comparable = priorTitles
            .Where(prior => prior.Scope == candidate.Scope)
            .Select(prior =>
            {
                if (!prior.GameHashtag.Equals(
                        candidate.GameHashtag,
                        StringComparison.Ordinal))
                {
                    throw new Qwen3VlOutputParseException(
                        "One candidate cut reported conflicting game hashtags.");
                }
                return prior;
            })
            .ToArray();
        ClipEditorialTitleDiversityResult result;
        try
        {
            result = ClipEditorialTitleDiversityPolicy.Evaluate(
                candidate.Title,
                candidate.GameHashtag,
                comparable.Select(static prior => prior.Title));
        }
        catch (ArgumentException exception)
        {
            throw new Qwen3VlOutputParseException(exception.Message);
        }
        return new(
            result.Code switch
            {
                ClipEditorialTitleDiversityCode.NoComparablePrior =>
                    Qwen3VlGroundedMetadataRerollTitleDiversityCode
                        .NoComparablePrior,
                ClipEditorialTitleDiversityCode.ExactCanonicalTitle =>
                    Qwen3VlGroundedMetadataRerollTitleDiversityCode
                        .ExactCanonicalTitle,
                ClipEditorialTitleDiversityCode.HighTokenOverlap =>
                    Qwen3VlGroundedMetadataRerollTitleDiversityCode
                        .HighTokenOverlap,
                ClipEditorialTitleDiversityCode.MateriallyDistinct =>
                    Qwen3VlGroundedMetadataRerollTitleDiversityCode
                        .MateriallyDistinct,
                _ => throw new Qwen3VlOutputParseException(
                    "Grounded Qwen returned an undefined reroll-diversity result."),
            },
            result.CanonicalTokens,
            result.ComparablePriorCount,
            result.MatchedPriorTitle,
            result.MatchedPriorTokens,
            result.TokenJaccardNumerator,
            result.TokenJaccardDenominator);
    }

    internal static string[] CanonicalTitleTokens(
        string title,
        string gameHashtag)
    {
        try
        {
            return ClipEditorialTitleDiversityPolicy.CanonicalTitleTokens(
                title,
                gameHashtag);
        }
        catch (ArgumentException exception)
        {
            throw new Qwen3VlOutputParseException(exception.Message);
        }
    }

    internal static void ValidateReportedProvenance(
        Qwen3VlGroundedMetadataRerollTitleDiversityResult actual,
        Qwen3VlGroundedMetadataGenerationValidation reported)
    {
        if (reported.PriorAcceptedTitleCount != actual.ComparablePriorCount ||
            reported.RerollTitleDiversityCode != actual.Code ||
            reported.RerollTitleTokenJaccardNumerator !=
                actual.TokenJaccardNumerator ||
            reported.RerollTitleTokenJaccardDenominator !=
                actual.TokenJaccardDenominator)
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen reroll-diversity provenance is invalid.");
        }
    }

}
