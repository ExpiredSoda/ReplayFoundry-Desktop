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
    private static Task
        SourceSelectionPreservesOrderReferenceAndValidation()
    {
        ViewModelContext context =
            CreateContext();

        string secondPath =
            TestMediaFactory.CreateExistingSourcePath(
                "generate-workflow-second.mp4");

        context.ViewModel.AddDroppedFiles(
        [
            context.PrimaryPath.ToUpperInvariant(),
            secondPath,
        ]);

        TestAssert.Equal(
            2,
            context.ViewModel.SelectedSourceCount,
            "A case-insensitive duplicate must not change the source count.");

        TestAssert.Equal(
            context.PrimaryPath,
            context.ViewModel.SelectedSources[0].FullPath,
            "The initial source must retain its ordered position.");

        TestAssert.Equal(
            secondPath,
            context.ViewModel.SelectedSources[1].FullPath,
            "A later source must be appended in candidate order.");

        TestAssert.True(
            context.ViewModel.SelectedSources[0].IsReference,
            "The first selected source must remain the explicit reference.");

        TestAssert.False(
            context.ViewModel.SelectedSources[1].IsReference,
            "Additional sources must not silently become the reference.");

        TestAssert.Equal(
            "2 files selected",
            context.ViewModel.SelectionSummary,
            "The selection summary must preserve its current plural copy.");

        TestAssert.True(
            context.ViewModel.ValidationMessage?.Contains(
                "already",
                StringComparison.OrdinalIgnoreCase) == true,
            "A case-insensitive duplicate must produce the current validation message.");

        TestAssert.Equal(
            Path.GetFileName(context.PrimaryPath),
            context.ViewModel.SelectedSources[0].FileName,
            "The selected source must preserve its display file name.");

        TestAssert.Equal(
            Path.GetDirectoryName(context.PrimaryPath) ?? string.Empty,
            context.ViewModel.SelectedSources[0].DirectoryPath,
            "The selected source must preserve its display directory.");

        context.ViewModel.ClearSelectionCommand.Execute(null);

        TestAssert.Equal(
            0,
            context.ViewModel.SelectedSourceCount,
            "Clear Selection must remove every selected source.");

        TestAssert.Null(
            context.ViewModel.ValidationMessage,
            "Clear Selection must clear source validation text.");

        return Task.CompletedTask;
    }

    private static Task
        SourceSelectionRaisesProjectionNotifications()
    {
        ViewModelContext context =
            CreateContext();

        var propertyNames =
            new HashSet<string>(
                StringComparer.Ordinal);

        context.ViewModel.PropertyChanged +=
            (_, eventArgs) =>
            {
                if (eventArgs.PropertyName is not null)
                {
                    propertyNames.Add(
                        eventArgs.PropertyName);
                }
            };

        context.ViewModel.ClearSelectionCommand.Execute(null);

        TestAssert.True(
            propertyNames.Contains(
                nameof(GenerateViewModel.HasSelectedSources)),
            "Clear Selection must notify HasSelectedSources.");

        TestAssert.True(
            propertyNames.Contains(
                nameof(GenerateViewModel.SelectedSourceCount)),
            "Clear Selection must notify SelectedSourceCount.");

        TestAssert.True(
            propertyNames.Contains(
                nameof(GenerateViewModel.SelectionSummary)),
            "Clear Selection must notify SelectionSummary.");

        TestAssert.False(
            context.ViewModel.ClearSelectionCommand.CanExecute(null),
            "Clear Selection must disable itself after the collection is cleared.");

        TestAssert.False(
            context.ViewModel.ContinueToGenerationSetupCommand
                .CanExecute(null),
            "Continue must disable itself after the collection is cleared.");

        return Task.CompletedTask;
    }

    private static Task SingleFilePickerAddsSource()
    {
        string selectedPath =
            TestMediaFactory.CreateExistingSourcePath(
                "generate-picker-single.mov");

        ViewModelContext context =
            CreateContext(
                new RecordingVideoFilePicker(
                    [selectedPath],
                    []),
                seedSource: false);

        context.ViewModel.SelectSingleFileCommand.Execute(null);

        TestAssert.Equal(
            1,
            context.ViewModel.SelectedSourceCount,
            "Single-file selection must add exactly one source.");

        TestAssert.Equal(
            selectedPath,
            context.ViewModel.SelectedSources[0].FullPath,
            "Single-file selection must preserve the picker path.");

        return Task.CompletedTask;
    }

    private static Task MultipleFilePickerPreservesOrder()
    {
        string first =
            TestMediaFactory.CreateExistingSourcePath(
                "generate-picker-first.mp4");
        string second =
            TestMediaFactory.CreateExistingSourcePath(
                "generate-picker-second.avi");

        ViewModelContext context =
            CreateContext(
                new RecordingVideoFilePicker(
                    [],
                    [first, second]),
                seedSource: false);

        context.ViewModel.SelectMultipleFilesCommand.Execute(null);

        TestAssert.Equal(
            first,
            context.ViewModel.SelectedSources[0].FullPath,
            "Multiple-file selection must preserve the first picker path.");

        TestAssert.Equal(
            second,
            context.ViewModel.SelectedSources[1].FullPath,
            "Multiple-file selection must preserve the second picker path.");

        return Task.CompletedTask;
    }

    private static Task PreservesBindingAndCommandSurface()
    {
        string[] propertyNames =
        [
            nameof(GenerateViewModel.SelectedSources),
            nameof(GenerateViewModel.GenerationProgress),
            nameof(GenerateViewModel.SelectSingleFileCommand),
            nameof(GenerateViewModel.SelectMultipleFilesCommand),
            nameof(GenerateViewModel.ClearSelectionCommand),
            nameof(GenerateViewModel.ContinueToGenerationSetupCommand),
            nameof(GenerateViewModel.IsSourceSelectionVisible),
            nameof(GenerateViewModel.IsProgressVisible),
            nameof(GenerateViewModel.IsIndividualClipsSelected),
            nameof(GenerateViewModel.IsMontageSelected),
            nameof(GenerateViewModel.HasSelectedSources),
            nameof(GenerateViewModel.SelectionSummary),
            nameof(GenerateViewModel.HasGenerationSetup),
            nameof(GenerateViewModel.GenerationSetupButtonText),
            nameof(GenerateViewModel.GenerationSetupSummary),
            nameof(GenerateViewModel.ValidationMessage),
        ];

        Type viewModelType =
            typeof(GenerateViewModel);

        foreach (string propertyName in propertyNames)
        {
            TestAssert.True(
                viewModelType.GetProperty(
                    propertyName) is not null,
                $"The live Generate binding property '{propertyName}' must remain available.");
        }

        return Task.CompletedTask;
    }

    private static Task DisposalDetachesStateNotifications()
    {
        ViewModelContext context =
            CreateContext();
        int notifications = 0;

        context.ViewModel.PropertyChanged +=
            (_, _) => notifications++;

        context.ViewModel.Dispose();
        context.SourceSelection.ReportValidation(
            "post-disposal validation");
        context.WorkflowSession.InvalidateAfterSourceChange();

        TestAssert.Equal(
            0,
            notifications,
            "Disposed Generate must not observe focused state-owner notifications.");

        return Task.CompletedTask;
    }

    private static async Task DialogWaitsForPreparation()
    {
        ViewModelContext context =
            CreateContext();

        context.Coordinator.BlockPreparation();

        Task continuation =
            context.ViewModel
                .ContinueToGenerationSetupAsync();

        TestAssert.Equal(
            0,
            context.Dialog.Requests.Count,
            "The dialog must wait for preparation.");

        context.Coordinator.ReleasePreparation();
        await continuation;

        TestAssert.Equal(
            1,
            context.Dialog.Requests.Count,
            "The dialog should open after preparation succeeds.");
    }

    private static async Task EntersPreparingState()
    {
        ViewModelContext context =
            CreateContext();

        context.Coordinator.BlockPreparation();

        Task continuation =
            context.ViewModel
                .ContinueToGenerationSetupAsync();

        TestAssert.Equal(
            GenerateWorkflowState.PreparingSources,
            context.ViewModel.WorkflowState,
            "Preparation should have an explicit workflow state.");

        TestAssert.Equal(
            "Cancel Preparation",
            context.ViewModel.GenerationProgress.CancelButtonLabel,
            "Preparation should expose the correct cancel label.");

        context.Coordinator.ReleasePreparation();
        await continuation;
    }

    private static async Task CachedPreparationDoesNotFlashProgress()
    {
        ViewModelContext context =
            CreateContext();

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        TestAssert.Equal(
            1,
            context.Coordinator.PreparationRunCount,
            "The first setup pass should prepare its source once.");

        var visibilitySamples = new List<(bool Source, bool Progress)>();
        context.ViewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName is
                nameof(GenerateViewModel.IsSourceSelectionVisible) or
                nameof(GenerateViewModel.IsProgressVisible))
            {
                visibilitySamples.Add((
                    context.ViewModel.IsSourceSelectionVisible,
                    context.ViewModel.IsProgressVisible));
            }
        };

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        TestAssert.Equal(
            1,
            context.Coordinator.PreparationRunCount,
            "Reopening setup should reuse the cached preparation.");
        TestAssert.False(
            visibilitySamples.Any(static sample => sample.Progress),
            "A cached preparation must not flash the progress surface before setup opens.");
        TestAssert.True(
            context.ViewModel.IsSourceSelectionVisible,
            "The source workspace should remain behind the setup dialog.");
    }

    private static async Task DisablesSourceCommands()
    {
        ViewModelContext context =
            CreateContext();

        context.Coordinator.BlockPreparation();

        Task continuation =
            context.ViewModel
                .ContinueToGenerationSetupAsync();

        TestAssert.False(
            context.ViewModel.SelectSingleFileCommand
                .CanExecute(null),
            "Single-file selection should be disabled.");

        TestAssert.False(
            context.ViewModel.ClearSelectionCommand
                .CanExecute(null),
            "Clear selection should be disabled.");

        TestAssert.False(
            context.ViewModel.ContinueToGenerationSetupCommand
                .CanExecute(null),
            "Continue should prevent duplicate execution.");

        context.Coordinator.ReleasePreparation();
        await continuation;
    }

    private static async Task ForwardsPreparationProgress()
    {
        ViewModelContext context =
            CreateContext();

        context.Coordinator.BlockPreparation();

        Task continuation =
            context.ViewModel
                .ContinueToGenerationSetupAsync();

        TestAssert.Equal(
            45d,
            context.ViewModel.GenerationProgress.ProgressPercent,
            "Preparation percentages should reach the progress ViewModel.");

        TestAssert.True(
            context.ViewModel.GenerationProgress
                .SourceProgressText?
                .Contains(
                    context.PrimaryPath,
                    StringComparison.Ordinal) == false,
            "Progress should show the source name rather than the full path.");

        TestAssert.True(
            context.ViewModel.GenerationProgress
                .SourceProgressText?
                .Contains(
                    Path.GetFileName(context.PrimaryPath),
                    StringComparison.Ordinal) == true,
            "Preparation progress should identify the active source.");

        context.Coordinator.ReleasePreparation();
        await continuation;
    }

    private static async Task CancellationSkipsSetup()
    {
        ViewModelContext context =
            CreateContext();

        context.Coordinator.BlockPreparation();

        Task continuation =
            context.ViewModel
                .ContinueToGenerationSetupAsync();

        context.ViewModel.GenerationProgress
            .CancelCommand.Execute(null);

        await continuation;

        TestAssert.Equal(
            GenerateWorkflowState.Cancelled,
            context.ViewModel.WorkflowState,
            "Cancellation should produce a cancelled state.");

        TestAssert.Equal(
            0,
            context.Dialog.Requests.Count,
            "Cancellation must not open setup.");
    }

    private static async Task FailureSkipsSetup()
    {
        ViewModelContext context =
            CreateContext();

        context.Coordinator.PreparationFailure =
            new GenerationSourcePreparationException(
                context.PrimaryPath,
                "Synthetic source preparation failure.",
                "Synthetic technical detail.");

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        TestAssert.Equal(
            GenerateWorkflowState.Failed,
            context.ViewModel.WorkflowState,
            "Preparation failure should show a failed state.");

        TestAssert.Equal(
            0,
            context.Dialog.Requests.Count,
            "Preparation failure must not open setup.");

        TestAssert.True(
            context.ViewModel.GenerationProgress
                .HasTechnicalDetails,
            "Preparation failure should retain technical details.");
    }

    private static async Task OpensSetupWithRetainedResult()
    {
        ViewModelContext context =
            CreateContext();

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        TestAssert.Same(
            context.Coordinator.Current!,
            context.Dialog.Requests[0].Preparation,
            "Setup should receive the exact retained preparation result.");
    }

    private static async Task SetupCancellationRetainsPreparation()
    {
        ViewModelContext context =
            CreateContext();

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        GenerationSourcePreparationResult retained =
            context.Coordinator.Current!;

        TestAssert.Same(
            retained,
            context.ViewModel.CurrentSourcePreparation!,
            "Cancelling setup should retain prepared sources.");

        TestAssert.Equal(
            1,
            context.Coordinator.PreparationRunCount,
            "Setup cancellation should not invalidate preparation.");
    }

    private static async Task ReopeningSetupReusesPreparation()
    {
        ViewModelContext context =
            CreateContext();

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        TestAssert.Equal(
            2,
            context.Dialog.Requests.Count,
            "Setup should open on both attempts.");

        TestAssert.Equal(
            1,
            context.Coordinator.PreparationRunCount,
            "The second attempt should reuse retained preparation.");
    }

    private static async Task SourceMutationInvalidatesState()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests.CreateOptions();

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        context.ViewModel.GenerationProgress
            .ReturnToSourceSelectionCommand.Execute(null);

        string secondPath =
            TestMediaFactory.CreateExistingSourcePath(
                "source-mutation-second.mkv");

        context.ViewModel.AddDroppedFiles(
            [secondPath]);

        TestAssert.Null(
            context.ViewModel.CurrentSourcePreparation,
            "Source mutation should discard preparation.");

        TestAssert.False(
            context.ViewModel.HasGenerationSetup,
            "Source mutation should discard saved setup.");

        TestAssert.False(
            context.ViewModel.HasCompositionReview,
            "Source mutation should discard confirmed layouts.");
    }

    private static async Task ModeChangeRetainsPreparation()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests.CreateOptions();

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        GenerationSourcePreparationResult retained =
            context.ViewModel.CurrentSourcePreparation!;

        GenerationCompositionReviewResult retainedComposition =
            context.ViewModel.CurrentCompositionReview!;

        context.ViewModel.GenerationProgress
            .ReturnToSourceSelectionCommand.Execute(null);

        context.ViewModel.IsMontageSelected = true;

        TestAssert.Same(
            retained,
            context.ViewModel.CurrentSourcePreparation!,
            "Mode changes should retain media preparation.");

        TestAssert.False(
            context.ViewModel.HasGenerationSetup,
            "Mode changes should clear mode-specific setup.");

        TestAssert.Same(
            retainedComposition,
            context.ViewModel.CurrentCompositionReview!,
            "Mode changes should retain source-layout confirmation.");
    }

    private static async Task StalenessAfterSetupBlocksGeneration()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests.CreateOptions();

        context.Dialog.OnShow =
            request =>
            {
                context.Coordinator.FreshnessFailure =
                    new GenerationSourcePreparationException(
                        request.ReferenceSource.FullPath,
                        "The reference changed while setup was open.");

                return context.Dialog.Result;
            };

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        TestAssert.Equal(
            0,
            context.Runner.Requests.Count,
            "Stale preparation must block generation.");

        TestAssert.Null(
            context.ViewModel.CurrentSourcePreparation,
            "Stale preparation should be invalidated.");

        TestAssert.False(
            context.ViewModel.HasGenerationSetup,
            "Setup based on stale data should be invalidated.");
    }

    private static async Task SetupCreatesPreparedGenerationRequest()
    {
        ViewModelContext context =
            CreateContext();

        context.Dialog.Result =
            PreparedGenerationWorkflowTests.CreateOptions();

        await context.ViewModel
            .ContinueToGenerationSetupAsync();

        TestAssert.Equal(
            1,
            context.Runner.Requests.Count,
            "Successful setup should start generation preflight.");

        TestAssert.Same(
            context.Coordinator.Current!,
            context.Runner.Requests[0].Preparation,
            "Generation should use the exact retained preparation result.");

        TestAssert.Same(
            context.ViewModel.CurrentCompositionReview!,
            context.Runner.Requests[0].CompositionReview,
            "Generation should use the completed composition review.");
    }

}
