namespace ReplayFoundry.Desktop.Features.Generate.Evidence;

public interface IGenerationEvidenceAnalysisCoordinator
{
    GenerationEvidenceAnalysisSettings Settings { get; }

    GenerationEvidenceAnalysisResult? Current { get; }

    Task<GenerationEvidenceAnalysisResult> GetOrAnalyzeAsync(
        GenerationEvidenceAnalysisRequest request,
        IProgress<GenerationEvidenceAnalysisProgress>? progress,
        CancellationToken cancellationToken);

    void Invalidate();
}
