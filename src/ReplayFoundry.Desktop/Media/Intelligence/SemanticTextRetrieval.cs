namespace ReplayFoundry.Desktop.Media.Intelligence;

/// <summary>Text similarity is retrieval relevance, never event detection or entailment.</summary>
public interface ISemanticTextEmbeddingService
{
    Task<SemanticTextEmbeddingResult> EmbedAsync(IReadOnlyList<string> texts,
        IProgress<string>? progress, CancellationToken cancellationToken);
}

public sealed record SemanticTextEmbeddingResult(IReadOnlyList<float[]> Vectors,
    string ModelIdentity, string ModelSha256, string VocabularySha256, TimeSpan Elapsed,
    int CacheHits);
