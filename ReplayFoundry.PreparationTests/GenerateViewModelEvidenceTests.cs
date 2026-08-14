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
    private static async Task EvidenceOpensBeforePreflight()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests
                .CreateOptions();

        context.EvidenceCoordinator.BlockAnalysis();

        Task continuation =
            context.ViewModel
                .ContinueToGenerationSetupAsync();

        TestAssert.Equal(
            GenerateWorkflowState.AnalyzingEvidence,
            context.ViewModel.WorkflowState,
            "Evidence analysis should have an explicit state after layout review.");

        TestAssert.Equal(
            0,
            context.Runner.Requests.Count,
            "Preflight must wait for the complete evidence batch.");

        context.EvidenceCoordinator.ReleaseAnalysis();
        await continuation;

        TestAssert.Equal(
            1,
            context.Runner.Requests.Count,
            "Preflight should begin after evidence completes.");
    }

    private static async Task UnsupportedSetupSkipsEvidence()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            new GenerationSetupOptions(
                GenerationMode.IndividualClips,
                DetectionMethod.LocalAi,
                AudioSelectionMode.Auto,
                desiredResultCount: 10,
                qualityThreshold: 70,
                ContentEmphasis.Balanced);

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        TestAssert.Equal(
            0,
            context.EvidenceCoordinator
                .Requests.Count,
            "Unsupported setup should stop before evidence is requested.");

        TestAssert.Equal(
            0,
            context.Runner.Requests.Count,
            "Unsupported setup should not reach preflight.");
    }

    private static async Task
        EvidenceCancellationPreservesPriorState()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests
                .CreateOptions();

        context.EvidenceCoordinator.BlockAnalysis();

        Task continuation =
            context.ViewModel
                .ContinueToGenerationSetupAsync();

        GenerationSourcePreparationResult preparation =
            context.ViewModel
                .CurrentSourcePreparation!;

        GenerationCompositionReviewResult composition =
            context.ViewModel
                .CurrentCompositionReview!;

        context.ViewModel.GenerationProgress
            .CancelCommand.Execute(null);

        await continuation;

        TestAssert.Equal(
            GenerateWorkflowState.Cancelled,
            context.ViewModel.WorkflowState,
            "Evidence cancellation should produce a cancelled state.");

        TestAssert.Same(
            preparation,
            context.ViewModel
                .CurrentSourcePreparation!,
            "Evidence cancellation should retain preparation.");

        TestAssert.True(
            context.ViewModel.HasGenerationSetup,
            "Evidence cancellation should retain Setup.");

        TestAssert.Same(
            composition,
            context.ViewModel
                .CurrentCompositionReview!,
            "Evidence cancellation should retain composition.");

        TestAssert.Null(
            context.ViewModel
                .CurrentEvidenceAnalysis,
            "Partial evidence should not be retained.");
    }

    private static async Task
        EvidenceFailurePreservesPriorState()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests
                .CreateOptions();

        context.EvidenceCoordinator.Failure =
            new GenerationEvidenceAnalysisException(
                context.PrimaryPath,
                1,
                1,
                "Synthetic source-specific evidence failure.",
                "Synthetic diagnostics.");

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        TestAssert.Equal(
            GenerateWorkflowState.Failed,
            context.ViewModel.WorkflowState,
            "Evidence failure should produce a failed state.");

        TestAssert.True(
            context.ViewModel.CurrentSourcePreparation
                is not null &&
            context.ViewModel.HasGenerationSetup &&
            context.ViewModel.HasCompositionReview,
            "A fresh analysis failure should preserve retryable prior state.");

        TestAssert.True(
            context.ViewModel.GenerationProgress
                .HasTechnicalDetails,
            "Evidence failure should expose technical details.");
    }

    private static async Task
        EvidenceStalenessInvalidatesAll()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests
                .CreateOptions();

        context.EvidenceCoordinator.Failure =
            new GenerationSourcePreparationException(
                context.PrimaryPath,
                "The source changed during evidence analysis.");

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        TestAssert.Null(
            context.ViewModel.CurrentSourcePreparation,
            "Staleness should invalidate preparation.");

        TestAssert.False(
            context.ViewModel.HasGenerationSetup,
            "Staleness should invalidate Setup.");

        TestAssert.False(
            context.ViewModel.HasCompositionReview,
            "Staleness should invalidate composition.");

        TestAssert.Null(
            context.ViewModel.CurrentEvidenceAnalysis,
            "Staleness should invalidate evidence.");
    }

    private static async Task
        SuccessfulEvidenceReachesGeneration()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests
                .CreateOptions();

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        TestAssert.Equal(
            1,
            context.EvidenceCoordinator
                .AnalysisRunCount,
            "One evidence batch should run.");

        TestAssert.Same(
            context.EvidenceCoordinator.Current!,
            context.Runner.Requests[0]
                .EvidenceAnalysis,
            "GenerationRequest should retain the completed evidence batch.");

        TestAssert.Same(
            context.Runner.Requests[0]
                .ReferencePreparedSource,
            context.Runner.Requests[0]
                .ReferenceAnalyzedSource
                .PreparedSource,
            "GenerationRequest should preserve the analyzed reference source.");
    }

    private static async Task
        AnalyzingEvidenceDisablesEditing()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests
                .CreateOptions();

        context.EvidenceCoordinator.BlockAnalysis();

        Task continuation =
            context.ViewModel
                .ContinueToGenerationSetupAsync();

        TestAssert.False(
            context.ViewModel
                .SelectSingleFileCommand
                .CanExecute(null),
            "Source selection should be disabled during evidence analysis.");

        TestAssert.False(
            context.ViewModel
                .ClearSelectionCommand
                .CanExecute(null),
            "Clear should be disabled during evidence analysis.");

        TestAssert.Equal(
            "Cancel Analysis",
            context.ViewModel.GenerationProgress
                .CancelButtonLabel,
            "Evidence analysis should expose the truthful cancel label.");

        TestAssert.True(
            context.ViewModel.GenerationProgress
                .IsIndeterminate &&
            context.ViewModel.GenerationProgress
                .SourceProgressText?
                .Contains(
                    "Video 1 of 1",
                    StringComparison.Ordinal) ==
            true,
            "Active evidence passes should be indeterminate and identify the source.");

        context.EvidenceCoordinator.ReleaseAnalysis();
        await continuation;
    }

    private static async Task
        ModeAndSetupChangesReuseEvidence()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests
                .CreateOptions();

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        GenerationEvidenceAnalysisResult firstEvidence =
            context.ViewModel
                .CurrentEvidenceAnalysis!;

        context.ViewModel.GenerationProgress
            .ReturnToSourceSelectionCommand
            .Execute(null);

        context.ViewModel.IsMontageSelected = true;

        context.Dialog.Result =
            new GenerationSetupOptions(
                GenerationMode.Montage,
                DetectionMethod.Heuristics,
                AudioSelectionMode.Auto,
                desiredResultCount: 24,
                qualityThreshold: 91,
                ContentEmphasis.CommentaryFocused);

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        TestAssert.Equal(
            1,
            context.EvidenceCoordinator
                .AnalysisRunCount,
            "Mode, clip count, quality, and emphasis should not rerun evidence.");

        TestAssert.True(
            context.ViewModel.CurrentEvidenceAnalysis
                is not null,
            "Compatible evidence should remain available.");

        TestAssert.Same(
            firstEvidence.Sources[0].Evidence,
            context.ViewModel
                .CurrentEvidenceAnalysis!
                .Sources[0].Evidence,
            "Immutable evidence payloads should remain reusable across setup changes.");
    }

    private static async Task
        UnchangedLayoutReusesEvidence()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests
                .CreateOptions();

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        context.ViewModel.GenerationProgress
            .ReturnToSourceSelectionCommand
            .Execute(null);

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        TestAssert.Equal(
            1,
            context.EvidenceCoordinator
                .AnalysisRunCount,
            "A semantically identical new review object should reuse evidence.");

        TestAssert.Equal(
            1,
            context.EvidenceCoordinator
                .CacheHitCount,
            "The reopened layout should take the saved-evidence path.");
    }

    private static async Task ChangedLayoutRerunsEvidence()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests
                .CreateOptions();

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        context.ViewModel.GenerationProgress
            .ReturnToSourceSelectionCommand
            .Execute(null);

        context.CompositionDialog.OnShow =
            (
                request,
                initial) =>
                CreateChangedCompositionReview(
                    request.Preparation);

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        TestAssert.Equal(
            2,
            context.EvidenceCoordinator
                .AnalysisRunCount,
            "A real composition geometry change should rerun evidence.");
    }

    private static async Task
        ReviewCancellationRetainsEvidence()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests
                .CreateOptions();

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        GenerationEvidenceAnalysisResult prior =
            context.ViewModel
                .CurrentEvidenceAnalysis!;

        context.ViewModel.GenerationProgress
            .ReturnToSourceSelectionCommand
            .Execute(null);

        context.CompositionDialog.Cancel = true;

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        TestAssert.Same(
            prior,
            context.ViewModel
                .CurrentEvidenceAnalysis!,
            "Cancelling composition review should retain prior evidence.");
    }

    private static async Task
        SourceChangeInvalidatesEvidence()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests
                .CreateOptions();

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        context.ViewModel.GenerationProgress
            .ReturnToSourceSelectionCommand
            .Execute(null);

        context.ViewModel.AddDroppedFiles(
        [
            TestMediaFactory
                .CreateExistingSourcePath(
                    "evidence-source-change.mkv"),
        ]);

        TestAssert.Null(
            context.ViewModel.CurrentEvidenceAnalysis,
            "Source mutation should invalidate evidence.");
    }

    private static async Task
        CancelCommandCancelsEvidence()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests
                .CreateOptions();

        context.EvidenceCoordinator.BlockAnalysis();

        Task continuation =
            context.ViewModel
                .ContinueToGenerationSetupAsync();

        context.ViewModel.GenerationProgress
            .CancelCommand.Execute(null);

        await continuation;

        TestAssert.True(
            context.EvidenceCoordinator
                .CancellationObserved,
            "CancelActiveOperation should cancel the active evidence token.");
    }

    private static async Task DisposeCancelsEvidence()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests
                .CreateOptions();

        context.EvidenceCoordinator.BlockAnalysis();

        Task continuation =
            context.ViewModel
                .ContinueToGenerationSetupAsync();

        context.ViewModel.Dispose();

        await continuation;

        TestAssert.True(
            context.EvidenceCoordinator
                .CancellationObserved,
            "Disposal should cancel active evidence analysis.");

        TestAssert.Equal(
            0,
            context.Runner.Requests.Count,
            "A disposed workflow must not reach preflight.");
    }

    private static async Task
        ReturnIsBlockedDuringEvidence()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests
                .CreateOptions();

        context.EvidenceCoordinator.BlockAnalysis();

        Task continuation =
            context.ViewModel
                .ContinueToGenerationSetupAsync();

        TestAssert.False(
            context.ViewModel.GenerationProgress
                .ReturnToSourceSelectionCommand
                .CanExecute(null),
            "Return should be blocked while evidence analysis is active.");

        context.EvidenceCoordinator.ReleaseAnalysis();
        await continuation;
    }

}
