using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Preview;

namespace ReplayFoundry.CompositionTests;

internal static partial class CompositionReviewTests
{
    private static void CompletionRequiresEverySource()
    {
        GenerationSourcePreparationResult preparation =
            CreateTwoSourcePreparation();

        using var viewModel =
            new CompositionReviewViewModel(
                new GenerationCompositionReviewRequest(
                    preparation),
                new NeverPreviewProvider());

        TestAssert.False(
            viewModel.CanContinue,
            "Unconfirmed initial drafts must not enable completion.");

        TestAssert.False(
            viewModel.ApplyCurrentLayoutCommand
                .CanExecute(null),
            "Batch copy should not silently become executable before review.");
    }

    private static void BatchCopyIsExplicitAndIndependent()
    {
        GenerationSourcePreparationResult preparation =
            CreateTwoSourcePreparation();

        GenerationCompositionReviewResult prior =
            CompositionTestData.CreateReviewResult(
                preparation);

        using var viewModel =
            new CompositionReviewViewModel(
                new GenerationCompositionReviewRequest(
                    preparation),
                new NeverPreviewProvider(),
                prior);

        CompositionReviewSourceViewModel second =
            viewModel.Sources[1];

        second.SelectedRegion!.MoveBy(
            -0.1,
            0);

        viewModel.SelectedSource =
            viewModel.Sources[0];

        viewModel.ApplyCurrentLayoutCommand
            .Execute(null);

        TestAssert.True(
            viewModel.CanContinue,
            "Explicit copy should confirm every target source.");

        TestAssert.Equal(
            preparation.Sources[0].Source.FullPath,
            viewModel.Sources[0]
                .ConfirmedPlan!.Plan.SourcePath,
            "The source plan should retain its own path.");

        TestAssert.Equal(
            preparation.Sources[1].Source.FullPath,
            viewModel.Sources[1]
                .ConfirmedPlan!.Plan.SourcePath,
            "The copied plan should use the target path.");

        TestAssert.Equal(
            preparation.Sources[1].Media.Duration,
            viewModel.Sources[1]
                .ConfirmedPlan!.Plan.SourceDuration,
            "The copied plan should use the target duration.");

        TestAssert.False(
            ReferenceEquals(
                viewModel.Sources[0]
                    .ConfirmedPlan!.Plan,
                viewModel.Sources[1]
                    .ConfirmedPlan!.Plan),
            "Every copied source must receive an independent plan object.");
    }

    private static void CopiedDraftsAreIndependent()
    {
        GenerationSourcePreparationResult preparation =
            CreateTwoSourcePreparation();

        GenerationCompositionReviewResult prior =
            CompositionTestData.CreateReviewResult(
                preparation);

        using var viewModel =
            new CompositionReviewViewModel(
                new GenerationCompositionReviewRequest(
                    preparation),
                new NeverPreviewProvider(),
                prior);

        viewModel.ApplyCurrentLayoutCommand
            .Execute(null);

        CompositionReviewSourceViewModel first =
            viewModel.Sources[0];

        CompositionReviewSourceViewModel second =
            viewModel.Sources[1];

        double firstX =
            first.Regions[0].X;

        second.Regions[0].SetGeometry(
            0.1,
            0.1,
            0.8,
            0.8);

        TestAssert.Equal(
            firstX,
            first.Regions[0].X,
            "Editing a copied target must not mutate the source draft.");

        TestAssert.True(
            first.IsConfirmed,
            "The untouched source should remain confirmed.");

        TestAssert.False(
            second.IsConfirmed,
            "Only the edited copied source should need review.");
    }

    private static void PriorResultRestoresIndependentDrafts()
    {
        GenerationSourcePreparationResult preparation =
            CompositionTestData.CreatePreparation(
                (
                    "prior.mkv",
                    true,
                    null));

        GenerationCompositionReviewResult prior =
            CompositionTestData.CreateReviewResult(
                preparation);

        using var viewModel =
            new CompositionReviewViewModel(
                new GenerationCompositionReviewRequest(
                    preparation),
                new NeverPreviewProvider(),
                prior);

        TestAssert.True(
            viewModel.Sources[0].IsConfirmed,
            "Prior results should restore confirmed status.");

        double originalX =
            prior.SourcePlans[0].Plan
                .Intervals[0]
                .Regions[0]
                .Geometry.X;

        viewModel.Sources[0]
            .Regions[0]
            .MoveBy(
                0.1,
                0);

        TestAssert.Equal(
            originalX,
            prior.SourcePlans[0].Plan
                .Intervals[0]
                .Regions[0]
                .Geometry.X,
            "Editing restored drafts must not mutate the prior immutable plan.");
    }

    private static void CancelDoesNotMutatePriorResult()
    {
        GenerationSourcePreparationResult preparation =
            CompositionTestData.CreatePreparation(
                (
                    "cancel-prior.mkv",
                    true,
                    null));

        GenerationCompositionReviewResult prior =
            CompositionTestData.CreateReviewResult(
                preparation);

        using var viewModel =
            new CompositionReviewViewModel(
                new GenerationCompositionReviewRequest(
                    preparation),
                new NeverPreviewProvider(),
                prior);

        bool cancelRaised = false;

        viewModel.CancelRequested +=
            (_, _) =>
                cancelRaised = true;

        PreparedSourceCompositionPlan retained =
            prior.SourcePlans[0];

        viewModel.Sources[0]
            .Regions[0]
            .MoveBy(
                0.1,
                0);

        viewModel.CancelCommand.Execute(null);

        TestAssert.True(
            cancelRaised,
            "Cancel should request dialog closure.");

        TestAssert.Same(
            retained,
            prior.SourcePlans[0],
            "Cancel must leave the prior result untouched.");
    }

    private static void ForeignPriorResultIsRejected()
    {
        GenerationSourcePreparationResult preparation =
            CompositionTestData.CreatePreparation(
                (
                    "current.mkv",
                    true,
                    null));

        GenerationSourcePreparationResult foreignPreparation =
            CompositionTestData.CreatePreparation(
                (
                    "foreign-prior.mkv",
                    true,
                    null));

        GenerationCompositionReviewResult foreign =
            CompositionTestData.CreateReviewResult(
                foreignPreparation);

        TestAssert.Throws<ArgumentException>(
            () =>
                _ = new CompositionReviewViewModel(
                    new GenerationCompositionReviewRequest(
                        preparation),
                    new NeverPreviewProvider(),
                    foreign),
            "Prior results from another preparation must be rejected.");
    }

    private static void CompletingEditsCreatesNewResult()
    {
        GenerationSourcePreparationResult preparation =
            CompositionTestData.CreatePreparation(
                (
                    "complete-edits.mkv",
                    true,
                    null));

        GenerationCompositionReviewResult prior =
            CompositionTestData.CreateReviewResult(
                preparation);

        using var viewModel =
            new CompositionReviewViewModel(
                new GenerationCompositionReviewRequest(
                    preparation),
                new NeverPreviewProvider(),
                prior);

        viewModel.Sources[0]
            .Regions[0]
            .SetGeometry(
                0.1,
                0.1,
                0.8,
                0.8);

        TestAssert.True(
            viewModel.Sources[0]
                .TryConfirm(
                    CompositionTestData.CreatedAtUtc),
            "A restored draft should be reconfirmable after edits.");

        GenerationCompositionReviewResult completed =
            viewModel.CreateResult();

        TestAssert.False(
            ReferenceEquals(
                prior,
                completed),
            "Completion should create a new immutable result.");

        TestAssert.Equal(
            0.1,
            completed.SourcePlans[0].Plan
                .Intervals[0]
                .Regions[0]
                .Geometry.X,
            "The new result should contain edited geometry.");
    }

}
