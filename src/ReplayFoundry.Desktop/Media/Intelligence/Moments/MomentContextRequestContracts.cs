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

public sealed class MomentContextModelLock
{
    private readonly ReadOnlyCollection<string> _knownLimitations;

    public MomentContextModelLock(
        string schemaVersion,
        string status,
        string decisionLockSha256,
        string lockedBenchmarkSha256,
        InferenceProviderIdentity providerIdentity,
        string executableSha256,
        string modelId,
        string modelSha256,
        AudioTranscriptionOptions options,
        bool postHoldoutRetuningPermitted,
        IEnumerable<string> knownLimitations)
    {
        if (string.IsNullOrWhiteSpace(schemaVersion) ||
            !string.Equals(
                status,
                "ProvisionalResearchDefault",
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(modelId) ||
            postHoldoutRetuningPermitted)
        {
            throw new ArgumentException(
                "The v0.2B context lock must be the frozen provisional research decision and must prohibit post-Holdout retuning.");
        }

        ArgumentNullException.ThrowIfNull(providerIdentity);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(knownLimitations);

        if (options.LanguageMode != AudioTranscriptionLanguageMode.Auto ||
            options.RequestedLanguage is not null ||
            options.TranslateToEnglish ||
            !options.RequireSegmentTimestamps ||
            options.RequestWordTimestamps ||
            options.Temperature != 0 ||
            options.ThreadCount is not null ||
            options.ProcessorHint != AudioTranscriptionProcessorHint.Auto ||
            options.OutputFormatPolicy !=
                AudioTranscriptionOutputFormatPolicy.StructuredJson)
        {
            throw new ArgumentException(
                "The v0.2B lock requires Auto language, segment timestamps, no word timestamps, temperature zero, and default processor/thread selection.",
                nameof(options));
        }

        string[] limitationSnapshot =
            knownLimitations.Select(Required).ToArray();

        if (limitationSnapshot.Length == 0)
        {
            throw new ArgumentException(
                "The research lock must preserve its known limitations.",
                nameof(knownLimitations));
        }

        SchemaVersion = schemaVersion.Trim();
        Status = status;
        DecisionLockSha256 =
            MomentDeterministicCandidateSnapshot.NormalizeSha256(
                decisionLockSha256);
        LockedBenchmarkSha256 =
            MomentDeterministicCandidateSnapshot.NormalizeSha256(
                lockedBenchmarkSha256);
        ProviderIdentity = providerIdentity;
        ExecutableSha256 =
            MomentDeterministicCandidateSnapshot.NormalizeSha256(
                executableSha256);
        ModelId = modelId.Trim();
        ModelSha256 =
            MomentDeterministicCandidateSnapshot.NormalizeSha256(
                modelSha256);
        Options = options;
        PostHoldoutRetuningPermitted = false;
        _knownLimitations = Array.AsReadOnly(limitationSnapshot);
    }

    public string SchemaVersion { get; }

    public string Status { get; }

    public string DecisionLockSha256 { get; }

    public string LockedBenchmarkSha256 { get; }

    public InferenceProviderIdentity ProviderIdentity { get; }

    public string ExecutableSha256 { get; }

    public string ModelId { get; }

    public string ModelSha256 { get; }

    public AudioTranscriptionOptions Options { get; }

    public bool PostHoldoutRetuningPermitted { get; }
    public IReadOnlyList<string> KnownLimitations => _knownLimitations;

    private static string Required(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Lock limitations cannot be blank.")
            : value.Trim();
}

public sealed record MomentContextInputArtifact
{
    public MomentContextInputArtifact(
        string kind,
        string sha256)
    {
        Kind =
            string.IsNullOrWhiteSpace(kind)
                ? throw new ArgumentException(
                    "Input artifacts require a kind.",
                    nameof(kind))
                : kind.Trim();
        Sha256 =
            MomentDeterministicCandidateSnapshot.NormalizeSha256(
                sha256);
    }

    public string Kind { get; }

    public string Sha256 { get; }
}

public sealed class MomentContextBundleRequest
{
    private readonly ReadOnlyCollection<MomentContextInputArtifact>
        _inputArtifacts;

    public MomentContextBundleRequest(
        MomentDeterministicContext deterministic,
        MomentTranscriptContext transcription,
        int absoluteAudioStreamIndex,
        AudioContentRoleAssignment audioRole,
        MomentContextModelLock modelLock,
        MomentContextOptions options,
        IEnumerable<MomentContextInputArtifact>? inputArtifacts = null)
    {
        ArgumentNullException.ThrowIfNull(deterministic);
        ArgumentNullException.ThrowIfNull(transcription);
        ArgumentNullException.ThrowIfNull(audioRole);
        ArgumentNullException.ThrowIfNull(modelLock);
        ArgumentNullException.ThrowIfNull(options);

        MomentContextInputArtifact[] artifactSnapshot =
            inputArtifacts?.OrderBy(
                    static artifact => artifact.Kind,
                    StringComparer.Ordinal)
                .ToArray() ??
            [];

        if (artifactSnapshot.Any(static artifact => artifact is null) ||
            artifactSnapshot
                .GroupBy(static artifact => artifact.Kind, StringComparer.Ordinal)
                .Any(static group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Input artifact identities must be non-null and unique.",
                nameof(inputArtifacts));
        }

        Deterministic = deterministic;
        Transcription = transcription;
        AbsoluteAudioStreamIndex = absoluteAudioStreamIndex;
        AudioRole = audioRole;
        ModelLock = modelLock;
        Options = options;
        _inputArtifacts = Array.AsReadOnly(artifactSnapshot);
    }

    public MomentDeterministicContext Deterministic { get; }

    public MomentTranscriptContext Transcription { get; }

    public int AbsoluteAudioStreamIndex { get; }

    public AudioContentRoleAssignment AudioRole { get; }

    public MomentContextModelLock ModelLock { get; }

    public MomentContextOptions Options { get; }

    public IReadOnlyList<MomentContextInputArtifact> InputArtifacts =>
        _inputArtifacts;
}

public sealed class MomentContextObservations
{
    private readonly ReadOnlyCollection<TranscriptTimingPrecision>
        _timingPrecisions;

    public MomentContextObservations(
        int lexicalSegmentCount,
        int nonSpeechTokenSegmentCount,
        int lexicalCharacterCount,
        int lexicalWordCount,
        double transcriptCoveredDurationRatio,
        double candidateOverlapDurationRatio,
        int spansFullyInsideCandidate,
        int spansCrossingCandidateStart,
        int spansCrossingCandidateEnd,
        int boundaryClampedSpanCount,
        bool noTextResult,
        bool languageAvailable,
        int absoluteAudioStreamIndex,
        AudioContentRole audioRole,
        AudioContentRoleSource audioRoleSource,
        IEnumerable<TranscriptTimingPrecision> timingPrecisions)
    {
        if (lexicalSegmentCount < 0 ||
            nonSpeechTokenSegmentCount < 0 ||
            lexicalCharacterCount < 0 ||
            lexicalWordCount < 0 ||
            !double.IsFinite(transcriptCoveredDurationRatio) ||
            transcriptCoveredDurationRatio is < 0 or > 1 ||
            !double.IsFinite(candidateOverlapDurationRatio) ||
            candidateOverlapDurationRatio is < 0 or > 1 ||
            spansFullyInsideCandidate < 0 ||
            spansCrossingCandidateStart < 0 ||
            spansCrossingCandidateEnd < 0 ||
            boundaryClampedSpanCount < 0 ||
            absoluteAudioStreamIndex < 0 ||
            !Enum.IsDefined(audioRole) ||
            !Enum.IsDefined(audioRoleSource))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lexicalSegmentCount));
        }

        ArgumentNullException.ThrowIfNull(timingPrecisions);
        TranscriptTimingPrecision[] timingSnapshot =
            timingPrecisions
                .Distinct()
                .OrderBy(static value => value)
                .ToArray();

        if (timingSnapshot.Any(static value => !Enum.IsDefined(value)))
        {
            throw new ArgumentException(
                "Timing precision values must be defined.",
                nameof(timingPrecisions));
        }

        LexicalSegmentCount = lexicalSegmentCount;
        NonSpeechTokenSegmentCount = nonSpeechTokenSegmentCount;
        LexicalCharacterCount = lexicalCharacterCount;
        LexicalWordCount = lexicalWordCount;
        TranscriptCoveredDurationRatio = transcriptCoveredDurationRatio;
        CandidateOverlapDurationRatio = candidateOverlapDurationRatio;
        SpansFullyInsideCandidate = spansFullyInsideCandidate;
        SpansCrossingCandidateStart = spansCrossingCandidateStart;
        SpansCrossingCandidateEnd = spansCrossingCandidateEnd;
        BoundaryClampedSpanCount = boundaryClampedSpanCount;
        NoTextResult = noTextResult;
        LanguageAvailable = languageAvailable;
        AbsoluteAudioStreamIndex = absoluteAudioStreamIndex;
        AudioRole = audioRole;
        AudioRoleSource = audioRoleSource;
        _timingPrecisions = Array.AsReadOnly(timingSnapshot);
    }

    public int LexicalSegmentCount { get; }

    public int NonSpeechTokenSegmentCount { get; }

    public int LexicalCharacterCount { get; }

    public int LexicalWordCount { get; }

    public double TranscriptCoveredDurationRatio { get; }

    public double CandidateOverlapDurationRatio { get; }

    public int SpansFullyInsideCandidate { get; }

    public int SpansCrossingCandidateStart { get; }

    public int SpansCrossingCandidateEnd { get; }

    public int BoundaryClampedSpanCount { get; }

    public bool NoTextResult { get; }

    public bool LanguageAvailable { get; }

    public int AbsoluteAudioStreamIndex { get; }

    public AudioContentRole AudioRole { get; }

    public AudioContentRoleSource AudioRoleSource { get; }

    public IReadOnlyList<TranscriptTimingPrecision> TimingPrecisions =>
        _timingPrecisions;
}
