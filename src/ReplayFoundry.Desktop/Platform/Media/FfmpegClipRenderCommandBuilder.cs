using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Features.Studio.Editing;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed class FfmpegClipRenderCommand
{
    private readonly ReadOnlyCollection<string> _arguments;

    public FfmpegClipRenderCommand(
        IEnumerable<string> arguments,
        TimeSpan timeout,
        string outputPath,
        string? workingDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string[] snapshot = arguments.ToArray();
        if (snapshot.Length == 0 ||
            snapshot.Any(static value => value is null) ||
            timeout <= TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(outputPath) ||
            !Path.IsPathFullyQualified(outputPath))
        {
            throw new ArgumentException(
                "An FFmpeg clip command requires bounded arguments, timeout, and output.");
        }
        _arguments = Array.AsReadOnly(snapshot);
        Timeout = timeout;
        OutputPath = Path.GetFullPath(outputPath);
        WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
            ? null
            : Path.GetFullPath(workingDirectory);
    }

    public IReadOnlyList<string> Arguments => _arguments;
    public TimeSpan Timeout { get; }
    public string OutputPath { get; }
    public string? WorkingDirectory { get; }
}

internal static class FfmpegClipRenderCommandBuilder
{
    public static FfmpegClipRenderCommand BuildSegment(
        GenerationMomentCandidate candidate,
        GenerationClipOutputProfile profile,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return BuildSegment(
            candidate.AnalyzedSource.PreparedSource.Media,
            candidate.Candidate.Window.Start,
            candidate.Candidate.Window.End,
            profile,
            outputPath);
    }

    public static FfmpegClipRenderCommand BuildSegment(
        MediaProbeResult media,
        TimeSpan sourceStart,
        TimeSpan sourceEnd,
        GenerationClipOutputProfile profile,
        string outputPath,
        string? subtitleFileName = null,
        string? workingDirectory = null,
        StudioVideoEffectPreset videoEffect =
            StudioVideoEffectPreset.None,
        double videoEffectIntensityPercent = 0,
        IReadOnlyList<StudioGraphicOverlay>? graphicOverlays = null,
        StudioRenderSettings? renderSettings = null,
        string? timedTextFileName = null)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(profile);
        RequireOutput(outputPath);
        if (sourceStart < TimeSpan.Zero ||
            sourceEnd <= sourceStart ||
            sourceEnd > media.Duration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceStart),
                "The rendered window must be positive and remain inside the source.");
        }
        if (subtitleFileName is not null &&
            (Path.GetFileName(subtitleFileName) != subtitleFileName ||
             !subtitleFileName.EndsWith(
                 ".ass",
                 StringComparison.OrdinalIgnoreCase) ||
             string.IsNullOrWhiteSpace(workingDirectory) ||
             !Path.IsPathFullyQualified(workingDirectory)))
        {
            throw new ArgumentException(
                "Subtitle scripts must use a simple ASS filename and an explicit working directory.",
                nameof(subtitleFileName));
        }
        if (timedTextFileName is not null &&
            (!System.Text.RegularExpressions.Regex.IsMatch(timedTextFileName, "^[A-Za-z0-9_-]+\\.ass$") ||
             string.IsNullOrWhiteSpace(workingDirectory) || !Path.IsPathFullyQualified(workingDirectory)))
            throw new ArgumentException("Text overlays require an owned ASS filename and working directory.", nameof(timedTextFileName));
        if (!Enum.IsDefined(videoEffect) ||
            !double.IsFinite(videoEffectIntensityPercent) ||
            videoEffectIntensityPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(videoEffectIntensityPercent));
        }
        StudioGraphicOverlay[] overlays = graphicOverlays?.ToArray() ?? [];
        if (overlays.Any(static overlay => overlay is null) ||
            overlays.Any(static overlay => !File.Exists(overlay.ImageFullPath)))
        {
            throw new ArgumentException(
                "Graphic overlay inputs must be existing validated image files.",
                nameof(graphicOverlays));
        }

        TimeSpan clipDuration = sourceEnd - sourceStart;
        string start = Seconds(sourceStart);
        string duration = Seconds(clipDuration);
        int videoBitRate = CalculateVideoBitRate(profile);
        videoBitRate = checked((int)(videoBitRate * (renderSettings?.Quality switch
        {
            StudioExportQuality.Compact => 0.65,
            StudioExportQuality.High => 1.5,
            _ => 1,
        })));
        string filter =
            $"scale={profile.Width}:{profile.Height}:" +
            "force_original_aspect_ratio=decrease," +
            $"pad={profile.Width}:{profile.Height}:" +
            "(ow-iw)/2:(oh-ih)/2:color=black," +
            "setsar=1," +
            $"fps={profile.FfmpegFrameRate}:start_time=0:eof_action=pass," +
            BuildVideoEffectFilter(
                videoEffect,
                videoEffectIntensityPercent) +
            (subtitleFileName is null
                ? string.Empty
                : $"ass=filename='{subtitleFileName}',") +
            (timedTextFileName is null ? string.Empty : $"ass=filename='{timedTextFileName}',") +
            "format=yuv420p";
        var arguments = new List<string>
        {
            "-hide_banner",
            "-nostdin",
            "-v",
            "error",
            "-n",
            "-ss",
            start,
            "-i",
            media.FullPath,
        };
        foreach (StudioGraphicOverlay overlay in overlays)
        {
            arguments.AddRange(["-loop", "1", "-i", overlay.ImageFullPath]);
        }
        int audioStreamCount = media.AudioStreams.Count;
        if (audioStreamCount == 0)
        {
            arguments.AddRange(
            [
                "-f",
                "lavfi",
                "-i",
                "anullsrc=r=48000:cl=stereo",
            ]);
        }
        var filterGraph = new List<string>();
        string videoMap = $"0:{media.PrimaryVideoStream.Index}";
        string videoInput = $"[0:{media.PrimaryVideoStream.Index}]";
        if (renderSettings is not null)
        {
            videoInput = FfmpegStudioCompositionGraph.AppendVideo(filterGraph, media, profile, renderSettings, sourceStart);
            filter = $"fps={profile.FfmpegFrameRate}:start_time=0:eof_action=pass," +
                BuildVideoEffectFilter(videoEffect, videoEffectIntensityPercent) +
                (subtitleFileName is null ? string.Empty : $"ass=filename='{subtitleFileName}',") +
                (timedTextFileName is null ? string.Empty : $"ass=filename='{timedTextFileName}',") +
                "format=yuv420p";
        }
        if (overlays.Length > 0)
        {
            filterGraph.Add($"{videoInput}{filter}[vstage0]");
            for (int index = 0; index < overlays.Length; index++)
            {
                StudioGraphicOverlay overlay = overlays[index];
                int width = Math.Max(2, checked((int)Math.Round(
                    profile.Width * overlay.WidthPercent / 100d)));
                int centerX = checked((int)Math.Round(
                    profile.Width * overlay.CenterXPercent / 100d));
                int centerY = checked((int)Math.Round(
                    profile.Height * overlay.CenterYPercent / 100d));
                filterGraph.Add(
                    $"[{index + 1}:v:0]format=rgba,scale={width}:-1[graphic{index}]");
                filterGraph.Add(
                    $"[vstage{index}][graphic{index}]" +
                    $"overlay=x={centerX}-overlay_w/2:y={centerY}-overlay_h/2:" +
                    $"format=auto:eof_action=repeat[vstage{index + 1}]");
            }
            videoMap = $"[vstage{overlays.Length}]";
        }
        else if (renderSettings is not null)
        {
            filterGraph.Add($"{videoInput}{filter}[vout]");
            videoMap = "[vout]";
        }
        string? customAudioMap = null;
        if (audioStreamCount > 0 && renderSettings is not null &&
            (renderSettings.AudioTracks.Count > 0 || renderSettings.DuckGameplay ||
             renderSettings.AudioMastering.NormalizeLoudness || renderSettings.AudioMastering.LimitTruePeak))
            customAudioMap = FfmpegStudioCompositionGraph.AppendAudio(filterGraph, media, renderSettings, clipDuration);
        else if (audioStreamCount > 1)
        {
            string audioMixInputs = string.Concat(
                media.AudioStreams.Select(
                    static stream => $"[0:{stream.Index}]"));
            filterGraph.Add(
                audioMixInputs +
                $"amix=inputs={audioStreamCount}:" +
                "duration=longest:dropout_transition=0:" +
                "normalize=0," +
                "alimiter=limit=0.95:level=disabled:latency=1[aout]");
        }
        if (filterGraph.Count > 0)
        {
            arguments.AddRange(["-filter_complex", string.Join(";", filterGraph)]);
        }
        arguments.AddRange(
        [
            "-t",
            duration,
            "-map",
            videoMap,
            "-map",
            customAudioMap ?? (audioStreamCount switch
            {
                0 => $"{overlays.Length + 1}:a:0",
                1 => $"0:{media.AudioStreams[0].Index}",
                _ => "[aout]",
            }),
        ]);
        arguments.AddRange(FfmpegH264EncodingPolicy.CreateArguments(videoBitRate));
        // Output -t owns the requested cut duration. -shortest can terminate
        // H.264 output one frame early while the AAC encoder drains.
        arguments.AddRange(
        [
            "-c:a",
            "aac",
            "-b:a",
            "192k",
            "-ar",
            "48000",
            "-ac",
            "2",
            "-movflags",
            "+faststart",
            outputPath,
        ]);
        if (overlays.Length == 0 && renderSettings is null)
        {
            int outputIndex = arguments.Count - 1;
            arguments.InsertRange(outputIndex, ["-vf", filter]);
        }

        TimeSpan timeout = TimeSpan.FromSeconds(
            Math.Clamp(
                clipDuration.TotalSeconds * 6,
                120,
                1800));
        return new(
            arguments,
            timeout,
            outputPath,
            workingDirectory);
    }

    public static FfmpegClipRenderCommand BuildConcatenation(
        string concatListPath,
        string outputPath,
        TimeSpan totalDuration)
    {
        if (string.IsNullOrWhiteSpace(concatListPath) ||
            !Path.IsPathFullyQualified(concatListPath) ||
            !File.Exists(concatListPath) ||
            totalDuration <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Montage concatenation requires a retained list and positive duration.",
                nameof(concatListPath));
        }
        RequireOutput(outputPath);
        string[] arguments =
        [
            "-hide_banner",
            "-nostdin",
            "-v",
            "error",
            "-n",
            "-f",
            "concat",
            "-safe",
            "0",
            "-i",
            concatListPath,
            "-c",
            "copy",
            "-movflags",
            "+faststart",
            outputPath,
        ];
        TimeSpan timeout = TimeSpan.FromSeconds(
            Math.Clamp(
                totalDuration.TotalSeconds * 2,
                120,
                1800));
        return new(arguments, timeout, outputPath);
    }

    public static FfmpegClipRenderCommand BuildNormalizedConcatenation(
        string concatListPath, string outputPath, TimeSpan totalDuration,
        GenerationClipOutputProfile profile, bool requiresBt709Tags = false,
        StudioExportQuality quality = StudioExportQuality.Standard)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!Enum.IsDefined(quality)) throw new ArgumentOutOfRangeException(nameof(quality));
        // Reuse path/duration validation, but retain only the demuxer prefix.
        FfmpegClipRenderCommand copy = BuildConcatenation(concatListPath, outputPath, totalDuration);
        var arguments = copy.Arguments.TakeWhile(static value => value != "-c").ToList();
        string duration = Seconds(totalDuration);
        arguments.AddRange([
            "-map", "0:v:0", "-map", "0:a:0",
            "-vf", $"fps={profile.FfmpegFrameRate}:start_time=0:eof_action=pass," +
                $"tpad=stop_mode=clone:stop_duration=0.3,trim=duration={duration},format=yuv420p",
            "-af", $"aresample=48000:async=1:first_pts=0,apad=whole_dur={duration}," +
                $"atrim=duration={duration},asetpts=N/SR/TB",
            "-t", duration,
        ]);
        int bitrate = checked((int)(CalculateVideoBitRate(profile) * (quality switch
        {
            StudioExportQuality.Compact => .65,
            StudioExportQuality.High => 1.5,
            _ => 1,
        })));
        arguments.AddRange(FfmpegH264EncodingPolicy.CreateArguments(bitrate));
        arguments.AddRange(["-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2"]);
        if (requiresBt709Tags)
            arguments.AddRange(["-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709", "-color_range", "tv"]);
        arguments.AddRange(["-movflags", "+faststart", outputPath]);
        return new(arguments, TimeSpan.FromSeconds(Math.Clamp(totalDuration.TotalSeconds * 6, 120, 1800)), outputPath);
    }

    public static FfmpegClipRenderCommand BuildThumbnail(
        string renderedVideoPath,
        TimeSpan duration,
        string outputPath)
    {
        if (string.IsNullOrWhiteSpace(renderedVideoPath) ||
            !Path.IsPathFullyQualified(renderedVideoPath) ||
            duration <= TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(outputPath) ||
            !Path.IsPathFullyQualified(outputPath) ||
            !string.Equals(
                Path.GetExtension(outputPath),
                ".jpg",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Thumbnail extraction requires a rendered video, positive duration, and a fully qualified JPEG output.");
        }

        TimeSpan sample = TimeSpan.FromTicks(
            Math.Min(
                duration.Ticks / 3,
                Math.Max(0, duration.Ticks - TimeSpan.FromMilliseconds(100).Ticks)));
        string[] arguments =
        [
            "-hide_banner",
            "-nostdin",
            "-v",
            "error",
            "-n",
            "-ss",
            Seconds(sample),
            "-i",
            Path.GetFullPath(renderedVideoPath),
            "-frames:v",
            "1",
            "-vf",
            "scale=640:640:force_original_aspect_ratio=decrease",
            "-q:v",
            "3",
            Path.GetFullPath(outputPath),
        ];
        return new(
            arguments,
            TimeSpan.FromMinutes(1),
            Path.GetFullPath(outputPath));
    }

    private static string Seconds(TimeSpan value) =>
        value.TotalSeconds.ToString(
            "0.#########",
            CultureInfo.InvariantCulture);

    private static int CalculateVideoBitRate(
        GenerationClipOutputProfile profile)
    {
        double bitsPerSecond =
            profile.Width *
            (double)profile.Height *
            profile.FramesPerSecond *
            0.12d;
        return checked((int)Math.Clamp(
            Math.Round(bitsPerSecond),
            1_500_000d,
            Math.Max(profile.Width, profile.Height) > 1920 ? 80_000_000d : 30_000_000d));
    }

    private static string BuildVideoEffectFilter(
        StudioVideoEffectPreset effect,
        double intensityPercent)
    {
        if (effect == StudioVideoEffectPreset.None ||
            intensityPercent <= 0)
        {
            return string.Empty;
        }

        double amount = intensityPercent / 100d;
        return effect switch
        {
            StudioVideoEffectPreset.Noir =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"hue=s={1 - amount:0.###}:" +
                    $"b={-0.025 * amount:0.###}," +
                    $"curves=preset=increase_contrast,"),
            StudioVideoEffectPreset.Chromatic =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"rgbashift=rh={Math.Max(1, (int)Math.Round(4 * amount))}:" +
                    $"bh={Math.Min(-1, -(int)Math.Round(4 * amount))}:edge=smear," +
                    $"hue=s={1 + 0.12 * amount:0.###},"),
            StudioVideoEffectPreset.SoftBloom =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"gblur=sigma={0.25 + 0.9 * amount:0.###}:steps=1," +
                    $"hue=b={0.035 * amount:0.###}:" +
                    $"s={1 + 0.10 * amount:0.###},"),
            StudioVideoEffectPreset.Vivid =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"vibrance=intensity={0.65 * amount:0.###},"),
            _ => throw new ArgumentOutOfRangeException(nameof(effect)),
        };
    }

    private static void RequireOutput(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath) ||
            !Path.IsPathFullyQualified(outputPath) ||
            !string.Equals(
                Path.GetExtension(outputPath),
                ".mp4",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "FFmpeg clip output must be a fully qualified MP4 path.",
                nameof(outputPath));
        }
    }
}
