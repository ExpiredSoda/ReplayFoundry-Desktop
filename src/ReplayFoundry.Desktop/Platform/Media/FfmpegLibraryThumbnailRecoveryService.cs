using System.IO;
using System.Security;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class LibraryThumbnailRecoveryFactory
{
    public static ILibraryThumbnailRecoveryService CreateDefault() =>
        new FfmpegLibraryThumbnailRecoveryService(
            new WindowsProcessRunner(),
            new FfmpegToolLocator());
}

internal sealed class FfmpegLibraryThumbnailRecoveryService :
    ILibraryThumbnailRecoveryService
{
    private readonly IProcessRunner _processRunner;
    private readonly IFfmpegToolLocator _toolLocator;

    public FfmpegLibraryThumbnailRecoveryService(
        IProcessRunner processRunner,
        IFfmpegToolLocator toolLocator)
    {
        _processRunner = processRunner ??
            throw new ArgumentNullException(nameof(processRunner));
        _toolLocator = toolLocator ??
            throw new ArgumentNullException(nameof(toolLocator));
    }

    public async Task<bool> TryRecoverAsync(
        LibraryMediaAsset asset,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        cancellationToken.ThrowIfCancellationRequested();
        string? thumbnail = asset.ThumbnailFullPath;
        if (!asset.IsAvailable || thumbnail is null)
        {
            return false;
        }
        if (File.Exists(thumbnail))
        {
            return true;
        }

        string directory = Path.GetDirectoryName(thumbnail)!;
        string temporary = Path.Combine(
            directory,
            $".{Path.GetFileNameWithoutExtension(thumbnail)}." +
            $"recovery-{Guid.NewGuid():N}.jpg");
        try
        {
            Directory.CreateDirectory(directory);
            FfmpegClipRenderCommand command =
                FfmpegClipRenderCommandBuilder.BuildThumbnail(
                    asset.OutputFullPath,
                    asset.Duration,
                    temporary);
            ProcessRunResult result = await _processRunner.RunAsync(
                new ProcessRunRequest(
                    _toolLocator.LocateFfmpeg(),
                    command.Arguments,
                    command.Timeout,
                    command.WorkingDirectory,
                    maxStandardOutputCharacters: 64 * 1024,
                    maxStandardErrorCharacters: 512 * 1024),
                cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded ||
                !File.Exists(temporary) ||
                new FileInfo(temporary).Length <= 0)
            {
                return false;
            }

            if (File.Exists(thumbnail))
            {
                return true;
            }
            try
            {
                File.Move(temporary, thumbnail);
            }
            catch (IOException) when (File.Exists(thumbnail))
            {
                // Another recovery or a user restore won the race.
            }
            return File.Exists(thumbnail) &&
                new FileInfo(thumbnail).Length > 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            SecurityException or
            ArgumentException or
            InvalidOperationException or
            ProcessExecutionException)
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                SecurityException)
            {
                // Thumbnail recovery is best effort; never disturb Library use.
            }
        }
    }
}
