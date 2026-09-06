using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Moments;

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
        var preferences = GenerationSemanticFinalSelectionPreference.Create(intelligence);
        GenerationMomentFindingResult source = intelligence.RefinedMoments;
        var selected = new GenerationMomentPortfolioSelector().SelectEligible(source.Request, source.Sources, refinements, eligible, preferences, cancellationToken);
        string note = visual.FallbackReason ?? "Automatic selection was limited to the visually reviewed pool. Inspect other moments manually in Studio.";
        var moments = new GenerationMomentFindingResult(source.Request, source.Sources, selected, refinements, eligible, note);
        return new(intelligence.BaseMoments, intelligence.SpeechActivity, intelligence.Refinements, moments, visual, intelligence.Transcripts);
    }
}
