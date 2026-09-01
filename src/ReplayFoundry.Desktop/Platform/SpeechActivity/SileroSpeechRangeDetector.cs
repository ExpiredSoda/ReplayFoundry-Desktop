using ReplayFoundry.Desktop.Media.Intelligence.SpeechActivity;

namespace ReplayFoundry.Desktop.Platform.SpeechActivity;

internal sealed record SpeechSampleRange(long Start, long End);

internal static class SileroSpeechRangeDetector
{
    public static IReadOnlyList<SpeechSampleRange> Detect(
        IReadOnlyList<float> probabilities,
        long audioLengthSamples,
        SpeechActivityOptions options,
        ICollection<SpeechActivityWarning> warnings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probabilities);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(warnings);
        if (audioLengthSamples <= 0 ||
            probabilities.Count == 0 ||
            probabilities.Any(static value => !float.IsFinite(value) || value is < 0 or > 1))
        {
            throw new ArgumentException(
                "Speech probability coverage must be finite and non-empty.",
                nameof(probabilities));
        }

        long minimumSpeechSamples = ToSamples(options.MinimumSpeechDuration);
        long minimumSilenceSamples = ToSamples(options.MinimumSilenceDuration);
        long paddingSamples = ToSamples(options.SpeechPadding);
        long maximumSpeechSamples =
            ToSamples(options.MaximumSpeechDuration) -
            SileroOnnxModelSession.WindowSamples -
            2 * paddingSamples;
        long minimumSplitSilenceSamples =
            ToSamples(TimeSpan.FromMilliseconds(98));
        bool triggered = false;
        long speechStart = 0;
        long temporaryEnd = 0;
        var possibleEnds = new List<(long Start, long Duration)>();
        var raw = new List<SpeechSampleRange>();

        for (int index = 0; index < probabilities.Count; index++)
        {
            if ((index & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            float probability = probabilities[index];
            long currentSample =
                (long)SileroOnnxModelSession.WindowSamples * index;
            if (probability >= options.SpeechThreshold && temporaryEnd > 0)
            {
                long silenceDuration = currentSample - temporaryEnd;
                if (silenceDuration > minimumSplitSilenceSamples)
                {
                    possibleEnds.Add((temporaryEnd, silenceDuration));
                }

                temporaryEnd = 0;
            }

            if (probability >= options.SpeechThreshold && !triggered)
            {
                triggered = true;
                speechStart = currentSample;
                continue;
            }

            if (triggered && currentSample - speechStart > maximumSpeechSamples)
            {
                (long end, long nextStart) = FindSplit(
                    possibleEnds,
                    currentSample);
                AddIfLongEnough(
                    raw,
                    speechStart,
                    end,
                    minimumSpeechSamples);
                warnings.Add(
                    new SpeechActivityWarning(
                        SpeechActivityWarningCode.MaximumSpeechDurationSplit,
                        "A sustained speech interval reached the configured maximum and was split at the strongest available silence boundary."));
                triggered = probability >= options.SpeechThreshold;
                speechStart = nextStart;
                temporaryEnd = 0;
                possibleEnds.Clear();
                continue;
            }

            if (probability < options.SilenceThreshold && triggered)
            {
                temporaryEnd = temporaryEnd == 0
                    ? currentSample
                    : temporaryEnd;
                if (currentSample - temporaryEnd >= minimumSilenceSamples)
                {
                    AddIfLongEnough(
                        raw,
                        speechStart,
                        temporaryEnd,
                        minimumSpeechSamples);
                    triggered = false;
                    temporaryEnd = 0;
                    possibleEnds.Clear();
                }
            }
        }

        if (triggered)
        {
            AddIfLongEnough(
                raw,
                speechStart,
                audioLengthSamples,
                minimumSpeechSamples);
        }

        return PadRanges(
            raw,
            audioLengthSamples,
            paddingSamples);
    }

    public static SpeechActivityInterval CreateInterval(
        SpeechSampleRange range,
        IReadOnlyList<float> probabilities,
        SpeechActivityRequest request,
        long totalSamples)
    {
        TimeSpan relativeStart = FromSamples(range.Start);
        TimeSpan relativeEnd = FromSamples(range.End);
        if (relativeEnd > request.InputDuration || range.End == totalSamples)
        {
            relativeEnd = request.InputDuration;
        }

        int first = Math.Clamp(
            (int)(range.Start / SileroOnnxModelSession.WindowSamples),
            0,
            probabilities.Count - 1);
        int lastExclusive = Math.Clamp(
            (int)((range.End + SileroOnnxModelSession.WindowSamples - 1) /
                SileroOnnxModelSession.WindowSamples),
            first + 1,
            probabilities.Count);
        double peak = 0;
        double sum = 0;
        for (int index = first; index < lastExclusive; index++)
        {
            peak = Math.Max(peak, probabilities[index]);
            sum += probabilities[index];
        }

        return new SpeechActivityInterval(
            relativeStart,
            relativeEnd,
            request.AbsoluteSourceOffset + relativeStart,
            request.AbsoluteSourceOffset + relativeEnd,
            peak,
            sum / (lastExclusive - first));
    }

    private static (long End, long NextStart) FindSplit(
        IReadOnlyList<(long Start, long Duration)> possibleEnds,
        long currentSample)
    {
        if (possibleEnds.Count == 0)
        {
            return (currentSample, currentSample);
        }

        (long Start, long Duration) best = possibleEnds
            .OrderByDescending(static item => item.Duration)
            .ThenBy(static item => item.Start)
            .First();
        return (best.Start, best.Start + best.Duration);
    }

    private static void AddIfLongEnough(
        ICollection<SpeechSampleRange> ranges,
        long start,
        long end,
        long minimumSpeechSamples)
    {
        if (end - start > minimumSpeechSamples)
        {
            ranges.Add(new SpeechSampleRange(start, end));
        }
    }

    private static IReadOnlyList<SpeechSampleRange> PadRanges(
        IReadOnlyList<SpeechSampleRange> raw,
        long audioLengthSamples,
        long paddingSamples)
    {
        SpeechSampleRange[] adjusted = raw.ToArray();
        for (int index = 0; index < adjusted.Length; index++)
        {
            long start = adjusted[index].Start;
            long end = adjusted[index].End;
            if (index == 0)
            {
                start = Math.Max(0, start - paddingSamples);
            }

            if (index < adjusted.Length - 1)
            {
                long gap = adjusted[index + 1].Start - end;
                if (gap < 2 * paddingSamples)
                {
                    end += gap / 2;
                    adjusted[index + 1] = new SpeechSampleRange(
                        Math.Max(0, adjusted[index + 1].Start - gap / 2),
                        adjusted[index + 1].End);
                }
                else
                {
                    end = Math.Min(audioLengthSamples, end + paddingSamples);
                    adjusted[index + 1] = new SpeechSampleRange(
                        Math.Max(0, adjusted[index + 1].Start - paddingSamples),
                        adjusted[index + 1].End);
                }
            }
            else
            {
                end = Math.Min(audioLengthSamples, end + paddingSamples);
            }

            adjusted[index] = new SpeechSampleRange(start, end);
        }

        return Array.AsReadOnly(
            adjusted.Where(static range => range.End > range.Start).ToArray());
    }

    private static long ToSamples(TimeSpan duration) =>
        checked((long)Math.Round(
            duration.TotalSeconds * SileroOnnxModelSession.SampleRate,
            MidpointRounding.AwayFromZero));

    private static TimeSpan FromSamples(long samples) =>
        TimeSpan.FromSeconds(
            samples / (double)SileroOnnxModelSession.SampleRate);
}
