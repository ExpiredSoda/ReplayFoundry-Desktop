using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReplayFoundry.Desktop.Media.Analysis;

public interface IMediaEvidenceAnalyzer
{
    MediaEvidenceAnalyzerIdentity Identity { get; }

    Task<MediaEvidenceResult> AnalyzeAsync(
        MediaEvidenceAnalysisRequest request,
        IProgress<MediaEvidenceProgressUpdate>? progress,
        CancellationToken cancellationToken);
}
