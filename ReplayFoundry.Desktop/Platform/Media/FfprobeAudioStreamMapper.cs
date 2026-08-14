using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfprobeAudioStreamMapper
{
    public static AudioStreamInfo Map(
        FfprobeStream stream,
        ICollection<MediaInspectionWarning> warnings)
    {
        string? channelLayout =
            FfprobeValueParser.NormalizeOptional(stream.ChannelLayout);
        if (channelLayout is null && stream.Channels is > 0)
        {
            warnings.Add(
                new MediaInspectionWarning(
                    MediaInspectionWarningCode.AudioChannelLayoutNotReported,
                    $"Audio stream {stream.Index} reports " +
                    $"{stream.Channels} channels but no channel layout. " +
                    "Replay Foundry will keep the layout unknown rather than " +
                    "guessing from channel count or track title.",
                    stream.Index));
        }

        return new AudioStreamInfo(
            stream.Index,
            stream.CodecName ?? "unknown",
            stream.CodecLongName ?? stream.CodecName ?? "Unknown audio codec",
            FfprobeValueParser.NormalizeOptional(stream.Profile),
            FfprobeValueParser.ParseInt32(stream.SampleRate),
            stream.Channels,
            channelLayout,
            stream.BitsPerSample is > 0
                ? stream.BitsPerSample
                : FfprobeValueParser.ParseInt32(stream.BitsPerRawSample),
            FfprobeValueParser.ParseInt64(stream.BitRate),
            FfprobeValueParser.ParseSeconds(stream.Duration),
            FfprobeValueParser.GetTag(stream.Tags, "language"),
            FfprobeValueParser.GetTag(stream.Tags, "title"),
            stream.Disposition?.Default == 1);
    }
}
