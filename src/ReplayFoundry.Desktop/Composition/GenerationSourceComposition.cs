using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Platform.Dialogs;
using ReplayFoundry.Desktop.Platform.Media;

namespace ReplayFoundry.Desktop.Composition;

internal sealed record GenerationSourceSelectionPlatform(
    WindowsVideoFilePicker VideoFilePicker,
    VideoSourceValidator SourceValidator);

internal sealed record GenerationSourceAnalysisServices(
    GenerationSourcePreparationCoordinator PreparationCoordinator,
    GenerationEvidenceAnalysisCoordinator EvidenceCoordinator,
    GenerationMomentFindingService MomentFinding);

internal static class GenerationSourceComposition
{
    public static GenerationSourceSelectionPlatform CreateSelectionPlatform() =>
        new(new WindowsVideoFilePicker(), new VideoSourceValidator());

    public static GenerationSourceAnalysisServices CreateAnalysisServices()
    {
        IMediaProbe mediaProbe = MediaInspectionFactory.CreateDefault();
        var snapshotProvider =
            new SystemGenerationSourceFileSnapshotProvider();
        var preparationService = new GenerationSourcePreparationService(
            mediaProbe,
            snapshotProvider);
        var freshnessValidator = new GenerationSourceFreshnessValidator(
            snapshotProvider);
        var preparationCoordinator =
            new GenerationSourcePreparationCoordinator(
                preparationService,
                freshnessValidator);
        var evidenceAnalyzer = MediaEvidenceAnalysisFactory.CreateDefault();
        GenerationEvidenceAnalysisSettings evidenceSettings =
            GenerationEvidenceAnalysisSettings.CreateDefault();
        var evidenceService = new GenerationEvidenceAnalysisService(
            evidenceAnalyzer,
            freshnessValidator);
        var evidenceCoordinator = new GenerationEvidenceAnalysisCoordinator(
            evidenceService,
            freshnessValidator,
            evidenceSettings);
        var momentFinding = new GenerationMomentFindingService(
            new DeterministicMediaMomentFinder());
        return new(
            preparationCoordinator,
            evidenceCoordinator,
            momentFinding);
    }
}
