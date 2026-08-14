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
    private readonly object _completedRenderLock = new();
    private readonly Dictionary<string, string> _completedRenderOwners =
        new(StringComparer.OrdinalIgnoreCase);

    public FfmpegStudioProjectRenderingService(
        IProcessRunner processRunner,
        IFfmpegToolLocator toolLocator)
    {
        _processRunner = processRunner ??
            throw new ArgumentNullException(nameof(processRunner));
        _toolLocator = toolLocator ??
            throw new ArgumentNullException(nameof(toolLocator));
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
                "Keep at least one candidate before rendering to Library.",
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
                GenerationClipOutputProfile.FromReference(
                    draft.IncludedAssets[0].SourceMedia.PrimaryVideoStream);
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
                    asset.Appearance.GraphicOverlays),
                cancellationToken,
                "final clip render");
            string thumbnail = ThumbnailPath(output);
            await RunAsync(
                FfmpegClipRenderCommandBuilder.BuildThumbnail(
                    output,
                    asset.Duration,
                    thumbnail),
                cancellationToken,
                "Library thumbnail extraction");
            assets.Add(asset.WithRenderedOutput(output, thumbnail));
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
                    asset.Appearance.GraphicOverlays),
                cancellationToken);
            segments.Add(output);
        }

        string listPath = Path.Combine(segmentDirectory, "concat.txt");
        await File.WriteAllTextAsync(
            listPath,
            string.Join(
                Environment.NewLine,
                segments.Select(
                    path =>
                        "file '" + EscapeConcatPath(path) + "'")) +
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
        await RunAsync(
            FfmpegClipRenderCommandBuilder.BuildConcatenation(
                listPath,
                montage,
                duration),
            cancellationToken,
            "montage join");
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

    private async Task RunAsync(
        FfmpegClipRenderCommand command,
        CancellationToken cancellationToken,
        string operation = "Studio final render")
    {
        ProcessRunResult result = await _processRunner.RunAsync(
            new ProcessRunRequest(
                _toolLocator.LocateFfmpeg(),
                command.Arguments,
                command.Timeout,
                command.WorkingDirectory,
                maxStandardOutputCharacters: 64 * 1024,
                maxStandardErrorCharacters: 2 * 1024 * 1024),
            cancellationToken);
        if (!result.Succeeded ||
            !File.Exists(command.OutputPath) ||
            new FileInfo(command.OutputPath).Length <= 0)
        {
            throw new InvalidOperationException(
                $"FFmpeg could not complete the Studio {operation}. " +
                $"Exit code: {result.ExitCode}. " +
                result.StandardError);
        }
    }

    private static async Task<string?> WriteCaptionScriptAsync(
        GenerationOutputAsset asset,
        GenerationClipOutputProfile profile,
        string captionWorkspace,
        CancellationToken cancellationToken)
    {
        if (asset.Captions is null)
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
            asset.Appearance.CaptionFontScalePercent);
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
