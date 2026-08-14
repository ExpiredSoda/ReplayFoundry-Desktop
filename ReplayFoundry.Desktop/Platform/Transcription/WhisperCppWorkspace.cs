using System.IO;
using System.Security;

namespace ReplayFoundry.Desktop.Platform.Transcription;

internal interface IWhisperCppWorkspaceFactory
{
    WhisperCppWorkspace Create();
}

internal sealed class SystemWhisperCppWorkspaceFactory :
    IWhisperCppWorkspaceFactory
{
    public WhisperCppWorkspace Create()
    {
        string root =
            Path.Combine(
                Path.GetTempPath(),
                "ReplayFoundry",
                "Transcription");
        Directory.CreateDirectory(root);

        for (int attempt = 0;
             attempt < 10;
             attempt++)
        {
            string directory =
                Path.Combine(
                    root,
                    Guid.NewGuid().ToString("N"));

            if (Directory.Exists(directory))
            {
                continue;
            }

            Directory.CreateDirectory(directory);

            return new WhisperCppWorkspace(
                directory,
                Path.Combine(
                    directory,
                    "transcript"));
        }

        throw new IOException(
            "Replay Foundry could not allocate a transcription workspace.");
    }
}

internal sealed class WhisperCppWorkspace
{
    public WhisperCppWorkspace(
        string directoryPath,
        string outputPrefix)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) ||
            !Path.IsPathFullyQualified(directoryPath) ||
            !Directory.Exists(directoryPath) ||
            string.IsNullOrWhiteSpace(outputPrefix) ||
            !Path.IsPathFullyQualified(outputPrefix) ||
            !string.Equals(
                Path.GetDirectoryName(outputPrefix),
                directoryPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Transcription workspace paths must be fully qualified, existing, and owned.");
        }

        DirectoryPath = Path.GetFullPath(directoryPath);
        OutputPrefix = Path.GetFullPath(outputPrefix);
    }

    public string DirectoryPath { get; }

    public string OutputPrefix { get; }

    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(
                    DirectoryPath,
                    recursive: true);
            }
        }
        catch (Exception exception)
            when (exception is
                  IOException or
                  UnauthorizedAccessException or
                  SecurityException)
        {
            throw new WhisperCppTranscriptionException(
                "Replay Foundry could not clean up the transcription workspace.",
                innerException: exception);
        }
    }
}
