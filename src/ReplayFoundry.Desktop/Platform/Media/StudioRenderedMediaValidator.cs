using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Media.Geometry;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.Media;

internal interface IStudioRenderedMediaValidator
{
    Task ValidateAsync(string path, TimeSpan expectedDuration, GenerationClipOutputProfile profile,
        bool requiresBt709ToneMap, CancellationToken cancellationToken);
}

/// <summary>A completed encode failed inspection; process failures and cancellation are different failures.</summary>
internal sealed class StudioRenderedMediaValidationException(string message) : InvalidOperationException(message) { }

internal sealed class StudioRenderedMediaValidator : IStudioRenderedMediaValidator
{
    private readonly IProcessRunner _runner;
    private readonly IFfmpegToolLocator _tools;
    private readonly IMediaProbe _probe;
    internal StudioRenderedMediaValidator(IProcessRunner runner, IFfmpegToolLocator tools)
    { _runner = runner; _tools = tools; _probe = new FfprobeMediaProbe(runner, tools); }
    public async Task ValidateAsync(string path, TimeSpan expectedDuration, GenerationClipOutputProfile profile,
        bool requiresBt709ToneMap, CancellationToken cancellationToken)
    {
        MediaProbeResult media = await _probe.ProbeAsync(path, cancellationToken);
        ValidateProbe(media, expectedDuration, profile, requiresBt709ToneMap);
        ProcessRunResult decode = await _runner.RunAsync(new ProcessRunRequest(_tools.LocateFfmpeg(),
            ["-hide_banner", "-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:v:0", "-map", "0:a:0", "-f", "null", "-"],
            TimeSpan.FromSeconds(Math.Clamp(expectedDuration.TotalSeconds * 3, 60, 1800))), cancellationToken);
        if (!decode.Succeeded)
            throw new StudioRenderedMediaValidationException("Rendered media failed the final decode check: " + decode.StandardError);
    }

    internal static void ValidateProbe(MediaProbeResult media, TimeSpan expectedDuration,
        GenerationClipOutputProfile profile, bool requiresBt709ToneMap)
    {
        EffectiveDisplayGeometry geometry = EffectiveDisplayGeometryCalculator.Calculate(media.PrimaryVideoStream);
        if (geometry.Width != profile.Width || geometry.Height != profile.Height ||
            Math.Abs((media.Duration - expectedDuration).TotalSeconds) > .3 || media.AudioStreams.Count != 1)
            throw new StudioRenderedMediaValidationException("Rendered media does not match the requested frame, duration, or audio mix.");
        VideoStreamInfo video = media.PrimaryVideoStream;
        AudioStreamInfo audio = media.AudioStreams[0];
        if (video.Duration is TimeSpan videoDuration && Math.Abs((videoDuration - expectedDuration).TotalSeconds) > .3 ||
            audio.Duration is TimeSpan audioDuration && Math.Abs((audioDuration - expectedDuration).TotalSeconds) > .3)
            throw new StudioRenderedMediaValidationException("Rendered picture and sound must each cover the requested duration.");
        if (video.CodecName != "h264" || video.PixelFormat != "yuv420p" ||
            audio.CodecName != "aac" || audio.SampleRate != 48000 || audio.Channels != 2 ||
            video.PreferredFrameRate is not double cadence || Math.Abs(cadence - profile.FramesPerSecond) > .01 ||
            video.ColorTransfer is "smpte2084" or "arib-std-b67")
            throw new StudioRenderedMediaValidationException("Rendered media does not match the requested H.264/AAC, stereo, frame-rate, or SDR format.");
        // Only the explicit HDR conversion promises BT.709. Existing SDR may correctly use another colorimetry.
        if (requiresBt709ToneMap && (video.ColorPrimaries != "bt709" || video.ColorTransfer != "bt709" ||
            video.ColorMatrix != "bt709" || video.ColorRange != "tv"))
            throw new StudioRenderedMediaValidationException("The HDR conversion did not produce the requested limited-range BT.709 color tags.");
    }
}
