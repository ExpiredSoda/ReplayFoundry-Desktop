using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

// ASR punctuation is bounded editing context, not a speaker-turn annotation.
internal static class GenerationTranscriptSentenceBeginningPolicy
{
    internal static readonly TimeSpan MaximumPause = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumLookback = TimeSpan.FromSeconds(30);

    public static TranscriptSentenceBeginning? Find(
        GenerationSourceTranscript transcript, TimeSpan cutStart, TimeSpan cutEnd)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        TimedText[] units = transcript.Segments
            .Where(segment => segment.AbsoluteSourceEnd >= cutStart - MaximumLookback &&
                segment.AbsoluteSourceStart <= cutStart + MaximumPause)
            .OrderBy(static segment => segment.AbsoluteSourceStart)
            .SelectMany(CreateUnits).ToArray();
        int index = Array.FindIndex(units, unit => unit.End > cutStart);
        if (index < 0 || units[index].Start >= cutEnd ||
            units[index].Start > cutStart + MaximumPause)
        {
            return null;
        }

        int last = index;
        for (int count = 0; count < 256; count++)
        {
            TimedText current = units[index];
            if (!current.Reliable || cutStart - current.Start > MaximumLookback)
            {
                return null;
            }
            if (index == 0)
            {
                // The first retained chunk is not necessarily the source beginning.
                return current.Start <= MaximumPause &&
                    Covers(transcript, TimeSpan.Zero, current.End)
                    ? Result() : null;
            }

            TimedText previous = units[index - 1];
            if (!previous.Reliable || previous.End > current.Start ||
                current.Start - previous.End > MaximumPause ||
                !Covers(transcript, previous.Start, current.End))
            {
                return null;
            }
            // Decoders may add punctuation at their input edge. A chunk edge
            // cannot by itself turn a continuing sentence into a new sentence.
            bool inputEdge = IsInputEdge(transcript, previous, current);
            if (HasSentenceEnding(previous.Text) && !inputEdge)
            {
                return Result();
            }
            index--;
        }
        return null;

        TranscriptSentenceBeginning? Result() => units[index].Start < cutStart
            ? new(units[index].Start, units.Skip(index).Take(last - index + 1)
                .Select(static unit => $"transcript:sentence-start:{unit.Segment.NeighborhoodId}:{unit.Segment.Id}:{unit.Start:c}-{unit.End:c}")
                .Distinct(StringComparer.Ordinal).ToArray())
            : null;
    }

    internal static IEnumerable<TimedText> CreateUnits(AudioTranscriptionSegment segment)
    {
        bool reliable = !segment.Warnings.Any(static warning => warning.Code is
            AudioTranscriptionWarningCode.BoundaryClamped or
            AudioTranscriptionWarningCode.SegmentTimingCanonicalized or
            AudioTranscriptionWarningCode.ProviderReportedWarning);
        bool measuredWords = segment.Words.Count > 0 &&
            !segment.Warnings.Any(static warning => warning.Code is
                AudioTranscriptionWarningCode.WordTimingCanonicalized or
                AudioTranscriptionWarningCode.WordTimestampsUnavailable) &&
            Compact(string.Concat(segment.Words.Select(static word => word.Text))) == Compact(segment.Text);
        if (measuredWords)
        {
            foreach (AudioTranscriptionWord word in segment.Words)
            {
                yield return new(word.Text, word.AbsoluteSourceStart,
                    word.AbsoluteSourceEnd, segment, reliable && word.AbsoluteSourceEnd > word.AbsoluteSourceStart);
            }
        }
        else
        {
            // Without exact word timing, include the complete timed phrase;
            // never interpolate a sentence boundary inside it.
            yield return new(segment.Text, segment.AbsoluteSourceStart,
                segment.AbsoluteSourceEnd, segment, reliable && segment.AbsoluteSourceEnd > segment.AbsoluteSourceStart);
        }
    }

    private static string Compact(string text) => new(text.Where(static character =>
        !char.IsWhiteSpace(character)).ToArray());

    internal static bool Covers(GenerationSourceTranscript transcript, TimeSpan start, TimeSpan end)
    {
        TimeSpan covered = start;
        foreach (AudioTranscriptionManifest manifest in transcript.Manifests
                     .Where(manifest => manifest.AbsoluteAudioStreamIndex == transcript.AudioStreamIndex &&
                         !manifest.Execution.WasCancelled)
                     .OrderBy(static manifest => manifest.AbsoluteSourceOffset))
        {
            if (manifest.AbsoluteSourceOffset > covered) break;
            TimeSpan through = manifest.AbsoluteSourceOffset + manifest.InputDuration;
            if (through > covered) covered = through;
            if (covered >= end) return true;
        }
        return false;
    }

    internal static bool IsInputEdge(GenerationSourceTranscript transcript, TimedText current, TimedText? next) =>
        (next is null || current.Segment.NeighborhoodId != next.Segment.NeighborhoodId) &&
        transcript.Manifests.Any(manifest =>
            manifest.AbsoluteAudioStreamIndex == transcript.AudioStreamIndex &&
            manifest.AbsoluteSourceOffset + manifest.InputDuration >= current.End &&
            manifest.AbsoluteSourceOffset + manifest.InputDuration <= (next?.Start ?? manifest.SourceDuration) &&
            manifest.AbsoluteSourceOffset + manifest.InputDuration - current.End <= TimeSpan.FromSeconds(1) &&
            (next is not null || manifest.AbsoluteSourceOffset + manifest.InputDuration < manifest.SourceDuration));

    internal static bool HasSentenceEnding(string text)
    {
        string trimmed = text.TrimEnd().TrimEnd('"', '\'', '”', '’', ')', ']', '}', '»');
        if (trimmed.EndsWith("...", StringComparison.Ordinal) || trimmed.EndsWith('…')) return false;
        if (trimmed.EndsWith('!') || trimmed.EndsWith('?') || trimmed.EndsWith('。') ||
            trimmed.EndsWith('！') || trimmed.EndsWith('？')) return true;
        if (!trimmed.EndsWith('.')) return false;
        string last = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[^1];
        return last.Length > 2 && !last.AsSpan(0, last.Length - 1).Contains('.') &&
            !last.Equals("Mr.", StringComparison.OrdinalIgnoreCase) &&
            !last.Equals("Mrs.", StringComparison.OrdinalIgnoreCase) &&
            !last.Equals("Ms.", StringComparison.OrdinalIgnoreCase) &&
            !last.Equals("Dr.", StringComparison.OrdinalIgnoreCase) &&
            !last.Equals("Prof.", StringComparison.OrdinalIgnoreCase) &&
            !last.Equals("St.", StringComparison.OrdinalIgnoreCase) &&
            !last.Equals("Jr.", StringComparison.OrdinalIgnoreCase) &&
            !last.Equals("Sr.", StringComparison.OrdinalIgnoreCase) &&
            !last.Equals("vs.", StringComparison.OrdinalIgnoreCase) &&
            !last.Equals("etc.", StringComparison.OrdinalIgnoreCase) &&
            !last.Equals("e.g.", StringComparison.OrdinalIgnoreCase) &&
            !last.Equals("i.e.", StringComparison.OrdinalIgnoreCase);
    }

    internal sealed record TimedText(string Text, TimeSpan Start, TimeSpan End,
        AudioTranscriptionSegment Segment, bool Reliable);
}

internal sealed record TranscriptSentenceBeginning(TimeSpan Start, IReadOnlyList<string> EvidenceReferences);
