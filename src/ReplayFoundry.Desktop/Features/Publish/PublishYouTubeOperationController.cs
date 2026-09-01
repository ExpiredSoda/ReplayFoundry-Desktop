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
    private TaskCompletionSource<bool>? _quiescence;
    private int _operationCount;
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
        BeginOperation();
        bool gateHeld = false;
        try
        {
            using var cancellation = CancellationTokenSource
                .CreateLinkedTokenSource(_lifetimeCancellation.Token);
            await _gate.WaitAsync(cancellation.Token)
                .ConfigureAwait(false);
            gateHeld = true;
            try
            {
                lock (_sync)
                {
                    ThrowIfDisposed();
                    _activeCancellation = cancellation;
                }

                return await operation(_service, cancellation.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_activeCancellation, cancellation))
                    {
                        _activeCancellation = null;
                    }
                }
            }
        }
        finally
        {
            if (gateHeld)
            {
                _gate.Release();
            }
            CompleteOperation();
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
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            cancellation = _activeCancellation;
        }
        TryCancel(cancellation);
    }

    public void CancelAll() => TryCancel(_lifetimeCancellation);

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
            disposeOwnedResources = _operationCount == 0;
        }

        TryCancel(_lifetimeCancellation);

        if (disposeOwnedResources)
        {
            DisposeOwnedResources();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        Task completion;
        lock (_sync)
        {
            completion = _quiescence?.Task ??
                Task.CompletedTask;
        }

        await completion.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private void BeginOperation()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_operationCount++ == 0)
            {
                _quiescence = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    private void CompleteOperation()
    {
        TaskCompletionSource<bool>? quiescence = null;
        bool disposeOwnedResources;
        lock (_sync)
        {
            _operationCount--;
            if (_operationCount == 0)
            {
                quiescence = _quiescence;
                _quiescence = null;
            }
            disposeOwnedResources = _disposed && _operationCount == 0;
        }

        if (disposeOwnedResources)
        {
            DisposeOwnedResources();
        }
        quiescence?.TrySetResult(true);
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

    private static void TryCancel(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The operation completed between capture and cancellation.
        }
    }
}
