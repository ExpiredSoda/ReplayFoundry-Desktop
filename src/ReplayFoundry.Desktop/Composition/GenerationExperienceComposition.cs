using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Editorial.GameKnowledge;
using ReplayFoundry.Desktop.Features.Generate.Editorial.VisualText;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Media.Preview;
using ReplayFoundry.Desktop.Platform.Dialogs;
using ReplayFoundry.Desktop.Platform.GameKnowledge;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.Storage;
using ReplayFoundry.Desktop.Platform.VisualText;

namespace ReplayFoundry.Desktop.Composition;

internal sealed record GenerationExperienceServices(
    IGenerationGameContextMemory GameContextMemory,
    IGameKnowledgePermissionStatus GameKnowledgePermissionStatus,
    IGenerationAudioRoleMemory AudioRoleMemory,
    IVideoPreviewFrameProvider PreviewFrames,
    WpfAudioStreamAuditionService AudioAudition,
    IGenerationGameKnowledgeService GameKnowledge,
    IGenerationSetupDialogService SetupDialog,
    IGenerationVisualTextAnalysisService VisualText,
    IGenerationCompositionReviewDialogService CompositionReviewDialog);

internal static class GenerationExperienceComposition
{
    public static GenerationExperienceServices Create(
        LocalVisualReviewServices visualReview)
    {
        IGenerationGameContextMemory gameContextMemory =
            new JsonGenerationGameContextMemory();
        IGenerationAudioRoleMemory audioRoleMemory =
            new JsonGenerationAudioRoleMemory();
        IVideoPreviewFrameProvider previewFrames =
            VideoPreviewFrameFactory.CreateDefault();
        var audioAudition = new WpfAudioStreamAuditionService(
            AudioSegmentExtractionFactory.CreateDefault());
        var wikimedia = new WikimediaGameKnowledgeProvider();
        var gameKnowledge = new GenerationGameKnowledgeService(
            wikimedia,
            new JsonGameKnowledgeSnapshotStore());
        var setupDialog = new GenerationSetupDialogService(
            (request, initialOptions) => new GenerationSetupViewModel(
                request,
                initialOptions,
                visualReview.RuntimeCapabilities,
                gameContextMemory,
                audioRoleMemory,
                audioAudition,
                previewFrames,
                wikimedia,
                gameKnowledge));
        var visualText = new GenerationVisualTextAnalysisService(
            previewFrames,
            new WindowsMediaOcrProvider());
        var compositionReviewDialog =
            new GenerationCompositionReviewDialogService(
                (request, initialResult) => new CompositionReviewViewModel(
                    request,
                    previewFrames,
                    initialResult,
                    new WindowsCompositionLayoutSuggestionService()));
        return new(
            gameContextMemory,
            (IGameKnowledgePermissionStatus)gameContextMemory,
            audioRoleMemory,
            previewFrames,
            audioAudition,
            gameKnowledge,
            setupDialog,
            visualText,
            compositionReviewDialog);
    }
}
