using System;

namespace ReplayFoundry.Desktop.Media.Inspection;

public sealed class AudioStreamInfo
{
    public AudioStreamInfo(
        int index,
        string codecName,
        string codecLongName,
        string? profile,
        int? sampleRate,
        int? channels,
        string? channelLayout,
        int? bitDepth,
        long? bitRate,
        TimeSpan? duration,
        string? language,
        string? title,
        bool isDefault)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                "A stream index cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(codecName))
        {
            throw new ArgumentException(
                "An audio stream requires a codec name.",
                nameof(codecName));
        }

        if (string.IsNullOrWhiteSpace(codecLongName))
        {
            throw new ArgumentException(
                "An audio stream requires a codec display name.",
                nameof(codecLongName));
        }

        if (sampleRate is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate),
                sampleRate,
                "Sample rate must be positive when supplied.");
        }

        if (channels is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channels),
                channels,
                "Channel count must be positive when supplied.");
        }

        if (bitDepth is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bitDepth),
                bitDepth,
                "Bit depth must be positive when supplied.");
        }

        if (bitRate is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bitRate),
                bitRate,
                "Bitrate must be positive when supplied.");
        }

        if (duration is TimeSpan actualDuration &&
            actualDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                "Stream duration must be positive when supplied.");
        }

        Index = index;
        CodecName = codecName;
        CodecLongName = codecLongName;
        Profile = profile;
        SampleRate = sampleRate;
        Channels = channels;
        ChannelLayout = channelLayout;
        BitDepth = bitDepth;
        BitRate = bitRate;
        Duration = duration;
        Language = language;
        Title = title;
        IsDefault = isDefault;
    }

    public int Index { get; }

    public string CodecName { get; }

    public string CodecLongName { get; }

    public string? Profile { get; }

    public int? SampleRate { get; }

    public int? Channels { get; }

    public string? ChannelLayout { get; }

    public int? BitDepth { get; }

    public long? BitRate { get; }

    public TimeSpan? Duration { get; }

    public string? Language { get; }

    public string? Title { get; }

    public bool IsDefault { get; }
}
