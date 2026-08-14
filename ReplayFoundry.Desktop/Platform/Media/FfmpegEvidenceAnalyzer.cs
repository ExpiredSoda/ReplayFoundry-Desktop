using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Analysis.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed class FfmpegEvidenceAnalyzer :
    IMediaEvidenceAnalyzer
{
    private static readonly MediaEvidenceAnalyzerIdentity
        AnalyzerIdentity =
        new(
            "ReplayFoundry.FfmpegEvidenceAnalyzer",
            "3.0.0");

    private static readonly TimeSpan VersionTimeout =
        TimeSpan.FromSeconds(10);

    private readonly IProcessRunner _processRunner;
    private readonly FfmpegEvidencePassRunner _passRunner;
    private readonly IFfmpegToolLocator _toolLocator;
    private readonly object _toolInfoSync = new();

    private Task<FfmpegToolInfo>? _toolInfoTask;

    public FfmpegEvidenceAnalyzer(
        IProcessRunner processRunner,
        IFfmpegToolLocator toolLocator)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(toolLocator);

        _processRunner = processRunner;
        _passRunner = new FfmpegEvidencePassRunner(processRunner);
        _toolLocator = toolLocator;
    }

    public MediaEvidenceAnalyzerIdentity Identity =>
        AnalyzerIdentity;

    public async Task<MediaEvidenceResult> AnalyzeAsync(
        MediaEvidenceAnalysisRequest request,
        IProgress<MediaEvidenceProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        VisualEvidenceTargetPlan targetPlan =
            VisualEvidenceTargetPlanner.Create(
                request);

        FfmpegToolInfo toolInfo =
            await GetToolInfoTask()
                .WaitAsync(
                    cancellationToken);

        var rootWarnings =
            new List<MediaEvidenceWarning>();

        var timings =
            new List<AnalysisPassTiming>();

        var totalStopwatch =
            Stopwatch.StartNew();

        int totalPasses =
            2 +
            request.Media.AudioStreams.Count;

        int completedPasses = 0;

        int sceneOutputLimit =
            FfmpegEvidenceOutputLimitEstimator
                .EstimateVisualOutputLimit(
                    targetPlan.Targets,
                    request.Options);

        progress?.Report(
            new MediaEvidenceProgressUpdate(
                MediaEvidenceAnalysisPhase.Preparing,
                request.IsCompositionAware
                    ? $"Replay Foundry will analyze the full frame and " +
                      $"{targetPlan.Targets.Count - 1} confirmed region targets " +
                      "in two shared video passes."
                    : "Replay Foundry will analyze the complete source in two full-frame video passes.",
                0));

        ReportPass(
            progress,
            completedPasses,
            totalPasses,
            MediaEvidenceAnalysisPhase.ScenePassStarted,
            "Studying scene changes, brightness, color, and sampled activity while a second shared pass checks dark and frozen sections.");

        ReportPass(
            progress,
            completedPasses,
            totalPasses,
            MediaEvidenceAnalysisPhase.VisualIntervalPassStarted,
            "The two shared visual passes are running together so the source is not scanned serially twice.");

        (
            ProcessRunResult sceneResult,
            ProcessRunResult visualResult) =
            await _passRunner.RunVisualPassesAsync(
                toolInfo.Path,
                request,
                targetPlan,
                sceneOutputLimit,
                cancellationToken);

        timings.Add(
            new AnalysisPassTiming(
                "Scene detection",
                sceneResult.Duration));
        timings.Add(
            new AnalysisPassTiming(
                "Black and freeze detection",
                visualResult.Duration));

        completedPasses++;
        ReportPass(
            progress,
            completedPasses,
            totalPasses,
            MediaEvidenceAnalysisPhase.ScenePassCompleted,
            "The shared scene pass finished for every visual target.");
        completedPasses++;

        FfmpegVisualEvidenceParseResult parsedVisual =
            FfmpegEvidenceResultParser
                .ParseVisualEvidence(
                    sceneResult.StandardOutput,
                    visualResult.StandardOutput,
                    targetPlan.Targets,
                    request.Options
                        .VisualSignalSampleInterval);

        rootWarnings.AddRange(
            parsedVisual.RootWarnings);

        ReportPass(
            progress,
            completedPasses,
            totalPasses,
            MediaEvidenceAnalysisPhase.VisualIntervalPassCompleted,
            "The shared black/freeze pass finished for every visual target.");

        var silenceIntervals =
            new List<SilenceInterval>();

        var audioSignalSamples =
            new List<AudioSignalSample>();

        var audioSignalCoverages =
            new List<AudioSignalCoverage>();

        foreach (AudioStreamInfo audioStream in
                 request.Media.AudioStreams)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ReportPass(
                progress,
                completedPasses,
                totalPasses,
                MediaEvidenceAnalysisPhase.AudioPassStarted,
                $"Listening for quiet sections and measuring audio energy in global audio stream {audioStream.Index}.",
                audioStream.Index);

            int audioOutputLimit =
                FfmpegEvidenceOutputLimitEstimator
                    .EstimateAudioOutputLimit(
                        request.Media.Duration,
                        audioStream,
                        request.Options);

            ProcessRunResult silenceResult =
                await _passRunner.RunPassAsync(
                    toolInfo.Path,
                    request.Media.FullPath,
                    $"Silence and signal analysis for audio stream {audioStream.Index}",
                    FfmpegEvidenceCommandBuilder
                        .BuildAudioEvidenceArguments(
                            audioStream,
                            request.Options),
                    request.Options.ProcessTimeout,
                    audioOutputLimit,
                    cancellationToken);

            timings.Add(
                new AnalysisPassTiming(
                    $"Silence and signal analysis stream {audioStream.Index}",
                    silenceResult.Duration));

            FfmpegAudioEvidenceParseResult parsedAudio =
                FfmpegEvidenceResultParser
                    .ParseAudioEvidence(
                        silenceResult.StandardOutput,
                        audioStream,
                        request.Media.Duration,
                        request.Options
                            .AudioSignalWindowDuration);

            silenceIntervals.AddRange(
                parsedAudio.SilenceIntervals);
            audioSignalSamples.AddRange(
                parsedAudio.SignalSamples);
            audioSignalCoverages.Add(
                parsedAudio.SignalCoverage);
            rootWarnings.AddRange(
                parsedAudio.Warnings);

            completedPasses++;

            ReportPass(
                progress,
                completedPasses,
                totalPasses,
                MediaEvidenceAnalysisPhase.AudioPassCompleted,
                $"Finished global audio stream {audioStream.Index}.",
                audioStream.Index);
        }

        totalStopwatch.Stop();

        var manifest =
            new MediaEvidenceAnalysisManifest(
                Identity.Name,
                Identity.Version,
                "ffmpeg",
                toolInfo.Version,
                toolInfo.Path,
                DateTimeOffset.UtcNow,
                AnalysisCoverage.FullTimeline,
                request.Options,
                request.Composition?.Manifest.SchemaVersion,
                request.Composition?.Manifest
                    .CoordinateSpaceVersion,
                request.Composition?.Manifest.Origin,
                request.IncludedRegionRoles,
                targetPlan.Targets,
                targetPlan.SkippedRegions,
                targetPlan.DisplayGeometry.Width,
                targetPlan.DisplayGeometry.Height,
                MediaSignalEvidencePolicy
                    .CurrentSchemaVersion,
                parsedVisual.Targets.Select(
                    static target =>
                        target.SignalCoverage),
                audioSignalCoverages,
                visualPassCount: 2,
                audioPassCount:
                    request.Media.AudioStreams.Count,
                timings,
                totalStopwatch.Elapsed);

        VisualTargetEvidenceResult fullFrame =
            parsedVisual.Targets.Single(
                static result =>
                    result.Target.Kind ==
                    VisualEvidenceTargetKind.FullFrame);

        VisualTargetEvidenceResult[] regionResults =
            parsedVisual.Targets
                .Where(
                    static result =>
                        result.Target.Kind ==
                        VisualEvidenceTargetKind
                            .CompositionRegion)
                .ToArray();

        progress?.Report(
            new MediaEvidenceProgressUpdate(
                MediaEvidenceAnalysisPhase.Completed,
                "Replay Foundry finished deterministic full-frame, region, and global-audio evidence analysis.",
                100));

        return new MediaEvidenceResult(
            request.Media.FullPath,
            request.Media.Duration,
            fullFrame,
            regionResults,
            silenceIntervals,
            audioSignalSamples,
            audioSignalCoverages,
            manifest,
            rootWarnings);
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
            throw new MediaEvidenceAnalysisException(
                "Replay Foundry found ffmpeg.exe but could not start it.",
                innerException: exception);
        }

        if (!result.Succeeded)
        {
            throw new MediaEvidenceAnalysisException(
                "Replay Foundry found ffmpeg.exe, but it did not report a usable version.",
                FfmpegProcessResultDiagnostics.Describe(result));
        }

        string versionOutput =
            !string.IsNullOrWhiteSpace(
                result.StandardOutput)
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

    private static void ReportPass(
        IProgress<MediaEvidenceProgressUpdate>? progress,
        int completedPasses,
        int totalPasses,
        MediaEvidenceAnalysisPhase phase,
        string detail,
        int? streamIndex = null)
    {
        double percent =
            completedPasses /
            (double)totalPasses *
            100;

        progress?.Report(
            new MediaEvidenceProgressUpdate(
                phase,
                detail,
                percent,
                streamIndex));
    }

    private sealed record FfmpegToolInfo(
        string Path,
        string Version);
}
