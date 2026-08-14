using System.IO;
using System.Security;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed class FfmpegVisualSemanticReviewVideoMaterializer :
    IVisualSemanticReviewVideoMaterializer
{
    private readonly IProcessRunner _processRunner;
    private readonly FfmpegToolLocator _toolLocator;

    public FfmpegVisualSemanticReviewVideoMaterializer(
        IProcessRunner processRunner,
        FfmpegToolLocator toolLocator)
    {
        _processRunner = processRunner ??
            throw new ArgumentNullException(nameof(processRunner));
        _toolLocator = toolLocator ??
            throw new ArgumentNullException(nameof(toolLocator));
    }

    public async Task<MaterializedVisualSemanticReviewVideo> MaterializeAsync(
        VisualSemanticReviewVideoMaterializationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        string directory = AllocateWorkspace();
        string outputPath = Path.Combine(directory, "review.mp4");
        try
        {
            string executablePath = _toolLocator.LocateFfmpeg();
            FfmpegVisualSemanticReviewVideoCommand command =
                FfmpegVisualSemanticReviewVideoCommandBuilder.Build(
                    request,
                    outputPath);
            ProcessRunResult process = await _processRunner.RunAsync(
                new ProcessRunRequest(
                    executablePath,
                    command.Arguments,
                    command.Timeout,
                    directory,
                    maxStandardOutputCharacters: 64 * 1024,
                    maxStandardErrorCharacters: 512 * 1024),
                cancellationToken);
            if (!process.Succeeded || !File.Exists(outputPath))
            {
                throw new VisualSemanticReviewVideoMaterializationException(
                    "FFmpeg could not create the bounded Qwen review video.",
                    $"Exit code: {process.ExitCode}{Environment.NewLine}" +
                    $"stderr: {process.StandardError.Trim()}");
            }

            var info = new FileInfo(outputPath);
            info.Refresh();
            if (info.Length <= 0)
            {
                throw new VisualSemanticReviewVideoMaterializationException(
                    "FFmpeg created an empty bounded Qwen review video.");
            }
            var input = new VisualSemanticInputManifest(
                outputPath,
                ModelArtifactManifest.ComputeSha256(outputPath),
                info.Length,
                request.Duration,
                new DateTimeOffset(
                    DateTime.SpecifyKind(
                        info.LastWriteTimeUtc,
                        DateTimeKind.Utc)));
            string ownedDirectory = directory;
            directory = string.Empty;
            return new MaterializedVisualSemanticReviewVideo(
                request,
                input,
                () => Cleanup(ownedDirectory));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (VisualSemanticReviewVideoMaterializationException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException or
                  ProcessExecutionException)
        {
            throw new VisualSemanticReviewVideoMaterializationException(
                "Replay Foundry could not create the bounded Qwen review video.",
                innerException: exception);
        }
        finally
        {
            if (directory.Length > 0)
            {
                Cleanup(directory);
            }
        }
    }

    private static string AllocateWorkspace()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundry",
            "VisualSemanticReview");
        Directory.CreateDirectory(root);
        for (int attempt = 0; attempt < 10; attempt++)
        {
            string directory = Path.Combine(
                root,
                Guid.NewGuid().ToString("N"));
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                return directory;
            }
        }
        throw new IOException(
            "Replay Foundry could not allocate a visual-semantic review workspace.");
    }

    private static void Cleanup(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException or
                  SecurityException)
        {
            throw new VisualSemanticReviewVideoMaterializationException(
                "Replay Foundry could not clean up a temporary Qwen review video.",
                innerException: exception);
        }
    }
}

public sealed class VisualSemanticReviewVideoMaterializationException : Exception
{
    public VisualSemanticReviewVideoMaterializationException(
        string message,
        string? diagnosticDetails = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        DiagnosticDetails = diagnosticDetails;
    }

    public string? DiagnosticDetails { get; }
}

public static class VisualSemanticReviewVideoMaterializerFactory
{
    public static IVisualSemanticReviewVideoMaterializer CreateDefault() =>
        new FfmpegVisualSemanticReviewVideoMaterializer(
            new WindowsProcessRunner(),
            new FfmpegToolLocator());
}
