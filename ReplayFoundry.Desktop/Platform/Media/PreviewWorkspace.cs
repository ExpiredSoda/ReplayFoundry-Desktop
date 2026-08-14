using System.IO;
using System.Security;

namespace ReplayFoundry.Desktop.Platform.Media;

internal interface IPreviewWorkspaceFactory
{
    PreviewWorkspace Create();
}

internal sealed class SystemPreviewWorkspaceFactory :
    IPreviewWorkspaceFactory
{
    private const int MaximumCreationAttempts = 10;

    public PreviewWorkspace Create()
    {
        string root =
            Path.Combine(
                Path.GetTempPath(),
                "ReplayFoundry",
                "PreviewFrames");

        Directory.CreateDirectory(root);

        for (int attempt = 0;
             attempt < MaximumCreationAttempts;
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

            try
            {
                return new PreviewWorkspace(
                    directory,
                    Path.Combine(
                        directory,
                        "preview.png"));
            }
            catch
            {
                TryDeleteDirectory(directory);
                throw;
            }
        }

        throw new IOException(
            "Replay Foundry could not allocate a unique preview workspace.");
    }

    private static void TryDeleteDirectory(
        string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(
                    directory,
                    recursive: true);
            }
        }
        catch (Exception exception)
            when (exception is
                  IOException or
                  UnauthorizedAccessException or
                  SecurityException)
        {
            // Preserve the workspace-construction failure.
        }
    }
}

internal sealed class PreviewWorkspace
{
    public PreviewWorkspace(
        string directoryPath,
        string outputPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) ||
            !Path.IsPathFullyQualified(directoryPath))
        {
            throw new ArgumentException(
                "Preview workspace directory must be fully qualified.",
                nameof(directoryPath));
        }

        if (string.IsNullOrWhiteSpace(outputPath) ||
            !Path.IsPathFullyQualified(outputPath))
        {
            throw new ArgumentException(
                "Preview workspace output path must be fully qualified.",
                nameof(outputPath));
        }

        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException(
                $"Preview workspace directory does not exist: '{directoryPath}'.");
        }

        if (!string.Equals(
                Path.GetDirectoryName(outputPath),
                directoryPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Preview output must belong to its workspace directory.",
                nameof(outputPath));
        }

        DirectoryPath = directoryPath;
        OutputPath = outputPath;
    }

    public string DirectoryPath { get; }

    public string OutputPath { get; }

    public void Cleanup()
    {
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(
                DirectoryPath,
                recursive: true);
        }
    }
}
