using System;
using System.IO;

namespace ReplayFoundry.Desktop.Platform.Processes;

internal class ProcessExecutionException : Exception
{
    public ProcessExecutionException(
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "A process failure requires a message.",
                nameof(message));
        }
    }
}

internal sealed class ProcessOutputLimitException :
    ProcessExecutionException
{
    public ProcessOutputLimitException(
        string streamName,
        int maximumCharacters)
        : base(
            $"The process produced more than {maximumCharacters:N0} " +
            $"characters on {streamName}.")
    {
    }
}

internal sealed class ProcessTimeoutException :
    ProcessExecutionException
{
    public ProcessTimeoutException(
        string executablePath,
        TimeSpan timeout)
        : base(
            $"'{Path.GetFileName(executablePath)}' " +
            $"did not finish within {timeout.TotalSeconds:0} seconds.")
    {
        ExecutablePath = executablePath;
        Timeout = timeout;
    }

    public string ExecutablePath { get; }

    public TimeSpan Timeout { get; }
}
