using System;
using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReplayFoundry.Desktop.Platform.Processes;

internal sealed class WindowsProcessRunner : IProcessRunner
{
    private const int ReadBufferSize = 8192;

    public async Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        var startInfo =
            new ProcessStartInfo
            {
                FileName = request.ExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory =
                    request.WorkingDirectory ??
                    Path.GetDirectoryName(
                        request.ExecutablePath) ??
                    AppContext.BaseDirectory,
            };

        foreach (string argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach ((string name, string value) in request.EnvironmentVariables)
        {
            startInfo.Environment[name] = value;
        }

        using var process =
            new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };

        using WindowsProcessJob processJob =
            CreateProtectedJob(
                request.ExecutablePath);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!process.Start())
            {
                throw new ProcessExecutionException(
                    $"Windows did not start '{request.ExecutablePath}'.");
            }
        }
        catch (Exception exception)
            when (exception is Win32Exception or
                  InvalidOperationException)
        {
            throw new ProcessExecutionException(
                $"Replay Foundry could not start '{request.ExecutablePath}'.",
                exception);
        }

        try
        {
            processJob.Assign(
                process);
        }
        catch (Exception exception)
            when (exception is Win32Exception or
                  InvalidOperationException)
        {
            processJob.TryTerminate();
            TryTerminateProcess(process);
            await ObserveTerminationAsync(process);

            throw new ProcessExecutionException(
                $"Replay Foundry could not protect '{request.ExecutablePath}' " +
                "from surviving its owning operation.",
                exception);
        }

        using var timeoutSource =
            new CancellationTokenSource(
                request.Timeout);

        using var linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);

        Task<string> standardOutputTask =
            ReadBoundedAsync(
                process.StandardOutput,
                request.MaxStandardOutputCharacters,
                "standard output",
                linkedSource.Token);

        Task<string> standardErrorTask =
            ReadBoundedAsync(
                process.StandardError,
                request.MaxStandardErrorCharacters,
                "standard error",
                linkedSource.Token);

        Task waitForExitTask =
            WaitForExitAndTerminateDescendantsAsync(
                process,
                processJob,
                linkedSource.Token);

        try
        {
            Task firstCompleted =
                await Task.WhenAny(
                    waitForExitTask,
                    standardOutputTask,
                    standardErrorTask);

            await firstCompleted;

            await Task.WhenAll(
                waitForExitTask,
                standardOutputTask,
                standardErrorTask);
        }
        catch (OperationCanceledException)
        {
            processJob.TryTerminate();
            TryTerminateProcess(process);

            await ObserveTerminationAsync(process);
            await ObserveAsync(standardOutputTask);
            await ObserveAsync(standardErrorTask);

            cancellationToken.ThrowIfCancellationRequested();

            throw new ProcessTimeoutException(
                request.ExecutablePath,
                request.Timeout);
        }
        catch
        {
            processJob.TryTerminate();
            TryTerminateProcess(process);

            await ObserveTerminationAsync(process);
            await ObserveAsync(standardOutputTask);
            await ObserveAsync(standardErrorTask);

            throw;
        }
        finally
        {
            stopwatch.Stop();
        }

        return new ProcessRunResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask,
            stopwatch.Elapsed);
    }

    private static WindowsProcessJob CreateProtectedJob(
        string executablePath)
    {
        try
        {
            return WindowsProcessJob.CreateKillOnClose();
        }
        catch (Win32Exception exception)
        {
            throw new ProcessExecutionException(
                $"Replay Foundry could not create process protection for " +
                $"'{executablePath}'.",
                exception);
        }
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        int maximumCharacters,
        string streamName,
        CancellationToken cancellationToken)
    {
        char[] buffer =
            ArrayPool<char>.Shared.Rent(
                ReadBufferSize);

        var builder =
            new StringBuilder(
                Math.Min(
                    maximumCharacters,
                    64 * 1024));

        try
        {
            while (true)
            {
                int charactersRead =
                    await reader.ReadAsync(
                        buffer.AsMemory(
                            0,
                            ReadBufferSize),
                        cancellationToken);

                if (charactersRead == 0)
                {
                    break;
                }

                if (builder.Length + charactersRead >
                    maximumCharacters)
                {
                    throw new ProcessOutputLimitException(
                        streamName,
                        maximumCharacters);
                }

                builder.Append(
                    buffer,
                    0,
                    charactersRead);
            }

            return builder.ToString();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(
                buffer);
        }
    }

    private static async Task WaitForExitAndTerminateDescendantsAsync(
        Process process,
        WindowsProcessJob processJob,
        CancellationToken cancellationToken)
    {
        await process.WaitForExitAsync(
            cancellationToken);

        // The root owns the operation. Once it exits, background descendants
        // are no longer valid work and may otherwise keep redirected handles
        // open indefinitely. Terminating the job also closes those handles so
        // the bounded output readers can finish without waiting for timeout.
        processJob.TryTerminate();
    }

    private static void TryTerminateProcess(
        Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(
                    entireProcessTree: true);
            }
        }
        catch (Exception exception)
            when (exception is
                  InvalidOperationException or
                  Win32Exception or
                  NotSupportedException)
        {
            // The process is already gone or Windows rejected termination.
            // The original failure remains the error reported to the caller.
        }
    }

    private static async Task ObserveTerminationAsync(
        Process process)
    {
        try
        {
            await process
                .WaitForExitAsync(
                    CancellationToken.None)
                .WaitAsync(
                    TimeSpan.FromSeconds(5));
        }
        catch (Exception exception)
            when (exception is
                  InvalidOperationException or
                  Win32Exception or
                  TimeoutException)
        {
            // Cleanup is best effort after the primary failure.
        }
    }

    private static async Task ObserveAsync(
        Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // Cleanup observes and suppresses secondary task failures.
        }
    }
}
