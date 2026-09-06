using ReplayFoundry.Desktop.Media.Intelligence.SpeechActivity;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal static class GenerationCandidateNaturalBeginningPolicy
{
    // Keep the same pause/tail policy at both sides of a creator utterance.
    public static GenerationCandidateNaturalEndingAdjustment Adjust(
        MomentCandidate candidate,
        IEnumerable<SpeechActivityInterval> creatorSpeechIntervals,
        TimeSpan maximumDuration,
        GenerationSourceTranscript? transcript = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(creatorSpeechIntervals);
        if (maximumDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDuration));
        }

        SpeechActivityInterval[] intervals = creatorSpeechIntervals
            .OrderByDescending(static interval => interval.AbsoluteEnd)
            .ThenByDescending(static interval => interval.AbsoluteStart)
            .ToArray();
        TimeSpan originalStart = candidate.Window.Start;
        TimeSpan gap = GenerationCandidateNaturalEndingPolicy.ContinuationGap;
        bool speechAtBeginning = intervals.Any(interval =>
            interval.AbsoluteEnd > originalStart &&
            interval.AbsoluteStart <= originalStart + gap);
        TimeSpan speechStart = originalStart;
        var contributing = new List<SpeechActivityInterval>();
        foreach (SpeechActivityInterval interval in speechAtBeginning ? intervals : [])
        {
            if (interval.AbsoluteStart >= speechStart)
            {
                continue;
            }
            if (interval.AbsoluteEnd < speechStart - gap)
            {
                break;
            }
            speechStart = interval.AbsoluteStart;
            contributing.Add(interval);
        }
        TranscriptSentenceBeginning? sentence = transcript is null ? null :
            GenerationTranscriptSentenceBeginningPolicy.Find(transcript, originalStart, candidate.Window.End);
        if (sentence is not null && sentence.Start < speechStart)
        {
            speechStart = sentence.Start;
        }
        if (speechStart >= originalStart)
        {
            return GenerationCandidateNaturalEndingAdjustment.Unchanged(candidate);
        }

        TimeSpan targetStart = speechStart -
            GenerationCandidateNaturalEndingPolicy.NaturalTail;
        if (targetStart < TimeSpan.Zero)
        {
            targetStart = TimeSpan.Zero;
        }
        TimeSpan targetEnd = candidate.Window.End;
        if (targetEnd - targetStart > maximumDuration)
        {
            targetEnd = targetStart + maximumDuration;
        }

        TimeSpan lastRetainedEvent = candidate.Anchors
            .Max(static anchor => anchor.Timestamp);
        if (candidate.Episode is { } episode &&
            episode.End < candidate.Window.End &&
            episode.End > lastRetainedEvent)
        {
            lastRetainedEvent = episode.End;
        }
        string[] evidence = contributing.Select(interval =>
                $"vad:natural-start:{interval.AbsoluteStart:c}-{interval.AbsoluteEnd:c}")
            .Concat(sentence?.EvidenceReferences ?? [])
            .ToArray();
        // Reclaim only empty post-roll. Never undo a repaired ending, cut a
        // retained utterance, or lose the event while repairing its beginning.
        bool losesSpeech = intervals.Any(interval =>
            interval.AbsoluteStart < candidate.Window.End &&
            interval.AbsoluteEnd > targetEnd &&
            interval.AbsoluteEnd <= candidate.Window.End);
        bool cutsSpeech = intervals.Any(interval =>
            interval.AbsoluteStart < targetEnd && interval.AbsoluteEnd > targetEnd);
        bool losesTranscript = targetEnd < candidate.Window.End && transcript is not null &&
            transcript.Segments.Any(segment => segment.AbsoluteSourceStart < candidate.Window.End &&
                segment.AbsoluteSourceEnd > targetEnd);
        if (targetEnd <= lastRetainedEvent || losesSpeech || cutsSpeech || losesTranscript)
        {
            return new(candidate,
                GenerationCandidateNaturalEndingStatus.Unsafe,
                evidence);
        }

        return new(candidate.WithWindow(new MomentCandidateWindow(
                targetStart, targetEnd, candidate.Window.SourceDuration)),
            GenerationCandidateNaturalEndingStatus.Adjusted,
            evidence);
    }
}
