using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed class FfprobeMediaProbe : IMediaProbe
{
    private const string InspectorName =
        "ReplayFoundry.FfprobeMediaProbe";

    private const string InspectorVersion =
        "1.1.0";

    private static readonly TimeSpan ProbeTimeout =
        TimeSpan.FromMinutes(2);

    private static readonly TimeSpan VersionTimeout =
        TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
        };

    private readonly IProcessRunner _processRunner;
    private readonly IFfmpegToolLocator _toolLocator;
    private readonly object _manifestSync = new();

    private Task<MediaInspectionManifest>? _manifestTask;

    public FfprobeMediaProbe(
        IProcessRunner processRunner,
        IFfmpegToolLocator toolLocator)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(toolLocator);

        _processRunner = processRunner;
        _toolLocator = toolLocator;
    }

    public async Task<MediaProbeResult> ProbeAsync(
        string fullPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            throw new ArgumentException(
                "Media inspection requires a source path.",
                nameof(fullPath));
        }

        if (!Path.IsPathFullyQualified(fullPath))
        {
            throw new ArgumentException(
                "Media inspection requires a fully qualified source path.",
                nameof(fullPath));
        }

        if (!File.Exists(fullPath))
        {
            throw new MediaProbeException(
                $"Replay Foundry could not find '{Path.GetFileName(fullPath)}'.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        MediaInspectionManifest manifest =
            await GetManifestTask()
                .WaitAsync(cancellationToken);

        var request =
            new ProcessRunRequest(
                manifest.ToolPath,
                [
                    "-hide_banner",
                    "-v",
                    "error",
                    "-print_format",
                    "json",
                    "-show_error",
                    "-show_format",
                    "-show_streams",
                    fullPath,
                ],
                ProbeTimeout);

        ProcessRunResult processResult;

        try
        {
            processResult =
                await _processRunner.RunAsync(
                    request,
                    cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ProcessExecutionException exception)
        {
            throw new MediaProbeException(
                $"Replay Foundry could not run media inspection for " +
                $"'{Path.GetFileName(fullPath)}'.",
                innerException: exception);
        }

        if (!processResult.Succeeded)
        {
            throw new MediaProbeException(
                $"Replay Foundry could not inspect " +
                $"'{Path.GetFileName(fullPath)}' as a video.",
                GetDiagnostics(processResult));
        }

        FfprobeDocument document;

        try
        {
            document =
                JsonSerializer.Deserialize<FfprobeDocument>(
                    processResult.StandardOutput,
                    JsonOptions) ??
                throw new JsonException(
                    "ffprobe returned an empty JSON document.");
        }
        catch (JsonException exception)
        {
            throw new MediaProbeException(
                $"Replay Foundry received invalid inspection data for " +
                $"'{Path.GetFileName(fullPath)}'.",
                GetDiagnostics(processResult),
                exception);
        }

        if (document.Error is not null)
        {
            throw new MediaProbeException(
                $"Replay Foundry could not inspect " +
                $"'{Path.GetFileName(fullPath)}'.",
                document.Error.Message);
        }

        return FfprobeResultMapper.Map(
            fullPath,
            document,
            manifest);
    }

    private Task<MediaInspectionManifest> GetManifestTask()
    {
        lock (_manifestSync)
        {
            if (_manifestTask is not null &&
                (_manifestTask.IsFaulted ||
                 _manifestTask.IsCanceled))
            {
                _manifestTask = null;
            }

            return _manifestTask ??=
                LoadManifestAsync();
        }
    }

    private async Task<MediaInspectionManifest> LoadManifestAsync()
    {
        string toolPath =
            _toolLocator.LocateFfprobe();

        var request =
            new ProcessRunRequest(
                toolPath,
                ["-version"],
                VersionTimeout,
                maxStandardOutputCharacters: 64 * 1024,
                maxStandardErrorCharacters: 64 * 1024);

        ProcessRunResult result =
            await RunVersionVerificationAsync(
                request);

        if (!result.Succeeded)
        {
            throw new MediaProbeException(
                "Replay Foundry found ffprobe.exe, but it did not report " +
                "a usable version.",
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
            "ffprobe version unknown";

        return new MediaInspectionManifest(
            InspectorName,
            InspectorVersion,
            "ffprobe",
            versionLine,
            toolPath,
            DateTimeOffset.UtcNow);
    }

    private async Task<ProcessRunResult> RunVersionVerificationAsync(
        ProcessRunRequest request)
    {
        const int maximumAttempts = 2;

        for (int attempt = 0;
             attempt < maximumAttempts;
             attempt++)
        {
            try
            {
                return await _processRunner.RunAsync(
                    request,
                    CancellationToken.None);
            }
            catch (ProcessTimeoutException)
                when (attempt == 0)
            {
                // A single cold-start timeout can be caused by first-launch
                // scanning. Retry once within the same fixed deadline.
            }
            catch (ProcessTimeoutException exception)
            {
                throw new MediaProbeException(
                    "Replay Foundry found ffprobe.exe, but version " +
                    "verification timed out after two attempts.",
                    exception.Message,
                    exception);
            }
            catch (ProcessExecutionException exception)
            {
                throw new MediaProbeException(
                    "Replay Foundry found ffprobe.exe, but version " +
                    "verification could not complete.",
                    exception.Message,
                    exception);
            }
        }

        throw new InvalidOperationException(
            "The bounded ffprobe version verification loop did not finish.");
    }

    private static string GetDiagnostics(
        ProcessRunResult result)
    {
        if (!string.IsNullOrWhiteSpace(
                result.StandardError))
        {
            return result.StandardError.Trim();
        }

        if (!string.IsNullOrWhiteSpace(
                result.StandardOutput))
        {
            return result.StandardOutput.Trim();
        }

        return $"ffprobe exited with code {result.ExitCode}.";
    }
}
