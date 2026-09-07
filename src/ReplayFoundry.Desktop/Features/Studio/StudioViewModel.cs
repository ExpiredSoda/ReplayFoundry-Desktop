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
using ReplayFoundry.Desktop.Features.Generate.Editorial.GameKnowledge;
using ReplayFoundry.Desktop.Features.Studio.HiddenMoments;
using ReplayFoundry.Desktop.Features.Studio.Projects;
using ReplayFoundry.Desktop.Features.Research;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Settings;

namespace ReplayFoundry.Desktop.Features.Studio;

public sealed class StudioViewModel : ObservableObject, IWorkspaceChromeSource,
    IStudioProjectSwitchService, IStudioProjectSwitchContext,
    IApplicationStopParticipant, IDisposable
{
    private readonly IGenerationOutputSession? _outputSession;
    private readonly IGenerationOutputSink? _outputSink;
    private readonly StudioPreviewPrewarmCoordinator _previewPrewarming;
    private readonly StudioProjectSwitchCoordinator _projectSwitcher;
    private readonly IStudioProjectPersistenceCoordinator? _projectPersistence;
    private readonly SynchronizationContext? _notificationContext;
    private readonly DelegateCommand<string> _selectBrowserAssetCommand;
    private readonly DelegateCommand<string> _queueBrowserAssetCommand;
    private readonly DelegateCommand<string> _removeBrowserAssetCommand;
    private readonly DelegateCommand<string> _restoreBrowserAssetCommand;
    private readonly DelegateCommand _reviewRenderRequirementsCommand;
    private CancellationTokenSource? _draftSaveCancellation;
    private string? _boundProjectId;
    private WorkspaceSurfaceState _surfaceState;
    private bool _isStopping;
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
            null,
        IGenerationGameKnowledgeService? gameKnowledge = null,
        ICorrectedCaptionAlignmentService? captionAlignment = null,
        StudioCaptionLanguageModel? captionLanguageCapabilities = null,
        StudioTimelineFilmstrip? timelineFilmstrip = null)
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
            editorialPreferenceRecorder,
            gameKnowledge, captionAlignment, captionLanguageCapabilities, timelineFilmstrip)
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
            null,
        IGenerationGameKnowledgeService? gameKnowledge = null,
        ICorrectedCaptionAlignmentService? captionAlignment = null,
        StudioCaptionLanguageModel? captionLanguageCapabilities = null,
        StudioTimelineFilmstrip? timelineFilmstrip = null)
    {
        _outputSession = outputSession;
        _outputSink = outputSession as IGenerationOutputSink;
        _previewPrewarming = new StudioPreviewPrewarmCoordinator(
            previewPrewarmer);
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
            editorialPreferenceRecorder,
            gameKnowledge);
        Inspector.Caption.ConfigurePreparation(captionPreparation);
        Inspector.Caption.ConfigureAlignment(captionAlignment);
        Inspector.Caption.ConfigureLanguageCapabilities(captionLanguageCapabilities);
        Inspector.Output.MixAudition.PlaybackStarting += (_, _) =>
        {
            if (Preview.IsPreviewPlaying) Preview.PlayCommand.Execute(null);
            Inspector.Caption.AudioAudition.Stop();
        };
        Inspector.SelectedAssetChanged += Inspector_SelectedAssetChanged;
        Inspector.Clip.DraftRangeChanged += Clip_DraftRangeChanged;
        Inspector.Clip.DraftAppearanceChanged += Clip_DraftAppearanceChanged;
        Inspector.Clip.Effects.ComparisonChanged += Clip_ComparisonChanged;
        FinalRender = new StudioFinalRenderViewModel(
            outputEditor,
            projectRenderingService,
            Inspector.Clip.ApplyPendingEdit,
            Inspector.SetHostBusy,
            hasPendingEdit: () => Inspector.Clip.HasPendingEdit,
            isPendingEditValid: () => Inspector.Clip.IsBoundaryDraftValid,
            hasUnsavedMetadata: () =>
                Inspector.Editorial.HasUnsavedChanges,
            isPendingMetadataValid: CanCommitPendingMetadataEdit,
            commitPendingMetadata: TryCommitPendingMetadataEdit,
            hasActiveProjectMutation: () =>
                Inspector.Editorial.IsGenerating ||
                HiddenMoments.HasUnfinishedQueueItems,
            selectedAsset: () => Inspector.SelectedAsset,
            libraryCatalog: libraryCatalog);
        ManualClips = new StudioManualClipViewModel(outputEditor as IGenerationManualClipEditor,
            previewMediaService, () => !FinalRender.IsRendering && !Inspector.Editorial.IsGenerating &&
                !Inspector.Editorial.HasUnsavedChanges && !Inspector.Clip.HasPendingEdit &&
                !HiddenMoments.HasUnfinishedQueueItems, timelineFilmstrip);
        ManualClips.ClipAdded += ManualClips_ClipAdded;
        _projectSwitcher = new StudioProjectSwitchCoordinator(this);
        Inspector.Editorial.PropertyChanged += Editorial_PropertyChanged;
        HiddenMoments.PropertyChanged += HiddenMoments_PropertyChanged;
        FinalRender.PropertyChanged += FinalRender_PropertyChanged;
        Preview.PropertyChanged += Preview_PropertyChanged;

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
        Preview.Bind(HasProject, CurrentProject, Inspector.Clip.Effects.ForPreview(Inspector.SelectedAsset));
        FinalRender.Bind(CurrentProject);
        HiddenMoments.Bind(CurrentProject);
        ManualClips.Bind(CurrentProject);
        WarmFirstAlternateWhenPreviewSettles();
        RestartPreviewPrewarming();
    }

    internal StudioViewModel(WorkspaceSurfaceState surfaceState)
        : this(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, surfaceState)
    {
    }


    public StudioInspectorViewModel Inspector { get; }
    public StudioPreviewViewModel Preview { get; }
    public StudioFinalRenderViewModel FinalRender { get; }
    public StudioHiddenMomentsViewModel HiddenMoments { get; }
    public StudioManualClipViewModel ManualClips { get; }

    public Task<StudioProjectSwitchResult> TrySwitchProjectAsync(
        GenerationOutputProject project,
        CancellationToken cancellationToken = default) =>
        _projectSwitcher.TrySwitchAsync(project, cancellationToken);
    public IReadOnlyList<StudioBrowserPreviewItem> BrowserPreviewItems =>
        StudioSurfaceCatalog.BuildBrowserPreviewItems(
            CurrentProject,
            Inspector.SelectedAsset?.Id,
            FinalRender.QueueItems
                .Select(static item => item.AssetId)
                .ToHashSet(StringComparer.Ordinal));
    public GenerationOutputAsset? SelectedAsset => Inspector.SelectedAsset;
    public WorkspaceSurfaceState SurfaceState => _surfaceState;
    public string WorkspaceEyebrow => "STUDIO";
    public string WorkspaceTitle => "Build the final cut";
    public string WorkspaceDescription =>
        "Choose clips, adjust the start and end, style the video, then save finished files to Library.";
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
    public string ProjectName =>
        StudioProjectStatusPresentation.ProjectName(CurrentProject, HasProject);
    public string SaveStateText =>
        StudioProjectStatusPresentation.SaveStateText(CurrentProject, HasProject, _projectPersistence?.LastError);
    public string ProjectPromptDescription => HasProject
        ? IsProjectFinalized
            ? "The finalized files are available in Library."
            : "Choose a clip, make your changes, add it to the queue, and save a Library copy."
        : "Generate a clip first, then return here to edit it.";
    public string StatusText =>
        StudioProjectStatusPresentation.StatusText(CurrentProject, SurfaceState, _projectPersistence?.LastError);
    public string ErrorSummary => "Studio could not load a project.";
    public string SurfaceSummary => IsEmpty ? "Studio is waiting for a project." : ErrorSummary;
    public string SurfaceSuggestion => IsEmpty
        ? "Start in Generate, then return here when a project is ready to shape."
        : "Studio cannot open this project yet. Return to Generate and finish preparing the source video.";
    public string SelectedClipDurationText => SelectedAsset is null
        ? "No clip selected"
        : StudioTimeFormatter.FormatDuration(SelectedAsset.Duration);

    public ICommand SelectBrowserAssetCommand => _selectBrowserAssetCommand;
    public ICommand QueueBrowserAssetCommand => _queueBrowserAssetCommand;
    public ICommand RemoveBrowserAssetCommand => _removeBrowserAssetCommand;
    public ICommand RestoreBrowserAssetCommand => _restoreBrowserAssetCommand;
    public ICommand ReviewRenderRequirementsCommand =>
        _reviewRenderRequirementsCommand;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _previewPrewarming.Dispose();
        _draftSaveCancellation?.Cancel();
        _draftSaveCancellation?.Dispose();
        _draftSaveCancellation = null;
        Inspector.SelectedAssetChanged -= Inspector_SelectedAssetChanged;
        Inspector.Clip.DraftRangeChanged -= Clip_DraftRangeChanged;
        Inspector.Clip.DraftAppearanceChanged -= Clip_DraftAppearanceChanged;
        Inspector.Clip.Effects.ComparisonChanged -= Clip_ComparisonChanged;
        Inspector.Editorial.PropertyChanged -= Editorial_PropertyChanged;
        HiddenMoments.PropertyChanged -= HiddenMoments_PropertyChanged;
        FinalRender.PropertyChanged -= FinalRender_PropertyChanged;
        Preview.PropertyChanged -= Preview_PropertyChanged;
        FinalRender.Dispose();
        HiddenMoments.MomentAccepted -= HiddenMoments_MomentAccepted;
        HiddenMoments.Dispose();
        ManualClips.ClipAdded -= ManualClips_ClipAdded;
        ManualClips.Dispose();
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

    async Task IApplicationStopParticipant.StopAsync(
        CancellationToken cancellationToken)
    {
        _isStopping = true;
        if (_isDisposed)
        {
            return;
        }

        _draftSaveCancellation?.Cancel();
        ScheduleDurableState();
        await Task.WhenAll(
                FinalRender.StopAsync(cancellationToken),
                Inspector.StopAsync(cancellationToken),
                HiddenMoments.StopAsync(cancellationToken),
                ManualClips.StopAsync(cancellationToken),
                Preview.StopAsync(cancellationToken))
            .ConfigureAwait(false);
    }

    private bool CanSelectBrowserAsset(string? assetId) =>
        assetId is not null &&
        !FinalRender.IsRendering &&
        (assetId.Equals(
             Inspector.SelectedAsset?.Id,
             StringComparison.Ordinal) ||
         CanCommitPendingMetadataEdit() &&
         CanCommitPendingClipEdit()) &&
        CurrentProject?.Assets.Any(asset =>
            asset.Id.Equals(assetId, StringComparison.Ordinal)) == true;

    private bool CanSetBrowserAssetInclusion(
        string? assetId,
        bool isIncluded) =>
        assetId is not null &&
        !FinalRender.IsRendering &&
        CanCommitPendingMetadataEdit() &&
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
        FinalRender.CanAttemptQueueAsset(assetId);

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
        if (FinalRender.NeedsValidClipEdit)
        {
            Inspector.SelectedInspector = StudioInspectorSection.Clip;
        }
        else if (FinalRender.NeedsMetadataFix)
        {
            Inspector.SelectedInspector = StudioInspectorSection.Metadata;
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
        if (!TryCommitPendingMetadataEdit())
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
            if (!TryCommitPendingMetadataEdit())
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
        Preview.Bind(HasProject, CurrentProject, Inspector.Clip.Effects.ForPreview(Inspector.SelectedAsset));
        if (pendingDrafts?.Clip is not null)
        {
            TimeSpan draftStart = Inspector.Clip.DraftSourceStart;
            TimeSpan draftEnd = Inspector.Clip.DraftSourceEnd >= draftStart
                ? Inspector.Clip.DraftSourceEnd
                : draftStart;
            Preview.UpdateRange(draftStart, draftEnd);
            Preview.UpdateAppearanceDraft(Inspector.Clip.Effects.PreviewAppearance);
        }
        FinalRender.Bind(CurrentProject);
        HiddenMoments.Bind(CurrentProject);
        ManualClips.Bind(CurrentProject);
        WarmFirstAlternateWhenPreviewSettles();
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

    private void RestartPreviewPrewarming() =>
        _previewPrewarming.Restart(
            CurrentProject,
            Inspector.SelectedAsset?.Id);

    private void ReviewRenderRequirements()
    {
        if (FinalRender.NeedsIncludedCandidate)
        {
            Inspector.SelectedInspector = StudioInspectorSection.Clip;
            return;
        }
        if (FinalRender.NeedsValidClipEdit)
        {
            Inspector.SelectedInspector = StudioInspectorSection.Clip;
            return;
        }
        if (FinalRender.NeedsMetadataFix ||
            FinalRender.NeedsMetadataSave)
        {
            Inspector.SelectedInspector = StudioInspectorSection.Metadata;
        }
    }

    private void Inspector_SelectedAssetChanged(
        object? sender,
        EventArgs e)
    {
        Preview.Bind(
            HasProject,
            CurrentProject,
            Inspector.Clip.Effects.ForPreview(Inspector.SelectedAsset));
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
        ManualClips.RefreshAvailability();
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
        ManualClips.RefreshAvailability();
        if (e.PropertyName is
            nameof(HiddenMoments.IsPreparingAcceptedMoment) or
            nameof(HiddenMoments.HasUnfinishedQueueItems))
        {
            FinalRender.RefreshReadiness();
        }
    }

    private void Preview_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Preview.IsPreviewSynchronized))
        {
            WarmFirstAlternateWhenPreviewSettles();
        }
    }

    private void WarmFirstAlternateWhenPreviewSettles()
    {
        if (!_isDisposed && !_isStopping && Preview.IsPreviewSynchronized)
        {
            HiddenMoments.WarmFirstAlternatePreview();
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
            ManualClips.RefreshAvailability();
            if (FinalRender.IsRendering)
            {
                _previewPrewarming.Suspend();
                _draftSaveCancellation?.Cancel();
                _draftSaveCancellation?.Dispose();
                _draftSaveCancellation = null;
            }
            else
            {
                RestartPreviewPrewarming();
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

    private void Clip_ComparisonChanged(object? sender, EventArgs e)
    {
        Preview.Bind(HasProject, CurrentProject, Inspector.Clip.Effects.ForPreview(Inspector.SelectedAsset));
        Preview.UpdateAppearanceDraft(Inspector.Clip.Effects.PreviewAppearance);
        Clip_DraftRangeChanged(sender, e);
    }

    private void Clip_DraftAppearanceChanged(
        object? sender,
        EventArgs e)
    {
        Preview.UpdateAppearanceDraft(Inspector.Clip.Effects.PreviewAppearance);
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
            Inspector.SelectedInspector = StudioInspectorSection.Graphics;
        }
    }

    private void HiddenMoments_MomentAccepted(
        object? sender,
        StudioHiddenMomentAcceptedEventArgs e)
    {
        if (!e.ShouldFocus)
        {
            return;
        }
        GenerationOutputAsset? accepted = CurrentProject?.Assets
            .SingleOrDefault(asset => asset.Id.Equals(
                e.CandidateId,
                StringComparison.Ordinal));
        if (accepted is not null)
        {
            Inspector.SelectedAsset = accepted;
        }
    }

    private void ManualClips_ClipAdded(object? sender, EventArgs e)
    {
        Inspector.SelectedAsset = CurrentProject?.Assets.LastOrDefault();
        Inspector.SelectedInspector = StudioInspectorSection.Metadata;
        if (Inspector.Editorial.RerollCommand.CanExecute(null))
            Inspector.Editorial.RerollCommand.Execute(null);
    }

    private bool CanCommitPendingClipEdit() =>
        !Inspector.Clip.HasPendingEdit ||
        Inspector.Clip.IsBoundaryDraftValid;

    private bool TryCommitPendingClipEdit() =>
        !Inspector.Clip.HasPendingEdit ||
        Inspector.Clip.IsBoundaryDraftValid &&
        Inspector.Clip.ApplyPendingEdit();

    private bool CanCommitPendingMetadataEdit()
    {
        StudioPendingEditorialDraft? draft =
            Inspector.Editorial.CapturePendingDraft();
        return draft is null ||
               Inspector.Editorial.CanPersistPendingDraft(draft);
    }

    private bool TryCommitPendingMetadataEdit()
    {
        StudioPendingEditorialDraft? draft =
            Inspector.Editorial.CapturePendingDraft();
        return draft is null ||
               Inspector.Editorial.TryPersistPendingDraft(draft);
    }

    bool IStudioProjectSwitchContext.IsDisposed =>
        _isDisposed || _isStopping;
    IGenerationOutputSink? IStudioProjectSwitchContext.OutputSink =>
        _outputSink;
    IStudioProjectPersistenceCoordinator?
        IStudioProjectSwitchContext.ProjectPersistence =>
            _projectPersistence;
    bool IStudioProjectSwitchContext.CanCommitPendingClipEdit() =>
        CanCommitPendingClipEdit();
    bool IStudioProjectSwitchContext.TryCommitPendingClipEdit() =>
        TryCommitPendingClipEdit();
    void IStudioProjectSwitchContext.CancelDelayedDraftSave()
    {
        _draftSaveCancellation?.Cancel();
        _draftSaveCancellation?.Dispose();
        _draftSaveCancellation = null;
    }
    void IStudioProjectSwitchContext.RestoreDurableRecovery(
        StudioProjectRecoveryState recovery) =>
        RestoreDurableRecovery(recovery);

    private void RefreshClipDraftCommandState()
    {
        ManualClips.RefreshAvailability();
        FinalRender.RefreshReadiness();
        _selectBrowserAssetCommand.RaiseCanExecuteChanged();
        _queueBrowserAssetCommand.RaiseCanExecuteChanged();
        _removeBrowserAssetCommand.RaiseCanExecuteChanged();
        _restoreBrowserAssetCommand.RaiseCanExecuteChanged();
        _reviewRenderRequirementsCommand.RaiseCanExecuteChanged();
    }

}
