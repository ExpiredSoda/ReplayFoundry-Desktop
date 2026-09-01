namespace ReplayFoundry.Desktop.Features.Generate.CompositionReview;

public interface IGenerationCompositionReviewDialogService
{
    GenerationCompositionReviewResult? Show(
        GenerationCompositionReviewRequest request,
        GenerationCompositionReviewResult? initialResult);
}
