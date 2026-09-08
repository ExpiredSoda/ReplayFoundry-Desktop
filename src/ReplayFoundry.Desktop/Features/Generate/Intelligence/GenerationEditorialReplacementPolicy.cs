using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal static class GenerationEditorialReplacementPolicy
{
    // Only an explicit per-clip editorial rejection can replace an automatic
    // pick. Runtime failures and deliberately marked user ranges remain errors.
    internal static GenerationCandidateIntelligenceResult? RejectAutomaticCut(
        GenerationCandidateIntelligenceResult? intelligence, string? candidateId, CancellationToken cancellationToken)
    {
        if (intelligence?.VisualSemantic is not { Outcome: GenerationVisualSemanticOutcome.Completed } visual ||
            candidateId is null) return null;
        var moments = intelligence.RefinedMoments;
        var rejected = moments.SelectedCandidates.SingleOrDefault(item => item.Id == candidateId);
        if (rejected is null || rejected.IsHumanPriority) return null;
        var eligible = new HashSet<MomentCandidate>(visual.Observations.Select(item => item.Candidate), ReferenceEqualityComparer.Instance);
        if (moments.SelectionEligibleCandidates is { } retainedPool) eligible.IntersectWith(retainedPool);
        eligible.Remove(rejected.Candidate);
        var refinements = intelligence.Refinements.ToDictionary(item => item.Candidate);
        if (!refinements.TryGetValue(rejected.Candidate, out var refinement)) return null;
        refinements[rejected.Candidate] = new(rejected.Candidate,
            [.. refinement.Components.Where(item => item.Code != GenerationCandidateRefinementComponentCode.GroundedVisualRejection),
                new(GenerationCandidateRefinementComponentCode.GroundedVisualRejection, 1, 0,
                    "AI could not produce supported, useful wording for this cut. Review or trim it in Find More.",
                    ["editorial-review:wording-rejected"])], refinement.PolicyVersion);
        var selected = new GenerationMomentPortfolioSelector().SelectEligible(moments.Request, moments.Sources,
            refinements, eligible, moments.SelectionPreferences, cancellationToken);
        if (selected.Count == 0) return null;
        var replacement = new GenerationMomentFindingResult(moments.Request, moments.Sources, selected, refinements,
            eligible, "A cut that could not support reliable AI wording was left in Find More. Automatic picks still require picture review.",
            moments.SelectionPreferences);
        return new(intelligence.BaseMoments, intelligence.SpeechActivity, refinements.Values,
            replacement, visual, intelligence.Transcripts);
    }
}
