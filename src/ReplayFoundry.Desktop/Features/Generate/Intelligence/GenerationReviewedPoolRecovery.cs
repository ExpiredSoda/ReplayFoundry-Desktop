namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal static class GenerationReviewedPoolRecovery
{
    internal static async Task<GenerationCandidateIntelligenceResult> FillAsync(
        GenerationCandidateIntelligenceResult baseline,
        GenerationCandidateIntelligenceResult current,
        IGenerationVisualSemanticAnalysisService visualService,
        IGenerationCandidateRefinementService refinement,
        IProgress<GenerationVisualSemanticProgress>? progress,
        Action<GenerationVisualSemanticAnalysisResult> retain,
        CancellationToken cancellationToken)
    {
        while (current.RefinedMoments.SelectedCount < current.BaseMoments.Request.Setup.DesiredResultCount &&
               current.VisualSemantic is { Outcome: GenerationVisualSemanticOutcome.Completed } previous)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expanded = await visualService.ReviewAlternativesAsync(baseline, previous, progress, cancellationToken);
            if (ReferenceEquals(expanded, previous)) break;
            retain(expanded);
            // Failed checks consume capacity too. A provider cannot keep this
            // loop alive by returning the same incomplete review repeatedly.
            if (expanded.AttemptedCandidates.Count <= previous.AttemptedCandidates.Count)
                throw new InvalidOperationException("Alternative review did not advance its bounded candidate pool.");
            var rescored = await Task.Run(() => refinement.ApplyVisualSemantic(baseline, expanded, cancellationToken), cancellationToken);
            var reviewed = GenerationReviewedSelectionPolicy.Apply(rescored, cancellationToken);
            current = GenerationEditorialReplacementPolicy.PreserveRejections(current, reviewed, cancellationToken);
        }
        return current;
    }
}
