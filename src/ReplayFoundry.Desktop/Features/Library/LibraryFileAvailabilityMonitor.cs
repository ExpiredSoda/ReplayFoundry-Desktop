using System.IO;

namespace ReplayFoundry.Desktop.Features.Library;

internal sealed class LibraryFilesChangedEventArgs : EventArgs
{
    private readonly HashSet<string> _fullPaths;

    public LibraryFilesChangedEventArgs(IEnumerable<string> fullPaths)
    {
        ArgumentNullException.ThrowIfNull(fullPaths);
        _fullPaths = fullPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_fullPaths.Count == 0)
        {
            throw new ArgumentException(
                "At least one changed Library path is required.",
                nameof(fullPaths));
        }
    }

    public IReadOnlyCollection<string> FullPaths => _fullPaths;

    public bool Touches(string? fullPath) =>
        !string.IsNullOrWhiteSpace(fullPath) &&
        _fullPaths.Contains(Path.GetFullPath(fullPath));
}

internal interface ILibraryFileAvailabilityMonitor : IDisposable
{
    event EventHandler<LibraryFilesChangedEventArgs>? FilesChanged;

    void Watch(IReadOnlyList<LibraryMediaAsset> assets);
}

/// <summary>
/// Observes the immutable media and thumbnail paths retained by Library.
/// File-system notifications make ordinary delete/restore operations prompt,
/// while a low-frequency snapshot check recovers from missed notifications or
/// a containing folder that disappears and later returns.
/// </summary>
internal sealed class LibraryFileAvailabilityMonitor :
    ILibraryFileAvailabilityMonitor
{
    private static readonly TimeSpan DefaultDebounceDelay =
        TimeSpan.FromMilliseconds(175);
    private static readonly TimeSpan DefaultPollInterval =
        TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly TimeSpan _debounceDelay;
    private readonly TimeSpan _pollInterval;
    private readonly Timer _debounceTimer;
    private readonly Timer _pollTimer;
    private readonly Dictionary<string, FileSnapshot> _snapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingPaths =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = [];
    private bool _disposed;

    public LibraryFileAvailabilityMonitor()
        : this(DefaultDebounceDelay, DefaultPollInterval)
    {
    }

    internal LibraryFileAvailabilityMonitor(
        TimeSpan debounceDelay,
        TimeSpan pollInterval)
    {
        if (debounceDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(debounceDelay));
        }
        if (pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }

        _debounceDelay = debounceDelay;
        _pollInterval = pollInterval;
        _debounceTimer = new Timer(
            static state =>
                ((LibraryFileAvailabilityMonitor)state!).PublishPending(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _pollTimer = new Timer(
            static state =>
                ((LibraryFileAvailabilityMonitor)state!).Poll(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    public event EventHandler<LibraryFilesChangedEventArgs>? FilesChanged;

    public void Watch(IReadOnlyList<LibraryMediaAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        string[] paths = assets
            .SelectMany(static asset => asset.ThumbnailFullPath is { } thumbnail
                ? new[] { asset.OutputFullPath, thumbnail }
                : new[] { asset.OutputFullPath })
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            DisposeWatchers();
            _pendingPaths.Clear();
            _snapshots.Clear();
            foreach (string path in paths)
            {
                _snapshots[path] = FileSnapshot.Capture(path);
            }

            CreateWatchers(paths);
            _pollTimer.Change(
                paths.Length == 0
                    ? Timeout.InfiniteTimeSpan
                    : _pollInterval,
                paths.Length == 0
                    ? Timeout.InfiniteTimeSpan
                    : _pollInterval);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _debounceTimer.Change(
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            _pollTimer.Change(
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            DisposeWatchers();
            _pendingPaths.Clear();
            _snapshots.Clear();
        }

        _debounceTimer.Dispose();
        _pollTimer.Dispose();
    }

    private void CreateWatchers(IReadOnlyList<string> paths)
    {
        foreach (IGrouping<string, string> group in paths
                     .Where(static path =>
                         Directory.Exists(Path.GetDirectoryName(path)))
                     .GroupBy(
                         static path => Path.GetDirectoryName(path)!,
                         StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var watcher = new FileSystemWatcher(group.Key)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName |
                        NotifyFilters.LastWrite |
                        NotifyFilters.Size |
                        NotifyFilters.CreationTime,
                };
                watcher.Changed += Watcher_Changed;
                watcher.Created += Watcher_Changed;
                watcher.Deleted += Watcher_Changed;
                watcher.Renamed += Watcher_Renamed;
                watcher.Error += Watcher_Error;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                ArgumentException or
                System.Security.SecurityException)
            {
                // Snapshot polling remains active when a directory cannot be
                // watched, including while a missing folder is restored.
            }
        }
    }

    private void Watcher_Changed(object sender, FileSystemEventArgs e) =>
        QueueIfTracked(e.FullPath);

    private void Watcher_Renamed(object sender, RenamedEventArgs e)
    {
        QueueIfTracked(e.OldFullPath);
        QueueIfTracked(e.FullPath);
    }

    private void Watcher_Error(object sender, ErrorEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            foreach (string path in _snapshots.Keys)
            {
                _pendingPaths.Add(path);
            }
            SchedulePendingPublication();
        }
    }

    private void QueueIfTracked(string fullPath)
    {
        string normalized;
        try
        {
            normalized = Path.GetFullPath(fullPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed || !_snapshots.ContainsKey(normalized))
            {
                return;
            }

            _pendingPaths.Add(normalized);
            SchedulePendingPublication();
        }
    }

    private void SchedulePendingPublication() =>
        _debounceTimer.Change(
            _debounceDelay,
            Timeout.InfiniteTimeSpan);

    private void PublishPending()
    {
        string[] changed;
        lock (_gate)
        {
            if (_disposed || _pendingPaths.Count == 0)
            {
                return;
            }

            changed = _pendingPaths.ToArray();
            _pendingPaths.Clear();
            foreach (string path in changed)
            {
                if (_snapshots.ContainsKey(path))
                {
                    _snapshots[path] = FileSnapshot.Capture(path);
                }
            }
        }

        FilesChanged?.Invoke(
            this,
            new LibraryFilesChangedEventArgs(changed));
    }

    private void Poll()
    {
        string[] changed;
        lock (_gate)
        {
            if (_disposed || _snapshots.Count == 0)
            {
                return;
            }

            var detected = new List<string>();
            foreach ((string path, FileSnapshot previous) in
                     _snapshots.ToArray())
            {
                FileSnapshot current = FileSnapshot.Capture(path);
                if (current == previous)
                {
                    continue;
                }

                _snapshots[path] = current;
                detected.Add(path);
            }
            changed = detected.ToArray();
        }

        if (changed.Length > 0)
        {
            FilesChanged?.Invoke(
                this,
                new LibraryFilesChangedEventArgs(changed));
        }
    }

    private void DisposeWatchers()
    {
        foreach (FileSystemWatcher watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= Watcher_Changed;
            watcher.Created -= Watcher_Changed;
            watcher.Deleted -= Watcher_Changed;
            watcher.Renamed -= Watcher_Renamed;
            watcher.Error -= Watcher_Error;
            watcher.Dispose();
        }
        _watchers.Clear();
    }

    private readonly record struct FileSnapshot(
        bool Exists,
        long Length,
        long LastWriteTicks)
    {
        public static FileSnapshot Capture(string fullPath)
        {
            try
            {
                var file = new FileInfo(fullPath);
                if (!file.Exists)
                {
                    return default;
                }

                return new FileSnapshot(
                    true,
                    file.Length,
                    file.LastWriteTimeUtc.Ticks);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                ArgumentException or
                NotSupportedException or
                System.Security.SecurityException)
            {
                return default;
            }
        }
    }
}
