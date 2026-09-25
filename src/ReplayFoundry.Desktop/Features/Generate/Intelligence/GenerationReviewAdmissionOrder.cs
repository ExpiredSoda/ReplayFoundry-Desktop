namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal static class GenerationReviewAdmissionOrder
{
    internal static bool IsRoutineInterface(IReadOnlyList<GenerationCandidateRefinementComponent> evidence) =>
        evidence.Any(item => item.Code == GenerationCandidateRefinementComponentCode.NeuralIndexCoverage && item.RawValue >= .8) &&
        evidence.Any(item => item.Code == GenerationCandidateRefinementComponentCode.NeuralMenu && item.RawValue >= .8) &&
        !evidence.Any(item => item.Code is GenerationCandidateRefinementComponentCode.NeuralHumor or
            GenerationCandidateRefinementComponentCode.NeuralLore or GenerationCandidateRefinementComponentCode.NeuralCommentary && item.RawValue >= .3);

    internal static IReadOnlyList<T> Diversify<T>(IEnumerable<T> ordered, Func<T, bool> human,
        Func<T, T, bool> overlaps) where T : class
    {
        var distinct = new List<T>();
        var deferred = new List<T>();
        foreach (T candidate in ordered)
        {
            // Defer redundant windows; do not discard quiet scenes or remove
            // alternatives that may be useful after a different cut fails.
            if (!human(candidate) && distinct.Any(previous => overlaps(previous, candidate)))
                deferred.Add(candidate);
            else distinct.Add(candidate);
        }
        return distinct.Concat(deferred).ToArray();
    }
}
