using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal static class GenerationEditorialReplacementPolicy
{
    // Only an explicit per-clip editorial rejection can replace an automatic
    // pick. Runtime failures and deliberately marked user ranges remain errors.
    internal static GenerationCandidateIntelligenceResult? RejectAutomaticCut(
        GenerationCandidateIntelligenceResult? intelligence, string? candidateId, CancellationToken cancellationToken,
        bool allowEmptyPool = false)
    {
        if (intelligence?.VisualSemantic is not { Outcome: GenerationVisualSemanticOutcome.Completed } visual ||
            candidateId is null) return null;
        var moments = intelligence.RefinedMoments;
        var rejected = moments.SelectedCandidates.SingleOrDefault(item => item.Id == candidateId);
        if (rejected is null || rejected.IsHumanPriority) return null;
        var eligible = new HashSet<MomentCandidate>(visual.Observations.Select(item => item.Candidate), ReferenceEqualityComparer.Instance);
        if (moments.SelectionEligibleCandidates is { } retainedPool) eligible.IntersectWith(retainedPool);
        if (!eligible.Remove(rejected.Candidate)) return null;
        var refinements = intelligence.Refinements.ToDictionary(item => item.Candidate);
        if (!refinements.TryGetValue(rejected.Candidate, out var refinement)) return null;
        refinements[rejected.Candidate] = new(rejected.Candidate,
            [.. refinement.Components.Where(item => item.Code != GenerationCandidateRefinementComponentCode.GroundedVisualRejection),
                new(GenerationCandidateRefinementComponentCode.GroundedVisualRejection, 1, 0,
                    "AI could not produce supported, useful wording for this cut. Review or trim it in Find More.",
                    ["editorial-review:wording-rejected"])], refinement.PolicyVersion);
        var selected = new GenerationMomentPortfolioSelector().SelectEligible(moments.Request, moments.Sources,
            refinements, eligible, moments.SelectionPreferences, cancellationToken);
        if (selected.Count == 0 && !allowEmptyPool) return null;
        var replacement = new GenerationMomentFindingResult(moments.Request, moments.Sources, selected, refinements,
            eligible, "A cut that could not support reliable AI wording was left in Find More. Automatic picks still require picture review.",
            moments.SelectionPreferences);
        return new(intelligence.BaseMoments, intelligence.SpeechActivity, refinements.Values,
            replacement, visual, intelligence.Transcripts);
    }

    internal static GenerationCandidateIntelligenceResult PreserveRejections(
        GenerationCandidateIntelligenceResult previous, GenerationCandidateIntelligenceResult refreshed,
        CancellationToken cancellationToken)
    {
        var rejected = previous.Refinements.Where(item => item.HasGroundedVisualRejection).ToDictionary(item => item.Candidate);
        if (rejected.Count == 0) return refreshed;
        var refinements = refreshed.Refinements.ToDictionary(item => item.Candidate);
        foreach (var (candidate, prior) in rejected)
        {
            if (!refinements.TryGetValue(candidate, out var current)) continue;
            refinements[candidate] = new(candidate,
                [.. current.Components.Where(item => item.Code != GenerationCandidateRefinementComponentCode.GroundedVisualRejection),
                 .. prior.Components.Where(item => item.Code == GenerationCandidateRefinementComponentCode.GroundedVisualRejection)],
                current.PolicyVersion);
        }
        var moments = refreshed.RefinedMoments;
        var eligible = new HashSet<MomentCandidate>(moments.SelectionEligibleCandidates is { } pool
            ? pool : Enumerable.Empty<MomentCandidate>(), ReferenceEqualityComparer.Instance);
        eligible.ExceptWith(rejected.Keys);
        var selected = new GenerationMomentPortfolioSelector().SelectEligible(moments.Request, moments.Sources,
            refinements, eligible, moments.SelectionPreferences, cancellationToken);
        return new(refreshed.BaseMoments, refreshed.SpeechActivity, refinements.Values,
            new GenerationMomentFindingResult(moments.Request, moments.Sources, selected, refinements, eligible,
                moments.SelectionReviewNote, moments.SelectionPreferences), refreshed.VisualSemantic, refreshed.Transcripts);
    }
}
