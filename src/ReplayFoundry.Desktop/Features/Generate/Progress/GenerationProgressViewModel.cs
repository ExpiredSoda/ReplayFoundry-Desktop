using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Generate.Progress;

public enum GenerationProgressState
{
    Idle,
    Running,
    Completed,
    Failed,
    Cancelled,
}

public sealed class GenerationProgressViewModel :
    INotifyPropertyChanged
{
    internal event EventHandler<Exception>? FailureOccurred;

    private void ApplyFailure(GenerationProgressPresentation presentation, Exception exception)
    {
        ApplyPresentation(presentation);
        FailureOccurred?.Invoke(this, exception);
    }
    private readonly Action _cancelActiveOperation;
    private readonly Action _returnToSourceSelection;
    private readonly Action? _openStudio;

    private readonly DelegateCommand _cancelCommand;
    private readonly DelegateCommand _returnToSourceSelectionCommand;
    private readonly DelegateCommand _openStudioCommand;

    private GenerationProgressState _state;
    private string _title = "Getting ready";
    private string _detail = "Preparing your workspace.";
    private string _modeDisplayName = string.Empty;
    private string _sourceSummary = string.Empty;
    private string? _sourceProgressText;
    private string? _errorMessage;
    private string? _technicalDetails;
    private string? _completionSummary;
    private double _progressPercent;
    private bool _isIndeterminate = true;
    private bool _isCancellationRequested;
    private string _cancelButtonLabel = "Stop";
    private bool _hasVisiblePreparationProgress;

    public GenerationProgressViewModel(
        Action cancelActiveOperation,
        Action returnToSourceSelection,
        Action? openStudio = null)
    {
        ArgumentNullException.ThrowIfNull(cancelActiveOperation);
        ArgumentNullException.ThrowIfNull(returnToSourceSelection);

        _cancelActiveOperation = cancelActiveOperation;
        _returnToSourceSelection = returnToSourceSelection;
        _openStudio = openStudio;

        _cancelCommand =
            new DelegateCommand(
                RequestCancellation,
                () => CanCancel);

        _returnToSourceSelectionCommand =
            new DelegateCommand(
                ReturnToSourceSelection,
                () => CanReturnToSourceSelection);
        _openStudioCommand = new DelegateCommand(
            OpenStudio,
            () => CanOpenStudio);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public GenerationProgressState State
    {
        get => _state;

        private set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsCompleted));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(IsCancelled));
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(CanReturnToSourceSelection));
            OnPropertyChanged(nameof(CanOpenStudio));

            _cancelCommand.RaiseCanExecuteChanged();
            _returnToSourceSelectionCommand.RaiseCanExecuteChanged();
            _openStudioCommand.RaiseCanExecuteChanged();
        }
    }

    public string Title
    {
        get => _title;
        private set => SetField(ref _title, value);
    }

    public string Detail
    {
        get => _detail;
        private set => SetField(ref _detail, value);
    }

    public string ModeDisplayName
    {
        get => _modeDisplayName;
        private set => SetField(ref _modeDisplayName, value);
    }

    public string SourceSummary
    {
        get => _sourceSummary;
        private set => SetField(ref _sourceSummary, value);
    }

    public string? SourceProgressText
    {
        get => _sourceProgressText;

        private set
        {
            if (SetField(ref _sourceProgressText, value))
            {
                OnPropertyChanged(nameof(HasSourceProgress));
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    public string? TechnicalDetails
    {
        get => _technicalDetails;

        private set
        {
            if (SetField(ref _technicalDetails, value))
            {
                OnPropertyChanged(nameof(HasTechnicalDetails));
            }
        }
    }

    public string? CompletionSummary
    {
        get => _completionSummary;
        private set => SetField(ref _completionSummary, value);
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetField(ref _progressPercent, value);
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set => SetField(ref _isIndeterminate, value);
    }

    public bool IsRunning =>
        State == GenerationProgressState.Running;

    public bool IsCompleted =>
        State == GenerationProgressState.Completed;

    public bool IsFailed =>
        State == GenerationProgressState.Failed;

    public bool IsCancelled =>
        State == GenerationProgressState.Cancelled;

    public bool HasSourceProgress =>
        !string.IsNullOrWhiteSpace(SourceProgressText);

    public bool HasTechnicalDetails =>
        !string.IsNullOrWhiteSpace(TechnicalDetails);

    internal bool HasVisiblePreparationProgress =>
        _hasVisiblePreparationProgress;

    public bool CanCancel =>
        IsRunning && !_isCancellationRequested;

    public bool CanReturnToSourceSelection =>
        State is
            GenerationProgressState.Completed or
            GenerationProgressState.Failed or
            GenerationProgressState.Cancelled;

    public bool CanOpenStudio =>
        IsCompleted && _openStudio is not null;

    public string CancelButtonLabel
    {
        get => _cancelButtonLabel;
        private set => SetField(
            ref _cancelButtonLabel,
            value);
    }

    public ICommand CancelCommand =>
        _cancelCommand;

    public ICommand ReturnToSourceSelectionCommand =>
        _returnToSourceSelectionCommand;

    public ICommand OpenStudioCommand =>
        _openStudioCommand;

    internal void BeginPreparation(
        GenerationMode mode,
        int sourceCount)
    {
        _hasVisiblePreparationProgress = false;
        ApplyPresentation(
            GenerationProgressPresentationFactory.BeginPreparation(
                mode,
                sourceCount));
    }

    internal void ReportPreparation(
        GenerationSourcePreparationProgress update)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (!IsRunning)
        {
            return;
        }

        if (!_hasVisiblePreparationProgress)
        {
            _hasVisiblePreparationProgress = true;
            OnPropertyChanged(nameof(HasVisiblePreparationProgress));
        }

        Title = update.Phase;
        Detail = update.Detail;
        IsIndeterminate = false;
        ProgressPercent = update.ProgressPercent;

        SourceProgressText = GenerationProgressPresentationFactory.FormatSourceProgress(
            update.SourceNumber,
            update.SourceCount,
            update.SourceName);
    }

    internal void FailPreparation(
        string friendlyMessage,
        Exception exception) =>
        ApplyFailure(
            GenerationProgressPresentationFactory.Failure(
                "Source preparation stopped",
                friendlyMessage,
                exception,
                CurrentRunContext,
                ProgressPercent), exception);

    internal void MarkPreparationCancelled() =>
        ApplyPresentation(
            GenerationProgressPresentationFactory.Cancelled(
                "Preparation cancelled",
                "Your selected videos are still available.",
                "Nothing was changed. You can return to your selected videos.",
                CurrentRunContext));

    internal void BeginEvidenceAnalysis(
        GenerationMode mode,
        int sourceCount) =>
        ApplyPresentation(
            GenerationProgressPresentationFactory.BeginEvidenceAnalysis(
                mode,
                sourceCount));

    internal void ReportEvidenceAnalysis(
        GenerationEvidenceAnalysisProgress update)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (!IsRunning)
        {
            return;
        }

        Title = update.Title;
        Detail = update.Detail;
        IsIndeterminate =
            update.IsIndeterminate;
        ProgressPercent =
            update.OverallPercentage ??
            ProgressPercent;

        SourceProgressText = GenerationProgressPresentationFactory.FormatSourceProgress(
            update.SourceNumber,
            update.SourceCount,
            update.SourceFileName);
    }

    internal void FailEvidenceAnalysis(
        string friendlyMessage,
        Exception exception) =>
        ApplyFailure(
            GenerationProgressPresentationFactory.Failure(
                "Video scan stopped",
                friendlyMessage,
                exception,
                CurrentRunContext,
                ProgressPercent), exception);

    internal void MarkEvidenceAnalysisCancelled() =>
        ApplyPresentation(
            GenerationProgressPresentationFactory.Cancelled(
                "Video scan cancelled",
                "Your selected videos, clip setup, and confirmed layouts are still available.",
                "No partial scan results were saved. You can return and try again with the same videos and layouts.",
                CurrentRunContext));

    internal void Begin(
        GenerationRequest request) =>
        ApplyPresentation(
            GenerationProgressPresentationFactory.BeginGeneration(request));

    internal void Report(
        GenerationProgressUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (!IsRunning)
        {
            return;
        }

        Title = update.Title;
        Detail = update.Detail;
        IsIndeterminate = update.IsIndeterminate;
        ProgressPercent = update.ProgressPercent ?? 0;

        SourceProgressText = GenerationProgressPresentationFactory.FormatSourceProgress(
            update.SourceNumber,
            update.SourceCount,
            update.SourceName);
    }

    internal void MarkCancellationRequested()
    {
        if (!IsRunning || _isCancellationRequested)
        {
            return;
        }

        _isCancellationRequested = true;

        Title = "Stopping safely";
        Detail = "Finishing the current check before stopping.";
        IsIndeterminate = true;
        SourceProgressText = null;

        OnPropertyChanged(nameof(CanCancel));
        _cancelCommand.RaiseCanExecuteChanged();
    }

    internal void Complete(
        GenerationResult result) =>
        ApplyPresentation(
            GenerationProgressPresentationFactory.Complete(
                result,
                CurrentRunContext));

    internal void Fail(
        string friendlyMessage,
        Exception exception) =>
        ApplyFailure(
            GenerationProgressPresentationFactory.Failure(
                exception is GenerationEngineUnavailableException
                    ? "Video scan finished"
                    : "Clip finding stopped",
                friendlyMessage,
                exception,
                CurrentRunContext,
                IsIndeterminate ? 0 : ProgressPercent), exception);

    internal void MarkCancelled() =>
        ApplyPresentation(
            GenerationProgressPresentationFactory.Cancelled(
                "Clip finding cancelled",
                "Your selected videos and clip setup are still available.",
                "Nothing was changed. Your selected videos and clip setup are still available.",
                CurrentRunContext));

    internal void Reset()
    {
        _hasVisiblePreparationProgress = false;
        ApplyPresentation(
            GenerationProgressPresentationFactory.Reset());
    }

    private GenerationProgressRunContext CurrentRunContext =>
        new(
            ModeDisplayName,
            SourceSummary,
            CancelButtonLabel);

    private void ApplyPresentation(
        GenerationProgressPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        _isCancellationRequested = presentation.IsCancellationRequested;
        Title = presentation.Title;
        Detail = presentation.Detail;
        ModeDisplayName = presentation.ModeDisplayName;
        SourceSummary = presentation.SourceSummary;
        SourceProgressText = presentation.SourceProgressText;
        ErrorMessage = presentation.ErrorMessage;
        TechnicalDetails = presentation.TechnicalDetails;
        CompletionSummary = presentation.CompletionSummary;
        ProgressPercent = presentation.ProgressPercent;
        IsIndeterminate = presentation.IsIndeterminate;
        CancelButtonLabel = presentation.CancelButtonLabel;
        State = presentation.State;

        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanReturnToSourceSelection));
        OnPropertyChanged(nameof(CanOpenStudio));
        _cancelCommand.RaiseCanExecuteChanged();
        _returnToSourceSelectionCommand.RaiseCanExecuteChanged();
        _openStudioCommand.RaiseCanExecuteChanged();
    }

    private void RequestCancellation()
    {
        if (!CanCancel)
        {
            throw new InvalidOperationException(
                "Generation cannot be cancelled in the current state.");
        }

        _cancelActiveOperation();
    }

    private void ReturnToSourceSelection()
    {
        if (!CanReturnToSourceSelection)
        {
            throw new InvalidOperationException(
                "The source-selection view is not available while generation is running.");
        }

        _returnToSourceSelection();
    }

    private void OpenStudio()
    {
        if (!CanOpenStudio)
        {
            throw new InvalidOperationException(
                "Studio is available only after generation completes.");
        }

        _openStudio!();
    }

    private bool SetField<TValue>(
        ref TValue field,
        TValue value,
        [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);

        return true;
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
