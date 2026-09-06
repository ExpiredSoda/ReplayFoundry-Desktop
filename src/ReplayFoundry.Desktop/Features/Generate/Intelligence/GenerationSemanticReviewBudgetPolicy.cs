using ReplayFoundry.Desktop.Features.Generate.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal static class GenerationSemanticReviewBudgetPolicy
{
    public const int MaximumCandidates = 32;
    public const int MaximumBatchSize = 8;

    public static int Resolve(GenerationMomentFindingResult moments, int limit)
    {
        ArgumentNullException.ThrowIfNull(moments);
        if (limit is < 1 or > MaximumCandidates)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }
        double minutes = moments.Sources.Sum(static source =>
            source.AnalyzedSource.PreparedSource.Media.Duration.TotalMinutes);
        // Reserve headroom for alternatives as requested output and source
        // duration grow. Every provider call remains independently bounded.
        double desired = Math.Max(8, Math.Max(
            moments.Request.Setup.DesiredResultCount * 2d,
            Math.Max(moments.Sources.Count * 4d, Math.Ceiling(minutes / 10) * 4)));
        return (int)Math.Min(limit, desired);
    }
}
