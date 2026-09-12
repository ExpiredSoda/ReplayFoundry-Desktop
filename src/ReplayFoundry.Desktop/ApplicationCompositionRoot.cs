using ReplayFoundry.Desktop.Composition;
using ReplayFoundry.Desktop.Features.Diagnostics;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Studio.Projects;
using ReplayFoundry.Desktop.Platform.Dialogs;
using ReplayFoundry.Desktop.Platform.RuntimePacks;
using ReplayFoundry.Desktop.Platform.Updates;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Shell;
using ReplayFoundry.Desktop.Shell.Navigation;

namespace ReplayFoundry.Desktop;

internal sealed class ApplicationComposition : IDisposable
{
    private readonly GenerationLibraryCatalog _libraryCatalog;
    private readonly IDisposable? _recentGenerationProjects;
    private readonly IDisposable? _audioAuditionService;
    private readonly IStudioProjectPersistenceCoordinator?
        _studioProjectPersistence;
    private readonly IDisposable? _userReportTransport;
    private readonly IDisposable? _ownedEditorialMetadataProvider;
    private readonly IDisposable? _evidenceAnalysisCoordinator;
    private readonly IDisposable? _youtubePublishing;
    private readonly IDisposable? _speechActivity;
    private readonly IDisposable? _tasteLearning;
    private readonly IDisposable? _tasteInteractions;
    private readonly IDisposable? _generationFailureReporting;
    private readonly object _lifecycleSync = new();
    private Task? _stopTask;
    private bool _disposeRequested;
    private bool _resourcesDisposed;

    public ApplicationComposition(
        MainWindowViewModel mainWindowViewModel,
        GenerationLibraryCatalog libraryCatalog,
        UserReportCoordinator userReports,
        IDisposable? recentGenerationProjects = null,
        IDisposable? audioAuditionService = null,
        IStudioProjectPersistenceCoordinator? studioProjectPersistence = null,
        IDisposable? userReportTransport = null,
        IDisposable? ownedEditorialMetadataProvider = null,
        IDisposable? evidenceAnalysisCoordinator = null,
        IDisposable? youtubePublishing = null,
        IDisposable? speechActivity = null,
        IDisposable? tasteLearning = null,
        IDisposable? tasteInteractions = null,
        IDisposable? generationFailureReporting = null)
    {
        MainWindowViewModel = mainWindowViewModel ??
            throw new ArgumentNullException(nameof(mainWindowViewModel));
        _libraryCatalog = libraryCatalog ??
            throw new ArgumentNullException(nameof(libraryCatalog));
        UserReports = userReports ??
            throw new ArgumentNullException(nameof(userReports));
        _recentGenerationProjects = recentGenerationProjects;
        _audioAuditionService = audioAuditionService;
        _studioProjectPersistence = studioProjectPersistence;
        _userReportTransport = userReportTransport;
        _ownedEditorialMetadataProvider = ownedEditorialMetadataProvider;
        _evidenceAnalysisCoordinator = evidenceAnalysisCoordinator;
        _youtubePublishing = youtubePublishing;
        _speechActivity = speechActivity;
        _tasteLearning = tasteLearning; _tasteInteractions = tasteInteractions;
        _generationFailureReporting = generationFailureReporting;
    }

    public MainWindowViewModel MainWindowViewModel { get; }
    public UserReportCoordinator UserReports { get; }
    public WinSparkleUpdateService? Updates { get; init; }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task stop;
        lock (_lifecycleSync)
        {
            if (_disposeRequested)
            {
                return Task.CompletedTask;
            }

            stop = _stopTask ??= StopCoreAsync();
        }

        return cancellationToken.CanBeCanceled
            ? stop.WaitAsync(cancellationToken)
            : stop;
    }

    public void Dispose()
    {
        lock (_lifecycleSync)
        {
            if (_disposeRequested)
            {
                return;
            }

            _disposeRequested = true;
        }

        // StopAsync is the graceful application-shutdown boundary. Dispose is
        // the synchronous ownership boundary and must not return while owned
        // providers remain undisposed. The normal App path awaits StopAsync
        // before reaching this forced, idempotent cleanup.
        DisposeResources();
    }

    private async Task StopCoreAsync()
    {
        await MainWindowViewModel.StopAsync(CancellationToken.None)
            .ConfigureAwait(false);
        if (_studioProjectPersistence is not null)
        {
            await _studioProjectPersistence.StopAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private void DisposeResources()
    {
        lock (_lifecycleSync)
        {
            if (_resourcesDisposed)
            {
                return;
            }
            _resourcesDisposed = true;
        }

        _generationFailureReporting?.Dispose();
        Updates?.Dispose();
        _tasteInteractions?.Dispose();
        _tasteLearning?.Dispose();
        MainWindowViewModel.Dispose();
        _studioProjectPersistence?.Dispose();
        _audioAuditionService?.Dispose();
        _recentGenerationProjects?.Dispose();
        _libraryCatalog.Dispose();
        _youtubePublishing?.Dispose();
        _evidenceAnalysisCoordinator?.Dispose();
        _speechActivity?.Dispose();
        _ownedEditorialMetadataProvider?.Dispose();
        _userReportTransport?.Dispose();
    }
}

internal static class ApplicationCompositionRoot
{
    public static ApplicationComposition Create(string? debugProjectPath = null)
    {
        var localDataMaintenance = LocalDataComposition.Initialize();
        GenerationSourceSelectionPlatform sourceSelectionPlatform =
            GenerationSourceComposition.CreateSelectionPlatform();
        ReplayFoundryRuntimeEnvironment runtime =
            ReplayFoundryRuntimeEnvironment.Current;
        ApplicationPreferenceServices preferences =
            ApplicationPreferenceComposition.Create();
        var folderLauncher = new WindowsLocalFolderLauncher();
        LocalSpeechServices speech =
            LocalIntelligenceComposition.CreateSpeechServices(runtime);
        EditorialFeedbackServices feedback =
            EditorialFeedbackComposition.Create(speech.SpeechActivity);
        LocalVisualReviewServices visualReview =
            LocalIntelligenceComposition.CreateVisualReviewServices(
                runtime,
                speech);
        GenerationExperienceServices experience =
            GenerationExperienceComposition.Create(visualReview);
        GenerationSourceAnalysisServices sourceAnalysis =
            GenerationSourceComposition.CreateAnalysisServices();
        GenerationWorkspaceServices workspace =
            GenerationWorkspaceComposition.Create();
        EditorialServices editorial = EditorialComposition.Create(
            new EditorialCompositionDependencies(
                visualReview,
                experience,
                workspace));
        PrimaryFeatureViewModels primaryFeatures =
            PrimaryFeatureComposition.Create(
                new PrimaryFeatureDependencies(
                    preferences,
                    sourceSelectionPlatform,
                    speech,
                    feedback,
                    visualReview,
                    experience,
                    sourceAnalysis,
                    workspace,
                    editorial,
                    folderLauncher));
        var tasteInteractions = feedback.TasteLearning is { } learning
            ? new Features.Personalization.TasteInteractionRecorder(learning, workspace.OutputSession, workspace.LibraryCatalog,
                primaryFeatures.Studio, feedback.CandidateDecisions) : null;
        PublishFeatureServices publish = PublishFeatureComposition.Create(
            new PublishFeatureDependencies(
                preferences,
                workspace,
                editorial,
                experience,
                tasteInteractions));
        if (publish.Publishing is { } publishing) tasteInteractions?.ImportPublishHistory(publishing.History);
        DiagnosticReportingServices diagnostics =
            DiagnosticReportingComposition.Create();
        var settings = SettingsFeatureComposition.Create(
            new SettingsFeatureDependencies(
                runtime,
                localDataMaintenance,
                preferences,
                feedback,
                editorial,
                publish,
                diagnostics,
                folderLauncher));
        var mainWindowViewModel = new MainWindowViewModel(
            primaryFeatures.Generate,
            primaryFeatures.Studio,
            primaryFeatures.Library,
            publish.ViewModel,
            settings,
            experience.GameKnowledgePermissionStatus,
            visualReview.RuntimeCapabilities);

#if DEBUG
        if (debugProjectPath is not null)
        {
            var file = new System.IO.FileInfo(System.IO.Path.GetFullPath(debugProjectPath));
            if (!file.Exists || file.Length > 64 * 1024 * 1024)
                throw new System.IO.InvalidDataException("Debug project must be an existing Studio JSON file under 64 MiB.");
            var jsonOptions = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                MaxDepth = 128,
            };
            jsonOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            var document = System.Text.Json.JsonSerializer.Deserialize<StudioProjectDocument>(
                System.IO.File.ReadAllText(file.FullName), jsonOptions)
                ?? throw new System.IO.InvalidDataException("Debug project is empty.");
            if (document.SchemaVersion != StudioProjectDocument.CurrentSchemaVersion)
                throw new System.IO.InvalidDataException("Debug project requires the current Studio schema.");
            workspace.OutputSession.Publish(StudioProjectDocumentMapper.Restore(document));
            mainWindowViewModel.NavigateCommand.Execute(ShellDestination.Studio);
        }
#endif
        return new ApplicationComposition(
            mainWindowViewModel,
            workspace.LibraryCatalog,
            diagnostics.Coordinator,
            workspace.RecentProjects,
            experience.AudioAudition,
            workspace.StudioProjectPersistence,
            diagnostics.Transport as IDisposable,
            editorial.OwnedAiProvider,
            sourceAnalysis.EvidenceCoordinator,
            publish.Publishing as IDisposable,
            speech.SpeechActivity as IDisposable,
            feedback.TasteLearning,
            tasteInteractions,
            new GenerationFailureReporting(primaryFeatures.Generate.GenerationProgress, diagnostics.Coordinator))
        {
            Updates = CreateUpdates(),
        };

        WinSparkleUpdateService CreateUpdates()
        {
            var studio = primaryFeatures.Studio;
            var updates = new WinSparkleUpdateService(() => ApplicationUpdateReadiness.GetBlockReason(new(
                Generation: primaryFeatures.Generate.WorkflowState is GenerateWorkflowState.PreparingSources or GenerateWorkflowState.AnalyzingEvidence or GenerateWorkflowState.Generating,
                Rendering: studio.FinalRender.IsRendering || studio.HiddenMoments.HasUnfinishedQueueItems,
                Publishing: publish.ViewModel.IsBusy || studio.Inspector.Editorial.IsGenerating,
                Learning: settings.Learning.IsWorking,
                UnsavedEdits: studio.Inspector.Editorial.HasUnsavedChanges || studio.Inspector.Editorial.HasUnsavedProfileChanges || studio.Inspector.Clip.HasPendingEdit || settings.CreatorVoice.HasUnsavedChanges,
                OpenWorkflow: primaryFeatures.Generate.WorkflowState == GenerateWorkflowState.ReviewingComposition || studio.ManualClips.IsOpen)));
            settings.Updates.Attach(updates);
            return updates;
        }
    }
}
