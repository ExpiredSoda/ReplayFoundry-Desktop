using System.Globalization;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Geometry;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfmpegStudioCompositionGraph
{
    internal static string AppendVideo(List<string> graph, MediaProbeResult media,
        GenerationClipOutputProfile profile, StudioRenderSettings settings, TimeSpan sourceStart = default)
    {
        EffectiveDisplayGeometry display = EffectiveDisplayGeometryCalculator.Calculate(media.PrimaryVideoStream);
        string source = $"[0:{media.PrimaryVideoStream.Index}]";
        string normalization = Normalization(media, settings);
        foreach (StudioSourceCropTrack track in settings.SourceCropTracks) track.RequireSource(media, checkFile: true);
        graph.Add(source + normalization + "[normalized]");
        bool hasHud = settings.Decoration.HudSourceRegion is not null;
        string baseInput = "[normalized]";
        if (hasHud)
        {
            graph.Add("[normalized]split=2[layoutinput][hudinput]");
            baseInput = "[layoutinput]";
        }
        string background = "0x" + settings.Decoration.BackgroundColor[1..];
        bool stack = settings.Layout is StudioCompositionLayout.FacecamTop or StudioCompositionLayout.FacecamTopFit && settings.FacecamRegion is not null;
        if (stack)
        {
            int faceHeight = Math.Clamp(((int)Math.Round(profile.Height * settings.FacecamHeightPercent / 100)) & ~1, 2, profile.Height - 2);
            graph.Add(baseInput + "split=2[gameinput][faceinput]");
            AppendGameplay(graph, "[gameinput]", "[gamecanvas]", display, settings, sourceStart,
                profile.Width, profile.Height - faceHeight, settings.Layout == StudioCompositionLayout.FacecamTop, background);
            graph.Add("[faceinput]" + Crop(display, settings.FacecamRegion!) + "," +
                Fit(profile.Width, faceHeight, fill: true, background) + "[facecanvas]");
            graph.Add("[facecanvas][gamecanvas]vstack=inputs=2[composed]");
        }
        else
        {
            AppendGameplay(graph, baseInput, "[composed]", display, settings, sourceStart,
                profile.Width, profile.Height, settings.Layout == StudioCompositionLayout.Fill, background);
        }
        string composed = "[composed]";
        if (hasHud)
        {
            NormalizedRectangle position = settings.Decoration.HudCanvasRegion!;
            int width = Math.Clamp(((int)Math.Round(profile.Width * position.Width)) & ~1, 2, profile.Width);
            int height = Math.Clamp(((int)Math.Round(profile.Height * position.Height)) & ~1, 2, profile.Height);
            int x = Math.Clamp((int)Math.Round(profile.Width * position.X), 0, profile.Width - width);
            int y = Math.Clamp((int)Math.Round(profile.Height * position.Y), 0, profile.Height - height);
            StudioSourceCropTrack? hudTrack = settings.SourceCropTracks.FirstOrDefault(static track => track.Target == StudioCropTrackingTarget.Hud);
            string crop = hudTrack is null ? Crop(display, settings.Decoration.HudSourceRegion!) :
                FfmpegTrackedSourceCropGraph.Crop(hudTrack, display, sourceStart, manualFallback: true);
            graph.Add("[hudinput]" + crop + "," + Fit(width, height, false, background) + "[hudcanvas]");
            graph.Add($"[composed][hudcanvas]overlay=x={x}:y={y}:shortest=1[withhud]");
            composed = "[withhud]";
        }
        if (settings.FrameKeyframes.Count == 0) return composed;
        string zoom = Interpolate(settings.FrameKeyframes, sourceStart, static frame => frame.Zoom);
        string panX = Interpolate(settings.FrameKeyframes, sourceStart, static frame => frame.PanXPercent);
        string panY = Interpolate(settings.FrameKeyframes, sourceStart, static frame => frame.PanYPercent);
        graph.Add($"{composed}fps={profile.FfmpegFrameRate},zoompan=z='{zoom}':" +
            $"x='(iw-iw/zoom)*({panX})/100':y='(ih-ih/zoom)*({panY})/100':" +
            $"d=1:s={profile.Width}x{profile.Height}:fps={profile.FfmpegFrameRate}[framed]");
        return "[framed]";
    }

    internal static string Normalization(MediaProbeResult media, StudioRenderSettings settings)
    {
        EffectiveDisplayGeometry display = EffectiveDisplayGeometryCalculator.Calculate(media.PrimaryVideoStream);
        string normalization = $"scale={display.Width}:{display.Height},setsar=1";
        bool hdr = media.PrimaryVideoStream.ColorTransfer is "smpte2084" or "arib-std-b67";
        if (hdr && settings.ColorOutput == StudioColorOutput.PreserveSdr)
            throw new InvalidOperationException("This recording is HDR. Choose AutomaticSdr color output to tone-map it for the SDR export.");
        if (hdr && settings.ColorOutput == StudioColorOutput.AutomaticSdr)
            normalization += ",zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709," +
                "tonemap=tonemap=mobius:desat=2,zscale=t=bt709:m=bt709:r=tv";
        return normalization;
    }

    private static void AppendGameplay(List<string> graph, string input, string output, EffectiveDisplayGeometry display,
        StudioRenderSettings settings, TimeSpan sourceStart, int width, int height, bool fill, string background)
    {
        StudioSourceCropTrack? track = settings.SourceCropTracks.FirstOrDefault(static item => item.Target == StudioCropTrackingTarget.Gameplay);
        if (track is null)
        {
            graph.Add(input + Crop(display, settings.GameplayRegion) + "," + Fit(width, height, fill, background) + output);
            return;
        }
        graph.Add(input + "split=2[manualgameinput][trackedgameinput]");
        graph.Add("[manualgameinput]" + Crop(display, settings.GameplayRegion) + "," + Fit(width, height, fill, background) + "[manualgame]");
        graph.Add("[trackedgameinput]" + FfmpegTrackedSourceCropGraph.Crop(track, display, sourceStart, manualFallback: false) +
            "," + Fit(width, height, false, background) + "[trackedgame]");
        graph.Add("[manualgame][trackedgame]overlay=shortest=1:enable='" + FfmpegTrackedSourceCropGraph.Enabled(track, sourceStart) + "'" + output);
    }

    private static string Interpolate(IReadOnlyList<StudioFrameKeyframe> frames, TimeSpan sourceStart,
        Func<StudioFrameKeyframe, double> value)
    {
        static string Number(double number) => number.ToString("0.######", CultureInfo.InvariantCulture);
        string expression = Number(value(frames[^1]));
        for (int index = frames.Count - 2; index >= 0; index--)
        {
            StudioFrameKeyframe left = frames[index], right = frames[index + 1];
            double start = (left.SourcePosition - sourceStart).TotalSeconds;
            double end = (right.SourcePosition - sourceStart).TotalSeconds;
            string blend = $"{Number(value(left))}+({Number(value(right) - value(left))})*(in_time-({Number(start)}))/{Number(end - start)}";
            expression = $"if(lt(in_time,{Number(end)}),{blend},{expression})";
        }
        return $"if(lt(in_time,{Number((frames[0].SourcePosition - sourceStart).TotalSeconds)}),{Number(value(frames[0]))},{expression})";
    }

    private static string Crop(EffectiveDisplayGeometry display, NormalizedRectangle region)
    {
        PixelRectangle crop = EffectiveDisplayGeometryCalculator.CalculateCrop(display, region);
        return $"crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y}";
    }

    private static string Fit(int width, int height, bool fill, string background) => fill
        ? $"scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height},setsar=1"
        : $"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color={background},setsar=1";

    internal static string AppendAudio(List<string> graph, MediaProbeResult media, StudioRenderSettings settings, TimeSpan? duration = null)
    {
        var active = media.AudioStreams.Where(stream =>
            settings.AudioTracks.FirstOrDefault(track => track.StreamIndex == stream.Index)?.Muted != true).ToArray();
        if (active.Length == 0)
        {
            graph.Add($"[0:{media.AudioStreams[0].Index}]volume=0[aout]");
            return "[aout]";
        }
        foreach (var stream in active)
        {
            double gain = settings.AudioTracks.FirstOrDefault(track => track.StreamIndex == stream.Index)?.GainDecibels ?? 0;
            graph.Add($"[0:{stream.Index}]volume={gain.ToString("0.###", CultureInfo.InvariantCulture)}dB," +
                $"aformat=sample_rates=48000:channel_layouts=stereo[mix{stream.Index}]");
        }
        string inputs;
        if (settings.DuckGameplay && active.Length > 1 && settings.VoiceAudioStreamIndex is int voice && active.Any(stream => stream.Index == voice))
        {
            graph.Add($"[mix{voice}]asplit=2[voiceaudible][voicekey]");
            string others = string.Concat(active.Where(stream => stream.Index != voice).Select(stream => $"[mix{stream.Index}]"));
            graph.Add(others + $"amix=inputs={active.Length - 1}:normalize=0:duration=longest[gameaudio]");
            graph.Add("[gameaudio][voicekey]sidechaincompress=threshold=0.05:ratio=8:attack=15:release=250[ducked]");
            inputs = "[ducked][voiceaudible]amix=inputs=2:normalize=0:duration=longest";
        }
        else
        {
            inputs = string.Concat(active.Select(stream => $"[mix{stream.Index}]")) +
                $"amix=inputs={active.Length}:normalize=0:duration=longest";
        }
        string trim = duration is { } window && (settings.AudioMastering.NormalizeLoudness || settings.AudioMastering.LimitTruePeak)
            ? ",atrim=duration=" + window.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture) + ",asetpts=PTS-STARTPTS" : "";
        graph.Add(inputs + trim + MasteringFilters(settings.AudioMastering) + "[aout]");
        return "[aout]";
    }

    private static string MasteringFilters(StudioAudioMastering mastering)
    {
        static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
        if (mastering.NormalizeLoudness)
            return $",loudnorm=I={Number(mastering.IntegratedLufs)}:TP={Number(mastering.TruePeakDecibels)}:LRA=11:linear=false,aresample=48000";
        if (mastering.LimitTruePeak)
            return $",aresample=192000,alimiter=limit={Number(Math.Pow(10, mastering.TruePeakDecibels / 20))}:level=disabled:latency=1,aresample=48000";
        return ",alimiter=limit=0.95:level=disabled:latency=1";
    }
}
