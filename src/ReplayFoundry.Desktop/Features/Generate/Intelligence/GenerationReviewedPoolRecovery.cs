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
        CancellationToken cancellationToken,
        Func<bool>? finishWithReadyClips = null,
        Action<int>? reportReadyClips = null)
    {
        while (current.RefinedMoments.SelectedCount < current.BaseMoments.Request.Setup.DesiredResultCount &&
               current.VisualSemantic is { Outcome: GenerationVisualSemanticOutcome.Completed } previous)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.RefinedMoments.SelectedCount > 0)
            {
                reportReadyClips?.Invoke(current.RefinedMoments.SelectedCount);
                if (finishWithReadyClips?.Invoke() == true) break;
            }
            progress?.Report(new(GenerationVisualSemanticPhase.ReviewingCandidates, "Finding the remaining clips",
                $"{current.RefinedMoments.SelectedCount} of {current.BaseMoments.Request.Setup.DesiredResultCount} clips passed selection. " +
                "Checking up to two more distinct moments before choosing again; completed checks are saved for reuse.",
                previous.AttemptedCandidates.Count, GenerationSemanticReviewBudgetPolicy.MaximumCandidates, isIndeterminate: true));
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
