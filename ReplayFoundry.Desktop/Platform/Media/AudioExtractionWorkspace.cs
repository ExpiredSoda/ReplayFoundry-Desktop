using System.IO;
using System.Security;
using ReplayFoundry.Desktop.Media.AudioExtraction;

namespace ReplayFoundry.Desktop.Platform.Media;

internal interface IAudioExtractionWorkspaceFactory
{
    AudioExtractionWorkspace Create();
}

internal sealed class SystemAudioExtractionWorkspaceFactory :
    IAudioExtractionWorkspaceFactory
{
    public AudioExtractionWorkspace Create()
    {
        string root =
            Path.Combine(
                Path.GetTempPath(),
                "ReplayFoundry",
                "AudioExtraction");
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

            return new AudioExtractionWorkspace(
                directory,
                Path.Combine(
                    directory,
                    "neighborhood.wav"));
        }

        throw new IOException(
            "Replay Foundry could not allocate an audio-extraction workspace.");
    }
}

internal sealed class AudioExtractionWorkspace
{
    public AudioExtractionWorkspace(
        string directoryPath,
        string outputPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) ||
            !Path.IsPathFullyQualified(directoryPath) ||
            string.IsNullOrWhiteSpace(outputPath) ||
            !Path.IsPathFullyQualified(outputPath) ||
            !Directory.Exists(directoryPath) ||
            !string.Equals(
                Path.GetDirectoryName(outputPath),
                directoryPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Audio workspace paths must be fully qualified, existing, and owned.");
        }

        DirectoryPath = Path.GetFullPath(directoryPath);
        OutputPath = Path.GetFullPath(outputPath);
    }

    public string DirectoryPath { get; }

    public string OutputPath { get; }

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
            throw new AudioSegmentExtractionException(
                "Replay Foundry could not clean up the temporary audio workspace.",
                innerException: exception);
        }
    }
}
