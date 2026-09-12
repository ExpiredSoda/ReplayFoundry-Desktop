using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.Subtitles;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed class FfmpegStudioProjectRenderingService :
    IStudioProjectRenderingService
{
    private readonly IProcessRunner _processRunner;
    private readonly IFfmpegToolLocator _toolLocator;
    private readonly IStudioRenderedMediaValidator? _validator;
    private readonly FfmpegEncodingExecutor? _encoding;
    private readonly StudioRenderCheckpointCache? _checkpoints;
    private readonly object _completedRenderLock = new();
    private readonly Dictionary<string, string> _completedRenderOwners =
        new(StringComparer.OrdinalIgnoreCase);

    public FfmpegStudioProjectRenderingService(
        IProcessRunner processRunner,
        IFfmpegToolLocator toolLocator,
        bool verifyOutput = false,
        bool hardwareEncoding = false,
        bool resumeCompletedSegments = false,
        IStudioRenderedMediaValidator? validator = null)
    {
        _processRunner = processRunner ??
            throw new ArgumentNullException(nameof(processRunner));
        _toolLocator = toolLocator ??
            throw new ArgumentNullException(nameof(toolLocator));
        _validator = validator ?? (verifyOutput ? new StudioRenderedMediaValidator(processRunner, toolLocator) : null);
        if (hardwareEncoding) _encoding = new FfmpegEncodingExecutor(processRunner);
        if (resumeCompletedSegments) _checkpoints = new StudioRenderCheckpointCache();
    }

    public async Task<StudioProjectRenderResult> FinalizeAsync(
        GenerationOutputProject draft,
        IProgress<StudioProjectRenderProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(progress);
        if (draft.IsFinalized)
        {
            throw new ArgumentException(
                "Studio can finalize only an unrendered draft.",
                nameof(draft));
        }
        if (draft.IncludedCount == 0)
        {
            throw new ArgumentException(
                "Keep at least one clip before rendering to Library.",
                nameof(draft));
        }

        cancellationToken.ThrowIfCancellationRequested();
        string finalDirectory = draft.OutputDirectory;
        RequireNewOutputDirectory(finalDirectory);
        string parent = Path.GetDirectoryName(finalDirectory)!;
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(
            parent,
            $".{Path.GetFileName(finalDirectory)}.studio-rendering-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        string captionWorkspace = Path.Combine(
            staging,
            ".caption-work");
        Directory.CreateDirectory(captionWorkspace);
        var stopwatch = Stopwatch.StartNew();
        bool movedToFinalDirectory = false;

        try
        {
            GenerationClipOutputProfile profile =
                GenerationClipOutputProfile.FromAsset(draft.IncludedAssets[0]);
            if (draft.Mode == GenerationMode.Montage && draft.IncludedAssets.Any(asset =>
                    GenerationClipOutputProfile.FromAsset(asset) is { } other &&
                    (other.Width != profile.Width || other.Height != profile.Height)))
                throw new InvalidOperationException("Choose one output canvas for this montage before rendering. Canvas changes apply to all montage clips.");
            GenerationOutputAsset[] stagedAssets =
                draft.Mode == GenerationMode.IndividualClips
                    ? await RenderIndividualAsync(
                        draft,
                        profile,
                        staging,
                        captionWorkspace,
                        progress,
                        cancellationToken)
                    : await RenderMontageAsync(
                        draft,
                        profile,
                        staging,
                        captionWorkspace,
                        progress,
                        cancellationToken);

            await StudioPlatformExportPackageWriter.WriteAsync(draft, stagedAssets, staging, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Delete(captionWorkspace, recursive: true);
            Directory.Move(staging, finalDirectory);
            movedToFinalDirectory = true;
            GenerationOutputAsset[] finalAssets = stagedAssets
                .Select(
                    asset =>
                        asset.WithRenderedOutput(
                            Path.Combine(
                                finalDirectory,
                                Path.GetFileName(
                                    asset.OutputFullPath!)),
                            Path.Combine(
                                finalDirectory,
                                Path.GetFileName(
                                    asset.ThumbnailFullPath!))))
                .ToArray();
            stopwatch.Stop();
            GenerationOutputProject finalized = draft.Finalize(
                finalAssets,
                DateTimeOffset.UtcNow);
            var result = new StudioProjectRenderResult(
                draft,
                finalized,
                stopwatch.Elapsed);
            lock (_completedRenderLock)
            {
                _completedRenderOwners.Add(
                    Path.GetFullPath(finalDirectory),
                    draft.Id);
            }
            return result;
        }
        catch
        {
            stopwatch.Stop();
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
            if (movedToFinalDirectory && Directory.Exists(finalDirectory))
            {
                Directory.Delete(finalDirectory, recursive: true);
            }
            throw;
        }
    }

    public void AcceptCompletedRender(StudioProjectRenderResult result) =>
        ReleaseCompletedRenderOwnership(result);

    public void DiscardCompletedRender(StudioProjectRenderResult result)
    {
        string outputDirectory = ReleaseCompletedRenderOwnership(result);

        try
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
        catch
        {
            lock (_completedRenderLock)
            {
                _completedRenderOwners[outputDirectory] = result.Draft.Id;
            }
            throw;
        }
    }

    internal int CompletedRenderOwnerCount
    {
        get
        {
            lock (_completedRenderLock)
            {
                return _completedRenderOwners.Count;
            }
        }
    }

    private string ReleaseCompletedRenderOwnership(
        StudioProjectRenderResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string outputDirectory = Path.GetFullPath(
            result.FinalizedProject.OutputDirectory);
        string draftOutputDirectory = Path.GetFullPath(
            result.Draft.OutputDirectory);
        string? parent = Path.GetDirectoryName(outputDirectory);
        if (!outputDirectory.Equals(
                draftOutputDirectory,
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(parent) ||
            outputDirectory.Equals(
                Path.GetPathRoot(outputDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The completed Studio render does not own a releasable output directory.");
        }

        lock (_completedRenderLock)
        {
            if (!_completedRenderOwners.TryGetValue(
                    outputDirectory,
                    out string? projectId) ||
                !projectId.Equals(result.Draft.Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Studio renderer cannot release an output it did not create.");
            }
            _completedRenderOwners.Remove(outputDirectory);
        }

        return outputDirectory;
    }

    private async Task<GenerationOutputAsset[]> RenderIndividualAsync(
        GenerationOutputProject draft,
        GenerationClipOutputProfile profile,
        string staging,
        string captionWorkspace,
        IProgress<StudioProjectRenderProgress> progress,
        CancellationToken cancellationToken)
    {
        var assets = new List<GenerationOutputAsset>();
        GenerationOutputAsset[] included = draft.IncludedAssets.ToArray();
        int total = included.Length;
        for (int index = 0; index < total; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GenerationOutputAsset asset = included[index];
            profile = GenerationClipOutputProfile.FromAsset(asset);
            progress.Report(
                new StudioProjectRenderProgress(
                    "Rendering final clips",
                    $"Creating clip {index + 1} of {total} with its Studio edits.",
                    index,
                    total));
            string output = Path.Combine(
                staging,
                BuildOutputFileName(asset));
            string? subtitleFileName = await WriteCaptionScriptAsync(
                asset,
                profile,
                captionWorkspace,
                cancellationToken);
            string? timedTextFileName = await WriteTimedTextScriptAsync(asset, profile, captionWorkspace, cancellationToken);
            await RunAsync(
                FfmpegClipRenderCommandBuilder.BuildSegment(
                    asset.SourceMedia,
                    asset.SourceStart,
                    asset.SourceEnd,
                    profile,
                    output,
                    subtitleFileName,
                    captionWorkspace,
                    asset.Appearance.VideoEffect,
                    asset.Appearance.VideoEffectIntensityPercent,
                    asset.Appearance.GraphicOverlays,
                    asset.RenderSettings,
                    timedTextFileName),
                cancellationToken,
                "final clip render",
                profile, asset.Duration,
                (fraction, detail) => progress.Report(new StudioProjectRenderProgress(
                    "Rendering final clips", detail ?? $"Encoding clip {index + 1} of {total}.", index, total, fraction)),
                requiresBt709ToneMap: RequiresBt709ToneMap(asset));
            string thumbnail = ThumbnailPath(output);
            await RunAsync(
                FfmpegClipRenderCommandBuilder.BuildThumbnail(
                    output,
                    asset.Duration,
                    thumbnail),
                cancellationToken,
                "Library thumbnail extraction");
            assets.Add(asset.WithRenderedOutput(output, thumbnail));
            if (asset.Captions is not null)
                await WriteSidecarsAsync(output, SubtitleSidecarSerializer.Project(asset.Captions,
                    asset.SourceStart, asset.Duration), asset.RenderSettings.BurnCaptions, cancellationToken);
            progress.Report(
                new StudioProjectRenderProgress(
                    "Rendering final clips",
                    $"Finished clip {index + 1} of {total}.",
                    index + 1,
                    total));
        }

        return assets.ToArray();
    }

    private async Task<GenerationOutputAsset[]> RenderMontageAsync(
        GenerationOutputProject draft,
        GenerationClipOutputProfile profile,
        string staging,
        string captionWorkspace,
        IProgress<StudioProjectRenderProgress> progress,
        CancellationToken cancellationToken)
    {
        string segmentDirectory = Path.Combine(staging, ".segments");
        Directory.CreateDirectory(segmentDirectory);
        var segments = new List<string>();
        GenerationOutputAsset[] included = draft.IncludedAssets.ToArray();
        int totalSteps = included.Length + 2;
        for (int index = 0; index < included.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GenerationOutputAsset asset = included[index];
            progress.Report(
                new StudioProjectRenderProgress(
                    "Rendering montage segments",
                    $"Creating segment {index + 1} of {included.Length}.",
                    index,
                    totalSteps));
            string output = Path.Combine(
                segmentDirectory,
                $"{index + 1:000}.mp4");
            string? subtitleFileName = await WriteCaptionScriptAsync(
                asset,
                profile,
                captionWorkspace,
                cancellationToken);
            string? timedTextFileName = await WriteTimedTextScriptAsync(asset, profile, captionWorkspace, cancellationToken);
            await RunAsync(
                FfmpegClipRenderCommandBuilder.BuildSegment(
                    asset.SourceMedia,
                    asset.SourceStart,
                    asset.SourceEnd,
                    profile,
                    output,
                    subtitleFileName,
                    captionWorkspace,
                    asset.Appearance.VideoEffect,
                    asset.Appearance.VideoEffectIntensityPercent,
                    asset.Appearance.GraphicOverlays,
                    asset.RenderSettings,
                    timedTextFileName),
                cancellationToken,
                "montage segment render", profile, asset.Duration,
                (fraction, detail) => progress.Report(new StudioProjectRenderProgress(
                    "Rendering montage segments", detail ?? $"Encoding segment {index + 1} of {included.Length}.",
                    index, totalSteps, fraction)), requiresBt709ToneMap: RequiresBt709ToneMap(asset));
            segments.Add(output);
        }

        string listPath = Path.Combine(segmentDirectory, "concat.txt");
        await File.WriteAllTextAsync(
            listPath,
            string.Join(
                Environment.NewLine,
                segments.Select((path, index) =>
                        "file '" + EscapeConcatPath(path) + "'" + Environment.NewLine +
                        "duration " + included[index].Duration.TotalSeconds.ToString("0.#########", CultureInfo.InvariantCulture))) +
            Environment.NewLine,
            new UTF8Encoding(false),
            cancellationToken);
        string montage = Path.Combine(
            staging,
            "ReplayFoundry-Montage.mp4");
        TimeSpan duration = TimeSpan.FromTicks(
            included.Sum(static asset => asset.Duration.Ticks));
        progress.Report(
            new StudioProjectRenderProgress(
                "Finishing montage",
                "Joining the completed segments without another video encode.",
                included.Length,
                totalSteps));
        try
        {
            await RunAsync(
                FfmpegClipRenderCommandBuilder.BuildConcatenation(listPath, montage, duration),
                cancellationToken, "montage join", profile, duration,
                requiresBt709ToneMap: RequiresBt709ToneMap(included[0]));
        }
        catch (StudioRenderedMediaValidationException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The copy completed, but packet-level joins can leave cadence gaps
            // between fractional video frames and independently primed AAC.
            // Reconstruct one continuous output clock without changing source
            // cut durations, caption offsets, or the validation requirements.
            File.Delete(montage);
            progress.Report(new StudioProjectRenderProgress("Finishing montage",
                "Normalizing picture and sound timing at the segment joins.", included.Length, totalSteps));
            await RunAsync(
                FfmpegClipRenderCommandBuilder.BuildNormalizedConcatenation(listPath, montage, duration,
                    profile, RequiresBt709ToneMap(included[0]),
                    // A mixed-quality montage must not downgrade a High segment
                    // merely because its first segment requested Compact.
                    included.Select(static asset => asset.RenderSettings.Quality).Max()),
                cancellationToken, "normalized montage join", profile, duration,
                requiresBt709ToneMap: RequiresBt709ToneMap(included[0]));
        }
        progress.Report(
            new StudioProjectRenderProgress(
                "Preparing Library preview",
                "Capturing a thumbnail from the completed montage.",
                totalSteps - 1,
                totalSteps));
        string thumbnail = ThumbnailPath(montage);
        await RunAsync(
            FfmpegClipRenderCommandBuilder.BuildThumbnail(
                montage,
                duration,
                thumbnail),
            cancellationToken,
            "Library thumbnail extraction");
        Directory.Delete(segmentDirectory, recursive: true);
        var cues = new List<SubtitleCue>();
        TimeSpan offset = TimeSpan.Zero;
        foreach (GenerationOutputAsset asset in included)
        {
            if (asset.Captions is not null)
                cues.AddRange(SubtitleSidecarSerializer.Project(asset.Captions, asset.SourceStart, asset.Duration)
                    .Select(cue => cue with { Start = cue.Start + offset, End = cue.End + offset }));
            offset += asset.Duration;
        }
        if (cues.Count > 0) await WriteSidecarsAsync(montage, cues,
            included.Any(asset => asset.RenderSettings.BurnCaptions && asset.Captions is not null), cancellationToken);
        progress.Report(
            new StudioProjectRenderProgress(
                "Finishing montage",
                "The final montage is ready for Library.",
                totalSteps,
                totalSteps));
        return included
            .Select(
                asset => asset.WithRenderedOutput(
                    montage,
                    thumbnail))
            .ToArray();
    }

    private static bool RequiresBt709ToneMap(GenerationOutputAsset asset) =>
        asset.SourceMedia.PrimaryVideoStream.ColorTransfer is "smpte2084" or "arib-std-b67";

    private async Task RunAsync(
        FfmpegClipRenderCommand command,
        CancellationToken cancellationToken,
        string operation = "Studio final render",
        GenerationClipOutputProfile? outputProfile = null,
        TimeSpan? expectedDuration = null,
        Action<double, string?>? progress = null,
        bool requiresBt709ToneMap = false)
    {
        using IDisposable mediaSlot = await MediaWorkBudget.AcquireAsync(cancellationToken, MediaWorkPriority.FinalOutput);
        string? checkpointKey = _checkpoints is not null && command.Arguments.Contains("-hw_encoding")
            ? StudioRenderCheckpointCache.CreateKey(command, _toolLocator.LocateFfmpeg()) : null;
        if (checkpointKey is not null && await _checkpoints!.TryRestoreAsync(checkpointKey, command.OutputPath, cancellationToken))
        {
            progress?.Invoke(1, "Reused a completed clip from the previous render.");
            return;
        }
        var elapsed = Stopwatch.StartNew();
        await using var watchdog = expectedDuration.HasValue ? new FfmpegProgressWatchdog(cancellationToken) : null;
        if (_encoding is not null) watchdog?.Pause();
        IReadOnlyList<string> arguments = expectedDuration.HasValue
            ? new[] { "-progress", "pipe:1", "-nostats", "-stats_period", "0.5" }.Concat(command.Arguments).ToArray()
            : command.Arguments;
        var request = new ProcessRunRequest(
                _toolLocator.LocateFfmpeg(),
                arguments,
                command.Timeout,
                command.WorkingDirectory,
                maxStandardOutputCharacters: 2 * 1024 * 1024,
                maxStandardErrorCharacters: 2 * 1024 * 1024,
                standardOutputLine: line =>
                {
                    if (line.StartsWith("encoder_started=", StringComparison.Ordinal))
                    {
                        elapsed.Restart();
                        watchdog?.Restart();
                        return;
                    }
                    if (line == "encoder_fallback=software")
                    {
                        elapsed.Restart();
                        watchdog?.Restart();
                        progress?.Invoke(0, "Hardware encoding was unavailable; continuing with the software encoder.");
                    }
                    if (expectedDuration is not { } duration || !line.StartsWith("out_time_us=", StringComparison.Ordinal) ||
                        !long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out long micros)) return;
                    watchdog?.Advance(micros);
                    double fraction = Math.Clamp(micros / 1_000_000d / duration.TotalSeconds, 0, .99);
                    string? remaining = fraction > .02 ?
                        $"Encoding · {fraction:P0} · about {Math.Max(1, (int)(elapsed.Elapsed.TotalSeconds * (1 - fraction) / fraction))} seconds remaining" : null;
                    progress?.Invoke(fraction, remaining);
                });
        async Task ValidateOutput(CancellationToken token)
        {
            watchdog?.Pause();
            if (_validator is not null && outputProfile is not null && expectedDuration is { } expected)
            {
                progress?.Invoke(.99, "Checking the rendered picture, sound, and duration.");
                await _validator.ValidateAsync(command.OutputPath, expected, outputProfile, requiresBt709ToneMap, token);
            }
        }
        ProcessRunResult result;
        try
        {
            result = _encoding is null
                ? await _processRunner.RunAsync(request, watchdog?.Token ?? cancellationToken)
                : await _encoding.RunAsync(request, watchdog?.Token ?? cancellationToken, ValidateOutput);
        }
        catch (OperationCanceledException) when (watchdog?.IsStalled == true && !cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("The encoder stopped making progress for two minutes. Retry the queue; completed clips remain reusable.");
        }
        if (!result.Succeeded ||
            !File.Exists(command.OutputPath) ||
            new FileInfo(command.OutputPath).Length <= 0)
        {
            throw new InvalidOperationException(
                $"FFmpeg could not complete the Studio {operation}. " +
                $"Exit code: {result.ExitCode}. " +
                result.StandardError);
        }
        if (_encoding is null) await ValidateOutput(cancellationToken);
        if (checkpointKey is not null)
        {
            try { await _checkpoints!.StoreAsync(checkpointKey, command.OutputPath, cancellationToken); }
            catch (IOException) { /* Cache capacity or access never invalidates a finished export. */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task WriteSidecarsAsync(string video, IEnumerable<SubtitleCue> cues, bool burnedCaptions, CancellationToken cancellationToken)
    {
        SubtitleCue[] snapshot = cues.ToArray();
        foreach (SubtitleSidecarFormat format in Enum.GetValues<SubtitleSidecarFormat>())
        {
            string path = StudioCaptionSidecarPaths.Resolve(video,
                format == SubtitleSidecarFormat.Srt ? ".srt" : ".vtt", burnedCaptions);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path,
                SubtitleSidecarSerializer.Build(snapshot, format), new UTF8Encoding(false), cancellationToken);
        }
    }

    private static async Task<string?> WriteTimedTextScriptAsync(GenerationOutputAsset asset,
        GenerationClipOutputProfile profile, string workspace, CancellationToken cancellationToken)
    {
        if (asset.RenderSettings.TimedTextOverlays.Count == 0) return null;
        string name = $"text-{asset.Rank:000}.ass";
        await File.WriteAllTextAsync(Path.Combine(workspace, name), StudioTimedTextScript.Build(
            asset.RenderSettings.TimedTextOverlays, profile, asset.SourceStart, asset.SourceEnd),
            new UTF8Encoding(true), cancellationToken);
        return name;
    }

    private static async Task<string?> WriteCaptionScriptAsync(
        GenerationOutputAsset asset,
        GenerationClipOutputProfile profile,
        string captionWorkspace,
        CancellationToken cancellationToken)
    {
        if (asset.Captions is null || !asset.RenderSettings.BurnCaptions)
        {
            return null;
        }

        string fileName = $"caption-{asset.Rank:000}.ass";
        AssSubtitleDocument document = AssSubtitleDocumentBuilder.Build(
            asset.Captions,
            profile.Width,
            profile.Height,
            asset.SourceStart,
            asset.Duration,
            asset.Appearance.CaptionVerticalPositionPercent,
            asset.Appearance.CaptionWordLimit,
            asset.Appearance.CaptionMaximumWidthPercent,
            asset.Appearance.CaptionFontScalePercent,
            asset.Appearance.CaptionTypography);
        await File.WriteAllTextAsync(
            Path.Combine(captionWorkspace, fileName),
            document.Script,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            cancellationToken);
        return fileName;
    }

    internal static string BuildOutputFileName(
        GenerationOutputAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string source = asset.EditorialMetadata?.Title ??
            Path.GetFileNameWithoutExtension(asset.SourceFullPath);
        char[] invalid = Path.GetInvalidFileNameChars();
        string safe = new(
            source.Select(character =>
                    invalid.Contains(character) ? '-' : character)
                .ToArray());
        safe = string.Join(
                " ",
                safe.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries))
            .Trim(' ', '.');
        if (safe.Length == 0)
        {
            safe = "Replay Foundry clip";
        }
        safe = safe.Length > 96 ? safe[..96].TrimEnd(' ', '.') : safe;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{asset.Rank:000}-{safe}.mp4");
    }

    private static string ThumbnailPath(string renderedOutputPath) =>
        Path.Combine(
            Path.GetDirectoryName(renderedOutputPath)!,
            Path.GetFileNameWithoutExtension(renderedOutputPath) +
            ".thumbnail.jpg");

    private static string EscapeConcatPath(string path) =>
        path.Replace("'", "'\\''", StringComparison.Ordinal)
            .Replace('\\', '/');

    private static void RequireNewOutputDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !Path.IsPathFullyQualified(path) ||
            Directory.Exists(path) ||
            File.Exists(path))
        {
            throw new IOException(
                "Studio final rendering requires a new fully qualified output directory.");
        }
    }
}
