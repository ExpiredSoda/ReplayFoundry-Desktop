using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.Preview;

namespace ReplayFoundry.Desktop.Features.Studio;

internal sealed class StudioPreviewPrewarmCoordinator : IDisposable
{
    private readonly IStudioPreviewPrewarmer? _prewarmer;
    private CancellationTokenSource? _cancellation;
    private bool _isDisposed;

    public StudioPreviewPrewarmCoordinator(
        IStudioPreviewPrewarmer? prewarmer)
    {
        _prewarmer = prewarmer;
    }

    public void Restart(
        GenerationOutputProject? project,
        string? priorityAssetId)
    {
        CancelPending();
        if (_isDisposed ||
            _prewarmer is null ||
            project is not { IsFinalized: false })
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        _ = PrewarmSafelyAsync(
            project,
            priorityAssetId,
            _cancellation.Token);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        CancelPending();
    }

    public void Suspend() => CancelPending();

    private void CancelPending()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private async Task PrewarmSafelyAsync(
        GenerationOutputProject project,
        string? priorityAssetId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _prewarmer!.PrewarmAsync(
                project,
                priorityAssetId,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
            // Foreground preview owns actionable errors. Prewarming is
            // opportunistic and must never block Studio editing.
        }
        catch (InvalidOperationException)
        {
            // The project can change while optional prewarming is queued.
            // Foreground preview owns any user-visible retry or error.
        }
    }
}
