using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal static class GenerationTranscriptCandidatePlanner
{
    public static GenerationMomentFindingResult Expand(GenerationMomentFindingResult moments,
        IReadOnlyList<GenerationSourceTranscript> transcripts, CancellationToken cancellationToken,
        GenerationSemanticRetrievalResult? semanticRetrieval = null)
    {
        var sources = new List<GenerationSourceMomentResult>();
        int remaining = GenerationSemanticReviewBudgetPolicy.MaximumCandidates;
        foreach (GenerationSourceMomentResult source in moments.Sources)
        {
            GenerationSourceTranscript? transcript = transcripts.SingleOrDefault(value =>
                value.SourceFullPath.Equals(source.AnalyzedSource.PreparedSource.Media.FullPath,
                    StringComparison.OrdinalIgnoreCase));
            AudioTranscriptionSegment[] eligible = transcript?.Segments
                .Where(segment => moments.Request.Setup.DiscoveryIntent.CountMatches(segment.Text) > 0 ||
                    segment.Text.Count(char.IsLetter) >= 20)
                .ToArray() ?? [];
            GenerationSemanticRetrievalMatch[] semanticMatches = semanticRetrieval?.Matches
                .Where(match => match.Similarity > 0 && string.Equals(match.Window.SourceFullPath,
                    source.AnalyzedSource.PreparedSource.Media.FullPath, StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
            int count = Math.Min(remaining, Math.Min(8, Math.Max(eligible.Length, semanticMatches.Length)));
            if (count == 0)
            {
                sources.Add(source);
                continue;
            }
            // Stratify across the full transcript instead of letting the first
            // few paragraphs monopolize the finite semantic review budget.
            int spokenCount = Math.Min(count, eligible.Length);
            var stratified = Enumerable.Range(0, spokenCount)
                .Select(index => eligible[(int)((index + 0.5) * eligible.Length / spokenCount)])
                .ToArray();
            AudioTranscriptionSegment[] seeds = eligible
                .Where(segment => moments.Request.Setup.DiscoveryIntent.CountMatches(segment.Text) > 0)
                .OrderByDescending(segment => moments.Request.Setup.DiscoveryIntent.CountMatches(segment.Text))
                .Take(Math.Max(1, count / 2))
                .Concat(stratified).DistinctBy(static segment => segment.Id)
                .Take(count).ToArray();
            var expanded = source.Moments;
            if (semanticRetrieval is not null)
            {
                // Spend at most half of the existing transcript proposal budget
                // on semantic retrieval; literal phrases retain their own path.
                GenerationTimedExplorationSeed[] semanticSeeds = semanticMatches
                    .Take(Math.Max(1, count / 2))
                    .Select(match => new GenerationTimedExplorationSeed(match.Window.Start, match.Window.End,
                        $"English transcript similarity nominated timed passages {string.Join(", ", match.Window.SegmentIds)} for review. Similarity is not event evidence."))
                    .ToArray();
                if (semanticSeeds.Length > 0)
                    expanded = GenerationSemanticExplorationPlanner.Expand(expanded, semanticSeeds.Length,
                        cancellationToken, semanticSeeds: semanticSeeds);
                int remainingSpoken = count - semanticSeeds.Length;
                if (remainingSpoken > 0 && seeds.Length > 0)
                    expanded = GenerationSemanticExplorationPlanner.Expand(expanded, Math.Min(remainingSpoken, seeds.Length),
                        cancellationToken, seeds);
            }
            else
                expanded = GenerationSemanticExplorationPlanner.Expand(expanded, Math.Min(count, seeds.Length),
                    cancellationToken, seeds);
            sources.Add(new GenerationSourceMomentResult(source.AnalyzedSource, expanded));
            remaining -= count;
        }
        return new(moments.Request, sources, moments.SelectedCandidates);
    }
}
