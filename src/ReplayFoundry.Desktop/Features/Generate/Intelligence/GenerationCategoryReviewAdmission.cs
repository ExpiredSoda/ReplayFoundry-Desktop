using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal static class GenerationCategoryReviewAdmission
{
    internal static IReadOnlyList<T> Select<T>(IEnumerable<T> ordered, int maximum, GenerationMomentIntent intent,
        Func<T, bool> humanPriority, Func<T, IReadOnlyList<GenerationCandidateRefinementComponent>> evidence) where T : class
    {
        var candidates = ordered.Distinct().ToArray();
        var selected = candidates.Take(maximum).ToList();
        GenerationCandidateRefinementComponentCode[] categories = intent switch
        {
            GenerationMomentIntent.Any => [GenerationCandidateRefinementComponentCode.NeuralHumor,
                GenerationCandidateRefinementComponentCode.NeuralLore, GenerationCandidateRefinementComponentCode.NeuralCommentary,
                GenerationCandidateRefinementComponentCode.NeuralGameplay],
            GenerationMomentIntent.Humor => [GenerationCandidateRefinementComponentCode.NeuralHumor],
            GenerationMomentIntent.Story or GenerationMomentIntent.Discovery => [GenerationCandidateRefinementComponentCode.NeuralLore],
            GenerationMomentIntent.Dialogue or GenerationMomentIntent.Tutorial or GenerationMomentIntent.Reaction =>
                [GenerationCandidateRefinementComponentCode.NeuralCommentary],
            GenerationMomentIntent.Action or GenerationMomentIntent.Clutch or GenerationMomentIntent.Failure => [GenerationCandidateRefinementComponentCode.NeuralGameplay],
            _ => [],
        };
        var reserved = new HashSet<T>(selected.Where(humanPriority));
        int budget = Math.Min(categories.Length, Math.Max(1, maximum / 4));
        int represented = 0;
        foreach (var category in categories)
        {
            if (represented >= budget) break;
            var nominees = candidates.Where(candidate => evidence(candidate).Any(item =>
                item.Code == GenerationCandidateRefinementComponentCode.NeuralIndexCoverage && item.RawValue >= .8) &&
                evidence(candidate).Any(item => item.Code == category && item.RawValue >= .3)).ToArray();
            if (nominees.Length == 0) continue;
            represented++;
            var representative = nominees.FirstOrDefault(selected.Contains);
            if (representative is not null) { reserved.Add(representative); continue; }
            representative = nominees[0];
            int replace = selected.FindLastIndex(candidate => !reserved.Contains(candidate) && !humanPriority(candidate));
            if (replace < 0) continue;
            selected[replace] = representative; reserved.Add(representative);
        }
        // These are review slots, never category proof, score bonuses or final-output quotas.
        return selected;
    }
}
