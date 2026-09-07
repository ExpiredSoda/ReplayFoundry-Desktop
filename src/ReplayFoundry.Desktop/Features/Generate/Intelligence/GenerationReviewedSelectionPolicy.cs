using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Features.Generate.Guidance;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal static class GenerationReviewedSelectionPolicy
{
    public static GenerationCandidateIntelligenceResult Apply(GenerationCandidateIntelligenceResult intelligence,
        CancellationToken cancellationToken)
    {
        if (intelligence.VisualSemantic is not { Outcome: GenerationVisualSemanticOutcome.Completed } visual)
            return intelligence;
        var eligible = new HashSet<MomentCandidate>(visual.Observations.Select(item => item.Candidate), ReferenceEqualityComparer.Instance);
        var refinements = intelligence.Refinements.ToDictionary(item => item.Candidate);
        foreach (var review in visual.Observations)
        {
            if (review.Observation.EditorialDisposition != VisualSemanticEditorialDisposition.Reject ||
                review.Observation.UncertaintyReasons.Count != 0 ||
                review.ReviewedSourceStart > review.Candidate.Window.Start ||
                review.ReviewedSourceEnd < review.Candidate.Window.End ||
                !refinements.TryGetValue(review.Candidate, out var refinement)) continue;
            // A deliberately requested range remains a human choice. Automatic count fill
            // and personal preference scores must not undo a completed editorial rejection.
            string path = review.Source.PreparedSource.Media.FullPath;
            if (intelligence.BaseMoments.Request.Setup.MomentGuidance.ForSource(path).Any(guidance =>
                    guidance.Kind == UserMomentGuidanceKind.PriorityPoint
                        ? review.Candidate.Window.Contains(guidance.Timestamp)
                        : review.Candidate.Window.Start < guidance.End && review.Candidate.Window.End > guidance.Start)) continue;
            refinements[review.Candidate] = new GenerationCandidateRefinement(review.Candidate,
                [.. refinement.Components.Where(item => item.Code != GenerationCandidateRefinementComponentCode.GroundedVisualRejection),
                    new(GenerationCandidateRefinementComponentCode.GroundedVisualRejection, 1, 0,
                        "AI review did not recommend this moment. You can still choose it manually in Find More.", ["visual-review:editorial-reject"])],
                refinement.PolicyVersion);
        }
        var preferences = GenerationSemanticFinalSelectionPreference.Create(intelligence);
        GenerationMomentFindingResult source = intelligence.RefinedMoments;
        var selected = new GenerationMomentPortfolioSelector().SelectEligible(source.Request, source.Sources, refinements, eligible, preferences, cancellationToken);
        string note = visual.FallbackReason ?? "Automatic selection was limited to the visually reviewed pool. Inspect other moments manually in Studio.";
        var moments = new GenerationMomentFindingResult(source.Request, source.Sources, selected, refinements, eligible, note);
        return new(intelligence.BaseMoments, intelligence.SpeechActivity, refinements.Values, moments, visual, intelligence.Transcripts);
    }
}
