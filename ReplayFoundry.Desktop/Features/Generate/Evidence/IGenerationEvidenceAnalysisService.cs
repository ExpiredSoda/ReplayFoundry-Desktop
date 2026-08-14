using ReplayFoundry.Desktop.Media.Analysis;

namespace ReplayFoundry.Desktop.Features.Generate.Evidence;

public interface IGenerationEvidenceAnalysisService
{
    MediaEvidenceAnalyzerIdentity AnalyzerIdentity { get; }

    Task<GenerationEvidenceAnalysisResult> AnalyzeAsync(
        GenerationEvidenceAnalysisRequest request,
        IProgress<GenerationEvidenceAnalysisProgress>? progress,
        CancellationToken cancellationToken);
}
