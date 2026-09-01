using System.IO;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Research;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.HiddenMoments;

public sealed class StudioHiddenMomentsViewModel : ObservableObject, IDisposable
{
    private readonly IGenerationOutputEditor? _outputEditor;
    private readonly IStudioHiddenMomentDecisionStore? _decisionStore;
    private readonly IResearchFeedbackRecorder? _researchFeedback;
    private readonly IGenerationCaptionPreparationService?
        _captionPreparation;
    private readonly IGenerationEditorialMetadataService?
        _editorialMetadata;
    private readonly TimeProvider _timeProvider;
    private readonly StudioHiddenMomentPreviewWarmup _previewWarmup;
    private readonly DelegateCommand _openCommand;
    private readonly DelegateCommand _closeCommand;
    private readonly AsyncDelegateCommand _acceptCommand;
    private readonly DelegateCommand _cancelAllCommand;
    private readonly DelegateCommand _skipCommand;
    private readonly DelegateCommand _previousMomentCommand;
    private readonly DelegateCommand _nextMomentCommand;
    private readonly DelegateCommand _resetCommand;
    private readonly ObservableCollection<StudioHiddenMomentQueueItem>
        _queueItems = [];
    private readonly ReadOnlyObservableCollection<
        StudioHiddenMomentQueueItem> _readOnlyQueueItems;
    private readonly SemaphoreSlim _queueSignal = new(0);
    private readonly CancellationTokenSource _queueLifetimeCancellation =
        new();
    private readonly Task _queueWorker;
    private GenerationOutputProject? _project;
    private GenerationHiddenMoment[] _pending = [];
    private GenerationHiddenMoment? _current;
    private string? _projectId;
    private string? _lastViewedCandidateId;
    private int _sessionTotal;
    private int _reviewedCount;
    private bool _isOpen;
    private bool _isProjectMutationBlocked;
    private bool _isStopping;
    private bool _isDisposed;
    private string? _error;
    private CancellationTokenSource? _activeQueueCancellation;
    private StudioHiddenMomentQueueItem? _activeQueueItem;
    private readonly object _operationSync = new();

    public StudioHiddenMomentsViewModel(
        IGenerationOutputEditor? outputEditor,
        IStudioPreviewMediaService? previewMediaService,
        IStudioHiddenMomentDecisionStore? decisionStore,
        IResearchFeedbackRecorder? researchFeedback = null,
        IGenerationCaptionPreparationService? captionPreparation = null,
        IGenerationEditorialMetadataService? editorialMetadata = null,
        TimeProvider? timeProvider = null)
    {
        _outputEditor = outputEditor;
        _decisionStore = decisionStore;
        _researchFeedback = researchFeedback;
        _captionPreparation = captionPreparation;
        _editorialMetadata = editorialMetadata;
        _timeProvider = timeProvider ?? TimeProvider.System;
        Preview = new StudioPreviewViewModel(
            previewMediaService,
            showCaptionControls: false,
            rangeMode: StudioPreviewRangeMode.ExactSelection);
        _previewWarmup = new StudioHiddenMomentPreviewWarmup(
            previewMediaService);
        _readOnlyQueueItems = new(_queueItems);
        Preview.PropertyChanged += Preview_PropertyChanged;
        _openCommand = new DelegateCommand(Open, CanOpen);
        _closeCommand = new DelegateCommand(Close, () => IsOpen);
        _acceptCommand = new AsyncDelegateCommand(AcceptAsync, CanDecide);
        _cancelAllCommand = new DelegateCommand(
            CancelAllQueuedMoments,
            () => HasCancelableQueueItems);
        _skipCommand = new DelegateCommand(Skip, CanDecide);
        _previousMomentCommand = new DelegateCommand(
            PreviousMoment,
            CanShowPreviousMoment);
        _nextMomentCommand = new DelegateCommand(
            NextMoment,
            CanShowNextMoment);
        _resetCommand = new DelegateCommand(ResetProject, CanResetProject);
        _queueWorker = ProcessQueueAsync();
    }

    public event EventHandler<StudioHiddenMomentAcceptedEventArgs>?
        MomentAccepted;
    public StudioPreviewViewModel Preview { get; }
    public GenerationHiddenMoment? Current => _current;
    public bool IsOpen => _isOpen;
    public bool HasAvailableMoments => _pending.Length > 0;
    public bool HasQueueItems => _queueItems.Count > 0;
    public bool HasUnfinishedQueueItems => _queueItems.Count > 0;
    public bool HasCancelableQueueItems => _queueItems.Any(static item => item.CanCancel);
    public bool IsExhausted => IsOpen && _current is null && !HasQueueItems;
    public int RemainingCount => _pending.Length;
    public int ReviewedCount => _reviewedCount;
    public int SessionTotal => _sessionTotal;
    public ReadOnlyObservableCollection<StudioHiddenMomentQueueItem> QueueItems =>
        _readOnlyQueueItems;
    public string OpenButtonText => StudioHiddenMomentsPresentation.OpenButtonText(
        QueueFailureCount, HasQueueItems, HasAvailableMoments,
        RemainingCount, QueueWorkCount);
    public string ProgressText => StudioHiddenMomentsPresentation.ProgressText(
        _current, HasQueueItems, QueueSummaryText, _reviewedCount, _sessionTotal);
    public string MomentTitle => StudioHiddenMomentsPresentation.MomentTitle(
        _current, HasQueueItems, QueueFailureCount);
    public string MomentDetail => StudioHiddenMomentsPresentation.MomentDetail(
        _current, HasQueueItems);
    public string EvidenceText => StudioHiddenMomentsPresentation.EvidenceText(
        _current, HasQueueItems);
    public string? Error => _error;
    public bool HasError => !string.IsNullOrWhiteSpace(_error);
    public bool IsPreparingAcceptedMoment => _activeQueueItem is not null;
    public StudioHiddenMomentAcceptanceStage AcceptanceStage =>
        StudioHiddenMomentsPresentation.AcceptanceStage(_activeQueueItem);
    public string AcceptanceStatus =>
        StudioHiddenMomentsPresentation.AcceptanceStatus(AcceptanceStage);
    public bool IsAcceptanceProgressIndeterminate => AcceptanceStage is
        StudioHiddenMomentAcceptanceStage.PreparingCaptions or
        StudioHiddenMomentAcceptanceStage.PreparingTitleAndDescription;
    public double AcceptanceProgressPercentage => AcceptanceStage ==
        StudioHiddenMomentAcceptanceStage.AddingToStudio ? 100d : 0d;
    public bool IsAcceptanceLivenessVisible => IsAcceptanceProgressIndeterminate;
    public string AcceptanceLivenessText => _activeQueueItem?.LivenessText ?? string.Empty;
    public bool IsAcceptanceTakingLong => IsAcceptanceLivenessVisible &&
        _activeQueueItem?.IsTakingLong == true;
    public string AcceptanceWaitGuidance => IsAcceptanceTakingLong
        ? "This can take several minutes. Replay Foundry will stop it if it takes too long."
        : string.Empty;
    public string QueueSummaryText =>
        StudioHiddenMomentsPresentation.QueueSummaryText(_queueItems);
    public string CloseButtonText => "Close";
    public string CloseButtonAutomationName => "Close Hidden Moments";
    public string CloseButtonHelpText =>
        StudioHiddenMomentsPresentation.CloseButtonHelpText(HasQueueItems);
    public bool IsProjectMutationBlocked => _isProjectMutationBlocked;
    public ICommand OpenCommand => _openCommand;
    public ICommand CloseCommand => _closeCommand;
    public ICommand AcceptCommand => _acceptCommand;
    public ICommand CancelAllCommand => _cancelAllCommand;
    public ICommand SkipCommand => _skipCommand;
    public ICommand PreviousMomentCommand => _previousMomentCommand;
    public ICommand NextMomentCommand => _nextMomentCommand;
    public ICommand ReviewSkippedAgainCommand => _resetCommand;
    private int QueueFailureCount => _queueItems.Count(static item =>
        item.State == StudioHiddenMomentQueueItemState.NeedsAttention);
    private int QueueWorkCount => _queueItems.Count(static item =>
        item.State != StudioHiddenMomentQueueItemState.NeedsAttention);

    public void Bind(GenerationOutputProject? project)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _previewWarmup.Cancel();
        string? currentCandidateId = _current?.Id;
        bool newProject = !string.Equals(
            _projectId,
            project?.Id,
            StringComparison.Ordinal);
        if (newProject)
        {
            CancelQueueForProjectChange();
        }
        _project = project;
        _projectId = project?.Id;
        if (newProject)
        {
            _isOpen = false;
            _lastViewedCandidateId = null;
            _sessionTotal = project?.HiddenMomentCount ?? 0;
            _reviewedCount = 0;
            _error = null;
        }

        HashSet<string> queuedIds = _queueItems
            .Where(item => item.ProjectId.Equals(
                project?.Id,
                StringComparison.Ordinal))
            .Select(static item => item.CandidateId)
            .ToHashSet(StringComparer.Ordinal);
        _pending = project?.HiddenMoments
            .Where(value => _decisionStore?.Find(project.Id, value.Id)
                ?.Decision !=
                StudioHiddenMomentReviewDecision.SkippedForProject &&
                !queuedIds.Contains(value.Id))
            .OrderBy(static value => value.ReviewOrder)
            .ToArray() ?? [];
        HashSet<string> retainedHiddenIds = project?.HiddenMoments
            .Select(static value => value.Id)
            .ToHashSet(StringComparer.Ordinal) ?? [];
        int stored = project is null || _decisionStore is null
            ? 0
            : _decisionStore.Current.Count(value =>
                value.ProjectId.Equals(project.Id, StringComparison.Ordinal) &&
                (value.Decision ==
                    StudioHiddenMomentReviewDecision.SkippedForProject ||
                 !retainedHiddenIds.Contains(value.CandidateId)));
        _sessionTotal = Math.Max(
            _sessionTotal,
            _pending.Length + _queueItems.Count + stored);
        RecalculateReviewedCount();
        GenerationHiddenMoment? next = _isOpen
            ? FindPending(currentCandidateId) ??
              FindPending(_lastViewedCandidateId) ??
              _pending.FirstOrDefault()
            : null;
        SetCurrent(next);
        NotifyAll();
    }

    public void SetProjectMutationBlocked(bool value)
    {
        if (_isProjectMutationBlocked == value)
        {
            return;
        }

        _isProjectMutationBlocked = value;
        NotifyAll();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }
        _isDisposed = true;
        CancellationTokenSource? cancellation;
        lock (_operationSync)
        {
            cancellation = _activeQueueCancellation;
        }
        TryCancel(_queueLifetimeCancellation);
        TryCancel(cancellation);
        Preview.PropertyChanged -= Preview_PropertyChanged;
        _previewWarmup.Dispose();
        Preview.Dispose();
    }

    internal async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? activeCancellation;
        lock (_operationSync)
        {
            _isStopping = true;
            activeCancellation = _activeQueueCancellation;
        }
        TryCancel(_queueLifetimeCancellation);
        TryCancel(activeCancellation);
        await Task.WhenAll(
                _queueWorker.WaitAsync(cancellationToken),
                _previewWarmup.StopAsync(cancellationToken),
                Preview.StopAsync(cancellationToken))
            .ConfigureAwait(false);
    }

    private bool CanOpen() =>
        !_isStopping &&
        !IsProjectMutationBlocked &&
        _project is { IsFinalized: false } &&
        (HasAvailableMoments || HasQueueItems);

    private void Open()
    {
        if (!CanOpen())
        {
            return;
        }
        _isOpen = true;
        SetCurrent(
            FindPending(_lastViewedCandidateId) ??
            _pending.FirstOrDefault());
        NotifyAll();
    }

    private void Close()
    {
        _isOpen = false;
        _error = null;
        SetCurrent(null);
        NotifyAll();
        _previewWarmup.Cancel();
    }

    private bool CanDecide() =>
        !_isStopping &&
        !IsProjectMutationBlocked &&
        _project is { IsFinalized: false } &&
        _current is not null &&
        _outputEditor is not null;

    private bool CanNavigateMoments() =>
        !_isStopping &&
        IsOpen &&
        !IsProjectMutationBlocked &&
        _current is not null;

    private bool CanShowPreviousMoment() =>
        CanNavigateMoments() && CurrentPendingIndex > 0;

    private bool CanShowNextMoment()
    {
        int currentIndex = CurrentPendingIndex;
        return CanNavigateMoments() &&
            currentIndex >= 0 &&
            currentIndex + 1 < _pending.Length;
    }

    private int CurrentPendingIndex => _current is null
        ? -1
        : Array.FindIndex(
            _pending,
            value => value.Id.Equals(_current.Id, StringComparison.Ordinal));

    private void PreviousMoment() => NavigateToPendingOffset(-1);

    private void NextMoment() => NavigateToPendingOffset(1);

    private void NavigateToPendingOffset(int offset)
    {
        int destination = CurrentPendingIndex + offset;
        if (!CanNavigateMoments() ||
            destination < 0 ||
            destination >= _pending.Length)
        {
            return;
        }

        _error = null;
        _previewWarmup.Cancel();
        SetCurrent(_pending[destination]);
        NotifyAll();
    }

    private Task AcceptAsync()
    {
        if (!CanDecide() ||
            _project is not { } project ||
            _current is not { } current ||
            _outputEditor is null)
        {
            return Task.CompletedTask;
        }
        if (_queueItems.Any(item =>
            item.ProjectId.Equals(project.Id, StringComparison.Ordinal) &&
            item.CandidateId.Equals(current.Id, StringComparison.Ordinal)))
        {
            return Task.CompletedTask;
        }

        string? nextCandidateId = FindNextCandidateId(current);
        StudioClipAppearance appearance =
            project.CaptionLook?.CreateAppearance() ??
            StudioClipAppearance.CreateDefault(
                current.CaptionStyle ??
                GenerationCaptionStylePreset.Clean);
        var item = new StudioHiddenMomentQueueItem(
            project.Id,
            current,
            appearance,
            _timeProvider,
            CancelQueuedMoment,
            RetryQueuedMoment);
        _queueItems.Add(item);
        _pending = _pending.Where(value => !value.Id.Equals(
            current.Id,
            StringComparison.Ordinal)).ToArray();
        RecalculateReviewedCount();
        GenerationHiddenMoment? next = FindPending(nextCandidateId) ??
            _pending.FirstOrDefault();
        _error = null;
        _previewWarmup.Cancel();
        SetCurrent(next);
        NotifyAll();
        _queueSignal.Release();
        return Task.CompletedTask;
    }

    private string? FindNextCandidateId(GenerationHiddenMoment current)
    {
        int currentIndex = Array.FindIndex(
            _pending,
            value => value.Id.Equals(current.Id, StringComparison.Ordinal));
        if (currentIndex < 0 || _pending.Length <= 1)
        {
            return null;
        }

        int nextIndex = currentIndex + 1 < _pending.Length
            ? currentIndex + 1
            : 0;
        return _pending[nextIndex].Id;
    }

    private GenerationHiddenMoment? FindPending(string? candidateId) =>
        candidateId is null
            ? null
            : _pending.FirstOrDefault(value => value.Id.Equals(
                candidateId,
                StringComparison.Ordinal));

    private async Task ProcessQueueAsync()
    {
        CancellationToken lifetime = _queueLifetimeCancellation.Token;
        try
        {
            while (true)
            {
                await _queueSignal.WaitAsync(lifetime);
                while (!_isStopping &&
                    _queueItems.FirstOrDefault(static item =>
                        item.State ==
                            StudioHiddenMomentQueueItemState.Waiting) is
                        { } item)
                {
                    await ProcessQueuedMomentAsync(item, lifetime);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // App shutdown owns cancellation of the background queue.
        }
    }

    private async Task ProcessQueuedMomentAsync(
        StudioHiddenMomentQueueItem item,
        CancellationToken lifetime)
    {
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        bool projectCommitted = false;
        lock (_operationSync)
        {
            if (_isDisposed || _isStopping ||
                !_queueItems.Contains(item))
            {
                return;
            }
            _activeQueueItem = item;
            _activeQueueCancellation = cancellation;
        }

        try
        {
            GenerationHiddenMoment moment = item.Moment;
            item.SetState(
                moment.CaptionsRequested
                    ? StudioHiddenMomentQueueItemState.PreparingCaptions
                    : StudioHiddenMomentQueueItemState
                        .PreparingTitleAndDescription);
            NotifyAll();

            GenerationCandidateCaptionTrack? captions = null;
            if (moment.CaptionSourceSelection is { } selection)
            {
                if (_captionPreparation is null)
                {
                    throw new InvalidOperationException(
                        "Captions were requested, but speech-to-text is not ready. Check Advanced AI in Settings.");
                }
                captions = await _captionPreparation
                    .PrepareRetainedCandidateAsync(
                        moment.Id,
                        moment.SourceMedia,
                        moment.SourceStart,
                        moment.SourceEnd,
                        selection,
                        item.Appearance.CaptionStyle,
                        cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                item.SetState(
                    StudioHiddenMomentQueueItemState
                        .PreparingTitleAndDescription);
                NotifyAll();
            }

            string[] existingProjectTitles = _project is { } currentProject &&
                currentProject.Id.Equals(
                    item.ProjectId,
                    StringComparison.Ordinal)
                    ? currentProject.Assets
                        .Select(static asset =>
                            asset.EditorialMetadata?.Title)
                        .OfType<string>()
                        .ToArray()
                    : [];
            GenerationHiddenMoment prepared = _editorialMetadata is null
                ? RequirePreparedMetadata(moment)
                : await _editorialMetadata.PrepareAcceptedHiddenAsync(
                    moment,
                    captions,
                    cancellation.Token,
                    existingProjectTitles);
            cancellation.Token.ThrowIfCancellationRequested();
            item.SetState(StudioHiddenMomentQueueItemState.AddingToStudio);
            NotifyAll();

            if (IsProjectMutationBlocked ||
                _project is not { IsFinalized: false } project ||
                !project.Id.Equals(item.ProjectId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Studio changed while this moment was being prepared. It was not added.");
            }
            cancellation.Token.ThrowIfCancellationRequested();

            bool alreadyAdded = project.Assets.Any(asset => asset.Id.Equals(
                item.CandidateId,
                StringComparison.Ordinal));
            if (!alreadyAdded)
            {
                _outputEditor!.AcceptHiddenMoment(
                    item.ProjectId,
                    item.CandidateId,
                    captions,
                    prepared.EditorialContext,
                    prepared.EditorialMetadata,
                    item.Appearance);
            }
            projectCommitted = true;
            CompleteQueuedMoment(item);
        }
        catch (OperationCanceledException)
        {
            if (!_isDisposed && !_isStopping &&
                _queueItems.Contains(item))
            {
                RemoveQueuedMomentAndReturnToReview(item);
            }
        }
        catch (Exception exception)
        {
            if (!_isDisposed && _queueItems.Contains(item))
            {
                bool wasAdded = projectCommitted ||
                    _project?.Assets.Any(asset => asset.Id.Equals(
                        item.CandidateId,
                        StringComparison.Ordinal)) == true;
                if (wasAdded)
                {
                    CompleteQueuedMoment(item, exception);
                }
                else
                {
                    item.SetError(ToQueueError(exception));
                    item.SetState(
                        StudioHiddenMomentQueueItemState.NeedsAttention);
                    _error =
                        "A queued moment needs attention. Retry it or return it to review.";
                    RecalculateReviewedCount();
                    NotifyAll();
                }
            }
        }
        finally
        {
            lock (_operationSync)
            {
                if (ReferenceEquals(_activeQueueItem, item))
                {
                    _activeQueueItem = null;
                }
                if (ReferenceEquals(_activeQueueCancellation, cancellation))
                {
                    _activeQueueCancellation = null;
                }
            }
            if (!_isDisposed)
            {
                NotifyAll();
            }
        }
    }

    private void CompleteQueuedMoment(
        StudioHiddenMomentQueueItem item,
        Exception? postCommitException = null)
    {
        try
        {
            Save(
                item.Moment,
                StudioHiddenMomentReviewDecision.AcceptedIntoStudio);
            RecordResearch(item.Moment, ResearchFeedbackValue.Accepted);
            _error = postCommitException is null
                ? null
                : "The clip was added to Studio, but Replay Foundry " +
                  "could not finish refreshing the screen.";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            _error =
                "The clip was added to Studio, but Replay Foundry " +
                "could not save the Hidden Moments choice.";
        }

        _queueItems.Remove(item);
        RecalculateReviewedCount();
        MomentAccepted?.Invoke(
            this,
            new StudioHiddenMomentAcceptedEventArgs(
                item.CandidateId,
                shouldFocus: false));
        NotifyAll();
    }

    private static string ToQueueError(Exception exception) => exception switch
    {
        ClipEditorialAiGenerationException =>
            "Local AI could not finish the title and description. Try this moment again.",
        InvalidOperationException when exception.Message.Contains(
            "Captions were requested",
            StringComparison.Ordinal) =>
            "Captions are not ready on this device. Check Advanced AI in Settings, then try again.",
        _ =>
            "Replay Foundry could not prepare this moment. Try again, or return it to review.",
    };

    private void CancelQueuedMoment(StudioHiddenMomentQueueItem item)
    {
        if (!_queueItems.Contains(item) || !item.CanCancel)
        {
            return;
        }

        CancellationTokenSource? cancellation = null;
        lock (_operationSync)
        {
            if (ReferenceEquals(_activeQueueItem, item))
            {
                item.SetState(StudioHiddenMomentQueueItemState.Canceling);
                cancellation = _activeQueueCancellation;
            }
        }
        if (cancellation is null)
        {
            RemoveQueuedMomentAndReturnToReview(item);
        }
        else
        {
            TryCancel(cancellation);
            NotifyAll();
        }
    }

    private void RetryQueuedMoment(StudioHiddenMomentQueueItem item)
    {
        if (!_queueItems.Contains(item) || !item.CanRetry ||
            _isStopping || IsProjectMutationBlocked)
        {
            return;
        }
        item.SetError(null);
        item.SetState(StudioHiddenMomentQueueItemState.Waiting);
        if (QueueFailureCount == 0)
        {
            _error = null;
        }
        NotifyAll();
        _queueSignal.Release();
    }

    private void CancelAllQueuedMoments()
    {
        foreach (StudioHiddenMomentQueueItem item in _queueItems.ToArray())
        {
            CancelQueuedMoment(item);
        }
    }

    private void RemoveQueuedMomentAndReturnToReview(
        StudioHiddenMomentQueueItem item)
    {
        if (!_queueItems.Remove(item))
        {
            return;
        }
        if (_project is { IsFinalized: false } project &&
            project.Id.Equals(item.ProjectId, StringComparison.Ordinal))
        {
            GenerationHiddenMoment? retained = project.HiddenMoments
                .FirstOrDefault(moment => moment.Id.Equals(
                    item.CandidateId,
                    StringComparison.Ordinal));
            if (retained is not null && !_pending.Any(moment =>
                moment.Id.Equals(item.CandidateId, StringComparison.Ordinal)))
            {
                _pending = [.. _pending, retained];
                _pending = _pending
                    .OrderBy(static moment => moment.ReviewOrder)
                    .ToArray();
            }
        }
        if (_isOpen && _current is null)
        {
            SetCurrent(
                FindPending(item.CandidateId) ?? _pending.FirstOrDefault());
        }
        if (QueueFailureCount == 0)
        {
            _error = null;
        }
        RecalculateReviewedCount();
        NotifyAll();
    }

    private void CancelQueueForProjectChange()
    {
        CancellationTokenSource? activeCancellation;
        lock (_operationSync)
        {
            activeCancellation = _activeQueueCancellation;
            _activeQueueCancellation = null;
            _activeQueueItem = null;
        }
        _queueItems.Clear();
        TryCancel(activeCancellation);
    }

    private void RecalculateReviewedCount() =>
        _reviewedCount = Math.Max(
            0,
            _sessionTotal - _pending.Length - _queueItems.Count);

    internal async Task WaitForQueueIdleAsync(
        CancellationToken cancellationToken = default)
    {
        while (_activeQueueItem is not null ||
            _queueItems.Any(static item => item.State is
                StudioHiddenMomentQueueItemState.Waiting or
                StudioHiddenMomentQueueItemState.PreparingCaptions or
                StudioHiddenMomentQueueItemState
                    .PreparingTitleAndDescription or
                StudioHiddenMomentQueueItemState.AddingToStudio or
                StudioHiddenMomentQueueItemState.Canceling))
        {
            await Task.Delay(10, cancellationToken);
        }
    }

    internal void RefreshAcceptanceLiveness()
    {
        if (_activeQueueItem is not { } active)
        {
            return;
        }

        active.RefreshLiveness();
        OnPropertyChanged(nameof(AcceptanceLivenessText));
        OnPropertyChanged(nameof(IsAcceptanceTakingLong));
        OnPropertyChanged(nameof(AcceptanceWaitGuidance));
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

    private void Skip()
    {
        if (!CanDecide() || _current is not { } current)
        {
            return;
        }
        try
        {
            string? nextCandidateId = FindNextCandidateId(current);
            Save(current, StudioHiddenMomentReviewDecision.SkippedForProject);
            RecordResearch(current, ResearchFeedbackValue.Skipped);
            _pending = _pending.Where(value => !value.Id.Equals(
                current.Id,
                StringComparison.Ordinal)).ToArray();
            RecalculateReviewedCount();
            _error = null;
            _previewWarmup.Cancel();
            SetCurrent(
                FindPending(nextCandidateId) ?? _pending.FirstOrDefault());
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            _error = "Replay Foundry could not save this decision: " +
                exception.Message;
        }
        NotifyAll();
    }

    private bool CanResetProject() =>
        !IsProjectMutationBlocked &&
        _project is { IsFinalized: false } &&
        _decisionStore is not null &&
        _decisionStore.Current.Any(value =>
            value.ProjectId.Equals(_project.Id, StringComparison.Ordinal) &&
            value.Decision ==
                StudioHiddenMomentReviewDecision.SkippedForProject);

    private void ResetProject()
    {
        if (_project is null || _decisionStore is null)
        {
            return;
        }
        _decisionStore.ClearSkippedForProject(_project.Id);
        Bind(_project);
        Open();
    }

    private void Save(
        GenerationHiddenMoment moment,
        StudioHiddenMomentReviewDecision decision)
    {
        if (_project is null || _decisionStore is null)
        {
            return;
        }
        string sourceIdentity = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(
                moment.SourceFullPath.ToUpperInvariant() + "|" +
                moment.SourceMedia.Duration.Ticks)));
        _decisionStore.Upsert(new StudioHiddenMomentDecision(
            _project.Id,
            moment.Id,
            sourceIdentity,
            moment.SourceStart,
            moment.SourceEnd,
            decision,
            DateTimeOffset.UtcNow));
    }

    private static GenerationHiddenMoment RequirePreparedMetadata(
        GenerationHiddenMoment moment)
    {
        if (moment.EditorialMetadata is null ||
            moment.HasWorkingEditorialMetadata ||
            !moment.HasCompatibleEditorialMetadata)
        {
            throw new InvalidOperationException(
                "This alternate clip still needs a title and description, " +
                "and Replay Foundry cannot prepare them right now. It was not added to Studio.");
        }

        return moment;
    }

    private void SetCurrent(GenerationHiddenMoment? value)
    {
        _current = value;
        if (value is not null)
        {
            _lastViewedCandidateId = value.Id;
        }
        GenerationOutputAsset? previewAsset = value is null || _project is null
            ? null
            : StudioHiddenMomentPreviewAssetFactory.Create(_project, value);
        Preview.Bind(value is not null, _project, previewAsset);
    }

    internal void WarmFirstAlternatePreview()
    {
        if (_isDisposed || _isStopping || _isOpen)
        {
            return;
        }
        _previewWarmup.Restart(CreatePreviewAsset(_pending.FirstOrDefault()));
    }

    private void Preview_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Preview.IsPreviewSynchronized) &&
            _isOpen &&
            Preview.IsPreviewSynchronized)
        {
            WarmNextAlternatePreview();
        }
    }

    private void WarmNextAlternatePreview()
    {
        int currentIndex = Array.FindIndex(
            _pending,
            value => ReferenceEquals(value, _current));
        GenerationHiddenMoment? next = currentIndex >= 0 &&
            currentIndex + 1 < _pending.Length
                ? _pending[currentIndex + 1]
                : null;
        _previewWarmup.Restart(CreatePreviewAsset(next));
    }

    private GenerationOutputAsset? CreatePreviewAsset(
        GenerationHiddenMoment? moment) =>
        moment is null || _project is null
            ? null
            : StudioHiddenMomentPreviewAssetFactory.Create(_project, moment);

    private void RecordResearch(
        GenerationHiddenMoment moment,
        ResearchFeedbackValue value) =>
        _researchFeedback?.Record(
            moment.Id,
            moment.SourceFullPath,
            moment.SourceMedia.Duration,
            moment.PreferenceFeatures,
            ResearchFeedbackChannel.HiddenMomentReview,
            value);

    private void NotifyAll()
    {
        foreach (string propertyName in
                 StudioHiddenMomentsPresentation.NotificationPropertyNames)
        {
            OnPropertyChanged(propertyName);
        }
        _openCommand.RaiseCanExecuteChanged();
        _closeCommand.RaiseCanExecuteChanged();
        _acceptCommand.RaiseCanExecuteChanged();
        _cancelAllCommand.RaiseCanExecuteChanged();
        _skipCommand.RaiseCanExecuteChanged();
        _previousMomentCommand.RaiseCanExecuteChanged();
        _nextMomentCommand.RaiseCanExecuteChanged();
        _resetCommand.RaiseCanExecuteChanged();
    }

}
