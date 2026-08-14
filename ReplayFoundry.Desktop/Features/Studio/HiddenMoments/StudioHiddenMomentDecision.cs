namespace ReplayFoundry.Desktop.Features.Studio.HiddenMoments;

public enum StudioHiddenMomentReviewDecision
{
    AcceptedIntoStudio,
    SkippedForProject,
}

public sealed record StudioHiddenMomentDecision
{
    public StudioHiddenMomentDecision(
        string projectId,
        string candidateId,
        string sourceIdentity,
        TimeSpan sourceStart,
        TimeSpan sourceEnd,
        StudioHiddenMomentReviewDecision decision,
        DateTimeOffset recordedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(projectId) ||
            string.IsNullOrWhiteSpace(candidateId) ||
            sourceIdentity.Length != 64 ||
            sourceIdentity.Any(static value => !Uri.IsHexDigit(value)) ||
            sourceStart < TimeSpan.Zero ||
            sourceEnd <= sourceStart ||
            !Enum.IsDefined(decision) ||
            recordedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A Hidden Moments decision requires bounded pseudonymous provenance.");
        }

        ProjectId = projectId.Trim();
        CandidateId = candidateId.Trim();
        SourceIdentity = sourceIdentity.ToUpperInvariant();
        SourceStart = sourceStart;
        SourceEnd = sourceEnd;
        Decision = decision;
        RecordedAtUtc = recordedAtUtc;
    }

    public string ProjectId { get; }
    public string CandidateId { get; }
    public string SourceIdentity { get; }
    public TimeSpan SourceStart { get; }
    public TimeSpan SourceEnd { get; }
    public StudioHiddenMomentReviewDecision Decision { get; }
    public DateTimeOffset RecordedAtUtc { get; }
}

public interface IStudioHiddenMomentDecisionStore
{
    IReadOnlyList<StudioHiddenMomentDecision> Current { get; }

    StudioHiddenMomentDecision? Find(
        string projectId,
        string candidateId);

    void Upsert(StudioHiddenMomentDecision decision);

    void ClearSkippedForProject(string projectId);
}
