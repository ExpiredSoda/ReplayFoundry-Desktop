using ReplayFoundry.Desktop.Features.Publish.YouTube;

namespace ReplayFoundry.Desktop.Features.Publish;

internal sealed class PublishYouTubeOperationController : IDisposable
{
    private readonly IYouTubePublishingService _service;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _sync = new();
#pragma warning disable CA2213 // Non-owning reference to the RunAsync-scoped source.
    private CancellationTokenSource? _activeCancellation;
#pragma warning restore CA2213
    private bool _disposed;
    private int _ownedResourcesDisposed;

    public PublishYouTubeOperationController(
        IYouTubePublishingService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public bool IsConfigured => _service.IsConfigured;

    public IReadOnlyList<YouTubePublishHistoryEntry> History =>
        _service.History;

    public async Task<T> RunAsync<T>(
        Func<IYouTubePublishingService, CancellationToken, Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfDisposed();

        using var cancellation = CancellationTokenSource
            .CreateLinkedTokenSource(_lifetimeCancellation.Token);
        await _gate.WaitAsync(cancellation.Token);
        try
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                _activeCancellation = cancellation;
            }

            return await operation(_service, cancellation.Token);
        }
        finally
        {
            bool disposeOwnedResources;
            lock (_sync)
            {
                if (ReferenceEquals(_activeCancellation, cancellation))
                {
                    _activeCancellation = null;
                }

                disposeOwnedResources = _disposed;
            }

            _gate.Release();
            if (disposeOwnedResources)
            {
                DisposeOwnedResources();
            }
        }
    }

    public Task RunAsync(
        Func<IYouTubePublishingService, CancellationToken, Task> operation) =>
        RunAsync(async (service, cancellationToken) =>
        {
            await operation(service, cancellationToken);
            return true;
        });

    public void CancelActive()
    {
        lock (_sync)
        {
            _activeCancellation?.Cancel();
        }
    }

    public void ClearHistory()
    {
        ThrowIfDisposed();
        _service.ClearHistory();
    }

    public void Dispose()
    {
        bool disposeOwnedResources;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _lifetimeCancellation.Cancel();
            _activeCancellation?.Cancel();
            disposeOwnedResources = _activeCancellation is null;
        }

        if (disposeOwnedResources)
        {
            DisposeOwnedResources();
        }
    }

    private void DisposeOwnedResources()
    {
        if (Interlocked.Exchange(ref _ownedResourcesDisposed, 1) != 0)
        {
            return;
        }

        _lifetimeCancellation.Dispose();
        _gate.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(PublishYouTubeOperationController));
        }
    }
}
