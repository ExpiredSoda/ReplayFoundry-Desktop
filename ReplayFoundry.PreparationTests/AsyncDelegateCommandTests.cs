using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.PreparationTests;

internal static class AsyncDelegateCommandTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Async command ExecuteAsync runs its operation",
            RunsOperation),
        new(
            "Async command blocks duplicate execution while running",
            BlocksDuplicateExecution),
        new(
            "Async command raises CanExecuteChanged around execution",
            RaisesCanExecuteChanged),
        new(
            "Async command respects external CanExecute",
            RespectsExternalCanExecute),
        new(
            "Async command preserves execution exceptions",
            PreservesExceptions),
    ];

    private static async Task RunsOperation()
    {
        int executions = 0;

        var command =
            new AsyncDelegateCommand(
                () =>
                {
                    executions++;
                    return Task.CompletedTask;
                });

        await command.ExecuteAsync();

        TestAssert.Equal(
            1,
            executions,
            "ExecuteAsync should run the supplied operation.");
    }

    private static async Task BlocksDuplicateExecution()
    {
        var gate =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var command =
            new AsyncDelegateCommand(
                () => gate.Task);

        Task first = command.ExecuteAsync();

        TestAssert.False(
            command.CanExecute(null),
            "The command should be disabled while executing.");

        await TestAssert.ThrowsAsync<InvalidOperationException>(
            command.ExecuteAsync,
            "A duplicate execution should be rejected.");

        gate.SetResult();
        await first;
    }

    private static async Task RaisesCanExecuteChanged()
    {
        var gate =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var command =
            new AsyncDelegateCommand(
                () => gate.Task);

        int changes = 0;

        command.CanExecuteChanged +=
            (_, _) => changes++;

        Task execution = command.ExecuteAsync();

        TestAssert.Equal(
            1,
            changes,
            "Execution start should raise CanExecuteChanged.");

        gate.SetResult();
        await execution;

        TestAssert.Equal(
            2,
            changes,
            "Execution completion should raise CanExecuteChanged.");
    }

    private static async Task RespectsExternalCanExecute()
    {
        bool allowed = false;

        var command =
            new AsyncDelegateCommand(
                () => Task.CompletedTask,
                () => allowed);

        TestAssert.False(
            command.CanExecute(null),
            "External CanExecute should disable the command.");

        await TestAssert.ThrowsAsync<InvalidOperationException>(
            command.ExecuteAsync,
            "ExecuteAsync should reject externally disabled execution.");

        allowed = true;

        TestAssert.True(
            command.CanExecute(null),
            "External CanExecute should enable the command.");

    }

    private static async Task PreservesExceptions()
    {
        var expected =
            new InvalidOperationException(
                "Synthetic async command failure.");

        var command =
            new AsyncDelegateCommand(
                () => Task.FromException(expected));

        InvalidOperationException actual =
            await TestAssert.ThrowsAsync<
                InvalidOperationException>(
                command.ExecuteAsync,
                "Command failures should reach the caller.");

        TestAssert.Same(
            expected,
            actual,
            "The original execution failure should be preserved.");
    }
}
