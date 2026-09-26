using ReplayFoundry.Desktop.Features.Generate;
using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerateViewModelWorkflowTests
{
    private static async Task CompositionReviewOpensInSequence()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests.CreateOptions();

        bool observedReviewState = false;

        context.CompositionDialog.OnShow =
            (
                request,
                _) =>
            {
                observedReviewState =
                    context.ViewModel.WorkflowState ==
                    GenerateWorkflowState.ReviewingComposition;

                TestAssert.Equal(
                    0,
                    context.Runner.Requests.Count,
                    "Generation must not start before review completes.");

                return PreparedGenerationWorkflowTests
                    .CreateCompositionReview(
                        request.Preparation);
            };

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        TestAssert.True(
            observedReviewState,
            "Composition review should have an explicit workflow state.");

        TestAssert.Equal(
            1,
            context.CompositionDialog.Requests.Count,
            "Review should open exactly once after Setup.");

        TestAssert.Equal(
            1,
            context.Runner.Requests.Count,
            "Generation should begin only after review completes.");
    }

    private static async Task
        CompositionReviewCancellationPreservesState()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests.CreateOptions();

        context.CompositionDialog.Cancel = true;

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        TestAssert.Same(
            context.Coordinator.Current!,
            context.ViewModel.CurrentSourcePreparation!,
            "Cancelling review should retain preparation.");

        TestAssert.True(
            context.ViewModel.HasGenerationSetup,
            "Cancelling review should retain Setup.");

        TestAssert.Equal(
            0,
            context.Runner.Requests.Count,
            "Cancelling review must not start generation.");

        TestAssert.Equal(
            GenerateWorkflowState.SourceSelection,
            context.ViewModel.WorkflowState,
            "Cancelling review should return to source selection.");
    }

    private static async Task
        CompletedCompositionReviewReachesGeneration()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests.CreateOptions();

        GenerationCompositionReviewResult? completed =
            null;

        context.CompositionDialog.OnShow =
            (
                request,
                _) =>
            {
                completed =
                    PreparedGenerationWorkflowTests
                        .CreateCompositionReview(
                            request.Preparation);

                return completed;
            };

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        TestAssert.Same(
            completed!,
            context.Runner.Requests[0]
                .CompositionReview,
            "GenerationRequest should retain the exact completed review result.");
    }

    private static async Task
        StalenessAfterReviewInvalidatesAll()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests.CreateOptions();

        context.CompositionDialog.OnShow =
            (
                request,
                _) =>
            {
                context.Coordinator.FreshnessFailure =
                    new GenerationSourcePreparationException(
                        request.ReferenceSource.Source.FullPath,
                        "The source changed during layout review.");

                return PreparedGenerationWorkflowTests
                    .CreateCompositionReview(
                        request.Preparation);
            };

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        TestAssert.Equal(
            0,
            context.Runner.Requests.Count,
            "Staleness after review must block generation.");

        TestAssert.Null(
            context.ViewModel.CurrentSourcePreparation,
            "Staleness should invalidate preparation.");

        TestAssert.False(
            context.ViewModel.HasGenerationSetup,
            "Staleness should invalidate Setup.");

        TestAssert.False(
            context.ViewModel.HasCompositionReview,
            "Staleness should invalidate composition review.");
    }

    private static async Task
        ReviewingCompositionDisablesSourceEditing()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests.CreateOptions();

        bool sourceEditingDisabled = false;
        bool visibilityCorrect = false;

        context.CompositionDialog.OnShow =
            (
                _,
                _) =>
            {
                sourceEditingDisabled =
                    !context.ViewModel
                        .SelectSingleFileCommand
                        .CanExecute(null) &&
                    !context.ViewModel
                        .ClearSelectionCommand
                        .CanExecute(null);

                visibilityCorrect =
                    context.ViewModel
                        .IsSourceSelectionVisible &&
                    !context.ViewModel
                        .IsProgressVisible;

                return null;
            };

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        TestAssert.True(
            sourceEditingDisabled,
            "Source commands must be disabled while the modal review is active.");

        TestAssert.True(
            visibilityCorrect,
            "Review should keep source selection behind the modal without showing a failure card.");
    }

    private static async Task
        ReopenedReviewCancellationPreservesPrior()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests.CreateOptions();

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        GenerationCompositionReviewResult prior =
            context.ViewModel.CurrentCompositionReview!;

        context.ViewModel.GenerationProgress
            .ReturnToSourceSelectionCommand
            .Execute(null);

        context.CompositionDialog.Cancel = true;

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        TestAssert.Same(
            prior,
            context.ViewModel.CurrentCompositionReview!,
            "Cancelling a reopened review should preserve the cached prior result.");

        TestAssert.Same(
            prior,
            context.CompositionDialog
                .InitialResults[^1]!,
            "The reopened dialog should receive the prior result.");

        TestAssert.Equal(
            1,
            context.Runner.Requests.Count,
            "A cancelled reopened review must not start another generation.");
    }

    private static async Task DisposeCancelsPreparation()
    {
        ViewModelContext context =
            CreateContext();

        context.Coordinator.BlockPreparation();

        Task continuation =
            context.ViewModel
                .ContinueToGenerationSetupAsync();

        context.ViewModel.Dispose();

        await continuation;

        TestAssert.True(
            context.Coordinator.CancellationObserved,
            "Dispose should cancel active preparation.");

        TestAssert.Equal(
            0,
            context.Dialog.Requests.Count,
            "A disposed workflow should not open setup.");
    }

}
