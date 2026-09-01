using System.IO;
using System.Runtime.ExceptionServices;
using System.Security;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Preview;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed class FfmpegVideoPreviewFrameProvider :
    IVideoPreviewFrameProvider
{
    private const string ProviderName =
        "ReplayFoundry.FfmpegVideoPreviewFrameProvider";

    private const string ProviderVersion =
        "1.0.0";

    private static readonly TimeSpan VersionTimeout =
        TimeSpan.FromSeconds(10);

    private static readonly TimeSpan ExtractionTimeout =
        TimeSpan.FromMinutes(5);

    private readonly IProcessRunner _processRunner;
    private readonly IFfmpegToolLocator _toolLocator;
    private readonly IPreviewWorkspaceFactory _workspaceFactory;
    private readonly object _toolInfoSync = new();

    private Task<FfmpegToolInfo>? _toolInfoTask;

    public FfmpegVideoPreviewFrameProvider(
        IProcessRunner processRunner,
        IFfmpegToolLocator toolLocator,
        IPreviewWorkspaceFactory workspaceFactory)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(toolLocator);
        ArgumentNullException.ThrowIfNull(workspaceFactory);

        _processRunner = processRunner;
        _toolLocator = toolLocator;
        _workspaceFactory = workspaceFactory;
    }

    public async Task<VideoPreviewFrame> GetFrameAsync(
        VideoPreviewFrameRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        FfmpegToolInfo toolInfo =
            await GetToolInfoTask()
                .WaitAsync(cancellationToken);

        PreviewWorkspace workspace;

        try
        {
            workspace =
                _workspaceFactory.Create();
        }
        catch (Exception exception)
            when (exception is
                  IOException or
                  UnauthorizedAccessException or
                  SecurityException)
        {
            throw new VideoPreviewFrameException(
                "Replay Foundry could not create a temporary preview workspace.",
                innerException: exception);
        }

        Exception? operationFailure = null;
        VideoPreviewFrame? frame = null;

        try
        {
            FfmpegPreviewCommand command =
                FfmpegPreviewCommandBuilder.Build(
                    request,
                    workspace.OutputPath);

            var processRequest =
                new ProcessRunRequest(
                    toolInfo.Path,
                    command.Arguments,
                    ExtractionTimeout,
                    workspace.DirectoryPath,
                    maxStandardOutputCharacters:
                        256 * 1024,
                    maxStandardErrorCharacters:
                        1024 * 1024);

            ProcessRunResult result;

            try
            {
                result =
                    await _processRunner.RunAsync(
                        processRequest,
                        cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ProcessExecutionException exception)
            {
                throw new VideoPreviewFrameException(
                    "Replay Foundry could not start preview-frame extraction.",
                    innerException: exception);
            }

            if (!result.Succeeded)
            {
                throw new VideoPreviewFrameException(
                    "FFmpeg did not produce the requested preview frame.",
                    GetDiagnostics(result));
            }

            if (!File.Exists(workspace.OutputPath))
            {
                throw new VideoPreviewFrameException(
                    "FFmpeg completed without creating a preview image.");
            }

            var outputFile =
                new FileInfo(workspace.OutputPath);

            if (outputFile.Length == 0)
            {
                throw new VideoPreviewFrameException(
                    "FFmpeg created an empty preview image.");
            }

            byte[] pngData;

            try
            {
                pngData =
                    await File.ReadAllBytesAsync(
                        workspace.OutputPath,
                        cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
                when (exception is
                      IOException or
                      UnauthorizedAccessException or
                      SecurityException)
            {
                throw new VideoPreviewFrameException(
                    "Replay Foundry could not read the extracted preview image.",
                    innerException: exception);
            }

            int width;
            int height;

            try
            {
                (width, height) =
                    PngDimensionsReader.Read(
                        pngData);
            }
            catch (InvalidDataException exception)
            {
                throw new VideoPreviewFrameException(
                    "FFmpeg produced an invalid PNG preview image.",
                    innerException: exception);
            }

            if (width != command.ExpectedWidth ||
                height != command.ExpectedHeight)
            {
                throw new VideoPreviewFrameException(
                    "The preview dimensions do not match the requested effective-display size.",
                    $"Expected {command.ExpectedWidth}x{command.ExpectedHeight}; " +
                    $"received {width}x{height}.");
            }

            var manifest =
                new VideoPreviewFrameManifest(
                    ProviderName,
                    ProviderVersion,
                    "ffmpeg",
                    toolInfo.Version,
                    toolInfo.Path,
                    DateTimeOffset.UtcNow,
                    result.Duration);

            frame = new VideoPreviewFrame(
                request.Media.FullPath,
                request.Media.Duration,
                request.Media.PrimaryVideoStream.Index,
                request.Timestamp,
                decodedTimestamp: null,
                width,
                height,
                CompositionCoordinateSpace
                    .EffectiveDisplayNormalizedBeforeCrop,
                pngData,
                manifest);
        }
        catch (Exception exception)
        {
            operationFailure = exception;
        }

        try
        {
            workspace.Cleanup();
        }
        catch (Exception cleanupException)
            when (cleanupException is
                  IOException or
                  UnauthorizedAccessException or
                  SecurityException)
        {
            if (operationFailure is null)
            {
                operationFailure = new VideoPreviewFrameException(
                    "Replay Foundry extracted the preview frame but could not clean up its temporary workspace.",
                    innerException: cleanupException);
            }
            else
            {
                operationFailure.Data[
                    "ReplayFoundry.PreviewWorkspaceCleanupFailure"] =
                    cleanupException.ToString();
            }
        }

        if (operationFailure is not null)
        {
            ExceptionDispatchInfo.Capture(operationFailure).Throw();
        }

        return frame ?? throw new InvalidOperationException(
            "Preview extraction completed without a frame or a failure.");
    }

    private Task<FfmpegToolInfo> GetToolInfoTask()
    {
        lock (_toolInfoSync)
        {
            if (_toolInfoTask is null ||
                _toolInfoTask.IsFaulted ||
                _toolInfoTask.IsCanceled)
            {
                _toolInfoTask =
                    LoadToolInfoAsync();
            }

            return _toolInfoTask;
        }
    }

    private async Task<FfmpegToolInfo> LoadToolInfoAsync()
    {
        string toolPath =
            _toolLocator.LocateFfmpeg();

        var request =
            new ProcessRunRequest(
                toolPath,
                ["-version"],
                VersionTimeout,
                maxStandardOutputCharacters:
                    64 * 1024,
                maxStandardErrorCharacters:
                    64 * 1024);

        ProcessRunResult result;

        try
        {
            result =
                await _processRunner.RunAsync(
                    request,
                    CancellationToken.None);
        }
        catch (ProcessExecutionException exception)
        {
            throw new VideoPreviewFrameException(
                "Replay Foundry found ffmpeg.exe but could not start it.",
                innerException: exception);
        }

        if (!result.Succeeded)
        {
            throw new VideoPreviewFrameException(
                "Replay Foundry found ffmpeg.exe, but it did not report a usable version.",
                GetDiagnostics(result));
        }

        string versionOutput =
            !string.IsNullOrWhiteSpace(result.StandardOutput)
                ? result.StandardOutput
                : result.StandardError;

        string versionLine =
            versionOutput
                .Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .FirstOrDefault() ??
            "ffmpeg version unknown";

        return new FfmpegToolInfo(
            toolPath,
            versionLine);
    }

    private static string GetDiagnostics(
        ProcessRunResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            return result.StandardError.Trim();
        }

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return result.StandardOutput.Trim();
        }

        return $"ffmpeg exited with code {result.ExitCode}.";
    }

    private sealed record FfmpegToolInfo(
        string Path,
        string Version);
}
