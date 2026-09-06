using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

public sealed record GenerationTranscriptLanguageSpan(TimeSpan Start, TimeSpan End,
    string? DetectedLanguage, bool TranslatedToEnglish, string? RequestedLanguage)
{
    public bool IsEnglish => TranslatedToEnglish ||
        string.Equals(DetectedLanguage ?? RequestedLanguage, "en", StringComparison.OrdinalIgnoreCase);
}

public sealed record GenerationSemanticTextWindow(string SourceFullPath, int AudioStreamIndex,
    TimeSpan Start, TimeSpan End, string Text, IReadOnlyList<string> SegmentIds);

public sealed record GenerationSemanticRetrievalMatch(GenerationSemanticTextWindow Window, double Similarity);

public sealed record GenerationSemanticRetrievalResult(
    IReadOnlyList<GenerationSemanticRetrievalMatch> Matches, string? ModelIdentity,
    string? ModelSha256, string? VocabularySha256, TimeSpan Elapsed, int CacheHits,
    string Limitations);

internal sealed record GenerationTimedExplorationSeed(TimeSpan Start, TimeSpan End, string EvidenceDescription);

internal static class GenerationSemanticRetrieval
{
    internal const int MaximumWindows = 256;
    internal const int MaximumTextCharacters = 240;
    internal const string Limitations =
        "English transcript similarity only, not event detection or entailment. Negation, hypothetical statements and sarcasm can match. " +
        "At most 256 source-stratified passages of 30 seconds and 240 characters are searched; longer phrases and unknown/non-English language spans are skipped. " +
        "A match requests bounded visual review and adds no event score. An explicit query may also supply a capped final selection preference after a complete Keep.";

    internal static async Task<GenerationSemanticRetrievalResult> SearchAsync(GenerationMomentFindingResult moments,
        IReadOnlyList<GenerationSourceTranscript> sources, ISemanticTextEmbeddingService embeddings,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!moments.Request.Setup.DiscoveryIntent.UsesSemanticRetrieval)
            throw new ArgumentException("Semantic search requires an explicit creator query or semantic objective.", nameof(moments));
        GenerationSemanticTextWindow[] windows = CreateWindows(sources, cancellationToken).ToArray();
        if (windows.Length == 0)
        {
            progress?.Report("No suitable English speech was found for this search. Continuing with the other moment checks.");
            return new([], null, null, null, TimeSpan.Zero, 0, Limitations);
        }
        SemanticTextEmbeddingResult embedded = await embeddings.EmbedAsync(
            [moments.Request.Setup.DiscoveryIntent.SemanticQuery, .. windows.Select(static window => window.Text)],
            progress, cancellationToken).ConfigureAwait(false);
        if (embedded.Vectors.Count != windows.Length + 1 ||
            embedded.Vectors.Any(vector => vector.Length != embedded.Vectors[0].Length || vector.Length == 0))
            throw new InvalidOperationException("Semantic search returned mismatched text vectors.");
        var matches = windows.Select((window, index) => new GenerationSemanticRetrievalMatch(window,
            Cosine(embedded.Vectors[0], embedded.Vectors[index + 1])))
            .OrderByDescending(static match => match.Similarity)
            .ThenBy(static match => match.Window.SourceFullPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static match => match.Window.Start).ToArray();
        return new(matches, embedded.ModelIdentity, embedded.ModelSha256, embedded.VocabularySha256,
            embedded.Elapsed, embedded.CacheHits, Limitations);
    }

    internal static IReadOnlyList<GenerationSemanticTextWindow> CreateWindows(
        IReadOnlyList<GenerationSourceTranscript> sources, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bySource = new List<List<GenerationSemanticTextWindow>>();
        foreach (GenerationSourceTranscript source in sources)
        {
            var windows = new List<GenerationSemanticTextWindow>();
            bool Eligible(ReplayFoundry.Desktop.Media.Transcription.AudioTranscriptionSegment segment) =>
                segment.Text.Length <= MaximumTextCharacters &&
                segment.Text.Count(char.IsLetter) >= 8 &&
                segment.AbsoluteSourceEnd - segment.AbsoluteSourceStart <= TimeSpan.FromSeconds(30) &&
                (source.LanguageSpans?.Any(span => span.IsEnglish &&
                    span.Start <= segment.AbsoluteSourceStart && span.End >= segment.AbsoluteSourceEnd) == true ||
                 source.LanguageSpans is null && string.Equals(segment.Language?.Code, "en", StringComparison.OrdinalIgnoreCase));
            var segments = source.Segments.OrderBy(static segment => segment.AbsoluteSourceStart).ToArray();
            for (int index = 0; index < segments.Length;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var first = segments[index++];
                if (!Eligible(first)) continue;
                TimeSpan end = first.AbsoluteSourceEnd;
                string text = first.Text;
                var ids = new List<string> { first.Id };
                while (index < segments.Length && Eligible(segments[index]) &&
                       segments[index].AbsoluteSourceStart >= end &&
                       segments[index].AbsoluteSourceStart - end <= TimeSpan.FromSeconds(3) &&
                       segments[index].AbsoluteSourceEnd - first.AbsoluteSourceStart <= TimeSpan.FromSeconds(30) &&
                       text.Length + 1 + segments[index].Text.Length <= MaximumTextCharacters)
                {
                    var next = segments[index++];
                    text += " " + next.Text; end = next.AbsoluteSourceEnd; ids.Add(next.Id);
                }
                windows.Add(new(source.SourceFullPath, source.AudioStreamIndex, first.AbsoluteSourceStart,
                    end, text, ids.ToArray()));
            }
            if (windows.Count > 0) bySource.Add(windows);
        }
        // Round-robin quotas reserve every source before dense speech can consume
        // the request budget; evenly spaced indexes retain full-duration coverage.
        int[] quotas = new int[bySource.Count]; int remaining = MaximumWindows;
        while (remaining > 0)
        {
            bool assigned = false;
            for (int index = 0; index < quotas.Length && remaining > 0; index++)
                if (quotas[index] < bySource[index].Count)
                { quotas[index]++; remaining--; assigned = true; }
            if (!assigned) break;
        }
        return bySource.SelectMany((windows, index) => Enumerable.Range(0, quotas[index])
            .Select(slot => windows[(int)((slot + .5) * windows.Count / quotas[index])])).ToArray();
    }

    internal static double Priority(GenerationSemanticRetrievalResult? retrieval, string sourcePath, MomentCandidate candidate)
    {
        // Scores only order review admission. A non-positive cosine cannot
        // nominate a passage; the best positive score is not a confidence level.
        return retrieval?.Matches.Where(match =>
            string.Equals(match.Window.SourceFullPath, sourcePath, StringComparison.OrdinalIgnoreCase) &&
            match.Window.Start >= candidate.Window.Start && match.Window.End <= candidate.Window.End)
            .Select(static match => Math.Max(0, match.Similarity)).DefaultIfEmpty(0).Max() ?? 0;
    }

    internal static double Cosine(float[] left, float[] right)
    {
        double dot = 0, leftNorm = 0, rightNorm = 0;
        for (int index = 0; index < left.Length; index++)
        { dot += (double)left[index] * right[index]; leftNorm += (double)left[index] * left[index]; rightNorm += (double)right[index] * right[index]; }
        double result = dot / Math.Sqrt(leftNorm * rightNorm);
        if (!double.IsFinite(result)) throw new InvalidOperationException("Semantic retrieval returned an invalid embedding.");
        return Math.Clamp(result, -1, 1);
    }
}
