using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.Progress;
using ReplayFoundry.Desktop.Features.Generate.RecentProjects;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;
using ReplayFoundry.Desktop.Presentation.Workspaces;
namespace ReplayFoundry.Desktop.Features.Generate;

public sealed class GenerateViewModel : IWorkspaceChromeSource, IDisposable, IGenerateWorkflowHost
{
    private readonly IVideoFilePicker _videoFilePicker;
    private readonly IMediaRightsConfirmation _mediaRightsConfirmation;
    private readonly GenerationSourceSelectionState _sourceSelection;
    private readonly GenerationWorkflowSessionState _session;
    private readonly GenerationOperationController _operations;
    private readonly GenerateWorkflowCoordinator _workflowCoordinator;
    private readonly DelegateCommand _selectSingleFileCommand;
    private readonly DelegateCommand _selectMultipleFilesCommand;
    private readonly DelegateCommand _clearSelectionCommand;
    private readonly AsyncDelegateCommand _continueToGenerationSetupCommand;
    private readonly DelegateCommand<RecentGenerationProject> _openRecentProjectCommand;
    private readonly DelegateCommand _clearRecentProjectsCommand;
    private readonly IRecentGenerationProjectCatalog? _recentProjectCatalog;
    private readonly IRecentProjectsClearConfirmation? _recentProjectsClearConfirmation;
    private readonly IStudioProjectSwitchService? _studioProjectSwitch;
    private readonly ReadOnlyObservableCollection<RecentGenerationProject> _recentProjects;
    private GenerationMode _selectedGenerationMode = GenerationMode.IndividualClips;
    private GenerateWorkflowState _workflowState = GenerateWorkflowState.SourceSelection;
    private bool _isDisposed;
    private bool _rightsConfirmedForCurrentSelection;
    private string? _recentProjectStatus;
    internal GenerateViewModel(
        IVideoFilePicker videoFilePicker,
        IMediaRightsConfirmation mediaRightsConfirmation,
        IGenerationSetupDialogService generationSetupDialogService,
        IGenerationCompositionReviewDialogService compositionReviewDialogService,
        IGenerationSourcePreparationCoordinator sourcePreparationCoordinator,
        IGenerationEvidenceAnalysisCoordinator evidenceAnalysisCoordinator,
        IGenerationRunner generationRunner,
        GenerationSourceSelectionState sourceSelection,
        GenerationWorkflowSessionState session,
        GenerationOperationController operations,
        GenerationRuntimeCapabilities? runtimeCapabilities = null,
        IRecentGenerationProjectCatalog? recentProjectCatalog = null,
        IStudioProjectSwitchService? studioProjectSwitch = null,
        IRecentProjectsClearConfirmation? recentProjectsClearConfirmation = null)
    {
        ArgumentNullException.ThrowIfNull(videoFilePicker);
        ArgumentNullException.ThrowIfNull(mediaRightsConfirmation);
        ArgumentNullException.ThrowIfNull(generationSetupDialogService);
        ArgumentNullException.ThrowIfNull(compositionReviewDialogService);
        ArgumentNullException.ThrowIfNull(sourcePreparationCoordinator);
        ArgumentNullException.ThrowIfNull(evidenceAnalysisCoordinator);
        ArgumentNullException.ThrowIfNull(generationRunner);
        ArgumentNullException.ThrowIfNull(sourceSelection);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(operations);
        _videoFilePicker = videoFilePicker;
        _mediaRightsConfirmation = mediaRightsConfirmation;
        _sourceSelection = sourceSelection;
        _session = session;
        _operations = operations;
        _recentProjectCatalog = recentProjectCatalog;
        _studioProjectSwitch = studioProjectSwitch;
        _recentProjectsClearConfirmation = recentProjectsClearConfirmation;
        _recentProjects = recentProjectCatalog?.Projects ??
            new ReadOnlyObservableCollection<RecentGenerationProject>(
                new ObservableCollection<RecentGenerationProject>());
        _sourceSelection.Changed += SourceSelection_Changed;
        _session.Changed += Session_Changed;
        ((INotifyCollectionChanged)_recentProjects).CollectionChanged +=
            RecentProjects_CollectionChanged;
        GenerationProgress = new GenerationProgressViewModel(
            CancelActiveOperation,
            ReturnToSourceSelection,
            RequestStudio);
        GenerationProgress.PropertyChanged += GenerationProgress_PropertyChanged;
        _workflowCoordinator = new GenerateWorkflowCoordinator(
            generationSetupDialogService,
            compositionReviewDialogService,
            sourcePreparationCoordinator,
            evidenceAnalysisCoordinator,
            generationRunner,
            sourceSelection,
            session,
            operations,
            runtimeCapabilities ?? GenerationRuntimeCapabilities.DeterministicOnly,
            this);
        _selectSingleFileCommand = new DelegateCommand(
            SelectSingleFile,
            CanEditSourceSelection);
        _selectMultipleFilesCommand = new DelegateCommand(
            SelectMultipleFiles,
            CanEditSourceSelection);
        _clearSelectionCommand = new DelegateCommand(
            ClearSelection,
            () => CanEditSourceSelection() && HasSelectedSources);
        _continueToGenerationSetupCommand = new AsyncDelegateCommand(
            ContinueToGenerationSetupAsync,
            () => CanEditSourceSelection() && HasSelectedSources);
        _openRecentProjectCommand = new DelegateCommand<RecentGenerationProject>(
            OpenRecentProject,
            _ => CanEditSourceSelection());
        _clearRecentProjectsCommand = new DelegateCommand(
            ClearRecentProjects,
            () => CanEditSourceSelection() && HasRecentProjects);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? StudioRequested;
    public ReadOnlyObservableCollection<SelectedVideoSource> SelectedSources =>
        _sourceSelection.Sources;
    public GenerationProgressViewModel GenerationProgress { get; }
    public ICommand SelectSingleFileCommand => _selectSingleFileCommand;
    public ICommand SelectMultipleFilesCommand => _selectMultipleFilesCommand;
    public ICommand ClearSelectionCommand => _clearSelectionCommand;
    public ICommand ContinueToGenerationSetupCommand => _continueToGenerationSetupCommand;
    public ICommand OpenRecentProjectCommand => _openRecentProjectCommand;
    public ICommand ClearRecentProjectsCommand => _clearRecentProjectsCommand;
    public ReadOnlyObservableCollection<RecentGenerationProject> RecentProjects =>
        _recentProjects;
    public bool HasRecentProjects => RecentProjects.Count > 0;
    public string? RecentProjectStatus
    {
        get => _recentProjectStatus;
        private set
        {
            if (_recentProjectStatus == value) return;
            _recentProjectStatus = value;
            OnPropertyChanged();
        }
    }
    public GenerateWorkflowState WorkflowState
    {
        get => _workflowState;
        private set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "The Generate workflow state is not defined.");
            }
            if (_workflowState == value)
            {
                return;
            }
            _workflowState = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HeaderStatusText));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsSourceSelectionVisible));
            OnPropertyChanged(nameof(IsProgressVisible));
            RaiseSourceCommandStateChanged();
        }
    }
    public string HeaderStatusText =>
        GenerateWorkflowPresentation.StatusText(WorkflowState);
    public string WorkspaceEyebrow => "GENERATE / CREATE";
    public string WorkspaceTitle => "Generate";
    public string WorkspaceDescription =>
        "Turn one or more local videos into individual clips or a montage.";
    public string StatusText => HeaderStatusText;
    public bool IsSourceSelectionVisible =>
        GenerateWorkflowPresentation.ShowsSourceSelection(WorkflowState) ||
        WorkflowState == GenerateWorkflowState.PreparingSources &&
        !GenerationProgress.HasVisiblePreparationProgress;
    public bool IsProgressVisible =>
        GenerateWorkflowPresentation.ShowsProgress(WorkflowState) &&
        (WorkflowState != GenerateWorkflowState.PreparingSources ||
         GenerationProgress.HasVisiblePreparationProgress);
    public GenerationMode SelectedGenerationMode
    {
        get => _selectedGenerationMode;
        private set
        {
            ThrowIfDisposed();
            if (!CanEditSourceSelection())
            {
                throw new InvalidOperationException(
                    "The generation mode cannot change while generation is active.");
            }
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "The generation mode is not defined.");
            }
            if (_selectedGenerationMode == value)
            {
                return;
            }
            _selectedGenerationMode = value;
            _session.InvalidateAfterModeChange();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsIndividualClipsSelected));
            OnPropertyChanged(nameof(IsMontageSelected));
        }
    }
    public bool IsIndividualClipsSelected
    {
        get => SelectedGenerationMode == GenerationMode.IndividualClips;
        set { if (value) SelectedGenerationMode = GenerationMode.IndividualClips; }
    }
    public bool IsMontageSelected
    {
        get => SelectedGenerationMode == GenerationMode.Montage;
        set { if (value) SelectedGenerationMode = GenerationMode.Montage; }
    }
    public bool HasSelectedSources => _sourceSelection.HasSources;
    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);
    public int SelectedSourceCount => _sourceSelection.Count;
    public string SelectionSummary =>
        GenerateWorkflowPresentation.SelectionSummary(SelectedSourceCount);
    public GenerationSetupOptions? CurrentGenerationSetup => _session.Setup;
    public bool HasGenerationSetup => CurrentGenerationSetup is not null;
    public GenerationSourcePreparationResult? CurrentSourcePreparation => _session.Preparation;
    public GenerationCompositionReviewResult? CurrentCompositionReview => _session.Composition;
    public bool HasCompositionReview => CurrentCompositionReview is not null;
    public GenerationEvidenceAnalysisResult? CurrentEvidenceAnalysis => _session.Evidence;
    public string? CompositionReviewSummary =>
        GenerateWorkflowPresentation.CompositionSummary(
            CurrentCompositionReview?.SourcePlans.Count);
    public string GenerationSetupButtonText =>
        GenerateWorkflowPresentation.SetupButtonText(HasGenerationSetup);
    public string? GenerationSetupSummary => CurrentGenerationSetup?.Summary;
    public string? ValidationMessage => _sourceSelection.ValidationMessage;
    public void AddDroppedFiles(IEnumerable<string> candidatePaths)
    {
        ThrowIfDisposed();
        EnsureSourceSelectionIsEditable();
        _sourceSelection.AddCandidates(candidatePaths);
    }
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }
        _isDisposed = true;
        _sourceSelection.Changed -= SourceSelection_Changed;
        _session.Changed -= Session_Changed;
        GenerationProgress.PropertyChanged -= GenerationProgress_PropertyChanged;
        ((INotifyCollectionChanged)_recentProjects).CollectionChanged -=
            RecentProjects_CollectionChanged;
        _operations.Dispose();
    }
    internal async Task ContinueToGenerationSetupAsync()
    {
        ThrowIfDisposed();
        EnsureSourceSelectionIsEditable();
        if (!_rightsConfirmedForCurrentSelection)
        {
            if (!_mediaRightsConfirmation.Confirm(SelectedSources.ToArray()))
            {
                return;
            }

            _rightsConfirmedForCurrentSelection = true;
        }

        await _workflowCoordinator.RunAsync();
    }
    private void CancelActiveOperation()
    {
        ThrowIfDisposed();
        if (WorkflowState is not (
                GenerateWorkflowState.PreparingSources or
                GenerateWorkflowState.AnalyzingEvidence or
                GenerateWorkflowState.Generating) ||
            !_operations.HasActiveOperation)
        {
            throw new InvalidOperationException(
                "There is no active Generate operation to cancel.");
        }
        GenerationProgress.MarkCancellationRequested();
        _operations.CancelActive();
    }
    private void ReturnToSourceSelection()
    {
        ThrowIfDisposed();
        if (WorkflowState is
            GenerateWorkflowState.PreparingSources or
            GenerateWorkflowState.ReviewingComposition or
            GenerateWorkflowState.AnalyzingEvidence or
            GenerateWorkflowState.Generating)
        {
            throw new InvalidOperationException(
                "Source selection cannot open while a Generate operation is running.");
        }
        GenerationProgress.Reset();
        WorkflowState = GenerateWorkflowState.SourceSelection;
    }
    private void RequestStudio()
    {
        ThrowIfDisposed();
        if (WorkflowState != GenerateWorkflowState.Completed)
        {
            throw new InvalidOperationException(
                "Studio cannot open until the Generate workflow completes.");
        }
        StudioRequested?.Invoke(this, EventArgs.Empty);
    }
    GenerationMode IGenerateWorkflowHost.SelectedGenerationMode =>
        SelectedGenerationMode;
    GenerateWorkflowState IGenerateWorkflowHost.WorkflowState
    {
        get => WorkflowState;
        set => WorkflowState = value;
    }
    GenerationProgressViewModel IGenerateWorkflowHost.Progress =>
        GenerationProgress;
    bool IGenerateWorkflowHost.IsDisposed => _isDisposed;
    void IGenerateWorkflowHost.RefreshCommandState() =>
        RaiseSourceCommandStateChanged();
    private bool CanEditSourceSelection() =>
        !_isDisposed &&
        !_operations.HasActiveOperation &&
        WorkflowState == GenerateWorkflowState.SourceSelection;
    private void SelectSingleFile()
    {
        ThrowIfDisposed();
        EnsureSourceSelectionIsEditable();
        _sourceSelection.AddCandidates(_videoFilePicker.PickSingleVideo());
    }
    private void SelectMultipleFiles()
    {
        ThrowIfDisposed();
        EnsureSourceSelectionIsEditable();
        _sourceSelection.AddCandidates(_videoFilePicker.PickMultipleVideos());
    }
    private void ClearSelection()
    {
        ThrowIfDisposed();
        EnsureSourceSelectionIsEditable();
        _sourceSelection.Clear();
    }

    private void OpenRecentProject(RecentGenerationProject project)
    {
        if (_recentProjectCatalog?.TryGetStudioProject(
                project.ProjectId,
                out GenerationOutputProject? retained) == true &&
            retained is not null)
        {
            StudioProjectSwitchResult result = _studioProjectSwitch is null
                ? new StudioProjectSwitchResult(
                    StudioProjectSwitchOutcome.Unavailable,
                    "Studio project switching is unavailable in this session.")
                : _studioProjectSwitch.TrySwitchProject(retained);
            RecentProjectStatus = result.Succeeded ? null : result.Message;
            if (result.Succeeded)
            {
                StudioRequested?.Invoke(this, EventArgs.Empty);
            }
            return;
        }

        RecentProjectStatus =
            $"{project.Title} has no Studio draft that can be reopened. Its " +
            "source may be missing or changed, or this older project may " +
            "predate durable Studio projects. No source or saved project " +
            "was changed.";
    }

    private void ClearRecentProjects()
    {
        if (_recentProjectCatalog is null ||
            !HasRecentProjects ||
            _recentProjectsClearConfirmation?.ConfirmClear(
                RecentProjects.Count) != true)
        {
            return;
        }
        try
        {
            int removed = _recentProjectCatalog.ClearAll();
            RecentProjectStatus = removed == 1
                ? "Cleared 1 recent project and its saved Studio draft."
                : $"Cleared {removed} recent projects and their saved Studio drafts.";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            RecentProjectStatus =
                "Replay Foundry could not clear every recent project. " +
                exception.Message;
        }
    }

    private void RecentProjects_CollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasRecentProjects));
        _clearRecentProjectsCommand.RaiseCanExecuteChanged();
    }
    private void SourceSelection_Changed(
        object? sender,
        GenerationSourceSelectionChangedEventArgs eventArgs)
    {
        if (eventArgs.SourcesChanged)
        {
            _rightsConfirmedForCurrentSelection = false;
            _session.InvalidateAfterSourceChange();
            OnPropertyChanged(nameof(HasSelectedSources));
            OnPropertyChanged(nameof(SelectedSourceCount));
            OnPropertyChanged(nameof(SelectionSummary));
        }
        if (eventArgs.ValidationChanged)
        {
            OnPropertyChanged(nameof(ValidationMessage));
            OnPropertyChanged(nameof(HasValidationMessage));
        }
        RaiseSourceCommandStateChanged();
    }

    private void GenerationProgress_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(
                GenerationProgressViewModel.HasVisiblePreparationProgress))
        {
            return;
        }
        OnPropertyChanged(nameof(IsSourceSelectionVisible));
        OnPropertyChanged(nameof(IsProgressVisible));
    }
    private void Session_Changed(
        object? sender,
        GenerationWorkflowSessionChangedEventArgs eventArgs)
    {
        if (eventArgs.Includes(GenerationWorkflowSessionChange.Preparation))
        {
            OnPropertyChanged(nameof(CurrentSourcePreparation));
        }
        if (eventArgs.Includes(GenerationWorkflowSessionChange.Setup))
        {
            OnPropertyChanged(nameof(CurrentGenerationSetup));
            OnPropertyChanged(nameof(HasGenerationSetup));
            OnPropertyChanged(nameof(GenerationSetupButtonText));
            OnPropertyChanged(nameof(GenerationSetupSummary));
        }
        if (eventArgs.Includes(GenerationWorkflowSessionChange.Composition))
        {
            OnPropertyChanged(nameof(CurrentCompositionReview));
            OnPropertyChanged(nameof(HasCompositionReview));
            OnPropertyChanged(nameof(CompositionReviewSummary));
        }
        if (eventArgs.Includes(GenerationWorkflowSessionChange.Evidence))
        {
            OnPropertyChanged(nameof(CurrentEvidenceAnalysis));
        }
    }
    private void RaiseSourceCommandStateChanged()
    {
        _selectSingleFileCommand.RaiseCanExecuteChanged();
        _selectMultipleFilesCommand.RaiseCanExecuteChanged();
        _clearSelectionCommand.RaiseCanExecuteChanged();
        _continueToGenerationSetupCommand.RaiseCanExecuteChanged();
        _openRecentProjectCommand.RaiseCanExecuteChanged();
    }
    private void EnsureSourceSelectionIsEditable()
    {
        if (!CanEditSourceSelection())
        {
            throw new InvalidOperationException(
                "Source selection cannot change while generation is active.");
        }
    }
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
