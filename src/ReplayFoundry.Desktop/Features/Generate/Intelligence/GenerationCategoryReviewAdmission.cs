using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal static class GenerationCategoryReviewAdmission
{
    internal static IReadOnlyList<T> Select<T>(IEnumerable<T> ordered, int maximum, GenerationMomentIntent intent,
        Func<T, bool> humanPriority, Func<T, IReadOnlyList<GenerationCandidateRefinementComponent>> evidence,
        Func<T, bool>? unmapped = null, Func<T, bool>? gameplayHint = null) where T : class
    {
        if (maximum <= 0) return [];
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
        int budget = Math.Min(categories.Length, Math.Max(1, maximum / 2));
        int represented = 0;
        foreach (var category in categories)
        {
            if (represented >= budget) break;
            var nominees = candidates.Where(candidate => !reserved.Contains(candidate) && evidence(candidate).Any(item =>
                item.Code == GenerationCandidateRefinementComponentCode.NeuralIndexCoverage && item.RawValue >= .8) &&
                evidence(candidate).Any(item => item.Code == category && item.RawValue >= .3 &&
                    item.RawValue >= evidence(candidate).Where(value => categories.Contains(value.Code)).Max(value => value.RawValue) * .9))
                .OrderByDescending(candidate => evidence(candidate).First(item => item.Code == category).RawValue).ToArray();
            if (nominees.Length == 0) continue;
            represented++;
            Reserve(nominees[0]);
        }
        // A partial recording map cannot monopolize the review budget. Coarse
        // proposals outside that map remain unverified, but deserve inspection.
        if (unmapped is not null)
            foreach (T candidate in candidates.Where(unmapped).Take(Math.Max(1, maximum / 4))) Reserve(candidate);
        if (gameplayHint is not null && intent is GenerationMomentIntent.Any or GenerationMomentIntent.Action or GenerationMomentIntent.Clutch)
        {
            T? challenger = candidates.FirstOrDefault(gameplayHint);
            if (challenger is not null) Reserve(challenger);
        }
        // These are review slots, never category proof, score bonuses or final-output quotas.
        return selected;

        void Reserve(T candidate)
        {
            if (selected.Contains(candidate)) { reserved.Add(candidate); return; }
            int replace = selected.FindLastIndex(value => !reserved.Contains(value) && !humanPriority(value));
            if (replace < 0) return;
            selected[replace] = candidate;
            reserved.Add(candidate);
        }
    }
}
