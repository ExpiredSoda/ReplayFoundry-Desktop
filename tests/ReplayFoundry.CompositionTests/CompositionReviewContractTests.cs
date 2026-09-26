using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Preview;

namespace ReplayFoundry.CompositionTests;

internal static partial class CompositionReviewTests
{
    private static void RequestRequiresPreparation()
    {
        TestAssert.Throws<ArgumentNullException>(
            () =>
                _ = new GenerationCompositionReviewRequest(
                    null!),
            "A request must reject null preparation.");
    }

    private static void ResultRequiresEverySource()
    {
        GenerationSourcePreparationResult preparation =
            CreateTwoSourcePreparation();

        var request =
            new GenerationCompositionReviewRequest(
                preparation);

        TestAssert.Throws<ArgumentException>(
            () =>
                _ = new GenerationCompositionReviewResult(
                    request,
                    [
                        CompositionTestData.CreateSourcePlan(
                            preparation.Sources[0]),
                    ]),
            "A result must reject a missing source plan.");
    }

    private static void ResultPreservesOrderAndIdentity()
    {
        GenerationSourcePreparationResult preparation =
            CreateTwoSourcePreparation();

        PreparedSourceCompositionPlan first =
            CompositionTestData.CreateSourcePlan(
                preparation.Sources[0]);

        PreparedSourceCompositionPlan second =
            CompositionTestData.CreateSourcePlan(
                preparation.Sources[1]);

        var result =
            new GenerationCompositionReviewResult(
                new GenerationCompositionReviewRequest(
                    preparation),
                [
                    second,
                    first,
                ]);

        TestAssert.Same(
            preparation.Sources[0],
            result.SourcePlans[0].PreparedSource,
            "Result order should follow preparation order.");

        TestAssert.Same(
            preparation.Sources[1],
            result.SourcePlans[1].PreparedSource,
            "Selected-source identity should be preserved.");
    }

    private static void ResultPreservesNonFirstReference()
    {
        GenerationSourcePreparationResult preparation =
            CreateTwoSourcePreparation();

        GenerationCompositionReviewResult result =
            CompositionTestData.CreateReviewResult(
                preparation);

        TestAssert.Same(
            preparation.ReferenceSource,
            result.ReferencePlan.PreparedSource,
            "The explicit second reference should remain independent of list position.");
    }

    private static void ResultRejectsInvalidSourceSets()
    {
        GenerationSourcePreparationResult preparation =
            CreateTwoSourcePreparation();

        var request =
            new GenerationCompositionReviewRequest(
                preparation);

        PreparedSourceCompositionPlan first =
            CompositionTestData.CreateSourcePlan(
                preparation.Sources[0]);

        TestAssert.Throws<ArgumentException>(
            () =>
                _ = new GenerationCompositionReviewResult(
                    request,
                    [
                        first,
                        first,
                    ]),
            "Duplicate prepared-source plans must be rejected.");

        GenerationSourcePreparationResult foreignPreparation =
            CompositionTestData.CreatePreparation(
                (
                    "foreign.mkv",
                    true,
                    null));

        PreparedSourceCompositionPlan foreign =
            CompositionTestData.CreateSourcePlan(
                foreignPreparation.Sources[0]);

        TestAssert.Throws<ArgumentException>(
            () =>
                _ = new GenerationCompositionReviewResult(
                    request,
                    [
                        first,
                        foreign,
                    ]),
            "Foreign source plans must be rejected.");
    }

    private static void SourcePlanRejectsPathMismatch()
    {
        GenerationSourcePreparationResult preparation =
            CompositionTestData.CreatePreparation(
                (
                    "path-source.mkv",
                    true,
                    null));

        TestAssert.Throws<ArgumentException>(
            () =>
                _ = CompositionTestData.CreateSourcePlan(
                    preparation.Sources[0],
                    sourcePath:
                        Path.GetFullPath(
                            Path.Combine(
                                Path.GetTempPath(),
                                "different-source.mkv"))),
            "Plan and preparation paths must match.");
    }

    private static void SourcePlanRejectsDurationMismatch()
    {
        GenerationSourcePreparationResult preparation =
            CompositionTestData.CreatePreparation(
                (
                    "duration-source.mkv",
                    true,
                    null));

        TestAssert.Throws<ArgumentException>(
            () =>
                _ = CompositionTestData.CreateSourcePlan(
                    preparation.Sources[0],
                    duration:
                        preparation.Sources[0]
                            .Media.Duration -
                        TimeSpan.FromSeconds(1)),
            "Plan and preparation durations must match exactly.");
    }

    private static void SourcePlanRequiresGameplay()
    {
        GenerationSourcePreparationResult preparation =
            CompositionTestData.CreatePreparation(
                (
                    "presenter-only.mkv",
                    true,
                    null));

        TestAssert.Throws<ArgumentException>(
            () =>
                _ = CompositionTestData.CreateSourcePlan(
                    preparation.Sources[0],
                    [
                        CompositionTestData
                            .CreateUserConfirmedRegion(
                                "presenter",
                                CompositionRegionRole.Presenter),
                    ]),
            "Product review plans must contain Gameplay.");
    }

    private static void ResultIsImmutableSnapshot()
    {
        GenerationSourcePreparationResult preparation =
            CreateTwoSourcePreparation();

        PreparedSourceCompositionPlan[] plans =
            preparation.Sources
                .Select(
                    source =>
                        CompositionTestData.CreateSourcePlan(
                            source))
                .ToArray();

        var result =
            new GenerationCompositionReviewResult(
                new GenerationCompositionReviewRequest(
                    preparation),
                plans);

        PreparedSourceCompositionPlan retained =
            result.SourcePlans[0];

        plans[0] =
            plans[1];

        TestAssert.Same(
            retained,
            result.SourcePlans[0],
            "Result plans should be an immutable snapshot.");

        TestAssert.True(
            result.SourcePlans is
                ICollection<PreparedSourceCompositionPlan>
                collection &&
            collection.IsReadOnly,
            "The externally exposed collection should be read-only.");
    }

}
