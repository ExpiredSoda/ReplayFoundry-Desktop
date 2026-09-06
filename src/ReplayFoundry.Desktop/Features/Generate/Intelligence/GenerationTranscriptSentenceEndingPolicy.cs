using ReplayFoundry.Desktop.Media.Intelligence.SpeechActivity;
using ReplayFoundry.Desktop.Media.Moments;
using static ReplayFoundry.Desktop.Features.Generate.Intelligence.GenerationTranscriptSentenceBeginningPolicy;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal static class GenerationTranscriptSentenceEndingPolicy
{
    private static readonly TimeSpan MaximumSearch = TimeSpan.FromSeconds(30);

    // Null means the available transcript cannot establish a trustworthy boundary;
    // the caller retains its existing VAD policy. Text is never interpolated.
    public static GenerationCandidateNaturalEndingAdjustment? Adjust(
        MomentCandidate candidate, IReadOnlyList<SpeechActivityInterval> speech,
        TimeSpan minimumDuration, TimeSpan maximumDuration, GenerationSourceTranscript transcript)
    {
        TimeSpan cutEnd = candidate.Window.End;
        TimedText[] units = transcript.Segments
            .Where(segment => segment.AbsoluteSourceEnd >= cutEnd - MaximumSearch &&
                segment.AbsoluteSourceStart <= cutEnd + MaximumSearch)
            .OrderBy(static segment => segment.AbsoluteSourceStart)
            .SelectMany(CreateUnits).ToArray();
        int last = Array.FindLastIndex(units, unit => unit.Start < cutEnd);
        if (last < 0 || units[last].End <= candidate.Window.Start ||
            cutEnd - units[last].End > MaximumPause || units[last].End - cutEnd > MaximumSearch ||
            !Reliable(last)) return null;

        int forward = last;
        for (int count = 0; count < 256; count++)
        {
            if (IsEnding(forward)) break;
            if (forward + 1 == units.Length || units[forward + 1].End - cutEnd > MaximumSearch)
            {
                // A final phrase without a terminal mark alone is insufficient
                // evidence of a truncated sentence. A known continuation is.
                if (forward == last) return null;
                forward = -1;
                break;
            }
            if (!Connected(forward, forward + 1)) return null;
            forward++;
            if (count == 255) return null;
        }

        if (forward >= 0 && TryBoundary(forward, last, out GenerationCandidateNaturalEndingAdjustment? extended))
            return extended;

        // Extending must not discard a complete beginning. If it does not fit,
        // a previous true sentence end is permissible only without losing events.
        for (int previous = last - 1, count = 0; previous >= 0 && count < 256; previous--, count++)
        {
            // A trustworthy ending was found (or its continuation observed)
            // but could not be retained safely. A gap farther back cannot waive
            // that conflict by sending the edit through VAD's start-shifting path.
            if (!Connected(previous, previous + 1)) break;
            if (cutEnd - units[previous].End > MaximumSearch) break;
            if (IsEnding(previous) && TryBoundary(previous, last, out GenerationCandidateNaturalEndingAdjustment? shortened))
                return shortened;
        }
        return new(candidate, GenerationCandidateNaturalEndingStatus.Unsafe, Evidence(last, Math.Max(last, forward)));

        bool Reliable(int index) => units[index].Reliable &&
            Covers(transcript, units[index].Start, units[index].End);

        bool Connected(int left, int right) => Reliable(left) && Reliable(right) &&
            units[left].End <= units[right].Start && units[right].Start - units[left].End <= MaximumPause &&
            Covers(transcript, units[left].Start, units[right].End);

        bool IsEnding(int index) => HasSentenceEnding(units[index].Text) &&
            !IsInputEdge(transcript, units[index], index + 1 < units.Length ? units[index + 1] : null);

        bool TryBoundary(int index, int evidenceThrough, out GenerationCandidateNaturalEndingAdjustment? adjustment)
        {
            adjustment = null;
            TimeSpan boundary = units[index].End;
            TimeSpan next = index + 1 < units.Length ? units[index + 1].Start : candidate.Window.SourceDuration;
            // Even an uncertain following phrase has an observed onset. Never
            // use punctuation to pad into it or through a new VAD utterance.
            foreach (SpeechActivityInterval interval in speech)
            {
                if (interval.AbsoluteStart >= boundary && interval.AbsoluteStart < next) next = interval.AbsoluteStart;
            }
            if (next < boundary) return false;
            TimeSpan targetEnd = boundary + GenerationCandidateNaturalEndingPolicy.NaturalTail;
            if (index == last && boundary <= cutEnd) targetEnd = cutEnd;
            if (targetEnd > next) targetEnd = next;
            if (targetEnd > candidate.Window.SourceDuration) targetEnd = candidate.Window.SourceDuration;
            // A measured VAD interval crossing the proposed boundary makes that
            // boundary unsuitable; do not invent a shorter silence inside it.
            if (speech.Any(interval => interval.AbsoluteStart < targetEnd && interval.AbsoluteEnd > targetEnd)) return false;
            TimeSpan duration = targetEnd - candidate.Window.Start;
            if (duration < minimumDuration || duration > maximumDuration || targetEnd <= candidate.Window.Start) return false;
            if (candidate.Anchors.Any(anchor => anchor.Timestamp >= targetEnd) ||
                candidate.Episode is { } episode &&
                    (episode.End < cutEnd ? episode.End : cutEnd) > targetEnd) return false;
            if (targetEnd == cutEnd)
            {
                adjustment = GenerationCandidateNaturalEndingAdjustment.Unchanged(candidate);
                return true;
            }
            adjustment = new(candidate.WithWindow(new MomentCandidateWindow(candidate.Window.Start,
                    targetEnd, candidate.Window.SourceDuration)), GenerationCandidateNaturalEndingStatus.Adjusted,
                Evidence(Math.Min(index, last), Math.Max(index, evidenceThrough)));
            return true;
        }

        string[] Evidence(int start, int end) => units.Skip(start).Take(end - start + 1)
            .Select(static unit => $"transcript:sentence-end:{unit.Segment.NeighborhoodId}:{unit.Segment.Id}:{unit.Start:c}-{unit.End:c}")
            .Distinct(StringComparer.Ordinal).ToArray();
    }
}
