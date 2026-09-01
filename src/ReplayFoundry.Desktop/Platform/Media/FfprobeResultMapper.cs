using System.IO;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfprobeResultMapper
{
    public static MediaProbeResult Map(
        string fullPath,
        FfprobeDocument document,
        MediaInspectionManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(manifest);

        FfprobeFormat format = document.Format ??
            throw new MediaProbeException(
                $"ffprobe did not return container information for " +
                $"'{Path.GetFileName(fullPath)}'.");
        List<FfprobeStream> streams = document.Streams ?? [];
        var warnings = new List<MediaInspectionWarning>();

        List<VideoStreamInfo> videoStreams = streams
            .Where(static stream => IsStreamType(stream, "video"))
            .Select(stream => FfprobeVideoStreamMapper.Map(stream, warnings))
            .ToList();
        if (videoStreams.Count == 0)
        {
            throw new MediaProbeException(
                $"'{Path.GetFileName(fullPath)}' does not contain a " +
                "usable video stream.");
        }

        List<AudioStreamInfo> audioStreams = streams
            .Where(static stream => IsStreamType(stream, "audio"))
            .Select(stream => FfprobeAudioStreamMapper.Map(stream, warnings))
            .ToList();
        TimeSpan duration = ResolveDuration(format, videoStreams, fullPath);
        var container = new MediaContainerInfo(
            format.FormatName ?? "unknown",
            format.FormatLongName ?? format.FormatName ?? "Unknown media container",
            duration,
            FfprobeValueParser.ParseSeconds(format.StartTime),
            FfprobeValueParser.ParseInt64(format.Size),
            FfprobeValueParser.ParseInt64(format.BitRate),
            format.ProbeScore,
            format.Tags);

        return new MediaProbeResult(
            fullPath,
            container,
            videoStreams,
            audioStreams,
            manifest,
            warnings);
    }

    private static bool IsStreamType(FfprobeStream stream, string expected) =>
        string.Equals(
            stream.CodecType,
            expected,
            StringComparison.OrdinalIgnoreCase);

    private static TimeSpan ResolveDuration(
        FfprobeFormat format,
        IEnumerable<VideoStreamInfo> videoStreams,
        string fullPath)
    {
        TimeSpan duration =
            FfprobeValueParser.ParseSeconds(format.Duration) ??
            videoStreams
                .Select(static stream => stream.Duration)
                .Where(static value => value is not null)
                .Select(static value => value!.Value)
                .DefaultIfEmpty()
                .Max();
        if (duration <= TimeSpan.Zero)
        {
            throw new MediaProbeException(
                $"Replay Foundry could not determine the duration of " +
                $"'{Path.GetFileName(fullPath)}'.");
        }

        return duration;
    }
}
