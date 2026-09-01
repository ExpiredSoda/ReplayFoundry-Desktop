using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.HiddenMoments;

public enum StudioHiddenMomentQueueItemState
{
    Waiting,
    PreparingCaptions,
    PreparingTitleAndDescription,
    AddingToStudio,
    NeedsAttention,
    Canceling,
}

public sealed class StudioHiddenMomentQueueItem : ObservableObject
{
    private readonly TimeProvider _timeProvider;
    private readonly DelegateCommand _cancelCommand;
    private readonly DelegateCommand _retryCommand;
    private StudioHiddenMomentQueueItemState _state;
    private long _stateStartedAt;
    private string? _error;

    internal StudioHiddenMomentQueueItem(
        string projectId,
        GenerationHiddenMoment moment,
        StudioClipAppearance appearance,
        TimeProvider timeProvider,
        Action<StudioHiddenMomentQueueItem> cancel,
        Action<StudioHiddenMomentQueueItem> retry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(moment);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(cancel);
        ArgumentNullException.ThrowIfNull(retry);

        ProjectId = projectId.Trim();
        Moment = moment;
        Appearance = appearance;
        _timeProvider = timeProvider;
        _state = StudioHiddenMomentQueueItemState.Waiting;
        _stateStartedAt = _timeProvider.GetTimestamp();
        _cancelCommand = new DelegateCommand(
            () => cancel(this),
            () => CanCancel);
        _retryCommand = new DelegateCommand(
            () => retry(this),
            () => CanRetry);
    }

    internal GenerationHiddenMoment Moment { get; }
    internal string ProjectId { get; }
    internal StudioClipAppearance Appearance { get; }

    public string CandidateId => Moment.Id;
    public string DisplayName => Moment.SourceName;
    public string DisplayDetail =>
        $"{MediaTimeFormatter.Format(Moment.SourceStart)}–" +
        $"{MediaTimeFormatter.Format(Moment.SourceEnd)} · " +
        MediaTimeFormatter.Format(Moment.Duration);
    public StudioHiddenMomentQueueItemState State => _state;
    public string StatusText => State switch
    {
        StudioHiddenMomentQueueItemState.Waiting =>
            "Waiting in queue",
        StudioHiddenMomentQueueItemState.PreparingCaptions =>
            "Preparing captions and transcript…",
        StudioHiddenMomentQueueItemState.PreparingTitleAndDescription =>
            "Preparing title and description…",
        StudioHiddenMomentQueueItemState.AddingToStudio =>
            "Adding to Studio…",
        StudioHiddenMomentQueueItemState.NeedsAttention =>
            "Needs attention",
        StudioHiddenMomentQueueItemState.Canceling =>
            "Canceling…",
        _ => throw new InvalidOperationException(
            "The Hidden Moments queue state is unsupported."),
    };
    public string LivenessText => State switch
    {
        StudioHiddenMomentQueueItemState.PreparingCaptions =>
            $"Captions are being prepared · {FormatElapsed()} elapsed",
        StudioHiddenMomentQueueItemState.PreparingTitleAndDescription =>
            $"Local AI is working · {FormatElapsed()} elapsed",
        StudioHiddenMomentQueueItemState.AddingToStudio =>
            $"Finishing the Studio update · {FormatElapsed()} elapsed",
        StudioHiddenMomentQueueItemState.Canceling =>
            $"Stopping this addition · {FormatElapsed()} elapsed",
        _ => string.Empty,
    };
    public string? Error => _error;
    public bool IsActive => State is
        StudioHiddenMomentQueueItemState.PreparingCaptions or
        StudioHiddenMomentQueueItemState.PreparingTitleAndDescription or
        StudioHiddenMomentQueueItemState.AddingToStudio or
        StudioHiddenMomentQueueItemState.Canceling;
    public bool IsProgressIndeterminate => State is
        StudioHiddenMomentQueueItemState.PreparingCaptions or
        StudioHiddenMomentQueueItemState.PreparingTitleAndDescription or
        StudioHiddenMomentQueueItemState.Canceling;
    public double ProgressPercentage => State ==
        StudioHiddenMomentQueueItemState.AddingToStudio ? 100d : 0d;
    public bool HasFailed =>
        State == StudioHiddenMomentQueueItemState.NeedsAttention;
    public bool CanCancel => State != StudioHiddenMomentQueueItemState.Canceling;
    public bool CanRetry =>
        State == StudioHiddenMomentQueueItemState.NeedsAttention;
    public bool IsTakingLong => IsActive &&
        _timeProvider.GetElapsedTime(_stateStartedAt) >=
            TimeSpan.FromSeconds(45);
    public ICommand CancelCommand => _cancelCommand;
    public ICommand RetryCommand => _retryCommand;

    internal void SetState(StudioHiddenMomentQueueItemState state)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }
        if (_state == state)
        {
            return;
        }

        _state = state;
        _stateStartedAt = _timeProvider.GetTimestamp();
        NotifyProjectedProperties();
    }

    internal void SetError(string? value)
    {
        string? normalized = string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
        if (string.Equals(_error, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _error = normalized;
        NotifyProjectedProperties();
    }

    internal void RefreshLiveness()
    {
        OnPropertyChanged(nameof(LivenessText));
        OnPropertyChanged(nameof(IsTakingLong));
    }

    private string FormatElapsed()
    {
        TimeSpan elapsed = _timeProvider.GetElapsedTime(_stateStartedAt);
        return elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:" +
              $"{elapsed.Seconds:00}"
            : $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
    }

    private void NotifyProjectedProperties()
    {
        foreach (string propertyName in new[]
        {
            nameof(CandidateId),
            nameof(DisplayName),
            nameof(DisplayDetail),
            nameof(State),
            nameof(StatusText),
            nameof(LivenessText),
            nameof(Error),
            nameof(IsActive),
            nameof(IsProgressIndeterminate),
            nameof(ProgressPercentage),
            nameof(HasFailed),
            nameof(CanCancel),
            nameof(CanRetry),
            nameof(IsTakingLong),
        })
        {
            OnPropertyChanged(propertyName);
        }

        _cancelCommand.RaiseCanExecuteChanged();
        _retryCommand.RaiseCanExecuteChanged();
    }
}
