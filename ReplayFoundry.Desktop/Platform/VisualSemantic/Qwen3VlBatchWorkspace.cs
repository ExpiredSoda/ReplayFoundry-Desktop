using System.IO;
using System.Security;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal interface IQwen3VlBatchWorkspaceFactory
{
    Qwen3VlBatchWorkspace Create();
}

internal sealed class SystemQwen3VlBatchWorkspaceFactory :
    IQwen3VlBatchWorkspaceFactory
{
    public Qwen3VlBatchWorkspace Create()
    {
        string root =
            Path.Combine(
                Path.GetTempPath(),
                "ReplayFoundry",
                "VisualSemantic");
        Directory.CreateDirectory(root);

        for (int attempt = 0; attempt < 10; attempt++)
        {
            string directory =
                Path.Combine(root, Guid.NewGuid().ToString("N"));

            if (Directory.Exists(directory))
            {
                continue;
            }

            Directory.CreateDirectory(directory);

            return new Qwen3VlBatchWorkspace(
                directory,
                Path.Combine(directory, "probe.json"),
                Path.Combine(directory, "input-batch.json"),
                Path.Combine(directory, "output-batch.json"),
                Path.Combine(directory, "attempt-batch.json"));
        }

        throw new IOException(
            "Replay Foundry could not allocate a Qwen batch workspace.");
    }
}

internal sealed class Qwen3VlBatchWorkspace
{
    public Qwen3VlBatchWorkspace(
        string directoryPath,
        string probeOutputPath,
        string inputBatchPath,
        string outputBatchPath,
        string? attemptOutputPath = null)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) ||
            !Path.IsPathFullyQualified(directoryPath) ||
            !Directory.Exists(directoryPath))
        {
            throw new ArgumentException(
                "A Qwen workspace directory must be fully qualified and existing.",
                nameof(directoryPath));
        }

        DirectoryPath = Path.GetFullPath(directoryPath);
        ProbeOutputPath = RequireOwned(
            probeOutputPath,
            nameof(probeOutputPath));
        InputBatchPath = RequireOwned(
            inputBatchPath,
            nameof(inputBatchPath));
        OutputBatchPath = RequireOwned(
            outputBatchPath,
            nameof(outputBatchPath));
        AttemptOutputPath = RequireOwned(
            attemptOutputPath ??
            Path.Combine(
                DirectoryPath,
                "attempt-batch.json"),
            nameof(attemptOutputPath));
        SamplingAuditOutputPath = RequireOwned(
            Path.Combine(
                DirectoryPath,
                "sampling-audit.json"),
            nameof(SamplingAuditOutputPath));
        FailureOutputPath = RequireOwned(
            Path.Combine(
                DirectoryPath,
                "failure.json"),
            nameof(FailureOutputPath));

        if (new[]
            {
                ProbeOutputPath,
                InputBatchPath,
                OutputBatchPath,
                AttemptOutputPath,
                SamplingAuditOutputPath,
                FailureOutputPath,
            }.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 6)
        {
            throw new ArgumentException(
                "Qwen workspace files must be distinct.");
        }
    }

    public string DirectoryPath { get; }

    public string ProbeOutputPath { get; }

    public string InputBatchPath { get; }

    public string OutputBatchPath { get; }

    public string AttemptOutputPath { get; }

    public string SamplingAuditOutputPath { get; }

    public string FailureOutputPath { get; }

    public void Cleanup()
    {
        Exception? failure = TryCleanup();

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(failure)
                .Throw();
        }
    }

    public Exception? TryCleanup()
    {
        try
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }

            return null;
        }
        catch (Exception exception)
            when (exception is
                  IOException or
                  UnauthorizedAccessException or
                  SecurityException)
        {
            return new Qwen3VlInferenceException(
                "Replay Foundry could not clean up the Qwen batch workspace.",
                innerException: exception);
        }
    }

    private string RequireOwned(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Path.IsPathFullyQualified(value))
        {
            throw new ArgumentException(
                "Qwen workspace file paths must be fully qualified.",
                parameterName);
        }

        string full = Path.GetFullPath(value);

        if (!string.Equals(
                Path.GetDirectoryName(full),
                DirectoryPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Qwen workspace files must belong to the owned workspace.",
                parameterName);
        }

        return full;
    }
}
