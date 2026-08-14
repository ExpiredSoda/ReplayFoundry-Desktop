using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Inspection;

public sealed class MediaContainerInfo
{
    private readonly ReadOnlyDictionary<string, string> _tags;

    public MediaContainerInfo(
        string formatName,
        string formatLongName,
        TimeSpan duration,
        TimeSpan? startTime,
        long? sizeBytes,
        long? bitRate,
        int? probeScore,
        IReadOnlyDictionary<string, string>? tags)
    {
        if (string.IsNullOrWhiteSpace(formatName))
        {
            throw new ArgumentException(
                "A media container requires a format name.",
                nameof(formatName));
        }

        if (string.IsNullOrWhiteSpace(formatLongName))
        {
            throw new ArgumentException(
                "A media container requires a display name.",
                nameof(formatLongName));
        }

        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                "Media duration must be greater than zero.");
        }

        if (sizeBytes is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sizeBytes),
                sizeBytes,
                "Media size must be greater than zero when supplied.");
        }

        if (bitRate is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bitRate),
                bitRate,
                "Media bitrate must be greater than zero when supplied.");
        }

        if (probeScore is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(probeScore),
                probeScore,
                "Probe score must be between 0 and 100 when supplied.");
        }

        var tagCopy = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        if (tags is not null)
        {
            foreach ((string key, string value) in tags)
            {
                if (string.IsNullOrWhiteSpace(key) ||
                    string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                tagCopy[key] = value;
            }
        }

        FormatName = formatName;
        FormatLongName = formatLongName;
        Duration = duration;
        StartTime = startTime;
        SizeBytes = sizeBytes;
        BitRate = bitRate;
        ProbeScore = probeScore;
        _tags = new ReadOnlyDictionary<string, string>(tagCopy);
    }

    public string FormatName { get; }

    public string FormatLongName { get; }

    public TimeSpan Duration { get; }

    public TimeSpan? StartTime { get; }

    public long? SizeBytes { get; }

    public long? BitRate { get; }

    public int? ProbeScore { get; }

    public IReadOnlyDictionary<string, string> Tags =>
        _tags;
}
