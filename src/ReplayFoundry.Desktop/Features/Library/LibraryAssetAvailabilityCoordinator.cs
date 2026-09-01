using System;
using System.Collections.Generic;
using System.IO;

namespace ReplayFoundry.Desktop.Features.Library;

internal sealed class LibraryAssetAvailabilityCoordinator : IDisposable
{
    private readonly ILibraryFileAvailabilityMonitor?
        _fileAvailabilityMonitor;
    private readonly ILibraryThumbnailRecoveryService?
        _thumbnailRecoveryService;
    private readonly Action<LibraryFilesChangedEventArgs> _filesChanged;
    private readonly CancellationTokenSource _thumbnailRecoveryCancellation =
        new();
    private readonly object _thumbnailRecoveryGate = new();
    private readonly HashSet<string> _thumbnailRecoveries =
        new(StringComparer.Ordinal);
    private readonly SynchronizationContext? _notificationContext;
    private readonly System.Windows.Threading.Dispatcher?
        _notificationDispatcher;
    private bool _isDisposed;
    private bool _isStarted;

    public LibraryAssetAvailabilityCoordinator(
        ILibraryFileAvailabilityMonitor? fileAvailabilityMonitor,
        ILibraryThumbnailRecoveryService? thumbnailRecoveryService,
        Action<LibraryFilesChangedEventArgs> filesChanged)
    {
        _fileAvailabilityMonitor = fileAvailabilityMonitor;
        _thumbnailRecoveryService = thumbnailRecoveryService;
        _filesChanged = filesChanged ??
            throw new ArgumentNullException(nameof(filesChanged));
        _notificationContext = SynchronizationContext.Current;
        _notificationDispatcher =
            System.Windows.Threading.Dispatcher.FromThread(
                Thread.CurrentThread);
    }

    public void Start(IReadOnlyList<LibraryMediaAsset> assets)
    {
        if (_isDisposed)
        {
            return;
        }

        if (!_isStarted && _fileAvailabilityMonitor is not null)
        {
            _fileAvailabilityMonitor.FilesChanged +=
                FileAvailabilityMonitor_FilesChanged;
            _isStarted = true;
        }
        Watch(assets);
    }

    public void Watch(IReadOnlyList<LibraryMediaAsset> assets)
    {
        if (!_isDisposed)
        {
            _fileAvailabilityMonitor?.Watch(assets);
        }
    }

    public void QueueMissingThumbnailRecoveries(
        IEnumerable<LibraryItem> items)
    {
        foreach (LibraryItem item in items)
        {
            QueueMissingThumbnailRecovery(item);
        }
    }

    public void QueueMissingThumbnailRecovery(LibraryItem item)
    {
        LibraryMediaAsset? asset = item.Asset;
        if (_isDisposed ||
            _thumbnailRecoveryService is null ||
            asset is null ||
            !asset.IsAvailable ||
            asset.ThumbnailFullPath is not { } thumbnail ||
            File.Exists(thumbnail))
        {
            return;
        }

        lock (_thumbnailRecoveryGate)
        {
            if (!_thumbnailRecoveries.Add(asset.Id))
            {
                return;
            }
        }
        CancellationToken cancellationToken =
            _thumbnailRecoveryCancellation.Token;
        _ = Task.Run(() => RecoverThumbnailAsync(
            asset,
            cancellationToken));
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _thumbnailRecoveryCancellation.Cancel();
        if (_isStarted && _fileAvailabilityMonitor is not null)
        {
            _fileAvailabilityMonitor.FilesChanged -=
                FileAvailabilityMonitor_FilesChanged;
        }
        _fileAvailabilityMonitor?.Dispose();
        _thumbnailRecoveryCancellation.Dispose();
    }

    private void FileAvailabilityMonitor_FilesChanged(
        object? sender,
        LibraryFilesChangedEventArgs change) =>
        DispatchFilesChanged(change);

    private void DispatchFilesChanged(LibraryFilesChangedEventArgs change)
    {
        if (_isDisposed)
        {
            return;
        }

        if (_notificationContext is { } context &&
            !ReferenceEquals(SynchronizationContext.Current, context))
        {
            context.Post(
                static state =>
                {
                    var notification =
                        ((LibraryAssetAvailabilityCoordinator Owner,
                            LibraryFilesChangedEventArgs Change))state!;
                    notification.Owner.DeliverFilesChanged(
                        notification.Change);
                },
                (this, change));
            return;
        }

        if (_notificationDispatcher is { } dispatcher &&
            !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(
                new Action(() => DeliverFilesChanged(change)),
                System.Windows.Threading.DispatcherPriority.DataBind);
            return;
        }

        DeliverFilesChanged(change);
    }

    private void DeliverFilesChanged(LibraryFilesChangedEventArgs change)
    {
        if (!_isDisposed)
        {
            _filesChanged(change);
        }
    }

    private async Task RecoverThumbnailAsync(
        LibraryMediaAsset asset,
        CancellationToken cancellationToken)
    {
        bool recovered = false;
        try
        {
            recovered = await _thumbnailRecoveryService!.TryRecoverAsync(
                    asset,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            System.Security.SecurityException)
        {
            // Recovery is optional. Keep the usable video and placeholder.
        }
        finally
        {
            lock (_thumbnailRecoveryGate)
            {
                _thumbnailRecoveries.Remove(asset.Id);
            }
        }

        if (recovered && asset.ThumbnailFullPath is { } thumbnail)
        {
            DispatchFilesChanged(
                new LibraryFilesChangedEventArgs([thumbnail]));
        }
    }
}
