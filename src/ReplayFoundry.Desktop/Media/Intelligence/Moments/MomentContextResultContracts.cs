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

public enum MomentContextWarningCode
{
    ApproximateSegmentTiming,
    BoundaryClamped,
    WordTimestampsUnavailable,
    EmptyOutputDoesNotProveSilence,
    LexicalTextIsNotGroundTruth,
    AudioRoleUnknown,
    ProviderWarning,
}

public sealed record MomentContextWarning
{
    public MomentContextWarning(
        MomentContextWarningCode code,
        string message,
        string? candidateId = null,
        string? spanId = null)
    {
        if (!Enum.IsDefined(code) ||
            string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "Moment context warnings require a defined code and message.");
        }

        Code = code;
        Message = message.Trim();
        CandidateId = Optional(candidateId);
        SpanId = Optional(spanId);
    }

    public MomentContextWarningCode Code { get; }

    public string Message { get; }

    public string? CandidateId { get; }

    public string? SpanId { get; }

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}

public sealed class MomentContextCandidate
{
    private readonly ReadOnlyCollection<TranscriptSpan>
        _transcriptSpans;
    private readonly ReadOnlyCollection<CandidateTranscriptRelation>
        _relationships;
    private readonly ReadOnlyCollection<MomentContextWarning>
        _warnings;

    public MomentContextCandidate(
        MomentDeterministicCandidateSnapshot deterministic,
        string neighborhoodId,
        int absoluteAudioStreamIndex,
        string? metadataTitleHint,
        AudioContentRoleAssignment audioRole,
        IEnumerable<TranscriptSpan> transcriptSpans,
        IEnumerable<CandidateTranscriptRelation> relationships,
        TranscriptEvidenceAssessment transcriptEvidence,
        MomentContextObservations observations,
        IEnumerable<MomentContextWarning>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(deterministic);
        ArgumentNullException.ThrowIfNull(audioRole);
        ArgumentNullException.ThrowIfNull(transcriptSpans);
        ArgumentNullException.ThrowIfNull(relationships);
        ArgumentNullException.ThrowIfNull(transcriptEvidence);
        ArgumentNullException.ThrowIfNull(observations);

        if (string.IsNullOrWhiteSpace(neighborhoodId) ||
            absoluteAudioStreamIndex < 0 ||
            observations.AbsoluteAudioStreamIndex !=
                absoluteAudioStreamIndex ||
            observations.AudioRole != audioRole.Role ||
            observations.AudioRoleSource != audioRole.Source)
        {
            throw new ArgumentException(
                "Context candidate stream, role, and neighborhood identity must align.");
        }

        TranscriptSpan[] spanSnapshot =
            transcriptSpans
                .OrderBy(static span => span.AbsoluteSourceStart)
                .ThenBy(static span => span.Id, StringComparer.Ordinal)
                .ToArray();
        CandidateTranscriptRelation[] relationshipSnapshot =
            relationships
                .OrderBy(static relation => relation.Kind)
                .ThenBy(static relation => relation.TranscriptSpanId, StringComparer.Ordinal)
                .ToArray();
        MomentContextWarning[] warningSnapshot =
            warnings?.ToArray() ??
            [];
        if (spanSnapshot.Any(static span => span is null) ||
            relationshipSnapshot.Any(static relation => relation is null) ||
            warningSnapshot.Any(static warning => warning is null) ||
            spanSnapshot
                .GroupBy(static span => span.Id, StringComparer.Ordinal)
                .Any(static group => group.Count() > 1) ||
            relationshipSnapshot
                .GroupBy(static relation => relation.Id, StringComparer.Ordinal)
                .Any(static group => group.Count() > 1) ||
            relationshipSnapshot.Any(
                relation =>
                    !string.Equals(
                        relation.CandidateId,
                        deterministic.Id,
                        StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "Context candidate spans, relationships, and warnings must be unique and owned by the candidate.");
        }

        Deterministic = deterministic;
        NeighborhoodId = neighborhoodId.Trim();
        AbsoluteAudioStreamIndex = absoluteAudioStreamIndex;
        MetadataTitleHint =
            string.IsNullOrWhiteSpace(metadataTitleHint)
                ? null
                : metadataTitleHint.Trim();
        MetadataTitleIsAuthoritative = false;
        AudioRole = audioRole;
        TranscriptEvidence = transcriptEvidence;
        Observations = observations;
        _transcriptSpans = Array.AsReadOnly(spanSnapshot);
        _relationships = Array.AsReadOnly(relationshipSnapshot);
        _warnings = Array.AsReadOnly(warningSnapshot);
    }

    public MomentDeterministicCandidateSnapshot Deterministic { get; }

    public string NeighborhoodId { get; }

    public int AbsoluteAudioStreamIndex { get; }

    public string? MetadataTitleHint { get; }

    public bool MetadataTitleIsAuthoritative { get; }

    public AudioContentRoleAssignment AudioRole { get; }

    public IReadOnlyList<TranscriptSpan> TranscriptSpans =>
        _transcriptSpans;

    public IReadOnlyList<CandidateTranscriptRelation> Relationships =>
        _relationships;

    public TranscriptEvidenceAssessment TranscriptEvidence { get; }

    public MomentContextObservations Observations { get; }

    public IReadOnlyList<MomentContextWarning> Warnings => _warnings;
}

public sealed class MomentContextManifest
{
    private readonly ReadOnlyCollection<MomentContextInputArtifact>
        _inputArtifacts;

    public MomentContextManifest(
        string schemaVersion,
        string contextPolicyVersion,
        DateTimeOffset builtAtUtc,
        int candidateCount,
        TimeSpan elapsed,
        MomentContextModelLock modelLock,
        IEnumerable<MomentContextInputArtifact> inputArtifacts,
        bool mediaProcessesInvoked,
        bool inferenceProcessesInvoked,
        bool deterministicFinderInvoked)
    {
        if (string.IsNullOrWhiteSpace(schemaVersion) ||
            string.IsNullOrWhiteSpace(contextPolicyVersion) ||
            builtAtUtc.Offset != TimeSpan.Zero ||
            candidateCount <= 0 ||
            elapsed < TimeSpan.Zero ||
            mediaProcessesInvoked ||
            inferenceProcessesInvoked ||
            deterministicFinderInvoked)
        {
            throw new ArgumentException(
                "Context manifests require UTC timing, candidates, and zero replay-time media/model/finder processes.");
        }

        ArgumentNullException.ThrowIfNull(modelLock);
        ArgumentNullException.ThrowIfNull(inputArtifacts);

        MomentContextInputArtifact[] artifactSnapshot =
            inputArtifacts.ToArray();

        if (artifactSnapshot.Any(static item => item is null) ||
            artifactSnapshot
                .GroupBy(static item => item.Kind, StringComparer.Ordinal)
                .Any(static group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Context manifest input artifacts must be unique.",
                nameof(inputArtifacts));
        }

        SchemaVersion = schemaVersion.Trim();
        ContextPolicyVersion = contextPolicyVersion.Trim();
        BuiltAtUtc = builtAtUtc;
        CandidateCount = candidateCount;
        Elapsed = elapsed;
        ModelLock = modelLock;
        MediaProcessesInvoked = false;
        InferenceProcessesInvoked = false;
        DeterministicFinderInvoked = false;
        _inputArtifacts = Array.AsReadOnly(artifactSnapshot);
    }

    public string SchemaVersion { get; }

    public string ContextPolicyVersion { get; }

    public DateTimeOffset BuiltAtUtc { get; }

    public int CandidateCount { get; }

    public TimeSpan Elapsed { get; }

    public MomentContextModelLock ModelLock { get; }

    public IReadOnlyList<MomentContextInputArtifact> InputArtifacts =>
        _inputArtifacts;

    public bool MediaProcessesInvoked { get; }

    public bool InferenceProcessesInvoked { get; }

    public bool DeterministicFinderInvoked { get; }
}

public sealed class MomentContextBundle
{
    private readonly ReadOnlyCollection<MomentContextCandidate>
        _candidates;
    private readonly ReadOnlyCollection<MomentContextWarning>
        _warnings;

    public MomentContextBundle(
        MomentContextBundleRequest request,
        IEnumerable<MomentContextCandidate> candidates,
        MomentContextManifest manifest,
        IEnumerable<MomentContextWarning>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(manifest);

        MomentContextCandidate[] candidateSnapshot =
            candidates
                .OrderBy(
                    static candidate =>
                        candidate.Deterministic.ProposalOrder)
                .ToArray();
        MomentContextWarning[] warningSnapshot =
            warnings?.ToArray() ??
            [];

        if (candidateSnapshot.Length !=
                request.Deterministic.Candidates.Count ||
            candidateSnapshot.Any(static candidate => candidate is null) ||
            warningSnapshot.Any(static warning => warning is null) ||
            !candidateSnapshot
                .Select(static candidate => candidate.Deterministic)
                .SequenceEqual(request.Deterministic.Candidates) ||
            manifest.CandidateCount != candidateSnapshot.Length ||
            !ReferenceEquals(manifest.ModelLock, request.ModelLock))
        {
            throw new ArgumentException(
                "A Moment Context Bundle must preserve every deterministic candidate in original order.",
                nameof(candidates));
        }

        Request = request;
        Manifest = manifest;
        _candidates = Array.AsReadOnly(candidateSnapshot);
        _warnings = Array.AsReadOnly(warningSnapshot);
    }

    public MomentContextBundleRequest Request { get; }

    public IReadOnlyList<MomentContextCandidate> Candidates =>
        _candidates;

    public MomentContextManifest Manifest { get; }

    public IReadOnlyList<MomentContextWarning> Warnings => _warnings;
}

public interface IMomentContextBundleBuilder
{
    MomentContextBundle Build(
        MomentContextBundleRequest request,
        CancellationToken cancellationToken = default);
}
