namespace ReplayFoundry.Desktop.Features.Generate.Workflow;

internal enum GenerationOperationKind
{
    SourcePreparation,
    EvidenceAnalysis,
    Generation,
}

internal sealed class GenerationOperationController :
    IDisposable
{
    private readonly object _sync = new();
#pragma warning disable CA2213 // The controller disposes the active lease via CancelAndDispose.
    private GenerationOperationLease? _current;
#pragma warning restore CA2213
    private long _nextIdentity;
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
            active = _current;
            _current = null;
        }

        active?.CancelAndDispose();
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
        return true;
    }
}

internal sealed class GenerationOperationLease :
    IDisposable
{
    private readonly GenerationOperationController _owner;
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

    public void Dispose()
    {
        _owner.Complete(this);
    }

    internal void Cancel()
    {
        _cancellationSource?.Cancel();
    }

    internal void CancelAndDispose()
    {
        _cancellationSource?.Cancel();
        DisposeCancellationSource();
    }

    internal void DisposeCancellationSource()
    {
        CancellationTokenSource? source =
            Interlocked.Exchange(
                ref _cancellationSource,
                null);

        source?.Dispose();
    }
}
