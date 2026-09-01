using System.Diagnostics;
using System.IO;
using ReplayFoundry.Desktop.Media.AudioExtraction;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed class FfmpegAudioSegmentExtractor :
    IAudioSegmentExtractor
{
    private const string ExtractorName =
        "ReplayFoundry.FfmpegAudioSegmentExtractor";

    private const string ExtractorVersion = "0.1.0";

    private readonly IProcessRunner _processRunner;
    private readonly IFfmpegToolLocator _toolLocator;
    private readonly IAudioExtractionWorkspaceFactory
        _workspaceFactory;
    private readonly object _toolInfoSync = new();
    private Task<AudioExtractionToolInfo>? _toolInfoTask;

    public FfmpegAudioSegmentExtractor(
        IProcessRunner processRunner,
        IFfmpegToolLocator toolLocator,
        IAudioExtractionWorkspaceFactory workspaceFactory)
    {
        _processRunner =
            processRunner ??
            throw new ArgumentNullException(nameof(processRunner));
        _toolLocator =
            toolLocator ??
            throw new ArgumentNullException(nameof(toolLocator));
        _workspaceFactory =
            workspaceFactory ??
            throw new ArgumentNullException(nameof(workspaceFactory));
    }

    public async Task<ExtractedAudioSegment> ExtractAsync(
        AudioSegmentExtractionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        AudioExtractionToolInfo tool =
            await GetToolInfoTask()
                .WaitAsync(cancellationToken);
        AudioExtractionWorkspace? workspace = null;

        try
        {
            workspace = _workspaceFactory.Create();
            FfmpegAudioSegmentCommand command =
                FfmpegAudioSegmentCommandBuilder.Build(
                    request,
                    workspace.OutputPath);
            DateTimeOffset startedAtUtc =
                DateTimeOffset.UtcNow;
            var processRequest =
                new ProcessRunRequest(
                    tool.Path,
                    command.Arguments,
                    request.ProcessTimeout,
                    workspace.DirectoryPath,
                    maxStandardOutputCharacters:
                        128 * 1024,
                    maxStandardErrorCharacters:
                        1024 * 1024);
            ProcessRunResult result =
                await _processRunner.RunAsync(
                    processRequest,
                    cancellationToken);
            DateTimeOffset completedAtUtc =
                DateTimeOffset.UtcNow;

            if (!result.Succeeded)
            {
                throw new AudioSegmentExtractionException(
                    "FFmpeg could not extract the bounded audio neighborhood.",
                    Diagnostics(result));
            }

            WaveFileInformation wave =
                WaveFileValidator.Validate(
                    workspace.OutputPath,
                    request.Duration,
                    command.SampleRate,
                    command.ChannelCount,
                    command.BitsPerSample);
            long length =
                new FileInfo(
                    workspace.OutputPath).Length;
            var warnings =
                new List<AudioSegmentExtractionWarning>();

            if (wave.Duration != request.Duration)
            {
                warnings.Add(
                    new AudioSegmentExtractionWarning(
                        AudioSegmentExtractionWarningCode
                            .DurationWithinTolerance,
                        $"The WAV duration {wave.Duration:c} differs slightly from " +
                        $"the requested {request.Duration:c} but remains inside tolerance."));
            }

            var manifest =
                new AudioSegmentExtractionManifest(
                    ExtractorName,
                    ExtractorVersion,
                    tool.Path,
                    tool.Sha256,
                    tool.Version,
                    command.Arguments,
                    request.SourcePath,
                    request.Start,
                    request.End,
                    request.AbsoluteAudioStreamIndex,
                    command.SampleRate,
                    command.ChannelCount,
                    command.BitsPerSample,
                    startedAtUtc,
                    completedAtUtc,
                    result.Duration,
                    warnings);
            AudioExtractionWorkspace ownedWorkspace =
                workspace;
            workspace = null;

            return new ExtractedAudioSegment(
                request.NeighborhoodId,
                ownedWorkspace.OutputPath,
                wave.Duration,
                length,
                manifest,
                ownedWorkspace.Cleanup);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AudioSegmentExtractionException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is
                  IOException or
                  UnauthorizedAccessException or
                  InvalidDataException or
                  ProcessExecutionException)
        {
            throw new AudioSegmentExtractionException(
                "Replay Foundry could not extract the bounded audio neighborhood.",
                innerException: exception);
        }
        finally
        {
            workspace?.Cleanup();
        }
    }

    private Task<AudioExtractionToolInfo> GetToolInfoTask()
    {
        lock (_toolInfoSync)
        {
            if (_toolInfoTask is null ||
                _toolInfoTask.IsFaulted ||
                _toolInfoTask.IsCanceled)
            {
                _toolInfoTask = LoadToolInfoAsync();
            }

            return _toolInfoTask;
        }
    }

    private async Task<AudioExtractionToolInfo>
        LoadToolInfoAsync()
    {
        string path = _toolLocator.LocateFfmpeg();
        ProcessRunResult result =
            await _processRunner.RunAsync(
                new ProcessRunRequest(
                    path,
                    ["-version"],
                    TimeSpan.FromSeconds(10),
                    maxStandardOutputCharacters:
                        64 * 1024,
                    maxStandardErrorCharacters:
                        64 * 1024),
                CancellationToken.None);

        if (!result.Succeeded ||
            string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            throw new AudioSegmentExtractionException(
                "Replay Foundry could not verify the configured FFmpeg executable.",
                Diagnostics(result));
        }

        return new AudioExtractionToolInfo(
            path,
            result.StandardOutput
                .Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries)
                .First()
                .Trim(),
            ModelArtifactManifest.ComputeSha256(path));
    }

    private static string Diagnostics(
        ProcessRunResult result) =>
        $"Exit code: {result.ExitCode}{Environment.NewLine}" +
        $"stderr: {result.StandardError.Trim()}";

    private sealed record AudioExtractionToolInfo(
        string Path,
        string Version,
        string Sha256);
}

public static class AudioSegmentExtractionFactory
{
    public static IAudioSegmentExtractor CreateDefault() =>
        new FfmpegAudioSegmentExtractor(
            new WindowsProcessRunner(),
            new FfmpegToolLocator(),
            new SystemAudioExtractionWorkspaceFactory());
}
