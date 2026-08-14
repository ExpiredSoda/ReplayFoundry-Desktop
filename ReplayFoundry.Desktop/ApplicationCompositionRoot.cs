using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Diagnostics;
using ReplayFoundry.Desktop.Features.Generate;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.Editorial.GameKnowledge;
using ReplayFoundry.Desktop.Features.Generate.Editorial.VisualText;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Generate.RecentProjects;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Publish;
using ReplayFoundry.Desktop.Features.Publish.Editorial;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Features.Research;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Editorial;
using ReplayFoundry.Desktop.Features.Studio.HiddenMoments;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Features.Studio.Projects;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial.Preferences;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;
using ReplayFoundry.Desktop.Media.Intelligence.SpeechActivity;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Platform;
using ReplayFoundry.Desktop.Platform.Dialogs;
using ReplayFoundry.Desktop.Platform.Diagnostics;
using ReplayFoundry.Desktop.Platform.GameKnowledge;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.RuntimePacks;
using ReplayFoundry.Desktop.Platform.SpeechActivity;
using ReplayFoundry.Desktop.Platform.Storage;
using ReplayFoundry.Desktop.Platform.Transcription;
using ReplayFoundry.Desktop.Platform.VisualSemantic;
using ReplayFoundry.Desktop.Platform.VisualText;
using ReplayFoundry.Desktop.Platform.YouTube;
using ReplayFoundry.Desktop.Shell;

namespace ReplayFoundry.Desktop;

internal sealed class ApplicationComposition : IDisposable
{
    private readonly GenerationLibraryCatalog _libraryCatalog;
    private readonly IDisposable? _recentGenerationProjects;
    private readonly IDisposable? _audioAuditionService;
    private readonly IDisposable? _studioProjectPersistence;
    private readonly IDisposable? _userReportTransport;
    private readonly IDisposable? _ownedEditorialMetadataProvider;
    private readonly IDisposable? _evidenceAnalysisCoordinator;
    private readonly IDisposable? _youtubePublishing;
    private readonly IDisposable? _speechActivity;
    private bool _disposed;

    public ApplicationComposition(
        MainWindowViewModel mainWindowViewModel,
        GenerationLibraryCatalog libraryCatalog,
        UserReportCoordinator userReports,
        IDisposable? recentGenerationProjects = null,
        IDisposable? audioAuditionService = null,
        IDisposable? studioProjectPersistence = null,
        IDisposable? userReportTransport = null,
        IDisposable? ownedEditorialMetadataProvider = null,
        IDisposable? evidenceAnalysisCoordinator = null,
        IDisposable? youtubePublishing = null,
        IDisposable? speechActivity = null)
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
    }

    public MainWindowViewModel MainWindowViewModel { get; }
    public UserReportCoordinator UserReports { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
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
    public static ApplicationComposition Create()
    {
        var localDataMaintenance =
            new ReplayFoundryLocalDataMaintenanceService();
        ApplyPendingLocalDataReset(localDataMaintenance);

        var videoFilePicker = new WindowsVideoFilePicker();
        var videoSourceValidator = new VideoSourceValidator();

        ReplayFoundryRuntimeEnvironment localRuntime =
            ReplayFoundryRuntimeEnvironment.Current;
        GenerationOutputLocationState outputLocation =
            CreateGenerationOutputLocation();
        EditorialRerollPreferenceState editorialRerollPreference =
            CreateEditorialRerollPreference();
        EditorialMetadataPreferenceLearningConsentState
            editorialMetadataPreferenceLearningConsent =
                CreateEditorialMetadataPreferenceLearningConsent();
        var editorialMetadataPreferenceRecorder =
            new StudioEditorialMetadataCorrectionRecorder(
                new EditorialMetadataPreferenceRecorder(
                    editorialMetadataPreferenceLearningConsent,
                    static () =>
                        new JsonEditorialMetadataPreferenceStore()));
        var folderLauncher = new WindowsLocalFolderLauncher();
        IGenerationCaptionPreparationService? captionPreparation =
            CreateCaptionPreparationService(localRuntime);
        IGenerationSpeechActivityService? speechActivity =
            CreateSpeechActivityService(localRuntime);
        IClipPreferenceFeedbackStore? clipPreferences =
            CreateClipPreferenceStore();
        IStudioCandidateDecisionStore? candidateDecisions =
            CreateCandidateDecisionStore();
        IStudioHiddenMomentDecisionStore? hiddenMomentDecisions =
            CreateHiddenMomentDecisionStore();
        ResearchParticipationState researchParticipation =
            CreateResearchParticipation();
        IResearchFeedbackStore researchFeedbackStore =
            CreateResearchFeedbackStore();
        var researchFeedback = new ResearchFeedbackRecorder(
            researchParticipation,
            researchFeedbackStore);
        IGenerationCandidateRefinementService? candidateRefinement =
            speechActivity is null
                ? null
                : new GenerationCandidateRefinementService(
                    preferenceProfiles: clipPreferences);
        Qwen3VlQualifiedEditorialRuntime? qwenRuntime =
            CreateQwenRuntime(localRuntime);
        IVisualSemanticReviewVideoMaterializer? visualReviewMaterializer =
            qwenRuntime is null
                ? null
                : VisualSemanticReviewVideoMaterializerFactory.CreateDefault();
        IGenerationVisualSemanticAnalysisService? visualSemantic =
            qwenRuntime is null
                ? null
                : CreateVisualSemanticAnalysisService(
                    qwenRuntime,
                    visualReviewMaterializer!);
        var runtimeCapabilities = new GenerationRuntimeCapabilities(
            captionPreparation is not null,
            speechActivity is not null,
            IsVisualSemanticReviewAvailable: visualSemantic is not null);

        IGenerationGameContextMemory gameContextMemory =
            new JsonGenerationGameContextMemory();
        IGenerationAudioRoleMemory audioRoleMemory =
            new JsonGenerationAudioRoleMemory();
        var previewFrameProvider =
            VideoPreviewFrameFactory.CreateDefault();
        var audioAuditionService = new WpfAudioStreamAuditionService(
            AudioSegmentExtractionFactory.CreateDefault());
        var generationGameKnowledge = new GenerationGameKnowledgeService(
            new WikimediaGameKnowledgeProvider(),
            new JsonGameKnowledgeSnapshotStore());
        var generationSetupDialogService =
            new GenerationSetupDialogService((request, initialOptions) =>
                new GenerationSetupViewModel(
                    request,
                    initialOptions,
                    runtimeCapabilities,
                    gameContextMemory,
                    audioRoleMemory,
                    audioAuditionService,
                    previewFrameProvider));
        var generationVisualText = new GenerationVisualTextAnalysisService(
            previewFrameProvider,
            new WindowsMediaOcrProvider());
        var compositionReviewDialogService =
            new GenerationCompositionReviewDialogService(
                (request, initialResult) =>
                    new CompositionReviewViewModel(
                        request,
                        previewFrameProvider,
                        initialResult));

        IMediaProbe mediaProbe = MediaInspectionFactory.CreateDefault();
        var sourceFileSnapshotProvider =
            new SystemGenerationSourceFileSnapshotProvider();
        var sourcePreparationService =
            new GenerationSourcePreparationService(
                mediaProbe,
                sourceFileSnapshotProvider);
        var sourceFreshnessValidator =
            new GenerationSourceFreshnessValidator(
                sourceFileSnapshotProvider);
        var sourcePreparationCoordinator =
            new GenerationSourcePreparationCoordinator(
                sourcePreparationService,
                sourceFreshnessValidator);

        var evidenceAnalyzer =
            MediaEvidenceAnalysisFactory.CreateDefault();
        GenerationEvidenceAnalysisSettings evidenceSettings =
            GenerationEvidenceAnalysisSettings.CreateDefault();
        var evidenceAnalysisService =
            new GenerationEvidenceAnalysisService(
                evidenceAnalyzer,
                sourceFreshnessValidator);
        var evidenceAnalysisCoordinator =
            new GenerationEvidenceAnalysisCoordinator(
                evidenceAnalysisService,
                sourceFreshnessValidator,
                evidenceSettings);
        var momentFindingService = new GenerationMomentFindingService(
            new DeterministicMediaMomentFinder());

        var generationOutputSession = new GenerationOutputSession();
        var studioProjectStore = new JsonStudioProjectStore();
        var studioProjectPersistence =
            new StudioProjectPersistenceCoordinator(
                generationOutputSession,
                studioProjectStore);
        var recentGenerationProjects = new RecentGenerationProjectCatalog(
            generationOutputSession,
            studioProjectStore: studioProjectStore);
        var libraryCatalog = new GenerationLibraryCatalog(
            generationOutputSession,
            CreateLibraryCatalogStore());

        var editorialProfileSession = new ClipEditorialProfileSession();
        Qwen3VlGroundedMetadataGenerator? editorialAiProvider =
            qwenRuntime is null
                ? null
                : new Qwen3VlGroundedMetadataGenerator(qwenRuntime);
        var editorialMetadataGenerator =
            new ClipEditorialMetadataGenerationService(
                new HeuristicClipEditorialMetadataGenerator(),
                editorialAiProvider,
                visualReviewMaterializer);
        var generationEditorialMetadataService =
            new GenerationEditorialMetadataService(
                editorialMetadataGenerator,
                editorialProfileSession,
                generationGameKnowledge,
                generationVisualText);
        var generationRunner = new GenerationPipelineRunner(
            new GenerationPreflightRunner(),
            momentFindingService,
            new SystemGenerationOutputPathProvider(outputLocation),
            generationOutputSession,
            captionPreparation,
            generationEditorialMetadataService,
            speechActivity,
            candidateRefinement,
            visualSemantic);

        var sourceSelection = new GenerationSourceSelectionState(
            videoSourceValidator);
        var workflowSession = new GenerationWorkflowSessionState(
            sourcePreparationCoordinator,
            evidenceAnalysisCoordinator);
        IStudioPreviewMediaService studioPreviewMedia =
            StudioPreviewMediaFactory.CreateDefault();
        var studioViewModel = new StudioViewModel(
            generationOutputSession,
            generationOutputSession,
            StudioProjectRenderingFactory.CreateDefault(),
            editorialMetadataGenerator,
            editorialProfileSession,
            studioPreviewMedia,
            clipPreferences is null
                ? null
                : new StudioClipPreferenceService(clipPreferences),
            candidateDecisions,
            hiddenMomentDecisions,
            researchFeedback,
            captionPreparation,
            generationEditorialMetadataService,
            new StudioPreviewPrewarmer(studioPreviewMedia),
            studioProjectPersistence,
            libraryCatalog,
            editorialRerollPreference,
            editorialMetadataPreferenceRecorder);
        var generateViewModel = new GenerateViewModel(
            videoFilePicker,
            new WindowsMediaRightsConfirmation(),
            generationSetupDialogService,
            compositionReviewDialogService,
            sourcePreparationCoordinator,
            evidenceAnalysisCoordinator,
            generationRunner,
            sourceSelection,
            workflowSession,
            new GenerationOperationController(),
            runtimeCapabilities,
            recentGenerationProjects,
            studioViewModel,
            new WindowsRecentProjectsClearConfirmation());
        var libraryViewModel = new LibraryViewModel(
            libraryCatalog,
            libraryCatalog,
            new WindowsLibraryMediaFilePicker(),
            folderLauncher,
            libraryCatalog,
            new WindowsLibraryRemovalConfirmation());

        YouTubeConnectionPermissionState youtubeConnectionPermission =
            CreateYouTubeConnectionPermission();
        IYouTubePublishingService? youtubePublishing =
            YouTubePublishingFactory.CreateDefault(
                youtubeConnectionPermission);
        var publishViewModel = new PublishViewModel(
            libraryCatalog,
            youtubePublishing,
            new JsonYouTubePublishPreferencesStore(),
            new WindowsThumbnailFilePicker(),
            youtubeConnectionPermission,
            new JsonYouTubePublishDraftStore(),
            new WindowsPublishPreparationDialogService(),
            new WindowsPublishBulkConfirmation(),
            new PublishEditorialMetadataService(
                generationOutputSession,
                editorialMetadataGenerator,
                editorialProfileSession,
                studioProjectStore),
            editorialRerollPreference);
        UserReportConsentState userReportConsent =
            CreateUserReportConsent();
        IUserReportOutbox userReportOutbox = CreateUserReportOutbox();
        IUserReportTransport userReportTransport =
            CreateUserReportTransport();
        var userReports = new UserReportCoordinator(
            userReportConsent,
            userReportOutbox,
            new ReplayFoundryDiagnosticCollector(),
            new UserReportSanitizer(),
            userReportTransport);
        var settingsViewModel = new SettingsViewModel(
            youtubeConnectionPermission,
            CreateSettingsRuntimeCapabilities(localRuntime),
            new RuntimePackMaintenanceLauncher(
                localRuntime.PackageStoreRoot),
            researchParticipation,
            researchFeedbackStore,
            outputLocation,
            new WindowsOutputFolderPicker(),
            folderLauncher,
            editorialProfileSession,
            new BugReportSettingsViewModel(
                userReportConsent,
                userReportOutbox,
                userReports),
            new LocalDataSettingsViewModel(
                localDataMaintenance,
                new WindowsLocalDataCleanupConfirmation()),
            editorialRerollPreference,
            editorialMetadataPreferenceLearningConsent);
        var mainWindowViewModel = new MainWindowViewModel(
            generateViewModel,
            studioViewModel,
            libraryViewModel,
            publishViewModel,
            settingsViewModel);

        return new ApplicationComposition(
            mainWindowViewModel,
            libraryCatalog,
            userReports,
            recentGenerationProjects,
            audioAuditionService,
            studioProjectPersistence,
            userReportTransport as IDisposable,
            editorialAiProvider,
            evidenceAnalysisCoordinator,
            youtubePublishing as IDisposable,
            speechActivity as IDisposable);
    }

    private static void ApplyPendingLocalDataReset(
        IReplayFoundryLocalDataMaintenance maintenance)
    {
        try
        {
            ReplayFoundryLocalDataCleanupResult result = maintenance
                .ApplyScheduledResetAsync()
                .GetAwaiter()
                .GetResult();
            foreach (string warning in result.Warnings)
            {
                SafeDiagnosticTrace.Write(
                    "Scheduled local-data reset warning",
                    warning);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException)
        {
            SafeDiagnosticTrace.Write(
                "The scheduled local-data reset could not be applied",
                exception);
        }
    }

    private static UserReportConsentState CreateUserReportConsent()
    {
        try
        {
            return new UserReportConsentState(
                new JsonUserReportConsentStore());
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidDataException)
        {
            SafeDiagnosticTrace.Write(
                "Bug-report consent storage is unavailable",
                exception);
            return new UserReportConsentState(
                new InMemoryUserReportConsentStore());
        }
    }

    private static IUserReportOutbox CreateUserReportOutbox()
    {
        try
        {
            return new JsonUserReportOutbox();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException)
        {
            SafeDiagnosticTrace.Write(
                "Bug-report outbox storage is unavailable",
                exception);
            return new InMemoryUserReportOutbox();
        }
    }

    private static IUserReportTransport CreateUserReportTransport()
    {
        try
        {
            return UserReportTransportFactory.CreateFromAssembly(
                typeof(App).Assembly);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            SafeDiagnosticTrace.Write(
                "Bug-report delivery is unavailable",
                exception);
            return new UnavailableUserReportTransport();
        }
    }

    private static SettingsRuntimeCapabilitySnapshot
        CreateSettingsRuntimeCapabilities(
            ReplayFoundryRuntimeEnvironment runtime) =>
        new(
            runtime.IsBaseReady,
            runtime.IsBalancedReady,
            runtime.IsThoroughReady,
            runtime.Capabilities.Any(capability =>
                capability.IsAvailable &&
                capability.Name != "Deterministic media analysis"),
            runtime.PackageStoreRoot,
            runtime.Capabilities.Select(capability =>
                CreateSettingsCapability(capability)));

    private static SettingsCapabilityItem CreateSettingsCapability(
        ReplayFoundryRuntimeCapabilityStatus capability)
    {
        (string name, string detail) = capability.Name switch
        {
            "Deterministic media analysis" =>
                ("Core video analysis", "Finds visual and audio changes locally."),
            "Speech activity" =>
                ("Speech detection", "Locates speech-like sections for Balanced and Thorough analysis."),
            "Local transcription runtime" =>
                ("Local transcription", "Creates subtitles and speech context on this PC."),
            "Multilingual transcription model" =>
                ("Multilingual speech model", "Supports local transcription across multiple languages."),
            "Qwen visual runtime" =>
                ("Visual review engine", "Runs optional deeper visual review on compatible graphics hardware."),
            "Qwen3-VL 4B model" =>
                ("Visual review model", "Adds grounded visual context during Thorough analysis."),
            _ => (capability.Name, "Installed local capability."),
        };
        string status = capability.IsAvailable
            ? "Ready"
            : capability.Status.StartsWith("Installed", StringComparison.Ordinal)
                ? "Needs attention"
                : "Not installed";
        return new SettingsCapabilityItem(
                    name,
                    status,
                    capability.Storage,
                    capability.License,
                    detail);
    }

    private static ResearchParticipationState CreateResearchParticipation()
    {
        try
        {
            return new ResearchParticipationState(
                new JsonResearchParticipationStore());
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            return new ResearchParticipationState(
                new InMemoryResearchParticipationStore());
        }
    }

    private static IResearchFeedbackStore CreateResearchFeedbackStore()
    {
        try
        {
            return new JsonResearchFeedbackStore();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            return new InMemoryResearchFeedbackStore();
        }
    }

    private static IClipPreferenceFeedbackStore? CreateClipPreferenceStore()
    {
        try
        {
            return JsonClipPreferenceFeedbackStore.CreateDefault();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            SafeDiagnosticTrace.Write(
                "Clip preference storage is unavailable",
                exception);
            return null;
        }
    }

    private static IStudioCandidateDecisionStore?
        CreateCandidateDecisionStore()
    {
        try
        {
            return new JsonStudioCandidateDecisionStore();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            SafeDiagnosticTrace.Write(
                "Studio candidate-decision storage is unavailable",
                exception);
            return null;
        }
    }

    private static IStudioHiddenMomentDecisionStore?
        CreateHiddenMomentDecisionStore()
    {
        try
        {
            return new JsonStudioHiddenMomentDecisionStore();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            SafeDiagnosticTrace.Write(
                "Hidden Moments decision storage is unavailable",
                exception);
            return null;
        }
    }

    private static ILibraryCatalogStore CreateLibraryCatalogStore()
    {
        try
        {
            return new JsonLibraryCatalogStore();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            SafeDiagnosticTrace.Write(
                "Library catalog storage is unavailable",
                exception);
            return new InMemoryLibraryCatalogStore();
        }
    }

    private static GenerationOutputLocationState
        CreateGenerationOutputLocation()
    {
        try
        {
            return new GenerationOutputLocationState(
                new JsonGenerationOutputLocationStore());
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException)
        {
            SafeDiagnosticTrace.Write(
                "Generation output-location storage is unavailable",
                exception);
            return new GenerationOutputLocationState(
                new InMemoryGenerationOutputLocationStore());
        }
    }

    private static YouTubeConnectionPermissionState
        CreateYouTubeConnectionPermission()
    {
        try
        {
            return new YouTubeConnectionPermissionState(
                new JsonYouTubeConnectionPermissionStore());
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            SafeDiagnosticTrace.Write(
                "YouTube connection-permission storage is unavailable",
                exception);
            return new YouTubeConnectionPermissionState(
                new InMemoryYouTubeConnectionPermissionStore());
        }
    }

    private static EditorialRerollPreferenceState
        CreateEditorialRerollPreference()
    {
        try
        {
            return new EditorialRerollPreferenceState(
                new JsonEditorialRerollPreferenceStore());
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            SafeDiagnosticTrace.Write(
                "Editorial reroll-preference storage is unavailable",
                exception);
            return new EditorialRerollPreferenceState(
                new InMemoryEditorialRerollPreferenceStore());
        }
    }

    private static EditorialMetadataPreferenceLearningConsentState
        CreateEditorialMetadataPreferenceLearningConsent()
    {
        try
        {
            return new EditorialMetadataPreferenceLearningConsentState(
                new JsonEditorialMetadataPreferenceLearningConsentStore());
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            SafeDiagnosticTrace.Write(
                "Editorial metadata preference-learning consent storage is unavailable",
                exception);
            return new EditorialMetadataPreferenceLearningConsentState(
                new InMemoryEditorialMetadataPreferenceLearningConsentStore());
        }
    }

    private static IGenerationCaptionPreparationService?
        CreateCaptionPreparationService(
            ReplayFoundryRuntimeEnvironment localRuntime)
    {
        string? executable = ExplicitRuntimeEnvironment.Read(
            "REPLAYFOUNDRY_WHISPER_EXE") ??
            localRuntime.WhisperExecutablePath;
        string? modelPath = ExplicitRuntimeEnvironment.Read(
            "REPLAYFOUNDRY_WHISPER_MODEL") ??
            localRuntime.WhisperModelPath;
        string? vadModelPath = ExplicitRuntimeEnvironment.Read(
            "REPLAYFOUNDRY_WHISPER_VAD_MODEL") ??
            localRuntime.WhisperVadModelPath;
        if (string.IsNullOrWhiteSpace(executable) ||
            string.IsNullOrWhiteSpace(modelPath) ||
            !Path.IsPathFullyQualified(executable) ||
            !Path.IsPathFullyQualified(modelPath) ||
            !File.Exists(executable) ||
            !File.Exists(modelPath))
        {
            return null;
        }

        var model = new AudioTranscriptionModelSettings(
            modelPath,
            Path.GetFileNameWithoutExtension(modelPath),
            "whisper.cpp GGML",
            sourceUrlOrNote:
                "Explicit local model path selected by the Replay Foundry user.",
            languageCapabilityDescription:
                "Multilingual capability is declared by the selected model, not inferred from audio metadata.");
        var provider = new WhisperCppTranscriptionProvider(
            new WhisperCppProviderSettings(
                executable,
                model,
                vadModelPath: vadModelPath));
        var options = new AudioTranscriptionOptions(
            AudioTranscriptionLanguageMode.Auto,
            requestedLanguage: null,
            translateToEnglish: false,
            requireSegmentTimestamps: true,
            requestWordTimestamps: true,
            temperature: 0,
            threadCount: null,
            AudioTranscriptionProcessorHint.Auto,
            TimeSpan.FromMinutes(10),
            AudioTranscriptionOutputFormatPolicy.StructuredJson);
        return new GenerationCaptionPreparationService(
            AudioSegmentExtractionFactory.CreateDefault(),
            provider,
            options,
            model);
    }

    private static IGenerationSpeechActivityService?
        CreateSpeechActivityService(
            ReplayFoundryRuntimeEnvironment localRuntime)
    {
        string? modelPath = ExplicitRuntimeEnvironment.Read(
            "REPLAYFOUNDRY_SILERO_VAD_MODEL") ??
            localRuntime.SileroModelPath;
        if (string.IsNullOrWhiteSpace(modelPath) ||
            !Path.IsPathFullyQualified(modelPath) ||
            !File.Exists(modelPath))
        {
            return null;
        }

        var file = new FileInfo(modelPath);
        var model = new ModelArtifactManifest(
            "Silero VAD v6.2.1",
            file.FullName,
            ModelArtifactManifest.ComputeSha256(file.FullName),
            file.Length,
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
            "ONNX",
            "MIT",
            "https://github.com/snakers4/silero-vad/tree/v6.2.1",
            "Speech activity only; no language or semantic classification.");
        return new GenerationSpeechActivityService(
            AudioSegmentExtractionFactory.CreateDefault(),
            new SileroOnnxSpeechActivityProvider(modelPath),
            new GenerationSpeechActivitySettings(
                SpeechActivityOptions.CreateBalancedDefaults(),
                model));
    }

    private static Qwen3VlQualifiedEditorialRuntime? CreateQwenRuntime(
        ReplayFoundryRuntimeEnvironment localRuntime)
    {
        QwenRuntimeSelection? runtime =
            QwenRuntimeResolver.Resolve(
                localRuntime.Qwen);
        if (runtime is null)
        {
            return null;
        }

        try
        {
            return Qwen3VlQualifiedEditorialRuntimeLoader.Load(
                runtime.PythonExecutablePath,
                runtime.HostScriptPath,
                runtime.FfmpegSharedDirectoryPath,
                runtime.ModelManifestPath,
                runtime.PromptManifestPath,
                runtime.QualificationLockPath,
                TimeSpan.FromMinutes(20),
                runtime.ModelDirectoryOverride,
                runtime.EnvironmentVariables);
        }
        catch (Exception exception)
            when (exception is ArgumentException or IOException or JsonException)
        {
            return null;
        }
    }

    private static IGenerationVisualSemanticAnalysisService
        CreateVisualSemanticAnalysisService(
            Qwen3VlQualifiedEditorialRuntime runtime,
            IVisualSemanticReviewVideoMaterializer materializer) =>
        new GenerationVisualSemanticAnalysisService(
            runtime.Provider,
            materializer,
            new GenerationVisualSemanticSettings(
                runtime.Prompt,
                runtime.Model,
                runtime.VideoPolicy));
}
