using ReplayFoundry.Desktop.Features.Generate;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Platform.Dialogs;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.Transcription;

namespace ReplayFoundry.Desktop.Composition;

internal sealed record PrimaryFeatureDependencies(
    ApplicationPreferenceServices Preferences,
    GenerationSourceSelectionPlatform SourceSelectionPlatform,
    LocalSpeechServices Speech,
    EditorialFeedbackServices Feedback,
    LocalVisualReviewServices VisualReview,
    GenerationExperienceServices Experience,
    GenerationSourceAnalysisServices SourceAnalysis,
    GenerationWorkspaceServices Workspace,
    EditorialServices Editorial,
    WindowsLocalFolderLauncher FolderLauncher);

internal sealed record PrimaryFeatureViewModels(
    GenerateViewModel Generate,
    StudioViewModel Studio,
    LibraryViewModel Library);

internal static class PrimaryFeatureComposition
{
    public static PrimaryFeatureViewModels Create(
        PrimaryFeatureDependencies dependencies)
    {
        var generationRunner = CreateGenerationRunner(dependencies);
        var sourceSelection = new GenerationSourceSelectionState(
            dependencies.SourceSelectionPlatform.SourceValidator);
        var workflowSession = new GenerationWorkflowSessionState(
            dependencies.SourceAnalysis.PreparationCoordinator,
            dependencies.SourceAnalysis.EvidenceCoordinator);
        IStudioPreviewMediaService previewMedia =
            StudioPreviewMediaFactory.CreateDefault();
        var studio = CreateStudioViewModel(dependencies, previewMedia);
        var generate = CreateGenerateViewModel(
            dependencies,
            generationRunner,
            sourceSelection,
            workflowSession,
            studio);
        var library = CreateLibraryViewModel(dependencies);
        return new(generate, studio, library);
    }

    private static GenerationPipelineRunner CreateGenerationRunner(
        PrimaryFeatureDependencies dependencies) =>
        new(
            new GenerationPreflightRunner(),
            dependencies.SourceAnalysis.MomentFinding,
            new SystemGenerationOutputPathProvider(
                dependencies.Preferences.OutputLocation),
            dependencies.Editorial.GenerationMetadata,
            dependencies.Workspace.OutputSession,
            dependencies.Speech.CaptionPreparation,
            dependencies.Speech.SpeechActivity,
            dependencies.Feedback.CandidateRefinement,
            dependencies.VisualReview.Analysis,
            dependencies.Speech.TranscriptAnalysis,
            new ReplayFoundry.Desktop.Features.Generate.Intelligence.GenerationCaptureContextScreeningService(
                dependencies.Experience.VisualText),
            dependencies.Feedback.TasteLearning is { } learning ? new Features.Generate.Intelligence.GenerationTasteRanking(learning) : null);

    private static StudioViewModel CreateStudioViewModel(
        PrimaryFeatureDependencies dependencies,
        IStudioPreviewMediaService previewMedia) =>
        new(
            dependencies.Workspace.OutputSession,
            dependencies.Workspace.OutputSession,
            StudioProjectRenderingFactory.CreateDefault(),
            dependencies.Editorial.MetadataGenerator,
            dependencies.Editorial.ProfileSession,
            previewMedia,
            dependencies.Feedback.TasteLearning is null
                ? null
                : new Features.Personalization.NeuralStudioClipPreferenceService(dependencies.Feedback.TasteLearning),
            dependencies.Feedback.CandidateDecisions,
            dependencies.Feedback.HiddenMomentDecisions,
            dependencies.Feedback.ResearchRecorder,
            dependencies.Speech.CaptionPreparation,
            dependencies.Editorial.GenerationMetadata,
            new StudioPreviewPrewarmer(previewMedia),
            dependencies.Workspace.StudioProjectPersistence,
            dependencies.Workspace.LibraryCatalog,
            dependencies.Preferences.EditorialReroll,
            dependencies.Preferences.MetadataCorrectionRecorder,
            dependencies.Experience.GameKnowledge,
            new OnnxCorrectedCaptionAlignmentService(AudioSegmentExtractionFactory.CreateDefault()),
            new StudioCaptionLanguageModel(dependencies.Speech.CaptionLanguageCapabilities),
            new StudioTimelineFilmstrip(dependencies.Experience.PreviewFrames));

    private static GenerateViewModel CreateGenerateViewModel(
        PrimaryFeatureDependencies dependencies,
        GenerationPipelineRunner generationRunner,
        GenerationSourceSelectionState sourceSelection,
        GenerationWorkflowSessionState workflowSession,
        StudioViewModel studio) =>
        new(
            dependencies.SourceSelectionPlatform.VideoFilePicker,
            new WindowsMediaRightsConfirmation(),
            dependencies.Experience.SetupDialog,
            dependencies.Experience.CompositionReviewDialog,
            dependencies.SourceAnalysis.PreparationCoordinator,
            dependencies.SourceAnalysis.EvidenceCoordinator,
            generationRunner,
            sourceSelection,
            workflowSession,
            new GenerationOperationController(),
            dependencies.VisualReview.RuntimeCapabilities,
            dependencies.Workspace.RecentProjects,
            studio,
            new WindowsRecentProjectsClearConfirmation());

    private static LibraryViewModel CreateLibraryViewModel(
        PrimaryFeatureDependencies dependencies) =>
        new(
            dependencies.Workspace.LibraryCatalog,
            dependencies.Workspace.LibraryCatalog,
            new WindowsLibraryMediaFilePicker(),
            dependencies.FolderLauncher,
            dependencies.Workspace.LibraryCatalog,
            new WindowsLibraryRemovalConfirmation(),
            LibraryThumbnailRecoveryFactory.CreateDefault());
}
