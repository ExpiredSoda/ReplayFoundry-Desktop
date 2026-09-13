using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Media.Intelligence.Learning;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed class StudioCandidateDecision
{
    public StudioCandidateDecision(
        string candidateId,
        string projectId,
        string sourceIdentity,
        TimeSpan sourceStart,
        TimeSpan sourceEnd,
        GenerationOutputAssetDisposition disposition,
        StudioClipPreferenceRating? rating,
        DateTimeOffset recordedAtUtc,
        TasteMomentCorrection? correction = null)
    {
        if (string.IsNullOrWhiteSpace(candidateId) ||
            string.IsNullOrWhiteSpace(projectId) ||
            string.IsNullOrWhiteSpace(sourceIdentity) ||
            sourceStart < TimeSpan.Zero ||
            sourceEnd <= sourceStart ||
            !Enum.IsDefined(disposition) ||
            rating is { } value && !Enum.IsDefined(value) ||
            recordedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A Studio candidate decision requires valid immutable candidate provenance.");
        }

        CandidateId = candidateId.Trim();
        ProjectId = projectId.Trim();
        SourceIdentity = sourceIdentity.Trim();
        SourceStart = sourceStart;
        SourceEnd = sourceEnd;
        Disposition = disposition;
        Rating = rating;
        RecordedAtUtc = recordedAtUtc;
        correction?.Validate();
        Correction = correction;
    }

    public string CandidateId { get; }
    public string ProjectId { get; }
    public string SourceIdentity { get; }
    public TimeSpan SourceStart { get; }
    public TimeSpan SourceEnd { get; }
    public GenerationOutputAssetDisposition Disposition { get; }
    public StudioClipPreferenceRating? Rating { get; }
    public DateTimeOffset RecordedAtUtc { get; }
    public TasteMomentCorrection? Correction { get; }
}

public interface IStudioCandidateDecisionStore
{
    IReadOnlyList<StudioCandidateDecision> Current { get; }
    StudioCandidateDecision? Find(string candidateId);
    void Upsert(StudioCandidateDecision decision);
}
