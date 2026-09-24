namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

public sealed partial class GenerationVisualSemanticAnalysisService
{
    public async Task<GenerationVisualSemanticAnalysisResult> ReviewAlternativesAsync(
        GenerationCandidateIntelligenceResult baseline,
        GenerationVisualSemanticAnalysisResult previous,
        IProgress<GenerationVisualSemanticProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(previous);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(previous.CandidateIntelligence, baseline))
            throw new ArgumentException("Alternative reviews require the original pre-visual intelligence.", nameof(baseline));
        if (previous.Outcome != GenerationVisualSemanticOutcome.Completed) return previous;
        int remaining = _settings.MaximumCandidateCount - previous.AttemptedCandidates.Count;
        if (remaining <= 0) return previous;
        var candidates = CreateShortlist(baseline, _settings.MaximumCandidateCount, _settings.VideoPolicy.MaximumReviewDuration,
                previous.AttemptedCandidates)
            .Take(Math.Min(remaining, GenerationSemanticReviewBudgetPolicy.MaximumSupplementalCandidates)).ToArray();
        if (candidates.Length == 0) return previous;
        var supplemental = await AnalyzeShortlistAsync(baseline, candidates, progress, true, cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GenerationVisualSemanticAnalysisResult.Combine(previous, supplemental, additionalAlternatives: true);
        }
        catch
        {
            supplemental.Dispose();
            throw;
        }
    }
}
