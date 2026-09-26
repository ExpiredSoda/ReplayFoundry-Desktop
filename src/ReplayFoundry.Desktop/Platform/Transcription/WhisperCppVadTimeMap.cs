using System.Globalization;
using System.Text.RegularExpressions;

namespace ReplayFoundry.Desktop.Platform.Transcription;

internal sealed class WhisperCppVadTimeMap
{
    private static readonly Regex SegmentPattern = new(
        @"vad_segment_info:\s*orig_start:\s*(?<originalStart>\d+(?:\.\d+)?)\s*,\s*orig_end:\s*(?<originalEnd>\d+(?:\.\d+)?)\s*,\s*vad_start:\s*(?<processedStart>\d+(?:\.\d+)?)\s*,\s*vad_end:\s*(?<processedEnd>\d+(?:\.\d+)?)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly TimeSpan ProviderInsertedSilence =
        TimeSpan.FromMilliseconds(100);

    private readonly IReadOnlyList<WhisperCppVadInterval> _intervals;
    private readonly IReadOnlyList<WhisperCppVadTimePoint> _points;

    private WhisperCppVadTimeMap(
        IReadOnlyList<WhisperCppVadInterval> intervals,
        TimeSpan samplesOverlap)
    {
        _intervals = intervals;
        _points = BuildPoints(intervals, samplesOverlap);
    }

    public static WhisperCppVadTimeMap? TryParse(
        TimeSpan samplesOverlap,
        params string[] outputs)
    {
        if (samplesOverlap < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(samplesOverlap));
        }

        WhisperCppVadInterval[] intervals = outputs
            .Where(static output => !string.IsNullOrWhiteSpace(output))
            .SelectMany(output => SegmentPattern.Matches(output).Cast<Match>())
            .Select(static match => new WhisperCppVadInterval(
                Seconds(match, "originalStart"),
                Seconds(match, "originalEnd"),
                Seconds(match, "processedStart"),
                Seconds(match, "processedEnd")))
            .Distinct()
            .OrderBy(static interval => interval.ProcessedStart)
            .ToArray();
        if (intervals.Length == 0 || !AreValid(intervals))
        {
            return null;
        }

        return new WhisperCppVadTimeMap(
            Array.AsReadOnly(intervals),
            samplesOverlap);
    }

    public TimeSpan Map(TimeSpan processed)
    {
        if (processed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(processed));
        }

        WhisperCppVadTimePoint first = _points[0];
        if (processed <= first.Processed)
        {
            return first.Original + (processed - first.Processed);
        }

        for (int index = 1; index < _points.Count; index++)
        {
            WhisperCppVadTimePoint right = _points[index];
            if (processed > right.Processed)
            {
                continue;
            }

            WhisperCppVadTimePoint left = _points[index - 1];
            if (right.Processed == left.Processed)
            {
                return right.Original;
            }
            double progress =
                (processed - left.Processed).Ticks /
                (double)(right.Processed - left.Processed).Ticks;
            return left.Original + TimeSpan.FromTicks(
                checked((long)Math.Round(
                    (right.Original - left.Original).Ticks * progress,
                    MidpointRounding.AwayFromZero)));
        }

        WhisperCppVadTimePoint last = _points[^1];
        return last.Original + (processed - last.Processed);
    }

    public TimeSpan MapWordStart(TimeSpan processed)
    {
        if (processed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(processed));
        }

        for (int index = 0; index + 1 < _intervals.Count; index++)
        {
            WhisperCppVadInterval current = _intervals[index];
            WhisperCppVadInterval next = _intervals[index + 1];
            if (processed > current.ProcessedEnd &&
                processed < next.ProcessedStart)
            {
                return next.OriginalStart;
            }
        }
        return Map(processed);
    }

    public TimeSpan MapWordEnd(
        TimeSpan processedStart,
        TimeSpan processedEnd)
    {
        if (processedStart < TimeSpan.Zero ||
            processedEnd < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(processedStart));
        }

        foreach (WhisperCppVadInterval interval in _intervals)
        {
            if (processedStart < interval.ProcessedStart ||
                processedStart > interval.ProcessedEnd)
            {
                continue;
            }

            // whisper.cpp tokens can span the short silence inserted between
            // compressed VAD pieces. Mapping such an end through the next
            // piece would paint one word across a much longer real pause.
            // Keep the word on the speech piece where its token began.
            return processedEnd > interval.ProcessedEnd
                ? interval.OriginalEnd
                : Map(processedEnd);
        }

        return Map(processedEnd);
    }

    public TimeSpan? FindSpeechEnd(TimeSpan originalPosition)
    {
        foreach (WhisperCppVadInterval interval in _intervals)
        {
            if (interval.OriginalStart <= originalPosition &&
                interval.OriginalEnd >= originalPosition)
            {
                return interval.OriginalEnd;
            }
        }
        return null;
    }

    private static IReadOnlyList<WhisperCppVadTimePoint> BuildPoints(
        IReadOnlyList<WhisperCppVadInterval> intervals,
        TimeSpan samplesOverlap)
    {
        var points = new List<WhisperCppVadTimePoint>(intervals.Count * 4);
        for (int index = 0; index < intervals.Count; index++)
        {
            WhisperCppVadInterval interval = intervals[index];
            points.Add(new(
                interval.ProcessedStart,
                interval.OriginalStart));
            points.Add(new(
                interval.ProcessedEnd,
                interval.OriginalEnd));
            if (index + 1 >= intervals.Count)
            {
                continue;
            }

            WhisperCppVadInterval next = intervals[index + 1];
            TimeSpan insertedSilenceStart =
                next.ProcessedStart - ProviderInsertedSilence;
            TimeSpan overlapEnd = interval.ProcessedEnd + samplesOverlap;
            TimeSpan heldBoundary = insertedSilenceStart >= overlapEnd
                ? insertedSilenceStart
                : overlapEnd;
            if (heldBoundary > interval.ProcessedEnd &&
                heldBoundary < next.ProcessedStart)
            {
                points.Add(new(heldBoundary, interval.OriginalEnd));
            }
        }

        WhisperCppVadTimePoint[] snapshot = points
            .OrderBy(static point => point.Processed)
            .GroupBy(static point => point.Processed)
            .Select(static group => group.Last())
            .ToArray();
        return Array.AsReadOnly(snapshot);
    }

    private static bool AreValid(
        IReadOnlyList<WhisperCppVadInterval> intervals)
    {
        for (int index = 0; index < intervals.Count; index++)
        {
            WhisperCppVadInterval current = intervals[index];
            if (current.OriginalStart < TimeSpan.Zero ||
                current.OriginalEnd <= current.OriginalStart ||
                current.ProcessedStart < TimeSpan.Zero ||
                current.ProcessedEnd <= current.ProcessedStart)
            {
                return false;
            }
            if (index > 0 &&
                (current.OriginalStart < intervals[index - 1].OriginalEnd ||
                 current.ProcessedStart < intervals[index - 1].ProcessedEnd))
            {
                return false;
            }
        }
        return true;
    }

    private static TimeSpan Seconds(Match match, string groupName) =>
        TimeSpan.FromSeconds(double.Parse(
            match.Groups[groupName].Value,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture));

    private sealed record WhisperCppVadInterval(
        TimeSpan OriginalStart,
        TimeSpan OriginalEnd,
        TimeSpan ProcessedStart,
        TimeSpan ProcessedEnd);

    private readonly record struct WhisperCppVadTimePoint(
        TimeSpan Processed,
        TimeSpan Original);
}
