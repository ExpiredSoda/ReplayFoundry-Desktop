namespace ReplayFoundry.Desktop.Presentation.Commands;

internal sealed class AsyncOperationLifetime
{
    private readonly object _sync = new();
    private TaskCompletionSource<bool>? _quiescence;
    private int _activeOperations;
    private bool _sealed;

    public bool IsSealed
    {
        get
        {
            lock (_sync) return _sealed;
        }
    }

    public Task RunAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_sync)
        {
            if (_sealed) return Task.CompletedTask;
            if (_activeOperations++ == 0)
            {
                _quiescence = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
        return RunTrackedAsync(operation);
    }

    public Task Seal()
    {
        lock (_sync)
        {
            _sealed = true;
            return _quiescence?.Task ?? Task.CompletedTask;
        }
    }

    public void DisposeWhenQuiescent(IDisposable? resource)
    {
        if (resource is null) return;
        Task quiescence = Seal();
        if (quiescence.IsCompleted)
        {
            resource.Dispose();
            return;
        }
        _ = DisposeWhenCompleteAsync(quiescence, resource);
    }

    private async Task RunTrackedAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        finally
        {
            CompleteOperation();
        }
    }

    private void CompleteOperation()
    {
        TaskCompletionSource<bool>? quiescence = null;
        lock (_sync)
        {
            if (--_activeOperations == 0)
            {
                quiescence = _quiescence;
                _quiescence = null;
            }
        }
        quiescence?.TrySetResult(true);
    }

    private static async Task DisposeWhenCompleteAsync(
        Task quiescence,
        IDisposable resource)
    {
        await quiescence.ConfigureAwait(false);
        resource.Dispose();
    }
}
