using System.Globalization;
using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Geometry;
using ReplayFoundry.Desktop.Platform.Processes;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed record StudioSourceTrackingDraft(StudioSourceCropTrack? Track,
    StudioTranslationAnalysis Analysis, IReadOnlyList<byte[]> Frames, int Width, int Height);

internal sealed class FfmpegStudioSourceTrackingService
{
    private readonly IProcessRunner _runner;
    private readonly IFfmpegToolLocator _tools;
    internal FfmpegStudioSourceTrackingService(IProcessRunner? runner = null, IFfmpegToolLocator? tools = null)
    { _runner = runner ?? new WindowsProcessRunner(); _tools = tools ?? new FfmpegToolLocator(); }

    internal async Task<StudioSourceTrackingDraft> AnalyzeAsync(GenerationOutputAsset asset,
        StudioCropTrackingTarget target, NormalizedRectangle seed, TimeSpan sourceStart, TimeSpan duration,
        double viewportZoom, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!Enum.IsDefined(target) || !double.IsFinite(viewportZoom) || viewportZoom is < 1 or > 4 ||
            sourceStart < asset.SourceStart || sourceStart >= asset.SourceEnd || duration < TimeSpan.FromSeconds(.25) ||
            duration > TimeSpan.FromSeconds(60) || sourceStart + duration > asset.SourceEnd)
            throw new ArgumentException("Analyze a 0.25–60 second interval inside this cut, with viewport zoom between 1 and 4.");
        var original = new FileInfo(asset.SourceFullPath);
        if (!original.Exists) throw new FileNotFoundException("The tracking source recording is missing.", asset.SourceFullPath);
        long length = original.Length; DateTime modified = original.LastWriteTimeUtc;
        var display = EffectiveDisplayGeometryCalculator.Calculate(asset.SourceMedia.PrimaryVideoStream);
        double scale = Math.Min(1d, 320d / Math.Max(display.Width, display.Height));
        int width = Math.Max(8, (int)Math.Round(display.Width * scale)), height = Math.Max(8, (int)Math.Round(display.Height * scale));
        NormalizedRectangle manual = target == StudioCropTrackingTarget.Gameplay ? asset.RenderSettings.GameplayRegion :
            asset.RenderSettings.Decoration.HudSourceRegion ?? throw new InvalidOperationException("Enable and position the HUD layer before tracking its source region.");
        NormalizedRectangle bounds = target == StudioCropTrackingTarget.Gameplay ? manual : NormalizedRectangle.FullFrame;
        if (!bounds.Contains(seed)) throw new ArgumentException("The seed must fit inside the allowed gameplay region.", nameof(seed));
        NormalizedRectangle viewport = CreateViewport(asset, target, seed, bounds, manual, viewportZoom);
        string temporaryRoot = ReplayFoundryLocalDataPaths.ResolveTemporary("SourceTracking");
        Directory.CreateDirectory(temporaryRoot);
        string raw = Path.Combine(temporaryRoot, Guid.NewGuid().ToString("N") + ".gray");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        try
        {
            using IDisposable lease = await MediaWorkBudget.AcquireAsync(timeout.Token, MediaWorkPriority.Foreground);
            ProcessRunResult result = await _runner.RunAsync(new(_tools.LocateFfmpeg(),
                ["-hide_banner", "-nostdin", "-v", "error", "-n", "-hwaccel", "none", "-threads:v", "2",
                    "-ss", Number(sourceStart.TotalSeconds), "-i", asset.SourceFullPath,
                    "-t", Number(duration.TotalSeconds), "-map", $"0:{asset.SourceMedia.PrimaryVideoStream.Index}", "-an",
                    "-vf", FfmpegStudioCompositionGraph.Normalization(asset.SourceMedia, asset.RenderSettings) +
                        $",scale={width}:{height},setsar=1,setpts=PTS-STARTPTS,fps=8:start_time=0:round=near,format=gray",
                    "-filter_threads", "1", "-threads:v", "1", "-frames:v", "480", "-c:v", "rawvideo", "-f", "rawvideo", raw],
                TimeSpan.FromSeconds(90), maxStandardErrorCharacters: 4096), timeout.Token);
            if (!result.Succeeded) throw new InvalidOperationException("Source tracking frames could not be decoded. " + result.StandardError);
            int frameBytes = checked(width * height);
            long rawLength = new FileInfo(raw).Length;
            if (rawLength > StudioRegionTranslationTracker.MaximumBytes || rawLength % frameBytes != 0 ||
                rawLength / frameBytes is < 2 or > StudioRegionTranslationTracker.MaximumFrames)
                throw new InvalidOperationException("The tracking decoder did not return a complete bounded frame sequence.");
            var frames = new byte[rawLength / frameBytes][];
            await using (var stream = new FileStream(raw, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
                for (int index = 0; index < frames.Length; index++)
                {
                    frames[index] = new byte[frameBytes];
                    await stream.ReadExactlyAsync(frames[index], timeout.Token);
                }
            var analysis = await Task.Run(() => StudioRegionTranslationTracker.Analyze(frames, width, height,
                sourceStart, seed, bounds, viewport, timeout.Token), timeout.Token);
            original.Refresh();
            if (!original.Exists || original.Length != length || original.LastWriteTimeUtc != modified)
                throw new InvalidOperationException("The source recording changed during tracking; the result was discarded.");
            timeout.Token.ThrowIfCancellationRequested();
            StudioSourceCropTrack? track = analysis.Runs.Count == 0 ? null : new(target, asset.SourceFullPath,
                length, modified, display.Width, display.Height, sourceStart, sourceStart + duration, seed, bounds, manual,
                viewport.Width, viewport.Height, analysis.Runs, frames.Length,
                analysis.Samples.Count(static sample => sample.Viewport is not null), analysis.Samples[^1].State,
                canvas: asset.RenderSettings.Canvas, layout: asset.RenderSettings.Layout,
                facecamHeightPercent: asset.RenderSettings.FacecamHeightPercent, hasFacecam: asset.RenderSettings.FacecamRegion is not null);
            return new(track, analysis, frames, width, height);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new InvalidOperationException("Tracking exceeded its two-minute budget. Shorten the interval and try again."); }
        finally
        {
            try { if (File.Exists(raw)) File.Delete(raw); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static NormalizedRectangle CreateViewport(GenerationOutputAsset asset, StudioCropTrackingTarget target,
        NormalizedRectangle seed, NormalizedRectangle bounds, NormalizedRectangle manual, double zoom)
    {
        if (target == StudioCropTrackingTarget.Hud) return manual;
        var profile = GenerationClipOutputProfile.FromAsset(asset);
        var display = EffectiveDisplayGeometryCalculator.Calculate(asset.SourceMedia.PrimaryVideoStream);
        double paneHeight = profile.Height;
        if (asset.RenderSettings.Layout is StudioCompositionLayout.FacecamTop or StudioCompositionLayout.FacecamTopFit &&
            asset.RenderSettings.FacecamRegion is not null)
            paneHeight -= Math.Clamp(((int)Math.Round(profile.Height * asset.RenderSettings.FacecamHeightPercent / 100)) & ~1, 2, profile.Height - 2);
        double ratio = profile.Width / paneHeight * display.Height / display.Width;
        double w = Math.Min(bounds.Width, bounds.Height * ratio) / zoom;
        double h = w / ratio;
        return new(Math.Clamp(seed.X + seed.Width / 2 - w / 2, bounds.X, bounds.Right - w),
            Math.Clamp(seed.Y + seed.Height / 2 - h / 2, bounds.Y, bounds.Bottom - h), w, h);
    }
    private static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
