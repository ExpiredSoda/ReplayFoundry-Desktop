using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Projects;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Rendering;

public sealed record StudioRenderQueueItem(
    string AssetId,
    string Title,
    string Detail,
    string Status,
    bool IsCompleted,
    bool HasLibraryCopy,
    StudioPersistedRenderState PersistedState =
        StudioPersistedRenderState.Ready);

public sealed class StudioFinalRenderViewModel :
    INotifyPropertyChanged,
    IDisposable
{
    private readonly IGenerationOutputEditor? _outputEditor;
    private readonly IGenerationRenderedOutputSink? _renderedOutputSink;
    private readonly IStudioProjectRenderingService? _renderingService;
    private readonly ILibraryCatalog? _libraryCatalog;
    private readonly Func<string> _renderTokenFactory;
    private readonly Func<bool> _applyPendingEdit;
    private readonly Func<bool> _hasPendingEdit;
    private readonly Func<bool> _isPendingEditValid;
    private readonly Func<bool> _hasUnsavedMetadata;
    private readonly Func<bool> _hasActiveProjectMutation;
    private readonly Func<GenerationOutputAsset?> _selectedAsset;
    private readonly Action<bool> _setHostBusy;
    private readonly ObservableCollection<StudioRenderQueueItem> _queue = [];
    private readonly ReadOnlyObservableCollection<StudioRenderQueueItem>
        _readOnlyQueue;
    private readonly DelegateCommand _addToQueueCommand;
    private readonly AsyncDelegateCommand _renderQueueCommand;
    private readonly DelegateCommand<string> _removeQueuedItemCommand;
    private readonly DelegateCommand<string> _rerenderQueuedItemCommand;
    private readonly DelegateCommand _cancelCommand;
    private CancellationTokenSource? _cancellation;
    private GenerationOutputProject? _project;
    private string? _boundProjectId;
    private bool _isRendering;
    private string _status =
        "No clips are queued. Select a kept clip in Browser, then add it here.";
    private string? _error;
    private double _percent;
    private bool _isDisposed;

    public StudioFinalRenderViewModel(
        IGenerationOutputEditor? outputEditor,
        IStudioProjectRenderingService? renderingService,
        Func<bool> applyPendingEdit,
        Action<bool> setHostBusy,
        Func<bool>? hasPendingEdit = null,
        Func<bool>? isPendingEditValid = null,
        Func<bool>? hasUnsavedMetadata = null,
        Func<bool>? hasActiveProjectMutation = null,
        Func<GenerationOutputAsset?>? selectedAsset = null,
        ILibraryCatalog? libraryCatalog = null,
        Func<string>? renderTokenFactory = null)
    {
        _outputEditor = outputEditor;
        _renderedOutputSink = outputEditor as IGenerationRenderedOutputSink;
        _renderingService = renderingService;
        _libraryCatalog = libraryCatalog;
        _renderTokenFactory = renderTokenFactory ??
            (() => Guid.NewGuid().ToString("N"));
        _applyPendingEdit = applyPendingEdit ??
            throw new ArgumentNullException(nameof(applyPendingEdit));
        _hasPendingEdit = hasPendingEdit ?? (() => false);
        _isPendingEditValid = isPendingEditValid ?? (() => true);
        _hasUnsavedMetadata = hasUnsavedMetadata ?? (() => false);
        _hasActiveProjectMutation = hasActiveProjectMutation ?? (() => false);
        _selectedAsset = selectedAsset ??
            (() => _project?.IncludedAssets.FirstOrDefault());
        _setHostBusy = setHostBusy ??
            throw new ArgumentNullException(nameof(setHostBusy));
        _readOnlyQueue = new ReadOnlyObservableCollection<StudioRenderQueueItem>(
            _queue);
        _addToQueueCommand = new DelegateCommand(
            AddSelectedAssetToQueue,
            CanAddToQueue);
        _renderQueueCommand = new AsyncDelegateCommand(
            RenderQueueAsync,
            CanRenderQueue);
        _removeQueuedItemCommand = new DelegateCommand<string>(
            RemoveQueuedItem,
            CanRemoveQueuedItem);
        _rerenderQueuedItemCommand = new DelegateCommand<string>(
            RerenderQueuedItem,
            CanRerenderQueuedItem);
        _cancelCommand = new DelegateCommand(
            Cancel,
            () => IsRendering);
        if (_libraryCatalog is not null)
        {
            _libraryCatalog.Changed += LibraryCatalog_Changed;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsRendering => _isRendering;
    public string Status => _status;
    public string? Error => _error;
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public double Percent => _percent;
    public bool IsProgressVisible => IsRendering;
    public IReadOnlyList<StudioRenderQueueItem> QueueItems => _readOnlyQueue;
    public bool HasQueuedItems => _queue.Count > 0;
    public int QueuedClipCount => _queue.Count;
    public string QueueSummary
    {
        get
        {
            if (_project is null)
            {
                return "Generate clips before creating a render queue.";
            }
            if (_queue.Count == 0)
            {
                return "The queue is empty. Use the plus icon on any kept Browser clip.";
            }
            int pending = _queue.Count(static item => !item.IsCompleted);
            int completed = _queue.Count(static item => item.IsCompleted);
            return pending == 0
                ? $"{completed} " +
                  (completed == 1 ? "clip has" : "clips have") +
                  " a Library copy. Remove an item or choose re-render to create another copy."
                : $"{pending} " +
                  (pending == 1 ? "clip is" : "clips are") +
                  " ready to render with the latest saved Studio edits.";
        }
    }
    public bool IsReadyToQueue => CanAddToQueue();
    public bool IsReadyToRender => CanRenderQueue();
    public bool NeedsIncludedCandidate =>
        _project is { IsFinalized: false } &&
        FindSelectedIncludedAsset() is null;
    public bool NeedsMetadataSave =>
        _project is { IsFinalized: false } && _hasUnsavedMetadata();
    public bool NeedsValidClipEdit =>
        _project is { IsFinalized: false } &&
        _hasPendingEdit() &&
        !_isPendingEditValid();
    public bool NeedsRenderAttention =>
        !IsRendering &&
        (NeedsIncludedCandidate ||
         NeedsValidClipEdit ||
         NeedsMetadataSave);
    public string ButtonText => _project switch
    {
        null => "Add to render queue",
        _ when IsRendering => "Rendering…",
        _ when FindSelectedIncludedAsset() is null => "Add selected clip",
        _ when IsSelectedAssetQueued => "Selected clip queued",
        _ => "Add selected clip",
    };
    public string RenderQueueButtonText => _project switch
    {
        _ when IsRendering => "Rendering queue…",
        _ => "Render queue",
    };
    public string ReadinessText => _project switch
    {
        null => "A generated project is required",
        _ when FindSelectedIncludedAsset() is null =>
            "Keep and select a Browser clip before queueing",
        _ when NeedsValidClipEdit =>
            "Move the clip end after its start before queueing",
        _ when _hasActiveProjectMutation() =>
            "Wait for the current Studio update to finish",
        _ when NeedsMetadataSave =>
            "Save the visible metadata changes before queueing",
        _ when IsSelectedAssetQueued =>
            "The selected clip is already queued",
        _ => "Ready to add the selected clip",
    };

    public ICommand AddToQueueCommand => _addToQueueCommand;
    public ICommand RenderQueueCommand => _renderQueueCommand;
    public ICommand RemoveQueuedItemCommand => _removeQueuedItemCommand;
    public ICommand RerenderQueuedItemCommand => _rerenderQueuedItemCommand;
    public ICommand CancelCommand => _cancelCommand;

    public void Bind(GenerationOutputProject? project)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        bool changedProject = !string.Equals(
            _boundProjectId,
            project?.Id,
            StringComparison.Ordinal);
        _project = project;
        _boundProjectId = project?.Id;

        if (changedProject)
        {
            _queue.Clear();
            _percent = 0;
            _error = null;
            _status =
                "No clips are queued. Use the plus icon on a kept Browser clip.";
        }
        else
        {
            RefreshQueueItems();
        }
        NotifyProperties();
    }

    public void RefreshReadiness() => NotifyProperties();

    internal StudioProjectRecoveryState CaptureRecoveryState(
        string? selectedAssetId,
        TimeSpan? previewPosition) =>
        new(
            selectedAssetId,
            _queue.Select(item => new StudioRenderQueueEntryDocument(
                item.AssetId,
                item.IsCompleted
                    ? StudioPersistedRenderState.Completed
                    : IsRendering
                        ? StudioPersistedRenderState.Interrupted
                        : item.PersistedState)).ToArray(),
            previewPosition);

    internal void RestoreRecoveryState(
        StudioProjectRecoveryState recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        if (_project is null)
        {
            return;
        }
        var assets = _project.Assets.ToDictionary(
            static asset => asset.Id,
            StringComparer.Ordinal);
        _queue.Clear();
        bool interrupted = false;
        foreach (StudioRenderQueueEntryDocument entry in recovery.RenderQueue)
        {
            if (!assets.TryGetValue(
                    entry.AssetId,
                    out GenerationOutputAsset? asset))
            {
                continue;
            }
            interrupted |= entry.State == StudioPersistedRenderState.Interrupted;
            bool hasLibraryCopy = HasLibraryCopy(asset.Id);
            bool isCompleted = hasLibraryCopy ||
                _libraryCatalog is null &&
                entry.State == StudioPersistedRenderState.Completed;
            StudioRenderQueueItem item = BuildQueueItem(
                asset,
                isCompleted,
                hasLibraryCopy);
            _queue.Add(entry.State == StudioPersistedRenderState.Interrupted
                ? item with
                {
                    Status = "INTERRUPTED",
                    PersistedState = StudioPersistedRenderState.Interrupted,
                }
                : item with { PersistedState = entry.State });
        }
        _status = interrupted
            ? "A previous render stopped before completion. Review the restored queue, then render it again."
            : _queue.Count == 0
                ? _status
                : "Studio restored this project's render queue.";
        NotifyProperties();
    }

    public bool RemoveAssetFromQueue(string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        StudioRenderQueueItem? item = _queue.FirstOrDefault(value =>
            value.AssetId.Equals(assetId, StringComparison.Ordinal));
        if (item is null || IsRendering || _project?.IsFinalized != false)
        {
            return false;
        }

        _queue.Remove(item);
        _status =
            $"Removed {item.Title} from this render queue. The Studio clip and its metadata are still available.";
        NotifyProperties();
        return true;
    }

    internal async Task FinalizeProjectAsync()
    {
        if (!CanRenderQueue())
        {
            throw new InvalidOperationException(
                "The Studio render queue is not ready for final rendering.");
        }

        await RenderQueueAsync();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        if (_libraryCatalog is not null)
        {
            _libraryCatalog.Changed -= LibraryCatalog_Changed;
        }
        SetRendering(false);
        _isDisposed = true;
    }

    private bool CanAttemptQueueChange() =>
        _project is { IsFinalized: false } &&
        _renderingService is not null &&
        _outputEditor is not null &&
        _renderedOutputSink is not null &&
        !_hasActiveProjectMutation() &&
        !IsRendering;

    private bool CanAddToQueue() =>
        CanAttemptQueueChange() &&
        FindSelectedIncludedAsset() is not null &&
        !IsSelectedAssetQueued &&
        !NeedsValidClipEdit &&
        !_hasUnsavedMetadata();

    private void AddSelectedAssetToQueue()
    {
        if (!CanAddToQueue() ||
            _project is null ||
            FindSelectedIncludedAsset() is not { } selected)
        {
            return;
        }

        string selectedAssetId = selected.Id;

        if (!TryApplyPendingEdit())
        {
            _status =
                "Move the clip end after its start before adding it to the render queue.";
            NotifyProperties();
            return;
        }

        // Applying a visible trim replaces the immutable project. Re-resolve
        // the selected ID so the queue always captures the cut the user sees.
        GenerationOutputAsset? currentSelected = _project?.Assets
            .FirstOrDefault(asset => asset.Id.Equals(
                selectedAssetId,
                StringComparison.Ordinal));
        if (currentSelected is not { IsIncludedInFinalRender: true } ||
            _queue.Any(item => item.AssetId.Equals(
                selectedAssetId,
                StringComparison.Ordinal)) ||
            _hasUnsavedMetadata())
        {
            _status = NeedsMetadataSave
                ? "Save the visible metadata changes before adding this clip to the render queue."
                : "The selected clip changed before it could be queued.";
            NotifyProperties();
            return;
        }

        bool hasLibraryCopy = HasLibraryCopy(currentSelected.Id);
        _queue.Add(BuildQueueItem(
            currentSelected,
            isCompleted: hasLibraryCopy,
            hasLibraryCopy));

        _error = null;
        _status = hasLibraryCopy
            ? "Added the clip to the queue. Its existing Library copy is unchanged; choose re-render to make another copy."
            : $"Added {currentSelected.EditorialMetadata?.Title ?? $"Clip {currentSelected.Rank:00}"} to the render queue.";
        NotifyProperties();
    }

    private bool CanRenderQueue()
    {
        if (!CanAttemptQueueChange() ||
            _project is null ||
            _queue.Count == 0 ||
            _queue.All(static item => item.IsCompleted) ||
            NeedsValidClipEdit ||
            _hasUnsavedMetadata())
        {
            return false;
        }

        var assets = _project.Assets.ToDictionary(
            static asset => asset.Id,
            StringComparer.Ordinal);
        return _queue.Where(static item => !item.IsCompleted).All(item =>
            assets.TryGetValue(item.AssetId, out GenerationOutputAsset? asset) &&
            asset.IsIncludedInFinalRender);
    }

    private async Task RenderQueueAsync()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!CanRenderQueue() ||
            _project is null ||
            _renderingService is null ||
            _outputEditor is null)
        {
            return;
        }

        if (!TryApplyPendingEdit())
        {
            _status =
                "Move the clip end after its start before rendering this queue.";
            NotifyProperties();
            return;
        }
        RefreshQueueItems();
        if (!CanRenderQueue() || _project is null)
        {
            _status = NeedsMetadataSave
                ? "Save the visible metadata changes before rendering this queue."
                : "A queued clip changed or was removed. Review the queue before rendering again.";
            NotifyProperties();
            return;
        }

        GenerationOutputProject sourceProject = _project;
        string[] pendingIds = _queue
            .Where(static item => !item.IsCompleted)
            .Select(static item => item.AssetId)
            .ToArray();
        GenerationOutputProject draft = BuildQueuedDraft(
            sourceProject,
            pendingIds);
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        SetRendering(true);
        _error = null;
        _percent = 0;
        _status =
            $"Rendering {pendingIds.Length} queued " +
            (pendingIds.Length == 1 ? "clip" : "clips") +
            " in order with the latest saved Studio edits.";
        NotifyProperties();

        var progress = new Progress<StudioProjectRenderProgress>(
            update =>
            {
                _status = update.Detail;
                _percent = update.Percentage;
                NotifyProperties();
            });
        StudioProjectRenderResult? completedRender = null;
        bool libraryCommitted = false;
        try
        {
            completedRender =
                await _renderingService.FinalizeAsync(
                    draft,
                    progress,
                    _cancellation.Token);
            if (!ReferenceEquals(_project, sourceProject))
            {
                _renderingService.DiscardCompletedRender(completedRender);
                completedRender = null;
                _error =
                    "Studio changed while this queue was rendering. The newer edits were kept, but this render was not added to Library.";
                _status =
                    "Review the current Studio edits, then render the queue again.";
                RefreshQueueItems();
                return;
            }
            _renderedOutputSink!.CommitRenderedOutput(
                completedRender.FinalizedProject);
            libraryCommitted = true;
            _renderingService.AcceptCompletedRender(completedRender);
            completedRender = null;
            _percent = 100;
            MarkRenderedItemsCompleted(pendingIds);
            _status =
                "The selected queue items finished and their files are in Library. The Studio project remains editable.";
        }
        catch (OperationCanceledException)
        {
            string? cleanupFailure = libraryCommitted
                ? null
                : TryDiscardCompletedRender(completedRender);
            if (cleanupFailure is not null)
            {
                _error = cleanupFailure;
            }
            _status =
                "Rendering was cancelled. The queue and open Studio session are unchanged.";
        }
        catch (Exception exception)
        {
            string? cleanupFailure = libraryCommitted
                ? null
                : TryDiscardCompletedRender(completedRender);
            _error = cleanupFailure is null
                ? exception.Message
                : $"{exception.Message} Cleanup also failed: {cleanupFailure}";
            if (libraryCommitted)
            {
                _percent = 100;
                MarkRenderedItemsCompleted(pendingIds);
            }
            _status = libraryCommitted
                ? "The finished files are in Library, but Studio could not release its completed-render record."
                : "The render queue failed. Its clips and the open Studio session are unchanged.";
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            SetRendering(false);
            NotifyProperties();
        }
    }

    private string? TryDiscardCompletedRender(
        StudioProjectRenderResult? result)
    {
        if (result is null || _renderingService is null)
        {
            return null;
        }

        try
        {
            _renderingService.DiscardCompletedRender(result);
            return null;
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }

    private GenerationOutputProject BuildQueuedDraft(
        GenerationOutputProject project,
        IReadOnlyCollection<string> pendingIds)
    {
        ArgumentNullException.ThrowIfNull(pendingIds);
        var queuedIds = pendingIds.ToHashSet(StringComparer.Ordinal);
        GenerationOutputProject renderBatch = project.CreateRenderBatch(
            _renderTokenFactory());
        GenerationOutputAsset[] replacements = renderBatch.Assets
            .Select(asset => asset.WithDisposition(
                queuedIds.Contains(asset.Id)
                    ? GenerationOutputAssetDisposition.IncludeInFinalRender
                    : GenerationOutputAssetDisposition.ExcludeFromFinalRender))
            .ToArray();
        return renderBatch.ReplaceAssets(replacements);
    }

    private bool TryApplyPendingEdit()
    {
        if (!_hasPendingEdit())
        {
            return true;
        }

        return _isPendingEditValid() && _applyPendingEdit();
    }

    private GenerationOutputAsset? FindSelectedIncludedAsset()
    {
        GenerationOutputAsset? selected = _selectedAsset();
        return selected is null || _project is null
            ? null
            : _project.Assets.FirstOrDefault(asset =>
                asset.IsIncludedInFinalRender &&
                asset.Id.Equals(selected.Id, StringComparison.Ordinal));
    }

    private bool IsSelectedAssetQueued =>
        FindSelectedIncludedAsset() is { } selected &&
        _queue.Any(item => item.AssetId.Equals(
            selected.Id,
            StringComparison.Ordinal));

    private bool CanRemoveQueuedItem(string? assetId) =>
        assetId is not null &&
        !IsRendering &&
        _project?.IsFinalized == false &&
        _queue.Any(item => item.AssetId.Equals(
            assetId,
            StringComparison.Ordinal));

    private bool CanRerenderQueuedItem(string? assetId) =>
        assetId is not null &&
        !IsRendering &&
        _project?.IsFinalized == false &&
        _queue.Any(item =>
            item.AssetId.Equals(assetId, StringComparison.Ordinal) &&
            item.IsCompleted);

    private void RemoveQueuedItem(string? assetId)
    {
        if (assetId is not null)
        {
            RemoveAssetFromQueue(assetId);
        }
    }

    private void RerenderQueuedItem(string? assetId)
    {
        if (!CanRerenderQueuedItem(assetId) || assetId is null)
        {
            return;
        }

        int index = _queue
            .Select(static item => item.AssetId)
            .ToList()
            .FindIndex(id => id.Equals(assetId, StringComparison.Ordinal));
        StudioRenderQueueItem current = _queue[index];
        _queue[index] = current with
        {
            IsCompleted = false,
            Status = current.HasLibraryCopy
                ? "READY · COPY EXISTS"
                : "READY",
            PersistedState = StudioPersistedRenderState.Ready,
        };
        _status =
            $"{current.Title} will render as a new Library copy. Existing files will not be overwritten.";
        NotifyProperties();
    }

    private void RefreshQueueItems()
    {
        if (_project is null || _queue.Count == 0)
        {
            return;
        }

        string[] queuedIds = _queue
            .Select(static item => item.AssetId)
            .ToArray();
        var assets = _project.Assets.ToDictionary(
            static asset => asset.Id,
            StringComparer.Ordinal);
        StudioRenderQueueItem[] refreshed = queuedIds
            .Where(id => assets.TryGetValue(id, out GenerationOutputAsset? asset) &&
                         asset.IsIncludedInFinalRender)
            .Select(id =>
            {
                StudioRenderQueueItem current = _queue.Single(item =>
                    item.AssetId.Equals(id, StringComparison.Ordinal));
                bool hasLibraryCopy = HasLibraryCopy(id);
                bool completed = current.IsCompleted &&
                    (_libraryCatalog is null || hasLibraryCopy);
                return BuildQueueItem(
                    assets[id],
                    completed,
                    hasLibraryCopy);
            })
            .ToArray();
        _queue.Clear();
        foreach (StudioRenderQueueItem item in refreshed)
        {
            _queue.Add(item);
        }
    }

    private static StudioRenderQueueItem BuildQueueItem(
        GenerationOutputAsset asset,
        bool isCompleted,
        bool hasLibraryCopy) =>
        new(
            asset.Id,
            asset.EditorialMetadata?.Title ?? $"Clip {asset.Rank:00}",
            $"Clip {asset.Rank:00} · " +
            $"{StudioTimeFormatter.FormatDuration(asset.Duration)} · " +
            Path.GetFileName(asset.SourceFullPath),
            isCompleted
                ? "IN LIBRARY"
                : hasLibraryCopy
                    ? "READY · COPY EXISTS"
                    : "READY",
            isCompleted,
            hasLibraryCopy,
            isCompleted
                ? StudioPersistedRenderState.Completed
                : StudioPersistedRenderState.Ready);

    private void MarkRenderedItemsCompleted(
        IReadOnlyCollection<string> renderedIds)
    {
        var completedIds = renderedIds.ToHashSet(StringComparer.Ordinal);
        for (int index = 0; index < _queue.Count; index++)
        {
            StudioRenderQueueItem item = _queue[index];
            if (!completedIds.Contains(item.AssetId))
            {
                continue;
            }

            _queue[index] = item with
            {
                Status = "IN LIBRARY",
                IsCompleted = true,
                HasLibraryCopy = true,
                PersistedState = StudioPersistedRenderState.Completed,
            };
        }
    }

    private bool HasLibraryCopy(string sourceCandidateId) =>
        _libraryCatalog?.Assets.Any(asset =>
            asset.SourceCandidateIds.Contains(
                sourceCandidateId,
                StringComparer.Ordinal)) == true;

    private void LibraryCatalog_Changed(object? sender, EventArgs e)
    {
        if (_isDisposed || IsRendering)
        {
            return;
        }

        bool hadCompleted = _queue.Any(static item => item.IsCompleted);
        RefreshQueueItems();
        if (hadCompleted && _queue.Any(static item => !item.IsCompleted))
        {
            _status =
                "A rendered copy was removed from Library. Its Studio queue item is ready to render again.";
        }
        NotifyProperties();
    }

    private void Cancel() => _cancellation?.Cancel();

    private void SetRendering(bool value)
    {
        if (_isRendering == value)
        {
            return;
        }

        _isRendering = value;
        _setHostBusy(value);
        OnPropertyChanged(nameof(IsRendering));
        _addToQueueCommand.RaiseCanExecuteChanged();
        _renderQueueCommand.RaiseCanExecuteChanged();
        _removeQueuedItemCommand.RaiseCanExecuteChanged();
        _rerenderQueuedItemCommand.RaiseCanExecuteChanged();
        _cancelCommand.RaiseCanExecuteChanged();
    }

    private void NotifyProperties()
    {
        foreach (string propertyName in new[]
        {
            nameof(IsRendering),
            nameof(Status),
            nameof(Error),
            nameof(HasError),
            nameof(Percent),
            nameof(IsProgressVisible),
            nameof(QueueItems),
            nameof(HasQueuedItems),
            nameof(QueuedClipCount),
            nameof(QueueSummary),
            nameof(ButtonText),
            nameof(RenderQueueButtonText),
            nameof(ReadinessText),
            nameof(IsReadyToQueue),
            nameof(IsReadyToRender),
            nameof(NeedsIncludedCandidate),
            nameof(NeedsValidClipEdit),
            nameof(NeedsMetadataSave),
            nameof(NeedsRenderAttention),
        })
        {
            OnPropertyChanged(propertyName);
        }
        _addToQueueCommand.RaiseCanExecuteChanged();
        _renderQueueCommand.RaiseCanExecuteChanged();
        _removeQueuedItemCommand.RaiseCanExecuteChanged();
        _rerenderQueuedItemCommand.RaiseCanExecuteChanged();
        _cancelCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
