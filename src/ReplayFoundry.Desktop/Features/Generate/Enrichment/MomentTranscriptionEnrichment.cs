using System.Collections.ObjectModel;
using ReplayFoundry.Desktop.Media.AudioExtraction;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Generate.Enrichment;

public enum MomentEnrichmentWarningCode
{
    NoCandidates,
    NoTranscriptText,
    CandidateBoundaryCutsSegment,
    WordTimingUnavailable,
    SilenceStatusUnavailable,
}

public sealed record MomentEnrichmentWarning
{
    public MomentEnrichmentWarning(
        MomentEnrichmentWarningCode code,
        string message,
        string? neighborhoodId = null,
        string? candidateId = null)
    {
        if (!Enum.IsDefined(code) ||
            string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "Enrichment warnings require a defined code and message.");
        }

        Code = code;
        Message = message.Trim();
        NeighborhoodId = Optional(neighborhoodId);
        CandidateId = Optional(candidateId);
    }

    public MomentEnrichmentWarningCode Code { get; }

    public string Message { get; }

    public string? NeighborhoodId { get; }

    public string? CandidateId { get; }

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}

public enum CandidateTranscriptBoundaryStatus
{
    Complete,
    BeginsInsideSegment,
    EndsInsideSegment,
    BeginsAndEndsInsideSegments,
}

public sealed class CandidateTranscriptFeatures
{
    public CandidateTranscriptFeatures(
        double transcriptCoveredDurationRatio,
        int segmentCount,
        int wordCount,
        double? wordsPerSecond,
        bool candidateBeginsInsideSegment,
        bool candidateEndsInsideSegment,
        int completeSegmentCount,
        int questionMarkCount,
        int exclamationMarkCount,
        bool detectedLanguageAvailable,
        bool? overlappingSpeechReported,
        bool? silenceOnlyNeighborhood)
    {
        if (!double.IsFinite(transcriptCoveredDurationRatio) ||
            transcriptCoveredDurationRatio is < 0 or > 1 ||
            segmentCount < 0 ||
            wordCount < 0 ||
            completeSegmentCount < 0 ||
            completeSegmentCount > segmentCount ||
            questionMarkCount < 0 ||
            exclamationMarkCount < 0 ||
            wordsPerSecond is double rate &&
            (!double.IsFinite(rate) || rate < 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(transcriptCoveredDurationRatio));
        }

        TranscriptCoveredDurationRatio =
            transcriptCoveredDurationRatio;
        SegmentCount = segmentCount;
        WordCount = wordCount;
        WordsPerSecond = wordsPerSecond;
        CandidateBeginsInsideSegment =
            candidateBeginsInsideSegment;
        CandidateEndsInsideSegment =
            candidateEndsInsideSegment;
        CompleteSegmentCount = completeSegmentCount;
        QuestionMarkCount = questionMarkCount;
        ExclamationMarkCount = exclamationMarkCount;
        DetectedLanguageAvailable =
            detectedLanguageAvailable;
        OverlappingSpeechReported =
            overlappingSpeechReported;
        SilenceOnlyNeighborhood =
            silenceOnlyNeighborhood;
    }

    public double TranscriptCoveredDurationRatio { get; }

    public int SegmentCount { get; }

    public int WordCount { get; }

    public double? WordsPerSecond { get; }

    public bool CandidateBeginsInsideSegment { get; }

    public bool CandidateEndsInsideSegment { get; }

    public int CompleteSegmentCount { get; }

    public int QuestionMarkCount { get; }

    public int ExclamationMarkCount { get; }

    public bool DetectedLanguageAvailable { get; }

    public bool? OverlappingSpeechReported { get; }

    public bool? SilenceOnlyNeighborhood { get; }
}

public sealed class CandidateTranscriptBinding
{
    private readonly ReadOnlyCollection<AudioTranscriptionSegment>
        _segments;

    public CandidateTranscriptBinding(
        string candidateId,
        TimeSpan candidateStart,
        TimeSpan candidateEnd,
        string neighborhoodId,
        IEnumerable<AudioTranscriptionSegment> segments,
        CandidateTranscriptBoundaryStatus boundaryStatus,
        CandidateTranscriptFeatures features)
    {
        if (string.IsNullOrWhiteSpace(candidateId) ||
            string.IsNullOrWhiteSpace(neighborhoodId) ||
            candidateStart < TimeSpan.Zero ||
            candidateEnd <= candidateStart ||
            !Enum.IsDefined(boundaryStatus))
        {
            throw new ArgumentException(
                "Candidate transcript bindings require valid identities, bounds, and boundary status.");
        }

        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(features);

        AudioTranscriptionSegment[] snapshot =
            segments.ToArray();

        if (snapshot.Any(static segment => segment is null) ||
            snapshot.Any(
                segment =>
                    !string.Equals(
                        segment.NeighborhoodId,
                        neighborhoodId,
                        StringComparison.Ordinal) ||
                    segment.AbsoluteSourceEnd <=
                        candidateStart ||
                    segment.AbsoluteSourceStart >=
                        candidateEnd))
        {
            throw new ArgumentException(
                "Bound transcript segments must intersect the candidate and neighborhood.",
                nameof(segments));
        }

        CandidateId = candidateId.Trim();
        CandidateStart = candidateStart;
        CandidateEnd = candidateEnd;
        NeighborhoodId = neighborhoodId.Trim();
        BoundaryStatus = boundaryStatus;
        Features = features;
        _segments = Array.AsReadOnly(snapshot);
    }

    public string CandidateId { get; }

    public TimeSpan CandidateStart { get; }

    public TimeSpan CandidateEnd { get; }

    public string NeighborhoodId { get; }

    public IReadOnlyList<AudioTranscriptionSegment> Segments =>
        _segments;

    public CandidateTranscriptBoundaryStatus BoundaryStatus { get; }

    public CandidateTranscriptFeatures Features { get; }
}

public sealed record NeighborhoodTranscriptionEnrichment
{
    public NeighborhoodTranscriptionEnrichment(
        CandidateNeighborhood neighborhood,
        AudioSegmentExtractionManifest extraction,
        AudioTranscriptionResult transcription)
    {
        ArgumentNullException.ThrowIfNull(neighborhood);
        ArgumentNullException.ThrowIfNull(extraction);
        ArgumentNullException.ThrowIfNull(transcription);

        if (!string.Equals(
                neighborhood.Id,
                transcription.NeighborhoodId,
                StringComparison.Ordinal) ||
            neighborhood.Start != extraction.Start ||
            neighborhood.End != extraction.End)
        {
            throw new ArgumentException(
                "Neighborhood extraction and transcription provenance must align.");
        }

        Neighborhood = neighborhood;
        Extraction = extraction;
        Transcription = transcription;
    }

    public CandidateNeighborhood Neighborhood { get; }

    public AudioSegmentExtractionManifest Extraction { get; }

    public AudioTranscriptionResult Transcription { get; }
}

public sealed class MomentEnrichmentManifest
{
    public MomentEnrichmentManifest(
        string policyVersion,
        InferenceProviderIdentity providerIdentity,
        ModelArtifactManifest model,
        int neighborhoodCount,
        int extractionCount,
        int transcriptionCount,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        TimeSpan totalElapsed)
    {
        if (string.IsNullOrWhiteSpace(policyVersion) ||
            neighborhoodCount < 0 ||
            extractionCount != neighborhoodCount ||
            transcriptionCount != neighborhoodCount ||
            startedAtUtc.Offset != TimeSpan.Zero ||
            completedAtUtc.Offset != TimeSpan.Zero ||
            completedAtUtc < startedAtUtc ||
            totalElapsed < TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The enrichment manifest contains invalid counts or timing.");
        }

        ArgumentNullException.ThrowIfNull(providerIdentity);
        ArgumentNullException.ThrowIfNull(model);

        PolicyVersion = policyVersion.Trim();
        ProviderIdentity = providerIdentity;
        Model = model;
        NeighborhoodCount = neighborhoodCount;
        ExtractionCount = extractionCount;
        TranscriptionCount = transcriptionCount;
        StartedAtUtc = startedAtUtc;
        CompletedAtUtc = completedAtUtc;
        TotalElapsed = totalElapsed;
    }

    public string PolicyVersion { get; }

    public InferenceProviderIdentity ProviderIdentity { get; }

    public ModelArtifactManifest Model { get; }

    public int NeighborhoodCount { get; }

    public int ExtractionCount { get; }

    public int TranscriptionCount { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset CompletedAtUtc { get; }

    public TimeSpan TotalElapsed { get; }
}

public sealed class MomentTranscriptionEnrichmentResult
{
    private readonly ReadOnlyCollection<NeighborhoodTranscriptionEnrichment>
        _neighborhoodResults;
    private readonly ReadOnlyCollection<CandidateTranscriptBinding>
        _candidateBindings;
    private readonly ReadOnlyCollection<MomentEnrichmentWarning>
        _warnings;

    public MomentTranscriptionEnrichmentResult(
        MomentEnrichmentRequest request,
        CandidateNeighborhoodPlan plan,
        IEnumerable<NeighborhoodTranscriptionEnrichment> neighborhoodResults,
        IEnumerable<CandidateTranscriptBinding> candidateBindings,
        MomentEnrichmentManifest manifest,
        IEnumerable<MomentEnrichmentWarning>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(neighborhoodResults);
        ArgumentNullException.ThrowIfNull(candidateBindings);
        ArgumentNullException.ThrowIfNull(manifest);

        NeighborhoodTranscriptionEnrichment[] resultSnapshot =
            neighborhoodResults.ToArray();
        CandidateTranscriptBinding[] bindingSnapshot =
            candidateBindings
                .OrderBy(
                    binding =>
                        request.Candidates.Single(
                            candidate =>
                                string.Equals(
                                    candidate.CandidateId,
                                    binding.CandidateId,
                                    StringComparison.Ordinal))
                            .SourceOrder)
                .ToArray();
        MomentEnrichmentWarning[] warningSnapshot =
            warnings?.ToArray() ??
            [];

        if (!ReferenceEquals(plan.Request, request) ||
            resultSnapshot.Length !=
                plan.Neighborhoods.Count ||
            resultSnapshot.Any(static item => item is null) ||
            bindingSnapshot.Any(static item => item is null) ||
            warningSnapshot.Any(static item => item is null) ||
            !resultSnapshot.Select(
                    static item =>
                        item.Neighborhood.Id)
                .SequenceEqual(
                    plan.Neighborhoods.Select(
                        static item =>
                            item.Id),
                    StringComparer.Ordinal) ||
            bindingSnapshot
                .Select(static item => item.CandidateId)
                .Distinct(StringComparer.Ordinal)
                .Count() != bindingSnapshot.Length ||
            manifest.NeighborhoodCount !=
                plan.Neighborhoods.Count)
        {
            throw new ArgumentException(
                "The enrichment result must completely match its request and neighborhood plan.");
        }

        Request = request;
        Plan = plan;
        Manifest = manifest;
        _neighborhoodResults =
            Array.AsReadOnly(resultSnapshot);
        _candidateBindings =
            Array.AsReadOnly(bindingSnapshot);
        _warnings =
            Array.AsReadOnly(warningSnapshot);
    }

    public MomentEnrichmentRequest Request { get; }

    public CandidateNeighborhoodPlan Plan { get; }

    public IReadOnlyList<NeighborhoodTranscriptionEnrichment>
        NeighborhoodResults =>
        _neighborhoodResults;

    public IReadOnlyList<CandidateTranscriptBinding>
        CandidateBindings =>
        _candidateBindings;

    public MomentEnrichmentManifest Manifest { get; }

    public IReadOnlyList<MomentEnrichmentWarning> Warnings =>
        _warnings;
}

public enum MomentEnrichmentProgressPhase
{
    PlanningNeighborhoods,
    ExtractingAudio,
    TranscribingAudio,
    BindingCandidates,
    Complete,
}

public sealed record MomentEnrichmentProgressUpdate(
    MomentEnrichmentProgressPhase Phase,
    string Detail,
    int NeighborhoodNumber,
    int NeighborhoodCount);

public sealed class MomentEnrichmentException : Exception
{
    public MomentEnrichmentException(
        string message,
        string? neighborhoodId,
        Exception innerException)
        : base(message, innerException)
    {
        NeighborhoodId =
            string.IsNullOrWhiteSpace(neighborhoodId)
                ? null
                : neighborhoodId.Trim();
    }

    public string? NeighborhoodId { get; }
}
