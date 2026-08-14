using System;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using ReplayFoundry.Desktop.Presentation.Commands;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Workspaces;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Inspector;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Features.Studio.Rendering;
using ReplayFoundry.Desktop.Features.Studio.Editorial;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Studio.HiddenMoments;
using ReplayFoundry.Desktop.Features.Studio.Projects;
using ReplayFoundry.Desktop.Features.Research;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Settings;

namespace ReplayFoundry.Desktop.Features.Studio;

public sealed class StudioViewModel : ObservableObject, IWorkspaceChromeSource,
    IStudioProjectSwitchService, IDisposable
{
    private StudioToolSection _selectedTool = StudioToolSection.MomentsClips;
    private readonly IGenerationOutputSession? _outputSession;
    private readonly IGenerationOutputSink? _outputSink;
    private readonly IStudioPreviewPrewarmer? _previewPrewarmer;
    private readonly IStudioProjectPersistenceCoordinator? _projectPersistence;
    private readonly SynchronizationContext? _notificationContext;
    private readonly DelegateCommand<string> _selectBrowserAssetCommand;
    private readonly DelegateCommand<string> _queueBrowserAssetCommand;
    private readonly DelegateCommand<string> _removeBrowserAssetCommand;
    private readonly DelegateCommand<string> _restoreBrowserAssetCommand;
    private readonly DelegateCommand _reviewRenderRequirementsCommand;
    private CancellationTokenSource? _previewPrewarmCancellation;
    private CancellationTokenSource? _draftSaveCancellation;
    private string? _boundProjectId;
    private WorkspaceSurfaceState _surfaceState;
    private bool _isDisposed;

    public StudioViewModel()
        : this(
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            WorkspaceSurfaceState.Empty)
    {
    }

    public StudioViewModel(
        IGenerationOutputSession outputSession)
        : this(
            outputSession ??
                throw new ArgumentNullException(nameof(outputSession)),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            outputSession.Current is null
                ? WorkspaceSurfaceState.Empty
                : WorkspaceSurfaceState.ContentReady)
    {
    }

    public StudioViewModel(
        IGenerationOutputSession outputSession,
        IGenerationOutputEditor outputEditor,
        IStudioProjectRenderingService projectRenderingService)
        : this(
            outputSession ??
                throw new ArgumentNullException(nameof(outputSession)),
            outputEditor ??
                throw new ArgumentNullException(nameof(outputEditor)),
            projectRenderingService ??
                throw new ArgumentNullException(nameof(projectRenderingService)),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            outputSession.Current is null
                ? WorkspaceSurfaceState.Empty
                : WorkspaceSurfaceState.ContentReady)
    {
    }

    public StudioViewModel(
        IGenerationOutputSession outputSession,
        IGenerationOutputEditor outputEditor,
        IStudioProjectRenderingService projectRenderingService,
        IClipEditorialMetadataGenerationService editorialMetadataGenerator,
        IClipEditorialProfileEditor editorialProfile,
        IStudioPreviewMediaService? previewMediaService = null,
        IStudioClipPreferenceService? preferenceService = null,
        IStudioCandidateDecisionStore? decisionStore = null,
        IStudioHiddenMomentDecisionStore? hiddenMomentDecisionStore = null,
        IResearchFeedbackRecorder? researchFeedback = null,
        IGenerationCaptionPreparationService? captionPreparation = null,
        IGenerationEditorialMetadataService? generationEditorialMetadata = null,
        IStudioPreviewPrewarmer? previewPrewarmer = null,
        IStudioProjectPersistenceCoordinator? projectPersistence = null,
        ILibraryCatalog? libraryCatalog = null,
        IEditorialRerollPreference? editorialRerollPreference = null,
        IStudioEditorialMetadataCorrectionRecorder?
            editorialPreferenceRecorder =
            null)
        : this(
            outputSession ??
                throw new ArgumentNullException(nameof(outputSession)),
            outputEditor ??
                throw new ArgumentNullException(nameof(outputEditor)),
            projectRenderingService ??
                throw new ArgumentNullException(nameof(projectRenderingService)),
            editorialMetadataGenerator ??
                throw new ArgumentNullException(nameof(editorialMetadataGenerator)),
            editorialProfile ??
                throw new ArgumentNullException(nameof(editorialProfile)),
            previewMediaService,
            preferenceService,
            decisionStore,
            hiddenMomentDecisionStore,
            researchFeedback,
            captionPreparation,
            generationEditorialMetadata,
            previewPrewarmer,
            projectPersistence,
            libraryCatalog,
            outputSession.Current is null
                ? WorkspaceSurfaceState.Empty
                : WorkspaceSurfaceState.ContentReady,
            editorialRerollPreference,
            editorialPreferenceRecorder)
    {
    }

    private StudioViewModel(
        IGenerationOutputSession? outputSession,
        IGenerationOutputEditor? outputEditor,
        IStudioProjectRenderingService? projectRenderingService,
        IClipEditorialMetadataGenerationService?
            editorialMetadataGenerator,
        IClipEditorialProfileEditor? editorialProfile,
        IStudioPreviewMediaService? previewMediaService,
        IStudioClipPreferenceService? preferenceService,
        IStudioCandidateDecisionStore? decisionStore,
        IStudioHiddenMomentDecisionStore? hiddenMomentDecisionStore,
        IResearchFeedbackRecorder? researchFeedback,
        IGenerationCaptionPreparationService? captionPreparation,
        IGenerationEditorialMetadataService? generationEditorialMetadata,
        IStudioPreviewPrewarmer? previewPrewarmer,
        IStudioProjectPersistenceCoordinator? projectPersistence,
        ILibraryCatalog? libraryCatalog,
        WorkspaceSurfaceState surfaceState,
        IEditorialRerollPreference? editorialRerollPreference = null,
        IStudioEditorialMetadataCorrectionRecorder?
            editorialPreferenceRecorder =
            null)
    {
        _outputSession = outputSession;
        _outputSink = outputSession as IGenerationOutputSink;
        _previewPrewarmer = previewPrewarmer;
        _projectPersistence = projectPersistence;
        _notificationContext = SynchronizationContext.Current;
        if (_projectPersistence is not null)
        {
            _projectPersistence.PersistenceStateChanged +=
                ProjectPersistence_PersistenceStateChanged;
        }
        _surfaceState = surfaceState;
        Preview = new StudioPreviewViewModel(previewMediaService);
        Preview.GraphicFileDropped += Preview_GraphicFileDropped;
        HiddenMoments = new StudioHiddenMomentsViewModel(
            outputEditor,
            previewMediaService,
            hiddenMomentDecisionStore,
            researchFeedback,
            captionPreparation,
            generationEditorialMetadata);
        HiddenMoments.MomentAccepted += HiddenMoments_MomentAccepted;
        Inspector = new StudioInspectorViewModel(
            outputEditor,
            editorialMetadataGenerator,
            editorialProfile,
            preferenceService,
            decisionStore,
            researchFeedback,
            editorialRerollPreference,
            editorialPreferenceRecorder);
        Inspector.SelectedAssetChanged += Inspector_SelectedAssetChanged;
        Inspector.Clip.DraftRangeChanged += Clip_DraftRangeChanged;
        Inspector.Clip.DraftAppearanceChanged += Clip_DraftAppearanceChanged;
        FinalRender = new StudioFinalRenderViewModel(
            outputEditor,
            projectRenderingService,
            Inspector.Clip.ApplyPendingEdit,
            Inspector.SetHostBusy,
            () => Inspector.Clip.HasPendingEdit,
            () => Inspector.Clip.IsBoundaryDraftValid,
            () => Inspector.Editorial.HasUnsavedChanges,
            () => Inspector.Editorial.IsGenerating ||
                  HiddenMoments.IsPreparingAcceptedMoment,
            () => Inspector.SelectedAsset,
            libraryCatalog);
        Inspector.Editorial.PropertyChanged += Editorial_PropertyChanged;
        HiddenMoments.PropertyChanged += HiddenMoments_PropertyChanged;
        FinalRender.PropertyChanged += FinalRender_PropertyChanged;

        ToolSections = StudioSurfaceCatalog.ToolSections;
        SelectToolCommand = new DelegateCommand<StudioToolSection>(value => SelectedTool = value);
        _selectBrowserAssetCommand = new DelegateCommand<string>(
            SelectBrowserAsset,
            CanSelectBrowserAsset);
        _queueBrowserAssetCommand = new DelegateCommand<string>(
            QueueBrowserAsset,
            CanQueueBrowserAsset);
        _removeBrowserAssetCommand = new DelegateCommand<string>(
            assetId => SetBrowserAssetInclusion(assetId, isIncluded: false),
            assetId => CanSetBrowserAssetInclusion(assetId, isIncluded: false));
        _restoreBrowserAssetCommand = new DelegateCommand<string>(
            assetId => SetBrowserAssetInclusion(assetId, isIncluded: true),
            assetId => CanSetBrowserAssetInclusion(assetId, isIncluded: true));
        _reviewRenderRequirementsCommand = new DelegateCommand(
            ReviewRenderRequirements,
            () => CurrentProject is { IsFinalized: false } &&
                  FinalRender.NeedsRenderAttention);
        if (_outputSession is not null)
        {
            _outputSession.CurrentChanged +=
                OutputSession_CurrentChanged;
        }
        Inspector.Bind(HasProject, CurrentProject, preferredAssetId: null);
        _boundProjectId = CurrentProject?.Id;
        Preview.Bind(HasProject, CurrentProject, Inspector.SelectedAsset);
        FinalRender.Bind(CurrentProject);
        HiddenMoments.Bind(CurrentProject);
        RestartPreviewPrewarming();
    }

    internal StudioViewModel(WorkspaceSurfaceState surfaceState)
        : this(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, surfaceState)
    {
    }


    public IReadOnlyList<StudioToolItem> ToolSections { get; }
    public StudioInspectorViewModel Inspector { get; }
    public StudioPreviewViewModel Preview { get; }
    public StudioFinalRenderViewModel FinalRender { get; }
    public StudioHiddenMomentsViewModel HiddenMoments { get; }
    public IReadOnlyList<StudioBrowserPreviewItem> BrowserPreviewItems =>
        StudioSurfaceCatalog.BuildBrowserPreviewItems(
            SelectedTool,
            CurrentProject,
            Inspector.SelectedAsset?.Id,
            FinalRender.QueueItems
                .Select(static item => item.AssetId)
                .ToHashSet(StringComparer.Ordinal));
    public GenerationOutputAsset? SelectedAsset => Inspector.SelectedAsset;
    public WorkspaceSurfaceState SurfaceState => _surfaceState;
    public string WorkspaceEyebrow => "STUDIO / EDIT";
    public string WorkspaceTitle => "Build the final cut";
    public string WorkspaceDescription =>
        "Review clips, trim boundaries, style captions and effects, then render finished files to Library.";
    public GenerationOutputProject? CurrentProject =>
        _outputSession?.Current;
    public bool IsEmpty => SurfaceState == WorkspaceSurfaceState.Empty;
    public bool IsContentReady => SurfaceState == WorkspaceSurfaceState.ContentReady;
    public bool IsLoading => SurfaceState == WorkspaceSurfaceState.Loading;
    public bool IsError => SurfaceState == WorkspaceSurfaceState.Error;
    public bool IsUnavailable => SurfaceState == WorkspaceSurfaceState.Unavailable;
    public bool ShouldShowPlaceholder => IsUnavailable || IsError;
    public bool HasProject => IsContentReady;
    public bool IsProjectMissing => !HasProject;
    public bool CanUseProjectCommands => HasProject;
    public bool IsProjectFinalized =>
        CurrentProject?.IsFinalized == true;
    public bool IsProjectDraft =>
        CurrentProject is not null && !CurrentProject.IsFinalized;
    public string ProjectName => CurrentProject is null
        ? HasProject
            ? "Open Studio project"
            : "No project open"
        : CurrentProject.Assets
            .Select(static asset => asset.SourceFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray() is { Length: 1 } sources
                ? Path.GetFileNameWithoutExtension(sources[0])
                : $"{CurrentProject.Assets.Select(static asset => asset.SourceFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Count()} source project";
    public string SaveStateText => _projectPersistence?.LastError is { } error
        ? "Local Studio recovery needs attention: " + error
        : CurrentProject is null
        ? HasProject
            ? "Studio is ready to save edits locally"
            : "No Studio editing session is open"
        : CurrentProject.IsFinalized
            ? "Final files saved in Library"
            : "Studio edits and the render queue are saved locally for reopening";
    public string ProjectPromptDescription => HasProject
        ? IsProjectFinalized
            ? "The finalized files are available in Library."
            : "Use the inspector to shape clips, queue the exact Browser cards you want, and render one or more Library copies without closing the Studio draft."
        : "The editing workspace stays visible so its workflow is clear. Generate a clip to enable playback, editing, and save controls.";
    public string StatusText => _projectPersistence?.LastError is not null
        ? "Studio recovery needs attention"
        : SurfaceState switch
        {
            WorkspaceSurfaceState.ContentReady => IsProjectFinalized
                ? "Finished files in Library"
                : CurrentProject is null
                    ? "Studio project ready"
                    : $"{CurrentProject.SelectedCount} " +
                      (CurrentProject.SelectedCount == 1
                          ? "clip ready to edit"
                          : "clips ready to edit"),
            WorkspaceSurfaceState.Loading => "Opening your Studio project…",
            WorkspaceSurfaceState.Error => "Studio needs your attention",
            WorkspaceSurfaceState.Unavailable => "Studio is unavailable right now",
            _ => "Waiting for a project",
        };
    public string ErrorSummary => "Studio could not load a project.";
    public string SurfaceSummary => IsEmpty ? "Studio is waiting for a project." : ErrorSummary;
    public string SurfaceSuggestion => IsEmpty
        ? "Start in Generate, then return here when a project is ready to shape."
        : "Studio cannot open this project yet. Return to Generate and finish preparing the source video.";
    public string SelectedToolTitle =>
        StudioSurfaceCatalog.GetTool(SelectedTool).Label;
    public string SelectedToolDescription =>
        StudioSurfaceCatalog.GetTool(SelectedTool).Description + ".";
    public string SelectedClipDurationText => SelectedAsset is null
        ? "No clip selected"
        : StudioTimeFormatter.FormatDuration(SelectedAsset.Duration);

    public StudioToolSection SelectedTool
    {
        get => _selectedTool;
        set
        {
            if (_selectedTool == value) return;
            _selectedTool = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedToolTitle));
            OnPropertyChanged(nameof(SelectedToolDescription));
            OnPropertyChanged(nameof(BrowserPreviewItems));
        }
    }

    public ICommand SelectToolCommand { get; }
    public ICommand SelectBrowserAssetCommand => _selectBrowserAssetCommand;
    public ICommand QueueBrowserAssetCommand => _queueBrowserAssetCommand;
    public ICommand RemoveBrowserAssetCommand => _removeBrowserAssetCommand;
    public ICommand RestoreBrowserAssetCommand => _restoreBrowserAssetCommand;
    public ICommand ReviewRenderRequirementsCommand =>
        _reviewRenderRequirementsCommand;

    public StudioProjectSwitchResult TrySwitchProject(
        GenerationOutputProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (_isDisposed || _outputSink is null)
        {
            return new StudioProjectSwitchResult(
                StudioProjectSwitchOutcome.Unavailable,
                "Studio cannot open another project in this session.");
        }
        if (CurrentProject?.Id.Equals(
                project.Id,
                StringComparison.Ordinal) == true &&
            CurrentProject.IsFinalized == false)
        {
            return new StudioProjectSwitchResult(
                StudioProjectSwitchOutcome.AlreadyOpen,
                "This Studio project is already open.");
        }
        if (FinalRender.IsRendering)
        {
            return new StudioProjectSwitchResult(
                StudioProjectSwitchOutcome.BlockedActiveRender,
                "Finish or cancel the active render before opening another Studio project.");
        }
        if (CurrentProject?.IsFinalized == false &&
            FinalRender.HasQueuedItems)
        {
            return new StudioProjectSwitchResult(
                StudioProjectSwitchOutcome.BlockedUnsavedDraft,
                "Render the queued clips or remove them from the render queue " +
                "before opening another Studio project.");
        }
        if (Inspector.Editorial.IsGenerating ||
            HiddenMoments.IsPreparingAcceptedMoment)
        {
            return new StudioProjectSwitchResult(
                StudioProjectSwitchOutcome.BlockedBusyOperation,
                "Finish the active Studio operation before opening another project.");
        }

        // A blocked switch must leave the visible drafts completely stable.
        // Stop the delayed appearance commit before inspecting manual-save
        // editors so it cannot publish underneath the blocking message.
        _draftSaveCancellation?.Cancel();
        _draftSaveCancellation?.Dispose();
        _draftSaveCancellation = null;
        if (Inspector.Caption.HasUnsavedChanges)
        {
            return new StudioProjectSwitchResult(
                StudioProjectSwitchOutcome.BlockedUnsavedDraft,
                "Save the caption text and timing changes before opening another Studio project.");
        }
        if (Inspector.Graphics.HasUnsavedChanges)
        {
            return new StudioProjectSwitchResult(
                StudioProjectSwitchOutcome.BlockedUnsavedDraft,
                "Apply the graphic placement changes before opening another Studio project.");
        }
        if (Inspector.Editorial.HasUnsavedProfileChanges)
        {
            return new StudioProjectSwitchResult(
                StudioProjectSwitchOutcome.BlockedUnsavedDraft,
                "Save the reusable metadata wording preferences before opening another Studio project.");
        }

        StudioPendingEditorialDraft? pendingMetadata =
            Inspector.Editorial.CapturePendingDraft();
        if (pendingMetadata is not null &&
            !Inspector.Editorial.CanPersistPendingDraft(pendingMetadata))
        {
            return new StudioProjectSwitchResult(
                StudioProjectSwitchOutcome.BlockedInvalidMetadata,
                "Fix the title and description before opening another Studio project.");
        }
        if (!CanCommitPendingClipEdit())
        {
            return new StudioProjectSwitchResult(
                StudioProjectSwitchOutcome.BlockedInvalidClipEdit,
                "Fix or reset the current clip boundaries before opening another Studio project.");
        }

        if (!TryCommitPendingClipEdit())
        {
            return new StudioProjectSwitchResult(
                StudioProjectSwitchOutcome.BlockedInvalidClipEdit,
                "Replay Foundry could not save the current clip edit, so the project stayed open.");
        }
        if (pendingMetadata is not null &&
            !Inspector.Editorial.TryPersistPendingDraft(pendingMetadata))
        {
            return new StudioProjectSwitchResult(
                StudioProjectSwitchOutcome.BlockedInvalidMetadata,
                "Replay Foundry could not save the current metadata, so the project stayed open.");
        }

        try
        {
            _projectPersistence?.FlushAsync().GetAwaiter().GetResult();
            StudioProjectRecoveryState? recovery = null;
            _projectPersistence?.TryGetRecovery(project.Id, out recovery);
            _outputSink.Publish(project.ReopenAsDraft());
            if (recovery is not null)
            {
                RestoreDurableRecovery(recovery);
            }
            return new StudioProjectSwitchResult(
                StudioProjectSwitchOutcome.Switched,
                "Studio opened the selected recent project.");
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException)
        {
            return new StudioProjectSwitchResult(
                StudioProjectSwitchOutcome.Unavailable,
                "Studio could not open the selected project: " +
                exception.Message);
        }
    }
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        ScheduleDurableState();
        _projectPersistence?.FlushAsync().GetAwaiter().GetResult();

        _isDisposed = true;
        _previewPrewarmCancellation?.Cancel();
        _previewPrewarmCancellation?.Dispose();
        _previewPrewarmCancellation = null;
        _draftSaveCancellation?.Cancel();
        _draftSaveCancellation?.Dispose();
        _draftSaveCancellation = null;
        Inspector.SelectedAssetChanged -= Inspector_SelectedAssetChanged;
        Inspector.Clip.DraftRangeChanged -= Clip_DraftRangeChanged;
        Inspector.Clip.DraftAppearanceChanged -= Clip_DraftAppearanceChanged;
        Inspector.Editorial.PropertyChanged -= Editorial_PropertyChanged;
        HiddenMoments.PropertyChanged -= HiddenMoments_PropertyChanged;
        FinalRender.PropertyChanged -= FinalRender_PropertyChanged;
        FinalRender.Dispose();
        HiddenMoments.MomentAccepted -= HiddenMoments_MomentAccepted;
        HiddenMoments.Dispose();
        Inspector.Dispose();
        Preview.Dispose();
        Preview.GraphicFileDropped -= Preview_GraphicFileDropped;
        if (_outputSession is not null)
        {
            _outputSession.CurrentChanged -=
                OutputSession_CurrentChanged;
        }
        if (_projectPersistence is not null)
        {
            _projectPersistence.PersistenceStateChanged -=
                ProjectPersistence_PersistenceStateChanged;
        }
    }

    private bool CanSelectBrowserAsset(string? assetId) =>
        assetId is not null &&
        !FinalRender.IsRendering &&
        (assetId.Equals(
             Inspector.SelectedAsset?.Id,
             StringComparison.Ordinal) ||
         !Inspector.Editorial.HasUnsavedChanges &&
         CanCommitPendingClipEdit()) &&
        CurrentProject?.Assets.Any(asset =>
            asset.Id.Equals(assetId, StringComparison.Ordinal)) == true;

    private bool CanSetBrowserAssetInclusion(
        string? assetId,
        bool isIncluded) =>
        assetId is not null &&
        !FinalRender.IsRendering &&
        !Inspector.Editorial.HasUnsavedChanges &&
        CanCommitPendingClipEdit() &&
        CurrentProject is { IsFinalized: false } project &&
        project.Assets.FirstOrDefault(asset =>
            asset.Id.Equals(assetId, StringComparison.Ordinal)) is { } asset &&
        Inspector.Preference.CanSetRenderInclusion(
            project,
            asset,
            isIncluded);

    private bool CanQueueBrowserAsset(string? assetId) =>
        CanSelectBrowserAsset(assetId) &&
        assetId is not null &&
        CurrentProject?.Assets.Any(asset =>
            asset.Id.Equals(assetId, StringComparison.Ordinal) &&
            asset.IsIncludedInFinalRender) == true &&
        !FinalRender.QueueItems.Any(item =>
            item.AssetId.Equals(assetId, StringComparison.Ordinal));

    private void QueueBrowserAsset(string? assetId)
    {
        if (!CanQueueBrowserAsset(assetId) || assetId is null)
        {
            return;
        }

        SelectBrowserAsset(assetId);
        if (FinalRender.AddToQueueCommand.CanExecute(null))
        {
            FinalRender.AddToQueueCommand.Execute(null);
        }
    }

    private void SetBrowserAssetInclusion(
        string? assetId,
        bool isIncluded)
    {
        if (!CanSetBrowserAssetInclusion(assetId, isIncluded) ||
            assetId is null)
        {
            return;
        }

        // A card action can target a different clip from the one open in the
        // inspector. Commit that visible draft first so the immutable session
        // rebind below cannot discard its trim or appearance changes.
        if (!TryCommitPendingClipEdit())
        {
            return;
        }
        if (!CanSetBrowserAssetInclusion(assetId, isIncluded) ||
            CurrentProject is not { IsFinalized: false } project)
        {
            return;
        }

        GenerationOutputAsset asset = project.Assets.Single(value =>
            value.Id.Equals(assetId, StringComparison.Ordinal));
        if (!Inspector.Preference.SetRenderInclusion(
                project,
                asset,
                isIncluded))
        {
            return;
        }
        if (!isIncluded)
        {
            FinalRender.RemoveAssetFromQueue(assetId);
        }
    }

    private void SelectBrowserAsset(string? assetId)
    {
        if (!CanSelectBrowserAsset(assetId) || assetId is null)
        {
            return;
        }
        if (assetId.Equals(
                Inspector.SelectedAsset?.Id,
                StringComparison.Ordinal))
        {
            return;
        }
        GenerationOutputAsset? asset = CurrentProject?.Assets.SingleOrDefault(
            value => value.Id.Equals(assetId, StringComparison.Ordinal));
        if (asset is not null)
        {
            _draftSaveCancellation?.Cancel();
            _draftSaveCancellation?.Dispose();
            _draftSaveCancellation = null;
            if (!TryCommitPendingClipEdit())
            {
                return;
            }
            asset = CurrentProject?.Assets.SingleOrDefault(value =>
                value.Id.Equals(assetId, StringComparison.Ordinal));
            Inspector.SelectedAsset = asset;
        }
    }

    private void OutputSession_CurrentChanged(
        object? sender,
        GenerationOutputChangedEventArgs e)
    {
        string? selectedId = Inspector.SelectedAsset?.Id;
        StudioInspectorDraftSnapshot? pendingDrafts =
            _boundProjectId is not null &&
            e.Current?.Id.Equals(
                _boundProjectId,
                StringComparison.Ordinal) == true
                ? Inspector.CapturePendingDrafts()
                : null;
        _surfaceState = e.Current is null
            ? WorkspaceSurfaceState.Empty
            : WorkspaceSurfaceState.ContentReady;

        // Refresh the collection binding before rebinding the selected object.
        // WPF clears a SelectedItem that no longer belongs to a replaced
        // ItemsSource; rebinding first allowed that transient clear to erase
        // the newly rendered asset from the Studio inspector.
        OnPropertyChanged(nameof(CurrentProject));
        Inspector.Bind(HasProject, CurrentProject, selectedId);
        if (pendingDrafts is not null)
        {
            Inspector.RestorePendingDrafts(pendingDrafts);
        }
        _boundProjectId = e.Current?.Id;
        Preview.Bind(HasProject, CurrentProject, Inspector.SelectedAsset);
        if (pendingDrafts?.Clip is not null)
        {
            TimeSpan draftStart = Inspector.Clip.DraftSourceStart;
            TimeSpan draftEnd = Inspector.Clip.DraftSourceEnd >= draftStart
                ? Inspector.Clip.DraftSourceEnd
                : draftStart;
            Preview.UpdateRange(draftStart, draftEnd);
            Preview.UpdateAppearanceDraft(Inspector.Clip.DraftAppearance);
        }
        FinalRender.Bind(CurrentProject);
        HiddenMoments.Bind(CurrentProject);
        RestartPreviewPrewarming();

        foreach (string propertyName in new[]
        {
            nameof(SurfaceState),
            nameof(IsEmpty),
            nameof(IsContentReady),
            nameof(IsLoading),
            nameof(IsError),
            nameof(IsUnavailable),
            nameof(ShouldShowPlaceholder),
            nameof(HasProject),
            nameof(IsProjectMissing),
            nameof(CanUseProjectCommands),
            nameof(ProjectName),
            nameof(SaveStateText),
            nameof(ProjectPromptDescription),
            nameof(StatusText),
            nameof(SurfaceSummary),
            nameof(SurfaceSuggestion),
            nameof(BrowserPreviewItems),
            nameof(SelectedAsset),
            nameof(SelectedClipDurationText),
            nameof(IsProjectFinalized),
            nameof(IsProjectDraft),
        })
        {
            OnPropertyChanged(propertyName);
        }

        _selectBrowserAssetCommand.RaiseCanExecuteChanged();
        _queueBrowserAssetCommand.RaiseCanExecuteChanged();
        _removeBrowserAssetCommand.RaiseCanExecuteChanged();
        _restoreBrowserAssetCommand.RaiseCanExecuteChanged();
        _reviewRenderRequirementsCommand.RaiseCanExecuteChanged();
        ScheduleDurableState();
    }

    private void RestartPreviewPrewarming()
    {
        _previewPrewarmCancellation?.Cancel();
        _previewPrewarmCancellation?.Dispose();
        _previewPrewarmCancellation = null;
        if (_previewPrewarmer is null ||
            CurrentProject is not { IsFinalized: false } project)
        {
            return;
        }
        _previewPrewarmCancellation = new CancellationTokenSource();
        _ = PrewarmSafelyAsync(
            project,
            Inspector.SelectedAsset?.Id,
            _previewPrewarmCancellation.Token);
    }

    private void ReviewRenderRequirements()
    {
        if (FinalRender.NeedsIncludedCandidate)
        {
            SelectedTool = StudioToolSection.MomentsClips;
            Inspector.SelectedInspector = StudioInspectorSection.Clip;
            return;
        }
        if (FinalRender.NeedsValidClipEdit)
        {
            Inspector.SelectedInspector = StudioInspectorSection.Clip;
            return;
        }
        if (FinalRender.NeedsMetadataSave)
        {
            Inspector.SelectedInspector = StudioInspectorSection.Metadata;
        }
    }

    private async Task PrewarmSafelyAsync(
        GenerationOutputProject project,
        string? priorityAssetId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _previewPrewarmer!.PrewarmAsync(
                project,
                priorityAssetId,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
            // Foreground preview reports actionable errors. Prewarming is
            // opportunistic and must never block Studio editing.
        }
        catch (InvalidOperationException)
        {
            // The project can change while the optional prewarm is queued.
            // Foreground preview owns any user-visible retry or error.
        }
    }

    private void Inspector_SelectedAssetChanged(
        object? sender,
        EventArgs e)
    {
        Preview.Bind(
            HasProject,
            CurrentProject,
            Inspector.SelectedAsset);
        OnPropertyChanged(nameof(SelectedAsset));
        OnPropertyChanged(nameof(BrowserPreviewItems));
        OnPropertyChanged(nameof(SelectedClipDurationText));
        FinalRender.RefreshReadiness();
        ScheduleDurableState();
    }

    private void Editorial_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Inspector.Editorial.HasUnsavedChanges) or
            nameof(Inspector.Editorial.Title) or
            nameof(Inspector.Editorial.Description) or
            nameof(Inspector.Editorial.Tags) or
            nameof(Inspector.Editorial.IsGenerating))
        {
            FinalRender.RefreshReadiness();
            _selectBrowserAssetCommand.RaiseCanExecuteChanged();
            _queueBrowserAssetCommand.RaiseCanExecuteChanged();
            _removeBrowserAssetCommand.RaiseCanExecuteChanged();
            _restoreBrowserAssetCommand.RaiseCanExecuteChanged();
            _reviewRenderRequirementsCommand.RaiseCanExecuteChanged();
        }
    }

    private void HiddenMoments_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HiddenMoments.IsPreparingAcceptedMoment))
        {
            FinalRender.RefreshReadiness();
        }
    }

    private void FinalRender_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FinalRender.QueueItems) or
            nameof(FinalRender.QueuedClipCount))
        {
            OnPropertyChanged(nameof(BrowserPreviewItems));
            ScheduleDurableState();
        }
        if (e.PropertyName is nameof(FinalRender.NeedsRenderAttention))
        {
            _reviewRenderRequirementsCommand.RaiseCanExecuteChanged();
        }
        if (e.PropertyName is nameof(FinalRender.IsRendering))
        {
            HiddenMoments.SetProjectMutationBlocked(FinalRender.IsRendering);
            if (FinalRender.IsRendering)
            {
                _draftSaveCancellation?.Cancel();
                _draftSaveCancellation?.Dispose();
                _draftSaveCancellation = null;
            }
            _selectBrowserAssetCommand.RaiseCanExecuteChanged();
            _queueBrowserAssetCommand.RaiseCanExecuteChanged();
            _removeBrowserAssetCommand.RaiseCanExecuteChanged();
            _restoreBrowserAssetCommand.RaiseCanExecuteChanged();
            _reviewRenderRequirementsCommand.RaiseCanExecuteChanged();
            ScheduleDurableState();
        }
    }

    private void ScheduleDurableState()
    {
        if (_projectPersistence is null ||
            CurrentProject is not { } project ||
            _isDisposed)
        {
            return;
        }
        TimeSpan? previewPosition = double.IsFinite(
                Preview.PreviewPositionSeconds) &&
            Preview.PreviewPositionSeconds >= 0
                ? TimeSpan.FromSeconds(Preview.PreviewPositionSeconds)
                : null;
        _projectPersistence.ScheduleSave(
            project,
            FinalRender.CaptureRecoveryState(
                Inspector.SelectedAsset?.Id,
                previewPosition));
    }

    private void ProjectPersistence_PersistenceStateChanged(
        object? sender,
        EventArgs e)
    {
        if (_notificationContext is { } context &&
            !ReferenceEquals(SynchronizationContext.Current, context))
        {
            context.Post(
                static state =>
                    ((StudioViewModel)state!).NotifyPersistenceState(),
                this);
            return;
        }

        NotifyPersistenceState();
    }

    private void NotifyPersistenceState()
    {
        OnPropertyChanged(nameof(SaveStateText));
        OnPropertyChanged(nameof(StatusText));
    }

    private void RestoreDurableRecovery(
        StudioProjectRecoveryState recovery)
    {
        FinalRender.RestoreRecoveryState(recovery);
        if (recovery.SelectedAssetId is { } selectedId)
        {
            GenerationOutputAsset? selected = CurrentProject?.Assets
                .SingleOrDefault(asset => asset.Id.Equals(
                    selectedId,
                    StringComparison.Ordinal));
            if (selected is not null)
            {
                Inspector.SelectedAsset = selected;
            }
        }
        if (recovery.PreviewPosition is { } previewPosition)
        {
            Preview.PreviewPositionSeconds = previewPosition.TotalSeconds;
        }
    }

    private void Clip_DraftRangeChanged(
        object? sender,
        EventArgs e)
    {
        TimeSpan start = Inspector.Clip.DraftSourceStart;
        TimeSpan end = Inspector.Clip.DraftSourceEnd >= start
            ? Inspector.Clip.DraftSourceEnd
            : start;
        Preview.UpdateRange(start, end);
        RefreshClipDraftCommandState();
    }

    private void Clip_DraftAppearanceChanged(
        object? sender,
        EventArgs e)
    {
        Preview.UpdateAppearanceDraft(Inspector.Clip.DraftAppearance);
        RefreshClipDraftCommandState();
        _draftSaveCancellation?.Cancel();
        _draftSaveCancellation?.Dispose();
        _draftSaveCancellation = new CancellationTokenSource();
        _ = SaveAppearanceDraftAfterDelayAsync(
            _draftSaveCancellation.Token);
    }

    private async Task SaveAppearanceDraftAfterDelayAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                Inspector.Clip.ApplyPendingEdit();
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void Preview_GraphicFileDropped(
        object? sender,
        StudioGraphicFileDroppedEventArgs e)
    {
        if (FinalRender.IsRendering)
        {
            return;
        }
        if (Inspector.Graphics.TryAddFile(e.ImageFullPath))
        {
            SelectedTool = StudioToolSection.StickersGraphics;
            Inspector.SelectedInspector = StudioInspectorSection.Graphics;
        }
    }

    private void HiddenMoments_MomentAccepted(
        object? sender,
        StudioHiddenMomentAcceptedEventArgs e)
    {
        GenerationOutputAsset? accepted = CurrentProject?.Assets
            .SingleOrDefault(asset => asset.Id.Equals(
                e.CandidateId,
                StringComparison.Ordinal));
        if (accepted is not null)
        {
            Inspector.SelectedAsset = accepted;
        }
    }

    private bool CanCommitPendingClipEdit() =>
        !Inspector.Clip.HasPendingEdit ||
        Inspector.Clip.IsBoundaryDraftValid;

    private bool TryCommitPendingClipEdit() =>
        !Inspector.Clip.HasPendingEdit ||
        Inspector.Clip.IsBoundaryDraftValid &&
        Inspector.Clip.ApplyPendingEdit();

    private void RefreshClipDraftCommandState()
    {
        FinalRender.RefreshReadiness();
        _selectBrowserAssetCommand.RaiseCanExecuteChanged();
        _queueBrowserAssetCommand.RaiseCanExecuteChanged();
        _removeBrowserAssetCommand.RaiseCanExecuteChanged();
        _restoreBrowserAssetCommand.RaiseCanExecuteChanged();
        _reviewRenderRequirementsCommand.RaiseCanExecuteChanged();
    }

}
