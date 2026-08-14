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

    bool TryGetRecovery(
        string projectId,
        out StudioProjectRecoveryState? recovery);

    Task FlushAsync(CancellationToken cancellationToken = default);
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
    private Task _pendingSave = Task.CompletedTask;
    private long _scheduleVersion;
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(project);
        lock (_gate)
        {
            _pendingProject = project;
            _pendingRecovery = recovery;
            _delayCancellation?.Cancel();
            _delayCancellation?.Dispose();
            _delayCancellation = new CancellationTokenSource();
            long version = ++_scheduleVersion;
            _pendingSave = SaveAfterDelayAsync(
                version,
                _delayCancellation.Token);
        }
    }

    public bool TryGetRecovery(
        string projectId,
        out StudioProjectRecoveryState? recovery)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        StudioProjectLoadResult result = _store.Load(projectId);
        recovery = result.Document?.Recovery;
        return recovery is not null;
    }

    public async Task FlushAsync(
        CancellationToken cancellationToken = default)
    {
        GenerationOutputProject? project;
        StudioProjectRecoveryState? recovery;
        lock (_gate)
        {
            _scheduleVersion++;
            _delayCancellation?.Cancel();
            _delayCancellation?.Dispose();
            _delayCancellation = null;
            project = _pendingProject;
            recovery = _pendingRecovery;
            _pendingProject = null;
            _pendingRecovery = null;
        }
        if (project is not null)
        {
            await SaveNowAsync(project, recovery, cancellationToken)
                .ConfigureAwait(false);
        }

        Task pending;
        lock (_gate)
        {
            pending = _pendingSave;
        }
        try
        {
            await pending.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            // A newer scheduled save cancelled the obsolete debounce task.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _session.CurrentChanged -= Session_CurrentChanged;
        try
        {
            FlushAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidDataException or InvalidOperationException or
            ArgumentException)
        {
            SetLastError(exception.Message);
        }
        lock (_gate)
        {
            _disposed = true;
            _delayCancellation?.Cancel();
            _delayCancellation?.Dispose();
            _delayCancellation = null;
        }
        _writer.Dispose();
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

        lock (_gate)
        {
            _scheduleVersion++;
            _delayCancellation?.Cancel();
            _delayCancellation?.Dispose();
            _delayCancellation = null;
            _pendingProject = null;
            _pendingRecovery = null;
        }
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
                await SaveNowAsync(project, recovery, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task SaveNowAsync(
        GenerationOutputProject project,
        StudioProjectRecoveryState? recovery,
        CancellationToken cancellationToken)
    {
        await _writer.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            long revision = NextRevision(project.Id);
            _store.Save(project, revision, recovery);
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
