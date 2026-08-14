using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.PreparationTests;

internal static class WindowsProcessRunnerTests
{
    private const string EchoMode =
        "--replay-foundry-process-runner-echo";

    private const string TreeMode =
        "--replay-foundry-process-runner-tree";

    private const string DescendantMode =
        "--replay-foundry-process-runner-descendant";

    private const string OwnerMode =
        "--replay-foundry-process-runner-owner";

    private const string TestEnvironmentVariable =
        "REPLAYFOUNDRY_PROCESS_RUNNER_TEST_VALUE";

    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Windows process runner preserves launch and stream semantics",
            PreservesLaunchAndStreamSemantics),
        new(
            "Windows process runner closes completed process jobs",
            ClosesCompletedProcessJobs),
        new(
            "Windows process runner cancellation terminates descendants",
            CancellationTerminatesDescendants),
        new(
            "Windows process runner hard timeout terminates descendants",
            HardTimeoutTerminatesDescendants),
        new(
            "Windows process runner owner exit terminates descendants",
            OwnerExitTerminatesDescendants),
    ];

    public static bool IsTestHostRequest(
        IReadOnlyList<string> arguments) =>
        arguments.Count > 0 &&
        arguments[0] is
            EchoMode or
            TreeMode or
            DescendantMode or
            OwnerMode;

    public static async Task<int> RunTestHostAsync(
        IReadOnlyList<string> arguments)
    {
        return arguments[0] switch
        {
            EchoMode =>
                RunEchoHost(
                    arguments.Skip(1).ToArray()),
            TreeMode =>
                await RunTreeHostAsync(
                    arguments),
            DescendantMode =>
                await RunDescendantHostAsync(
                    arguments),
            OwnerMode =>
                await RunOwnerHostAsync(
                    arguments),
            _ =>
                throw new InvalidOperationException(
                    "Unknown process-runner test-host mode."),
        };
    }

    private static async Task PreservesLaunchAndStreamSemantics()
    {
        string root =
            CreateTestRoot();

        try
        {
            string[] expectedArguments =
            [
                "alpha beta",
                "quote\"value",
                "trailing\\",
                "snow-雪",
            ];

            const string expectedEnvironment =
                "environment value 雪";

            var request =
                CreateSelfRequest(
                    [
                        EchoMode,
                        .. expectedArguments,
                    ],
                    root,
                    new Dictionary<string, string>
                    {
                        [TestEnvironmentVariable] =
                            expectedEnvironment,
                    });

            var runner =
                new WindowsProcessRunner();

            ProcessRunResult result =
                await runner.RunAsync(
                    request,
                    CancellationToken.None);

            EchoPayload? payload =
                JsonSerializer.Deserialize<EchoPayload>(
                    result.StandardOutput);

            TestAssert.Equal(
                23,
                result.ExitCode,
                "The native process exit code should be preserved.");

            TestAssert.Equal(
                "process-runner-stderr-雪",
                result.StandardError,
                "UTF-8 standard error should be preserved.");

            TestAssert.True(
                payload is not null,
                "The test host should return its launch payload.");

            TestAssert.True(
                expectedArguments.SequenceEqual(
                    payload!.Arguments,
                    StringComparer.Ordinal),
                "ArgumentList quoting should remain lossless.");

            TestAssert.Equal(
                root,
                payload.WorkingDirectory,
                "The requested working directory should be preserved.");

            TestAssert.Equal(
                expectedEnvironment,
                payload.EnvironmentValue,
                "Environment overlays should be preserved.");
        }
        finally
        {
            await DeleteTestRootAsync(
                root);
        }
    }

    private static async Task ClosesCompletedProcessJobs()
    {
        string root =
            CreateTestRoot();

        string rootPidPath =
            Path.Combine(
                root,
                "root.pid");

        string descendantPidPath =
            Path.Combine(
                root,
                "descendant.pid");

        int? rootProcessId = null;
        int? descendantProcessId = null;

        try
        {
            var runner =
                new WindowsProcessRunner();

            ProcessRunResult result =
                await runner.RunAsync(
                    CreateTreeRequest(
                        root,
                        rootPidPath,
                        descendantPidPath,
                        remainRunning: false),
                    CancellationToken.None);

            rootProcessId =
                await ReadProcessIdAsync(
                    rootPidPath);

            descendantProcessId =
                await ReadProcessIdAsync(
                    descendantPidPath);

            TestAssert.Equal(
                0,
                result.ExitCode,
                "The tree root should complete normally.");

            await AssertProcessExitedAsync(
                rootProcessId.Value,
                "The completed root should exit.");

            await AssertProcessExitedAsync(
                descendantProcessId.Value,
                "Closing the completed run's job must terminate its descendant.");
        }
        finally
        {
            TryKillProcess(
                rootProcessId);

            TryKillProcess(
                descendantProcessId);

            await DeleteTestRootAsync(
                root);
        }
    }

    private static async Task CancellationTerminatesDescendants()
    {
        string root =
            CreateTestRoot();

        string rootPidPath =
            Path.Combine(
                root,
                "root.pid");

        string descendantPidPath =
            Path.Combine(
                root,
                "descendant.pid");

        int? rootProcessId = null;
        int? descendantProcessId = null;

        using var cancellationSource =
            new CancellationTokenSource();

        try
        {
            var runner =
                new WindowsProcessRunner();

            Task<ProcessRunResult> runTask =
                runner.RunAsync(
                    CreateTreeRequest(
                        root,
                        rootPidPath,
                        descendantPidPath,
                        remainRunning: true),
                    cancellationSource.Token);

            rootProcessId =
                await ReadProcessIdAsync(
                    rootPidPath);

            descendantProcessId =
                await ReadProcessIdAsync(
                    descendantPidPath);

            cancellationSource.Cancel();

            OperationCanceledException exception =
                await TestAssert.ThrowsAsync<
                    OperationCanceledException>(
                    async () =>
                        await runTask,
                    "Caller cancellation should be preserved.");

            TestAssert.Equal(
                cancellationSource.Token,
                exception.CancellationToken,
                "Cancellation should retain the caller token.");

            await AssertProcessExitedAsync(
                rootProcessId.Value,
                "Cancellation must terminate the root process.");

            await AssertProcessExitedAsync(
                descendantProcessId.Value,
                "Cancellation must terminate every inherited descendant.");
        }
        finally
        {
            cancellationSource.Cancel();

            TryKillProcess(
                rootProcessId);

            TryKillProcess(
                descendantProcessId);

            await DeleteTestRootAsync(
                root);
        }
    }

    private static async Task OwnerExitTerminatesDescendants()
    {
        string root =
            CreateTestRoot();

        string rootPidPath =
            Path.Combine(
                root,
                "root.pid");

        string descendantPidPath =
            Path.Combine(
                root,
                "descendant.pid");

        int? rootProcessId = null;
        int? descendantProcessId = null;

        Process? owner = null;

        try
        {
            ProcessStartInfo startInfo =
                CreateSelfStartInfo(
                    [
                        OwnerMode,
                        rootPidPath,
                        descendantPidPath,
                        root,
                    ]);

            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            owner =
                Process.Start(
                    startInfo) ??
                throw new InvalidOperationException(
                    "The process-runner owner test host did not start.");

            rootProcessId =
                await ReadProcessIdAsync(
                    rootPidPath);

            descendantProcessId =
                await ReadProcessIdAsync(
                    descendantPidPath);

            owner.Kill(
                entireProcessTree: false);

            await owner
                .WaitForExitAsync()
                .WaitAsync(
                    TimeSpan.FromSeconds(5));

            await AssertProcessExitedAsync(
                rootProcessId.Value,
                "Closing the owning process must terminate the job root.");

            await AssertProcessExitedAsync(
                descendantProcessId.Value,
                "Closing the owning process must terminate every job descendant.");
        }
        finally
        {
            if (owner is not null)
            {
                try
                {
                    if (!owner.HasExited)
                    {
                        owner.Kill(
                            entireProcessTree: true);
                    }
                }
                catch (Exception exception)
                    when (exception is
                          InvalidOperationException or
                          System.ComponentModel.Win32Exception)
                {
                    // The explicit helper owner is already gone.
                }

                owner.Dispose();
            }

            TryKillProcess(
                rootProcessId);

            TryKillProcess(
                descendantProcessId);

            await DeleteTestRootAsync(
                root);
        }
    }

    private static async Task HardTimeoutTerminatesDescendants()
    {
        string root =
            CreateTestRoot();

        string rootPidPath =
            Path.Combine(
                root,
                "root.pid");

        string descendantPidPath =
            Path.Combine(
                root,
                "descendant.pid");

        int? rootProcessId = null;
        int? descendantProcessId = null;

        try
        {
            var runner =
                new WindowsProcessRunner();

            Task<ProcessRunResult> runTask =
                runner.RunAsync(
                    CreateTreeRequest(
                        root,
                        rootPidPath,
                        descendantPidPath,
                        remainRunning: true,
                        timeout: TimeSpan.FromSeconds(5)),
                    CancellationToken.None);

            rootProcessId =
                await ReadProcessIdAsync(
                    rootPidPath);

            descendantProcessId =
                await ReadProcessIdAsync(
                    descendantPidPath);

            ProcessTimeoutException exception =
                await TestAssert.ThrowsAsync<
                    ProcessTimeoutException>(
                    async () =>
                        await runTask,
                    "A hard process timeout should remain distinguishable.");

            TestAssert.True(
                exception.Message.Contains(
                    "did not finish within 5 seconds",
                    StringComparison.Ordinal),
                "The hard-timeout failure should report its configured deadline.");

            TestAssert.Equal(
                TimeSpan.FromSeconds(5),
                exception.Timeout,
                "The typed hard-timeout failure should retain its configured deadline.");

            TestAssert.Equal(
                Environment.ProcessPath!,
                exception.ExecutablePath,
                "The typed hard-timeout failure should retain its executable path.");

            await AssertProcessExitedAsync(
                rootProcessId.Value,
                "A hard timeout must terminate the root process.");

            await AssertProcessExitedAsync(
                descendantProcessId.Value,
                "A hard timeout must terminate every inherited descendant.");
        }
        finally
        {
            TryKillProcess(
                rootProcessId);

            TryKillProcess(
                descendantProcessId);

            await DeleteTestRootAsync(
                root);
        }
    }

    private static int RunEchoHost(
        string[] arguments)
    {
        Console.OutputEncoding =
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false);

        Console.SetError(
            new StreamWriter(
                Console.OpenStandardError(),
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            });

        Console.Out.Write(
            JsonSerializer.Serialize(
                new EchoPayload(
                    arguments,
                    Directory.GetCurrentDirectory(),
                    Environment.GetEnvironmentVariable(
                        TestEnvironmentVariable))));

        Console.Error.Write(
            "process-runner-stderr-雪");

        return 23;
    }

    private static async Task<int> RunTreeHostAsync(
        IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 5 ||
            !bool.TryParse(
                arguments[4],
                out bool remainRunning))
        {
            return 91;
        }

        string rootPidPath =
            arguments[1];

        string descendantPidPath =
            arguments[2];

        string readyPath =
            arguments[3];

        await File.WriteAllTextAsync(
            rootPidPath,
            Environment.ProcessId.ToString());

        ProcessStartInfo startInfo =
            CreateSelfStartInfo(
                [
                    DescendantMode,
                    descendantPidPath,
                ]);

        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        using Process descendant =
            Process.Start(
                startInfo) ??
            throw new InvalidOperationException(
                "The descendant test host did not start.");

        await WaitForFileAsync(
            descendantPidPath,
            TimeSpan.FromSeconds(10));

        await File.WriteAllTextAsync(
            readyPath,
            "ready");

        if (!remainRunning)
        {
            return 0;
        }

        await Task.Delay(
            Timeout.InfiniteTimeSpan);

        return 0;
    }

    private static async Task<int> RunDescendantHostAsync(
        IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 2)
        {
            return 92;
        }

        await File.WriteAllTextAsync(
            arguments[1],
            Environment.ProcessId.ToString());

        await Task.Delay(
            Timeout.InfiniteTimeSpan);

        return 0;
    }

    private static async Task<int> RunOwnerHostAsync(
        IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 4)
        {
            return 93;
        }

        var runner =
            new WindowsProcessRunner();

        await runner.RunAsync(
            CreateTreeRequest(
                arguments[3],
                arguments[1],
                arguments[2],
                remainRunning: true),
            CancellationToken.None);

        return 0;
    }

    private static ProcessRunRequest CreateTreeRequest(
        string workingDirectory,
        string rootPidPath,
        string descendantPidPath,
        bool remainRunning,
        TimeSpan? timeout = null)
    {
        string readyPath =
            Path.Combine(
                workingDirectory,
                "ready");

        return CreateSelfRequest(
            [
                TreeMode,
                rootPidPath,
                descendantPidPath,
                readyPath,
                remainRunning.ToString(),
            ],
            workingDirectory,
            timeout: timeout);
    }

    private static ProcessRunRequest CreateSelfRequest(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        TimeSpan? timeout = null)
    {
        return new ProcessRunRequest(
            GetTestHostPath(),
            arguments,
            timeout ??
                TimeSpan.FromSeconds(20),
            workingDirectory,
            environmentVariables:
                environmentVariables);
    }

    private static ProcessStartInfo CreateSelfStartInfo(
        IReadOnlyList<string> arguments)
    {
        var startInfo =
            new ProcessStartInfo
            {
                FileName = GetTestHostPath(),
                UseShellExecute = false,
                CreateNoWindow = true,
            };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(
                argument);
        }

        return startInfo;
    }

    private static string GetTestHostPath()
    {
        string assemblyPath =
            typeof(Program).Assembly.Location;

        string appHostPath =
            Path.ChangeExtension(
                assemblyPath,
                ".exe");

        if (!File.Exists(appHostPath))
        {
            throw new FileNotFoundException(
                "The process-runner test app host is unavailable.",
                appHostPath);
        }

        return appHostPath;
    }

    private static async Task<int> ReadProcessIdAsync(
        string path)
    {
        using var timeoutSource =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(10));

        string? value = null;

        while (value is null)
        {
            timeoutSource.Token.ThrowIfCancellationRequested();

            try
            {
                if (File.Exists(path))
                {
                    value =
                        await File.ReadAllTextAsync(
                            path,
                            timeoutSource.Token);
                }
            }
            catch (IOException)
            {
                // File creation becomes visible before the helper closes its
                // exclusive writer. Retry until the complete marker is readable.
            }

            if (value is null)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(25),
                    timeoutSource.Token);
            }
        }

        if (!int.TryParse(
                value,
                out int processId) ||
            processId <= 0)
        {
            throw new InvalidOperationException(
                $"The process marker '{path}' was invalid.");
        }

        return processId;
    }

    private static async Task WaitForFileAsync(
        string path,
        TimeSpan timeout)
    {
        using var timeoutSource =
            new CancellationTokenSource(
                timeout);

        while (!File.Exists(path))
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(25),
                timeoutSource.Token);
        }
    }

    private static async Task AssertProcessExitedAsync(
        int processId,
        string message)
    {
        DateTime deadline =
            DateTime.UtcNow +
            TimeSpan.FromSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            if (!IsProcessAlive(processId))
            {
                return;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(50));
        }

        TestAssert.False(
            IsProcessAlive(processId),
            message);
    }

    private static bool IsProcessAlive(
        int processId)
    {
        try
        {
            using Process process =
                Process.GetProcessById(
                    processId);

            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void TryKillProcess(
        int? processId)
    {
        if (processId is not > 0)
        {
            return;
        }

        try
        {
            using Process process =
                Process.GetProcessById(
                    processId.Value);

            if (!process.HasExited)
            {
                process.Kill(
                    entireProcessTree: true);
            }
        }
        catch (Exception exception)
            when (exception is
                  ArgumentException or
                  InvalidOperationException or
                  System.ComponentModel.Win32Exception)
        {
            // A failed test must not leave its explicit helper PID alive.
        }
    }

    private static string CreateTestRoot()
    {
        string root =
            Path.Combine(
                Path.GetTempPath(),
                "ReplayFoundryPreparationTests",
                "WindowsProcessRunner",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(
            root);

        return root;
    }

    private static async Task DeleteTestRootAsync(
        string root)
    {
        DateTime deadline =
            DateTime.UtcNow +
            TimeSpan.FromSeconds(5);

        while (Directory.Exists(root))
        {
            try
            {
                Directory.Delete(
                    root,
                    recursive: true);

                return;
            }
            catch (Exception exception)
                when ((exception is IOException or
                       UnauthorizedAccessException) &&
                      DateTime.UtcNow < deadline)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(25));
            }
        }
    }

    private sealed record EchoPayload(
        string[] Arguments,
        string WorkingDirectory,
        string? EnvironmentValue);
}
