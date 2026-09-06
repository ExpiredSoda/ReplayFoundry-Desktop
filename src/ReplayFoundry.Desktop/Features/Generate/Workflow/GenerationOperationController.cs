namespace ReplayFoundry.Desktop.Features.Generate.Workflow;

internal enum GenerationOperationKind
{
    SourcePreparation,
    EvidenceAnalysis,
    Generation,
    FolderImport,
}

internal sealed class GenerationOperationController :
    IDisposable
{
    private readonly object _sync = new();
#pragma warning disable CA2213 // Dispose detaches and cancels an active lease; its operation scope remains the terminal CTS owner.
    private GenerationOperationLease? _current;
#pragma warning restore CA2213
    private long _nextIdentity;
    private bool _isStopping;
    private bool _isDisposed;

    public bool HasActiveOperation
    {
        get
        {
            lock (_sync)
            {
                return _current is not null;
            }
        }
    }

    public GenerationOperationKind? ActiveKind
    {
        get
        {
            lock (_sync)
            {
                return _current?.Kind;
            }
        }
    }

    public GenerationOperationLease Begin(
        GenerationOperationKind kind)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The generation operation kind is not defined.");
        }

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(
                _isDisposed,
                this);
            if (_isStopping)
            {
                throw new InvalidOperationException(
                    "Generate is stopping and cannot begin another operation.");
            }

            if (_current is not null)
            {
                throw new InvalidOperationException(
                    "Another Generate operation is already active.");
            }

            var lease = new GenerationOperationLease(
                this,
                checked(++_nextIdentity),
                kind,
                new CancellationTokenSource());

            _current = lease;
            return lease;
        }
    }

    public bool IsCurrent(
        GenerationOperationLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);

        lock (_sync)
        {
            return ReferenceEquals(_current, lease);
        }
    }

    public void CancelActive()
    {
        GenerationOperationLease active;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(
                _isDisposed,
                this);

            active = _current ??
                throw new InvalidOperationException(
                    "There is no active Generate operation to cancel.");
        }

        active.Cancel();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        GenerationOperationLease active;
        Task completion;
        lock (_sync)
        {
            _isStopping = true;
            if (_current is null)
            {
                return;
            }

            active = _current;
            completion = active.Completion;
        }

        active.Cancel();
        await completion.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        GenerationOperationLease? active;

        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _isStopping = true;
            active = _current;
            _current = null;
        }

        active?.Cancel();
    }

    internal bool Complete(
        GenerationOperationLease lease)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(_current, lease))
            {
                return false;
            }

            _current = null;
        }

        lease.DisposeCancellationSource();
        lease.SignalCompletion();
        return true;
    }
}

internal sealed class GenerationOperationLease :
    IDisposable
{
    private readonly GenerationOperationController _owner;
    private readonly object _cancellationSync = new();
    private readonly TaskCompletionSource<bool> _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _cancellationSource;

    internal GenerationOperationLease(
        GenerationOperationController owner,
        long identity,
        GenerationOperationKind kind,
        CancellationTokenSource cancellationSource)
    {
        _owner = owner;
        Identity = identity;
        Kind = kind;
        _cancellationSource = cancellationSource;
    }

    public long Identity { get; }

    public GenerationOperationKind Kind { get; }

    public CancellationToken CancellationToken =>
        (_cancellationSource ??
         throw new ObjectDisposedException(
             nameof(GenerationOperationLease)))
        .Token;

    public bool IsCancellationRequested =>
        _cancellationSource?.IsCancellationRequested == true;

    public bool IsCurrent => _owner.IsCurrent(this);

    internal Task Completion => _completion.Task;

    public void Dispose()
    {
        if (!_owner.Complete(this))
        {
            DisposeCancellationSource();
            SignalCompletion();
        }
    }

    internal void Cancel()
    {
        CancellationTokenSource? cancellation;
        lock (_cancellationSync)
        {
            cancellation = _cancellationSource;
        }
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The operation completed between capture and cancellation.
        }
    }

    internal void DisposeCancellationSource()
    {
        lock (_cancellationSync)
        {
            CancellationTokenSource? source = _cancellationSource;
            _cancellationSource = null;
            source?.Dispose();
        }
    }

    internal void SignalCompletion() =>
        _completion.TrySetResult(true);
}
