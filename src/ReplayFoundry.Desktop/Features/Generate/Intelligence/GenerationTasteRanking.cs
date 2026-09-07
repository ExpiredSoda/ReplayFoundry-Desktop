using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Personalization;
using ReplayFoundry.Desktop.Media.Intelligence.Learning;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal sealed class GenerationTasteRanking(ITasteLearningService learning)
{
    internal async Task<GenerationMomentFindingResult> ApplyAsync(GenerationMomentFindingResult moments,
        GenerationCandidateIntelligenceResult? intelligence, CancellationToken cancellationToken)
    {
        if (!learning.Status.IsActive) return moments;
        var refinements = intelligence?.Refinements.ToDictionary(x => x.Candidate) ?? new Dictionary<MomentCandidate, GenerationCandidateRefinement>();
        var eligible = intelligence?.VisualSemantic is { Outcome: GenerationVisualSemanticOutcome.Completed } visual
            ? new HashSet<MomentCandidate>(visual.Observations.Select(x => x.Candidate), ReferenceEqualityComparer.Instance)
            : new HashSet<MomentCandidate>(moments.Sources.SelectMany(x => x.Moments.Proposals), ReferenceEqualityComparer.Instance);
        var entries = moments.Sources.SelectMany(source => source.Moments.Proposals.Where(eligible.Contains)
            .Select(candidate => (Candidate: candidate, Clip: TasteClipFactory.FromCandidate(source, candidate,
                refinements.GetValueOrDefault(candidate), moments, intelligence)))).ToArray();
        var predictions = await learning.PredictAsync(entries.Select(x => x.Clip).ToArray(), cancellationToken).ConfigureAwait(false);
        var preferences = intelligence is null ? new Dictionary<MomentCandidate, double>() :
            new Dictionary<MomentCandidate, double>(GenerationSemanticFinalSelectionPreference.Create(intelligence));
        bool applied = false;
        foreach (var entry in entries)
            if (predictions.TryGetValue(entry.Clip.Id, out var prediction) && prediction.IsActive)
            {
                // Bounded selection preference preserves the detector's quality, hard exclusions, explicit guidance and diversity rules.
                preferences[entry.Candidate] = preferences.GetValueOrDefault(entry.Candidate) + Math.Clamp(prediction.Preference, -1, 1) * 4;
                applied = true;
            }
        if (!applied) return moments;
        var selected = new GenerationMomentPortfolioSelector().SelectEligible(moments.Request, moments.Sources, refinements, eligible, preferences, cancellationToken);
        return new(moments.Request, moments.Sources, selected, refinements, eligible, moments.SelectionReviewNote, preferences);
    }
}
