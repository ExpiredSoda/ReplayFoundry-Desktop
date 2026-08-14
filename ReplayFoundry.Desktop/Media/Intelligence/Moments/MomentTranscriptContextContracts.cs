using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Enrichment;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Media.Intelligence.Moments;

public sealed class MomentTranscriptNeighborhoodContext
{
    private readonly ReadOnlyCollection<AudioTranscriptionSegment>
        _segments;
    private readonly ReadOnlyCollection<AudioTranscriptionWarning>
        _warnings;

    public MomentTranscriptNeighborhoodContext(
        string id,
        TimeSpan start,
        TimeSpan end,
        TimeSpan sourceDuration,
        int absoluteAudioStreamIndex,
        AudioTranscriptionLanguage? detectedLanguage,
        IEnumerable<AudioTranscriptionSegment> segments,
        IEnumerable<AudioTranscriptionWarning> warnings,
        AudioTranscriptionOptions options,
        InferenceExecutionManifest execution)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            sourceDuration <= TimeSpan.Zero ||
            start < TimeSpan.Zero ||
            end <= start ||
            end > sourceDuration ||
            absoluteAudioStreamIndex < 0)
        {
            throw new ArgumentException(
                "Transcript neighborhoods require stable identity and bounded source timing.");
        }

        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(warnings);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(execution);

        AudioTranscriptionSegment[] segmentSnapshot =
            segments
                .OrderBy(static segment => segment.AbsoluteSourceStart)
                .ThenBy(static segment => segment.Id, StringComparer.Ordinal)
                .ToArray();
        AudioTranscriptionWarning[] warningSnapshot =
            warnings.ToArray();

        if (segmentSnapshot.Any(static segment => segment is null) ||
            warningSnapshot.Any(static warning => warning is null) ||
            segmentSnapshot.Any(
                segment =>
                    !string.Equals(
                        segment.NeighborhoodId,
                        id,
                        StringComparison.Ordinal) ||
                    segment.AbsoluteSourceStart < start ||
                    segment.AbsoluteSourceEnd > end ||
                    segment.RelativeStart !=
                        segment.AbsoluteSourceStart - start ||
                    segment.RelativeEnd !=
                        segment.AbsoluteSourceEnd - start))
        {
            throw new ArgumentException(
                "Transcript segments must remain inside and relative to their neighborhood.",
                nameof(segments));
        }

        Id = id.Trim();
        Start = start;
        End = end;
        SourceDuration = sourceDuration;
        AbsoluteAudioStreamIndex = absoluteAudioStreamIndex;
        DetectedLanguage = detectedLanguage;
        Options = options;
        Execution = execution;
        _segments = Array.AsReadOnly(segmentSnapshot);
        _warnings = Array.AsReadOnly(warningSnapshot);
    }

    public string Id { get; }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public TimeSpan Duration => End - Start;

    public TimeSpan SourceDuration { get; }

    public int AbsoluteAudioStreamIndex { get; }

    public AudioTranscriptionLanguage? DetectedLanguage { get; }

    public IReadOnlyList<AudioTranscriptionSegment> Segments => _segments;

    public IReadOnlyList<AudioTranscriptionWarning> Warnings => _warnings;

    public AudioTranscriptionOptions Options { get; }

    public InferenceExecutionManifest Execution { get; }
}

public sealed record MomentTranscriptMembership
{
    public MomentTranscriptMembership(
        string candidateId,
        string neighborhoodId,
        TimeSpan candidateStart,
        TimeSpan candidateEnd,
        int candidateSourceOrder)
    {
        if (string.IsNullOrWhiteSpace(candidateId) ||
            string.IsNullOrWhiteSpace(neighborhoodId) ||
            candidateStart < TimeSpan.Zero ||
            candidateEnd <= candidateStart ||
            candidateSourceOrder < 0)
        {
            throw new ArgumentException(
                "Transcript membership requires candidate and neighborhood identity.");
        }
        CandidateId = candidateId.Trim();
        NeighborhoodId = neighborhoodId.Trim();
        CandidateStart = candidateStart;
        CandidateEnd = candidateEnd;
        CandidateSourceOrder = candidateSourceOrder;
    }

    public string CandidateId { get; }

    public string NeighborhoodId { get; }

    public TimeSpan CandidateStart { get; }

    public TimeSpan CandidateEnd { get; }

    public int CandidateSourceOrder { get; }
}

public sealed class MomentTranscriptContext
{
    private readonly ReadOnlyCollection<MomentTranscriptNeighborhoodContext>
        _neighborhoods;
    private readonly ReadOnlyCollection<MomentTranscriptMembership>
        _memberships;
    private readonly ReadOnlyCollection<MomentEnrichmentWarning>
        _warnings;

    public MomentTranscriptContext(
        string sourcePath,
        TimeSpan sourceDuration,
        string finderName,
        string finderVersion,
        string policyHash,
        int absoluteAudioStreamIndex,
        string? metadataTitleHint,
        IEnumerable<MomentTranscriptNeighborhoodContext> neighborhoods,
        IEnumerable<MomentTranscriptMembership> memberships,
        MomentEnrichmentOptions neighborhoodOptions,
        IEnumerable<MomentEnrichmentWarning>? warnings = null,
        string? inputArtifactSha256 = null)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) ||
            !Path.IsPathFullyQualified(sourcePath) ||
            sourceDuration <= TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(finderName) ||
            string.IsNullOrWhiteSpace(finderVersion) ||
            string.IsNullOrWhiteSpace(policyHash) ||
            absoluteAudioStreamIndex < 0)
        {
            throw new ArgumentException(
                "Transcript context requires source, finder, policy, and exact stream identity.");
        }

        ArgumentNullException.ThrowIfNull(neighborhoods);
        ArgumentNullException.ThrowIfNull(memberships);
        ArgumentNullException.ThrowIfNull(neighborhoodOptions);

        MomentTranscriptNeighborhoodContext[] neighborhoodSnapshot =
            neighborhoods
                .OrderBy(static item => item.Start)
                .ThenBy(static item => item.Id, StringComparer.Ordinal)
                .ToArray();
        MomentTranscriptMembership[] membershipSnapshot =
            memberships
                .OrderBy(static item => item.CandidateSourceOrder)
                .ToArray();
        MomentEnrichmentWarning[] warningSnapshot =
            warnings?.ToArray() ??
            [];

        if (neighborhoodSnapshot.Any(static item => item is null) ||
            membershipSnapshot.Any(static item => item is null) ||
            warningSnapshot.Any(static item => item is null) ||
            neighborhoodSnapshot
                .GroupBy(static item => item.Id, StringComparer.Ordinal)
                .Any(static group => group.Count() > 1) ||
            membershipSnapshot
                .GroupBy(static item => item.CandidateId, StringComparer.Ordinal)
                .Any(static group => group.Count() > 1) ||
            neighborhoodSnapshot.Any(
                item =>
                    item.SourceDuration != sourceDuration ||
                    item.AbsoluteAudioStreamIndex !=
                    absoluteAudioStreamIndex) ||
            membershipSnapshot.Any(
                membership =>
                    !neighborhoodSnapshot.Any(
                        neighborhood =>
                            string.Equals(
                                neighborhood.Id,
                                membership.NeighborhoodId,
                                StringComparison.Ordinal) &&
                            membership.CandidateStart >=
                                neighborhood.Start &&
                            membership.CandidateEnd <=
                                neighborhood.End)))
        {
            throw new ArgumentException(
                "Transcript neighborhoods and memberships must be unique, complete, and bound to one source and stream.");
        }

        SourcePath = Path.GetFullPath(sourcePath);
        SourceDuration = sourceDuration;
        FinderName = finderName.Trim();
        FinderVersion = finderVersion.Trim();
        PolicyHash = policyHash.Trim();
        AbsoluteAudioStreamIndex = absoluteAudioStreamIndex;
        MetadataTitleHint =
            string.IsNullOrWhiteSpace(metadataTitleHint)
                ? null
                : metadataTitleHint.Trim();
        NeighborhoodOptions = neighborhoodOptions;
        InputArtifactSha256 =
            inputArtifactSha256 is null
                ? null
                : MomentDeterministicCandidateSnapshot.NormalizeSha256(
                    inputArtifactSha256);
        _neighborhoods = Array.AsReadOnly(neighborhoodSnapshot);
        _memberships = Array.AsReadOnly(membershipSnapshot);
        _warnings = Array.AsReadOnly(warningSnapshot);
    }

    public string SourcePath { get; }

    public TimeSpan SourceDuration { get; }

    public string FinderName { get; }

    public string FinderVersion { get; }

    public string PolicyHash { get; }

    public int AbsoluteAudioStreamIndex { get; }

    public string? MetadataTitleHint { get; }

    public MomentEnrichmentOptions NeighborhoodOptions { get; }

    public string? InputArtifactSha256 { get; }

    public IReadOnlyList<MomentTranscriptNeighborhoodContext>
        Neighborhoods =>
        _neighborhoods;

    public IReadOnlyList<MomentTranscriptMembership> Memberships =>
        _memberships;

    public IReadOnlyList<MomentEnrichmentWarning> Warnings => _warnings;

    public static MomentTranscriptContext FromResult(
        MomentTranscriptionEnrichmentResult result,
        string? metadataTitleHint = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        MomentTranscriptNeighborhoodContext[] neighborhoods =
            result.NeighborhoodResults
                .Select(
                    item =>
                        new MomentTranscriptNeighborhoodContext(
                            item.Neighborhood.Id,
                            item.Neighborhood.Start,
                            item.Neighborhood.End,
                            item.Neighborhood.SourceDuration,
                            item.Transcription.AbsoluteAudioStreamIndex,
                            item.Transcription.DetectedLanguage,
                            item.Transcription.Segments,
                            item.Transcription.Warnings,
                            item.Transcription.Manifest.Options,
                            item.Transcription.Manifest.Execution))
                .ToArray();
        MomentTranscriptMembership[] memberships =
            result.Plan.Neighborhoods
                .SelectMany(
                    neighborhood =>
                        neighborhood.Memberships.Select(
                            membership =>
                                new MomentTranscriptMembership(
                                    membership.CandidateId,
                                    neighborhood.Id,
                                    membership.CandidateStart,
                                    membership.CandidateEnd,
                                    membership.CandidateSourceOrder)))
                .ToArray();

        return new MomentTranscriptContext(
            result.Request.SourcePath,
            result.Request.SourceDuration,
            result.Request.FinderName,
            result.Request.FinderVersion,
            result.Request.PolicyHash,
            result.Request.AbsoluteAudioStreamIndex,
            metadataTitleHint,
            neighborhoods,
            memberships,
            result.Request.Options,
            result.Warnings);
    }
}
