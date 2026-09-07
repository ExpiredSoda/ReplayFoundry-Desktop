using ReplayFoundry.Desktop.Features.Generate.Progress;

namespace ReplayFoundry.Desktop.Features.Diagnostics;

internal sealed class GenerationFailureReporting : IDisposable
{
    private readonly GenerationProgressViewModel _progress;
    private readonly UserReportCoordinator _reports;

    public GenerationFailureReporting(GenerationProgressViewModel progress, UserReportCoordinator reports)
    {
        _progress = progress;
        _reports = reports;
        _progress.FailureOccurred += OnFailure;
    }

    private void OnFailure(object? sender, Exception exception) => _reports.TryCaptureGenerationFailure(exception);
    public void Dispose() => _progress.FailureOccurred -= OnFailure;
}
