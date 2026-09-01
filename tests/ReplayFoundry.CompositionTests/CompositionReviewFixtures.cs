using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Preview;

namespace ReplayFoundry.CompositionTests;

internal static partial class CompositionReviewTests
{
    private static GenerationSourcePreparationResult
        CreateTwoSourcePreparation()
    {
        return CompositionTestData.CreatePreparation(
            (
                "first.mkv",
                false,
                TimeSpan.FromMinutes(5)),
            (
                "reference.mkv",
                true,
                TimeSpan.FromMinutes(7)));
    }

    private static CompositionReviewSourceViewModel
        CreateConfirmedSource()
    {
        GenerationSourcePreparationResult preparation =
            CompositionTestData.CreatePreparation(
                (
                    $"draft-{Guid.NewGuid():N}.mkv",
                    true,
                    null));

        PreparedSourceCompositionPlan initialPlan =
            CompositionTestData.CreateSourcePlan(
                preparation.Sources[0]);

        return new CompositionReviewSourceViewModel(
            preparation.Sources[0],
            isReference: true,
            new NeverPreviewProvider(),
            initialPlan);
    }

    private sealed class NeverPreviewProvider :
        IVideoPreviewFrameProvider
    {
        public Task<VideoPreviewFrame> GetFrameAsync(
            VideoPreviewFrameRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(
                "Preview extraction was not expected.");
        }
    }
}
