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
        new(
            "ICommand async failures reach the application error boundary",
            RoutesICommandFailure),
        new(
            "Async command lifetime includes ICommand failure reporting",
            LifetimeIncludesFailureReporting),
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

    private static async Task RoutesICommandFailure()
    {
        var expected = new IOException("Synthetic routed command failure.");
        var routed = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<AsyncCommandFailureEventArgs> handler =
            (_, eventArgs) => routed.TrySetResult(eventArgs.Exception);
        AsyncDelegateCommand.UnhandledExecutionFailure += handler;
        try
        {
            var command = new AsyncDelegateCommand(
                () => Task.FromException(expected));
            command.Execute(null);
            Exception actual = await routed.Task.WaitAsync(
                TimeSpan.FromSeconds(5));
            TestAssert.Same(
                expected,
                actual,
                "The ICommand boundary replaced the original failure.");
        }
        finally
        {
            AsyncDelegateCommand.UnhandledExecutionFailure -= handler;
        }
    }

    private static async Task LifetimeIncludesFailureReporting()
    {
        var lifetime = new AsyncOperationLifetime();
        var releaseFailure = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseReport = new ManualResetEventSlim();
        var reportReached = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new AsyncDelegateCommand(
            async () =>
            {
                await releaseFailure.Task;
                throw new InvalidOperationException("Expected failure.");
            },
            operationLifetime: lifetime);
        EventHandler<AsyncCommandFailureEventArgs> handler = (_, _) =>
        {
            reportReached.TrySetResult();
            releaseReport.Wait();
        };
        AsyncDelegateCommand.UnhandledExecutionFailure += handler;
        Task stop = Task.CompletedTask;
        try
        {
            command.Execute(null);
            stop = lifetime.Seal();
            releaseFailure.TrySetResult();
            await reportReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            TestAssert.False(
                stop.IsCompleted,
                "The command lifetime must include its final failure report.");
        }
        finally
        {
            releaseReport.Set();
            await stop.WaitAsync(TimeSpan.FromSeconds(5));
            AsyncDelegateCommand.UnhandledExecutionFailure -= handler;
        }
    }
}
