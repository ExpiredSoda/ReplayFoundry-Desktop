using ReplayFoundry.Desktop.Features.Generate;
using ReplayFoundry.Desktop.Presentation;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerateViewModelWorkflowTests
{
    private static async Task FolderImportRestoresEditingAfterCompletion()
    {
        ViewModelContext context = CreateContext(seedSource: false);
        using GenerateViewModel viewModel = context.ViewModel;
        string folder = Directory.CreateTempSubdirectory("replay-foundry-folder-import-").FullName;
        string source = Path.Combine(folder, "stable.mkv");
        File.WriteAllText(source, "Stable folder-import eligibility fixture.");
        Task import = viewModel.AddFolderAsync(folder);
        try
        {
            TestAssert.False(viewModel.SelectSingleFileCommand.CanExecute(null),
                "A folder scan must block source-picker edits.");
            TestAssert.False(viewModel.ImportFolderCommand.CanExecute(null),
                "A second folder scan must not overlap the first.");
            TestAssert.Throws<InvalidOperationException>(
                () => viewModel.AddDroppedFiles([context.PrimaryPath]),
                "Drop edits must share the active operation gate.");
            TestAssert.Throws<InvalidOperationException>(
                () => viewModel.AddFolderAsync(folder),
                "Direct folder imports must share the active operation gate.");

            await import.WaitAsync(TimeSpan.FromSeconds(10));

            TestAssert.Equal(1, viewModel.SelectedSourceCount,
                "The stable recording should be admitted once.");
            TestAssert.Equal(source, viewModel.SelectedSources[0].FullPath,
                "Import must retain the original source path.");
            TestAssert.True(viewModel.SelectSingleFileCommand.CanExecute(null),
                "Completing the lease must restore the source picker.");
            TestAssert.True(viewModel.ImportFolderCommand.CanExecute(null),
                "Completing the lease must allow another folder import.");
            TestAssert.True(viewModel.ContinueToGenerationSetupCommand.CanExecute(null),
                "The imported source must be available for preparation.");
        }
        finally
        {
            await ((IApplicationStopParticipant)viewModel).StopAsync(CancellationToken.None);
            await import;
            Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task FolderImportFailureRestoresEditing()
    {
        ViewModelContext context = CreateContext(seedSource: false);
        using GenerateViewModel viewModel = context.ViewModel;
        string missing = Path.Combine(Path.GetTempPath(), "missing-folder-" + Guid.NewGuid().ToString("N"));

        await viewModel.AddFolderAsync(missing);

        TestAssert.True(viewModel.FolderImportStatus?.StartsWith(
            "Folder import failed:", StringComparison.Ordinal) == true,
            "An invalid folder must produce the existing failure status.");
        TestAssert.Equal(0, viewModel.SelectedSourceCount,
            "A failed import must not add sources.");
        TestAssert.True(viewModel.SelectSingleFileCommand.CanExecute(null),
            "Failure must release the operation gate.");
        TestAssert.True(viewModel.ImportFolderCommand.CanExecute(null),
            "Failure must leave a retry available.");
    }

    private static async Task FolderImportShutdownDrainsFinalNotifications()
    {
        foreach (bool disposeFirst in new[] { false, true })
        {
            ViewModelContext context = CreateContext(seedSource: false);
            using GenerateViewModel viewModel = context.ViewModel;
            string folder = Directory.CreateTempSubdirectory("replay-foundry-folder-stop-").FullName;
            using var releaseNotification = new ManualResetEventSlim();
            var notificationReached = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(GenerateViewModel.FolderImportStatus) &&
                    viewModel.FolderImportStatus == "Folder import cancelled.")
                {
                    notificationReached.TrySetResult();
                    releaseNotification.Wait();
                }
            };
            Task import = viewModel.AddFolderAsync(folder);
            Task stop = Task.Run(async () =>
            {
                if (disposeFirst) viewModel.Dispose();
                await ((IApplicationStopParticipant)viewModel).StopAsync(CancellationToken.None);
            });
            try
            {
                await notificationReached.Task.WaitAsync(TimeSpan.FromSeconds(10));

                TestAssert.False(stop.IsCompleted,
                    "Stop must drain the full import, including notifications after its lease ends.");
                TestAssert.False(viewModel.ImportFolderCommand.CanExecute(null),
                    "Shutdown must seal folder ingress while the final notification drains.");
                TestAssert.False(viewModel.SelectSingleFileCommand.CanExecute(null),
                    "Shutdown must keep source editing disabled after cancellation.");
            }
            finally
            {
                releaseNotification.Set();
                await Task.WhenAll(import, stop).WaitAsync(TimeSpan.FromSeconds(10));
                Directory.Delete(folder);
            }

            TestAssert.Equal(0, viewModel.SelectedSourceCount,
                "A cancelled scan must not add any source.");
            TestAssert.Equal("Folder import cancelled.", viewModel.FolderImportStatus,
                "Stop and disposal must preserve cancellation feedback.");
            TestAssert.False(viewModel.ImportFolderCommand.CanExecute(null),
                "A stopped or disposed view must not accept another import.");
        }
    }
}
