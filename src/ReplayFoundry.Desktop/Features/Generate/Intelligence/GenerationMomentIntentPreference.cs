using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal static class GenerationMomentIntentPreference
{
    // Retain the existing creator-type preference budget, outside model quality.
    // Coarse recording labels cannot establish what a particular cut contains.
    internal const double MaximumBonus = 6;

    internal static void Apply(GenerationCandidateIntelligenceResult intelligence,
        IReadOnlyDictionary<MomentCandidate, GenerationCandidateRefinement> refinements,
        IDictionary<MomentCandidate, double> preferences)
    {
        if (intelligence.VisualSemantic is not { Outcome: GenerationVisualSemanticOutcome.Completed } visual) return;
        var setup = intelligence.BaseMoments.Request.Setup;
        foreach (var source in intelligence.BaseMoments.Sources)
        foreach (var candidate in source.Moments.Proposals)
        {
            if (!refinements.TryGetValue(candidate, out var refinement) ||
                !refinement.HasNeuralSceneValue && !refinement.Components.Any(component =>
                    component.Code == GenerationCandidateRefinementComponentCode.NeuralPersonalValue) ||
                !GenerationAutomaticCandidateEligibility.IsEligible(candidate, refinement) ||
                refinement.FinalScore < setup.QualityThreshold) continue;
            var review = visual.Observations.SingleOrDefault(item => ReferenceEquals(item.Candidate, candidate));
            if (review is null || !ReferenceEquals(review.Source, source.AnalyzedSource) || review.NeuralEditorialValue is <= .5) continue;
            var observation = review.Observation;
            if (observation.RejectReason != VisualSemanticEditorialRejectReason.None ||
                observation.HasDistinctEvent != VisualSemanticTernary.Yes ||
                observation.CandidateRequiresMissingContext != VisualSemanticTernary.No ||
                observation.TranscriptContextSupport == VisualSemanticTranscriptContextSupport.UnreliableOrAmbiguous ||
                setup.Mode == GenerationMode.IndividualClips && observation.HasObservablePayoff != VisualSemanticTernary.Yes ||
                GenerationDiscoveryIntentPolicy.SemanticMatch(setup.DiscoveryIntent, review).RawValue <= 0) continue;
            // Query similarity and the category request share one preference
            // budget. Neither makes the event more certain or crosses a gate.
            preferences[candidate] = Math.Max(preferences.TryGetValue(candidate, out double existing) ? existing : 0, MaximumBonus);
        }
    }
}
