using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Media.Intelligence.Moments;

public enum TranscriptSpanSource
{
    ProviderSegment,
    HumanReference,
}

public enum TranscriptTimingPrecision
{
    SegmentApproximate,
    SegmentBoundaryClamped,
    HumanReviewedReference,
    Unknown,
}

public enum CandidateTranscriptRelationKind
{
    FullyInsideCandidate,
    CrossesCandidateStart,
    CrossesCandidateEnd,
    SpansCandidate,
    TouchesCandidateStart,
    TouchesCandidateEnd,
    OutsideCandidate,
    TimingAmbiguous,
    NoTranscriptEvidence,
}

public sealed class TranscriptSpan
{
    private readonly ReadOnlyCollection<AudioTranscriptionWarning>
        _warnings;

    public TranscriptSpan(
        string id,
        string neighborhoodId,
        string providerSegmentId,
        string text,
        TimeSpan neighborhoodRelativeStart,
        TimeSpan neighborhoodRelativeEnd,
        TimeSpan absoluteSourceStart,
        TimeSpan absoluteSourceEnd,
        TranscriptSpanSource source,
        TranscriptTimingPrecision timingPrecision,
        IEnumerable<AudioTranscriptionWarning>? warnings = null)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(neighborhoodId) ||
            string.IsNullOrWhiteSpace(providerSegmentId) ||
            string.IsNullOrWhiteSpace(text) ||
            neighborhoodRelativeStart < TimeSpan.Zero ||
            neighborhoodRelativeEnd < neighborhoodRelativeStart ||
            absoluteSourceStart < TimeSpan.Zero ||
            absoluteSourceEnd < absoluteSourceStart ||
            neighborhoodRelativeEnd - neighborhoodRelativeStart !=
            absoluteSourceEnd - absoluteSourceStart ||
            !Enum.IsDefined(source) ||
            !Enum.IsDefined(timingPrecision))
        {
            throw new ArgumentException(
                "Transcript spans require stable identities, text, defined provenance, and matching ordered timestamps.");
        }

        AudioTranscriptionWarning[] warningSnapshot =
            warnings?.ToArray() ??
            [];

        if (warningSnapshot.Any(static warning => warning is null))
        {
            throw new ArgumentException(
                "Transcript span warnings cannot contain null values.",
                nameof(warnings));
        }

        Id = id.Trim();
        NeighborhoodId = neighborhoodId.Trim();
        ProviderSegmentId = providerSegmentId.Trim();
        Text = text.Trim();
        NeighborhoodRelativeStart = neighborhoodRelativeStart;
        NeighborhoodRelativeEnd = neighborhoodRelativeEnd;
        AbsoluteSourceStart = absoluteSourceStart;
        AbsoluteSourceEnd = absoluteSourceEnd;
        Source = source;
        TimingPrecision = timingPrecision;
        _warnings = Array.AsReadOnly(warningSnapshot);
    }

    public string Id { get; }

    public string NeighborhoodId { get; }

    public string ProviderSegmentId { get; }

    public string Text { get; }

    public TimeSpan NeighborhoodRelativeStart { get; }

    public TimeSpan NeighborhoodRelativeEnd { get; }

    public TimeSpan AbsoluteSourceStart { get; }

    public TimeSpan AbsoluteSourceEnd { get; }

    public TimeSpan Duration => AbsoluteSourceEnd - AbsoluteSourceStart;

    public TranscriptSpanSource Source { get; }

    public TranscriptTimingPrecision TimingPrecision { get; }

    public IReadOnlyList<AudioTranscriptionWarning> Warnings => _warnings;

    public static TranscriptSpan FromProviderSegment(
        AudioTranscriptionSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        bool clamped =
            segment.Warnings.Any(
                warning =>
                    warning.Code ==
                    AudioTranscriptionWarningCode.BoundaryClamped);
        string id =
            StableId(
                "span",
                segment.NeighborhoodId,
                segment.Id,
                segment.AbsoluteSourceStart.Ticks.ToString(),
                segment.AbsoluteSourceEnd.Ticks.ToString());

        return new TranscriptSpan(
            id,
            segment.NeighborhoodId,
            segment.Id,
            segment.Text,
            segment.RelativeStart,
            segment.RelativeEnd,
            segment.AbsoluteSourceStart,
            segment.AbsoluteSourceEnd,
            TranscriptSpanSource.ProviderSegment,
            clamped
                ? TranscriptTimingPrecision.SegmentBoundaryClamped
                : TranscriptTimingPrecision.SegmentApproximate,
            segment.Warnings);
    }

    internal static string StableId(
        string prefix,
        params string[] values)
    {
        string material = string.Join("\u001F", values);
        string hash =
            Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(material)));

        return $"{prefix}-{hash[..16].ToLowerInvariant()}";
    }
}

public sealed record CandidateTranscriptRelation
{
    public CandidateTranscriptRelation(
        string id,
        string candidateId,
        string? transcriptSpanId,
        CandidateTranscriptRelationKind kind,
        TimeSpan overlapDuration,
        TimeSpan? boundaryDistance)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(candidateId) ||
            !Enum.IsDefined(kind) ||
            overlapDuration < TimeSpan.Zero ||
            boundaryDistance < TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Candidate/transcript relations require stable identities and non-negative measurements.");
        }

        if ((kind == CandidateTranscriptRelationKind.NoTranscriptEvidence) !=
            (transcriptSpanId is null))
        {
            throw new ArgumentException(
                "Only NoTranscriptEvidence relations omit a transcript span identity.",
                nameof(transcriptSpanId));
        }

        Id = id.Trim();
        CandidateId = candidateId.Trim();
        TranscriptSpanId =
            string.IsNullOrWhiteSpace(transcriptSpanId)
                ? null
                : transcriptSpanId.Trim();
        Kind = kind;
        OverlapDuration = overlapDuration;
        BoundaryDistance = boundaryDistance;
    }

    public string Id { get; }

    public string CandidateId { get; }

    public string? TranscriptSpanId { get; }

    public CandidateTranscriptRelationKind Kind { get; }

    public TimeSpan OverlapDuration { get; }

    public TimeSpan? BoundaryDistance { get; }
}

public static class CandidateTranscriptRelationshipClassifier
{
    public static CandidateTranscriptRelation Classify(
        string candidateId,
        TimeSpan candidateStart,
        TimeSpan candidateEnd,
        TranscriptSpan span,
        TimeSpan tolerance)
    {
        if (string.IsNullOrWhiteSpace(candidateId) ||
            candidateStart < TimeSpan.Zero ||
            candidateEnd <= candidateStart ||
            tolerance < TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Relationship classification requires a valid candidate interval and non-negative tolerance.");
        }

        ArgumentNullException.ThrowIfNull(span);

        CandidateTranscriptRelationKind kind;
        TimeSpan overlap =
            Max(
                TimeSpan.Zero,
                Min(candidateEnd, span.AbsoluteSourceEnd) -
                Max(candidateStart, span.AbsoluteSourceStart));
        TimeSpan? distance = null;

        if (span.AbsoluteSourceEnd == candidateStart)
        {
            kind = CandidateTranscriptRelationKind.TouchesCandidateStart;
            distance = TimeSpan.Zero;
        }
        else if (span.AbsoluteSourceStart == candidateEnd)
        {
            kind = CandidateTranscriptRelationKind.TouchesCandidateEnd;
            distance = TimeSpan.Zero;
        }
        else if (span.AbsoluteSourceStart < candidateStart &&
                 span.AbsoluteSourceEnd > candidateEnd)
        {
            kind = CandidateTranscriptRelationKind.SpansCandidate;
        }
        else if (span.AbsoluteSourceStart < candidateStart &&
                 span.AbsoluteSourceEnd > candidateStart)
        {
            kind = CandidateTranscriptRelationKind.CrossesCandidateStart;
        }
        else if (span.AbsoluteSourceStart < candidateEnd &&
                 span.AbsoluteSourceEnd > candidateEnd)
        {
            kind = CandidateTranscriptRelationKind.CrossesCandidateEnd;
        }
        else if (span.AbsoluteSourceStart >= candidateStart &&
                 span.AbsoluteSourceEnd <= candidateEnd)
        {
            kind = CandidateTranscriptRelationKind.FullyInsideCandidate;
        }
        else
        {
            distance =
                span.AbsoluteSourceEnd < candidateStart
                    ? candidateStart - span.AbsoluteSourceEnd
                    : span.AbsoluteSourceStart - candidateEnd;
            kind =
                distance <= tolerance
                    ? CandidateTranscriptRelationKind.TimingAmbiguous
                    : CandidateTranscriptRelationKind.OutsideCandidate;
        }

        string id =
            TranscriptSpan.StableId(
                "relation",
                candidateId,
                span.Id,
                kind.ToString(),
                tolerance.Ticks.ToString());

        return new CandidateTranscriptRelation(
            id,
            candidateId,
            span.Id,
            kind,
            overlap,
            distance);
    }

    public static CandidateTranscriptRelation NoEvidence(
        string candidateId)
    {
        if (string.IsNullOrWhiteSpace(candidateId))
        {
            throw new ArgumentException(
                "A candidate identity is required.",
                nameof(candidateId));
        }

        return new CandidateTranscriptRelation(
            TranscriptSpan.StableId(
                "relation",
                candidateId,
                "none"),
            candidateId,
            transcriptSpanId: null,
            CandidateTranscriptRelationKind.NoTranscriptEvidence,
            TimeSpan.Zero,
            boundaryDistance: null);
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) =>
        left <= right
            ? left
            : right;

    private static TimeSpan Max(TimeSpan left, TimeSpan right) =>
        left >= right
            ? left
            : right;
}
