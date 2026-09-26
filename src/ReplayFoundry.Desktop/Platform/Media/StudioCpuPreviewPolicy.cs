using ReplayFoundry.Desktop.Features.Studio.Preview;

namespace ReplayFoundry.Desktop.Platform.Media;

/// <summary>Admission bounds and best-effort FFmpeg thread limits, not an OS CPU or RAM ceiling.</summary>
internal static class StudioCpuPreviewPolicy
{
    internal static bool IsEligible(StudioPreviewMediaRequest request)
    {
        var video = request.Asset.SourceMedia.PrimaryVideoStream;
        return request.WorkIntent == StudioPreviewWorkIntent.Foreground &&
            request.Duration <= TimeSpan.FromSeconds(180) &&
            video.Width > 0 && video.Height > 0 &&
            Math.Max(video.Width, video.Height) <= 3840 && Math.Min(video.Width, video.Height) <= 2160 &&
            video.PreferredFrameRate is > 0 and <= 60 && request.Asset.SourceMedia.AudioStreams.Count <= 8 &&
            request.Asset.Appearance.GraphicOverlays.Count == 0;
    }

    internal static IReadOnlyList<string> ConstrainArguments(IReadOnlyList<string> original)
    {
        var arguments = original.ToList();
        int firstInput = arguments.IndexOf("-i");
        int hardwareFlag = arguments.IndexOf("-hw_encoding");
        int codecFlag = arguments.IndexOf("-c:v");
        if (firstInput < 0 || hardwareFlag < 0 || arguments[hardwareFlag + 1] != "0" ||
            codecFlag < 0 || arguments[codecFlag + 1] != "h264_mf")
            throw new InvalidOperationException("The CPU preview lane requires the tested software encoder command.");
        arguments.InsertRange(firstInput, ["-hwaccel", "none", "-threads:v", "2"]);
        arguments.InsertRange(0, ["-filter_threads", "1", "-filter_complex_threads", "1"]);
        arguments.InsertRange(arguments.Count - 1, ["-threads:v", "2"]);
        return arguments;
    }
}
