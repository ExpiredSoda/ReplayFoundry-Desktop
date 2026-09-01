using System.Collections.ObjectModel;
using ReplayFoundry.Desktop.Media.Intelligence.SpeechActivity;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal enum GenerationCandidateNaturalEndingStatus
{
    Unchanged,
    Adjusted,
    Unsafe,
}

internal sealed class GenerationCandidateNaturalEndingAdjustment
{
    private readonly ReadOnlyCollection<string> _evidenceReferences;

    public GenerationCandidateNaturalEndingAdjustment(
        MomentCandidate candidate,
        GenerationCandidateNaturalEndingStatus status,
        IEnumerable<string>? evidenceReferences = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        string[] references = evidenceReferences?
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        if (references.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Natural-ending evidence references cannot be blank.",
                nameof(evidenceReferences));
        }

        Candidate = candidate;
        Status = status;
        _evidenceReferences = Array.AsReadOnly(references);
    }

    public MomentCandidate Candidate { get; }
    public GenerationCandidateNaturalEndingStatus Status { get; }
    public bool WasAdjusted =>
        Status == GenerationCandidateNaturalEndingStatus.Adjusted;
    public bool RequiresAutomaticRejection =>
        Status == GenerationCandidateNaturalEndingStatus.Unsafe;
    public IReadOnlyList<string> EvidenceReferences => _evidenceReferences;

    public static GenerationCandidateNaturalEndingAdjustment Unchanged(
        MomentCandidate candidate) =>
        new(candidate, GenerationCandidateNaturalEndingStatus.Unchanged);
}

internal static class GenerationCandidateNaturalEndingPolicy
{
    internal static readonly TimeSpan ContinuationGap =
        TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan NaturalTail =
        TimeSpan.FromMilliseconds(750);

    public static GenerationCandidateNaturalEndingAdjustment Adjust(
        MomentCandidate candidate,
        IEnumerable<SpeechActivityInterval> creatorSpeechIntervals,
        TimeSpan maximumDuration)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(creatorSpeechIntervals);
        if (maximumDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDuration));
        }

        SpeechActivityInterval[] suppliedIntervals =
            creatorSpeechIntervals.ToArray();
        if (suppliedIntervals.Any(static interval => interval is null))
        {
            throw new ArgumentException(
                "Creator-speech intervals cannot contain null entries.",
                nameof(creatorSpeechIntervals));
        }
        SpeechActivityInterval[] intervals = suppliedIntervals
            .OrderBy(static interval => interval.AbsoluteStart)
            .ThenBy(static interval => interval.AbsoluteEnd)
            .ToArray();

        TimeSpan originalEnd = candidate.Window.End;
        bool hasCreatorSpeechAtTheEnding = intervals.Any(interval =>
            interval.AbsoluteStart < originalEnd &&
            interval.AbsoluteEnd >= originalEnd - ContinuationGap);
        if (!hasCreatorSpeechAtTheEnding)
        {
            return GenerationCandidateNaturalEndingAdjustment.Unchanged(
                candidate);
        }

        TimeSpan speechEnd = originalEnd;
        var contributing = new List<SpeechActivityInterval>();
        foreach (SpeechActivityInterval interval in intervals)
        {
            if (interval.AbsoluteEnd <= speechEnd)
            {
                continue;
            }
            if (interval.AbsoluteStart > speechEnd + ContinuationGap)
            {
                break;
            }

            speechEnd = interval.AbsoluteEnd;
            contributing.Add(interval);
        }
        if (speechEnd <= originalEnd)
        {
            return GenerationCandidateNaturalEndingAdjustment.Unchanged(
                candidate);
        }

        TimeSpan targetEnd = speechEnd + NaturalTail;
        if (targetEnd > candidate.Window.SourceDuration)
        {
            targetEnd = candidate.Window.SourceDuration;
        }
        TimeSpan targetStart = candidate.Window.Start;
        if (targetEnd - targetStart > maximumDuration)
        {
            targetStart = targetEnd - maximumDuration;
        }

        TimeSpan firstRetainedEvent = candidate.Anchors
            .Min(static anchor => anchor.Timestamp);
        if (candidate.Episode is { } episode &&
            episode.Start > candidate.Window.Start &&
            episode.Start < firstRetainedEvent)
        {
            firstRetainedEvent = episode.Start;
        }

        string[] evidence = contributing.Select(interval =>
                $"vad:natural-end:{interval.AbsoluteStart:c}-{interval.AbsoluteEnd:c}")
            .ToArray();
        if (targetStart > firstRetainedEvent)
        {
            return new GenerationCandidateNaturalEndingAdjustment(
                candidate,
                GenerationCandidateNaturalEndingStatus.Unsafe,
                evidence);
        }

        var adjustedWindow = new MomentCandidateWindow(
            targetStart,
            targetEnd,
            candidate.Window.SourceDuration);
        return new GenerationCandidateNaturalEndingAdjustment(
            candidate.WithWindow(adjustedWindow),
            GenerationCandidateNaturalEndingStatus.Adjusted,
            evidence);
    }
}
