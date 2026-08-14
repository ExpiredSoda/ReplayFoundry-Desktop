using System;

namespace ReplayFoundry.Desktop.Platform.Processes;

internal sealed class ProcessRunResult
{
    public ProcessRunResult(
        int exitCode,
        string standardOutput,
        string standardError,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                "Process duration cannot be negative.");
        }

        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
        Duration = duration;
    }

    public int ExitCode { get; }

    public string StandardOutput { get; }

    public string StandardError { get; }

    public TimeSpan Duration { get; }

    public bool Succeeded =>
        ExitCode == 0;
}
