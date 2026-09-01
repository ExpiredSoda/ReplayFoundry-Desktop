using ReplayFoundry.Desktop.Features.Generate.Handoff;
using System.IO;

namespace ReplayFoundry.Desktop.Features.Studio.Projects;

public interface IStudioProjectPersistenceCoordinator : IDisposable
{
    event EventHandler? PersistenceStateChanged;

    string? LastError { get; }

    void ScheduleSave(
        GenerationOutputProject project,
        StudioProjectRecoveryState? recovery = null);

    Task<StudioProjectRecoveryState?> GetRecoveryAsync(
        string projectId,
        CancellationToken cancellationToken = default);

    Task FlushAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

public sealed class StudioProjectPersistenceCoordinator :
    IStudioProjectPersistenceCoordinator
{
    public static readonly TimeSpan DefaultSaveDelay =
        TimeSpan.FromMilliseconds(750);

    private readonly object _gate = new();
    private readonly IGenerationOutputSession _session;
    private readonly IStudioProjectStore _store;
    private readonly TimeSpan _saveDelay;
    private readonly SemaphoreSlim _writer = new(1, 1);
    private readonly Dictionary<string, long> _revisions =
        new(StringComparer.Ordinal);
    private CancellationTokenSource? _delayCancellation;
    private GenerationOutputProject? _pendingProject;
    private StudioProjectRecoveryState? _pendingRecovery;
    private TaskCompletionSource<bool>? _saveQuiescence;
    private int _activeSaveOperations;
    private TaskCompletionSource<bool>? _stopCompletion;
    private long _scheduleVersion;
    private bool _stopping;
    private bool _disposed;

    public StudioProjectPersistenceCoordinator(
        IGenerationOutputSession session,
        IStudioProjectStore store,
        TimeSpan? saveDelay = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _saveDelay = saveDelay ?? DefaultSaveDelay;
        if (_saveDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(saveDelay));
        }

        _session.CurrentChanged += Session_CurrentChanged;
        if (_session.Current is not null)
        {
            ScheduleSave(_session.Current);
        }
    }

    public string? LastError { get; private set; }
    public event EventHandler? PersistenceStateChanged;

    public void ScheduleSave(
        GenerationOutputProject project,
        StudioProjectRecoveryState? recovery = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        CancellationTokenSource? superseded;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopping)
            {
                return;
            }

            _pendingProject = project;
            _pendingRecovery = recovery;
            superseded = _delayCancellation;
            _delayCancellation = new CancellationTokenSource();
            long version = ++_scheduleVersion;
            BeginSaveOperation();
            _ = SaveAfterDelayAsync(
                version,
                _delayCancellation.Token);
        }
        CancelAndDispose(superseded);
    }

    public async Task<StudioProjectRecoveryState?> GetRecoveryAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        await _writer.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            StudioProjectLoadResult result = await Task.Run(
                () => _store.Load(projectId),
                CancellationToken.None).ConfigureAwait(false);
            return result.Document?.Recovery;
        }
        finally
        {
            _writer.Release();
        }
    }

    public async Task FlushAsync(
        CancellationToken cancellationToken = default)
    {
        GenerationOutputProject? project;
        StudioProjectRecoveryState? recovery;
        CancellationTokenSource? delayCancellation;
        long version;
        lock (_gate)
        {
            delayCancellation = _delayCancellation;
            _delayCancellation = null;
            project = _pendingProject;
            recovery = _pendingRecovery;
            _pendingProject = null;
            _pendingRecovery = null;
            version = project is null
                ? _scheduleVersion
                : ++_scheduleVersion;
        }
        CancelAndDispose(delayCancellation);
        if (project is not null)
        {
            await SaveNowAsync(
                    project,
                    recovery,
                    version,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await WaitForSaveOperationsAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task StopAsync(
        CancellationToken cancellationToken = default)
    {
        Task stop = BeginStop();
        return cancellationToken.CanBeCanceled
            ? stop.WaitAsync(cancellationToken)
            : stop;
    }

    public void Dispose()
    {
        Task stop = BeginStop();
        if (stop.IsCompleted)
        {
            DisposeOwnedResources();
            return;
        }

        _ = DisposeAfterStopAsync(stop);
    }

    private Task BeginStop()
    {
        TaskCompletionSource<bool>? completion;
        lock (_gate)
        {
            if (_stopCompletion is not null)
            {
                return _stopCompletion.Task;
            }

            completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _stopCompletion = completion;
            _stopping = true;
        }

        _session.CurrentChanged -= Session_CurrentChanged;
        _ = CompleteStopAsync(completion);
        return completion.Task;
    }

    private async Task CompleteStopAsync(
        TaskCompletionSource<bool> completion)
    {
        try
        {
            await FlushPendingAsync(CancellationToken.None)
                .ConfigureAwait(false);
            completion.TrySetResult(true);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private async Task DisposeAfterStopAsync(Task stop)
    {
        try
        {
            await stop.ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidDataException or InvalidOperationException or
            ArgumentException)
        {
            SetLastError(exception.Message);
        }
        finally
        {
            DisposeOwnedResources();
        }
    }

    private void Session_CurrentChanged(
        object? sender,
        GenerationOutputChangedEventArgs e)
    {
        if (e.Current is not null)
        {
            ScheduleSave(e.Current);
            return;
        }

        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            _scheduleVersion++;
            cancellation = _delayCancellation;
            _delayCancellation = null;
            _pendingProject = null;
            _pendingRecovery = null;
        }
        CancelAndDispose(cancellation);
    }

    private async Task SaveAfterDelayAsync(
        long version,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_saveDelay, cancellationToken)
                .ConfigureAwait(false);
            GenerationOutputProject? project;
            StudioProjectRecoveryState? recovery;
            lock (_gate)
            {
                if (version != _scheduleVersion)
                {
                    return;
                }
                project = _pendingProject;
                recovery = _pendingRecovery;
                _pendingProject = null;
                _pendingRecovery = null;
            }
            if (project is not null)
            {
                await SaveNowAsync(
                        project,
                        recovery,
                        version,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SetLastError(exception.Message);
        }
        finally
        {
            CompleteSaveOperation();
        }
    }

    private void BeginSaveOperation()
    {
        if (_activeSaveOperations++ == 0)
        {
            _saveQuiescence = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void CompleteSaveOperation()
    {
        TaskCompletionSource<bool>? quiescence = null;
        lock (_gate)
        {
            _activeSaveOperations--;
            if (_activeSaveOperations == 0)
            {
                quiescence = _saveQuiescence;
                _saveQuiescence = null;
            }
        }
        quiescence?.TrySetResult(true);
    }

    private Task WaitForSaveOperationsAsync(
        CancellationToken cancellationToken)
    {
        Task completion;
        lock (_gate)
        {
            completion = _saveQuiescence?.Task ?? Task.CompletedTask;
        }
        return cancellationToken.CanBeCanceled
            ? completion.WaitAsync(cancellationToken)
            : completion;
    }

    private async Task SaveNowAsync(
        GenerationOutputProject project,
        StudioProjectRecoveryState? recovery,
        long version,
        CancellationToken cancellationToken)
    {
        await _writer.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (version != _scheduleVersion)
                {
                    return;
                }
            }
            await Task.Run(
                () =>
                {
                    long revision = NextRevision(project.Id);
                    _store.Save(project, revision, recovery);
                },
                CancellationToken.None).ConfigureAwait(false);
            SetLastError(null);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidDataException or InvalidOperationException or
            ArgumentException)
        {
            SetLastError(exception.Message);
        }
        finally
        {
            _writer.Release();
        }
    }

    private long NextRevision(string projectId)
    {
        if (!_revisions.TryGetValue(projectId, out long current))
        {
            StudioProjectLoadResult existing = _store.Load(projectId);
            current = existing.Document?.Revision ?? 0;
        }
        long next = checked(current + 1);
        _revisions[projectId] = next;
        return next;
    }

    private Task FlushPendingAsync(
        CancellationToken cancellationToken) =>
        FlushAsync(cancellationToken);

    private void DisposeOwnedResources()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            cancellation = _delayCancellation;
            _delayCancellation = null;
        }

        CancelAndDispose(cancellation);
        _writer.Dispose();
    }

    private static void CancelAndDispose(
        CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
        {
            return;
        }
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        cancellation.Dispose();
    }

    private void SetLastError(string? value)
    {
        if (string.Equals(LastError, value, StringComparison.Ordinal))
        {
            return;
        }

        LastError = value;
        PersistenceStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
