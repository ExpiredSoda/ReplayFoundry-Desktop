using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfmpegMetadataParser
{
    private static readonly Regex PtsTimeRegex =
        new(
            @"(?:^|\s)pts_time:(?<value>-?\d+(?:\.\d+)?)",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    public static IReadOnlyList<FfmpegMetadataRecord> Parse(
        string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var records =
            new List<FfmpegMetadataRecord>();

        TimeSpan? timestamp = null;
        Dictionary<string, string>? tags = null;

        foreach (string rawLine in output.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            string line = rawLine.Trim();

            if (line.StartsWith(
                    "frame:",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddRecordIfNeeded(
                    records,
                    timestamp,
                    tags);

                timestamp =
                    ParseFrameTimestamp(line);

                tags =
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);

                continue;
            }

            int separatorIndex =
                line.IndexOf('=');

            if (separatorIndex <= 0)
            {
                continue;
            }

            tags ??=
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            string key =
                line[..separatorIndex].Trim();

            string value =
                line[(separatorIndex + 1)..].Trim();

            if (key.Length == 0 ||
                value.Length == 0)
            {
                continue;
            }

            tags[key] = value;
        }

        AddRecordIfNeeded(
            records,
            timestamp,
            tags);

        return records;
    }

    private static TimeSpan? ParseFrameTimestamp(
        string line)
    {
        Match match =
            PtsTimeRegex.Match(line);

        if (!match.Success ||
            !double.TryParse(
                match.Groups["value"].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double seconds) ||
            !double.IsFinite(seconds) ||
            seconds < 0 ||
            seconds > TimeSpan.MaxValue.TotalSeconds)
        {
            return null;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static void AddRecordIfNeeded(
        ICollection<FfmpegMetadataRecord> records,
        TimeSpan? timestamp,
        IReadOnlyDictionary<string, string>? tags)
    {
        if (tags is null ||
            tags.Count == 0)
        {
            return;
        }

        records.Add(
            new FfmpegMetadataRecord(
                timestamp,
                tags));
    }
}
