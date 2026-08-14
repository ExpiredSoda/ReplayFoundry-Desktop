using System.Collections.ObjectModel;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

public sealed class GenerationVisualSemanticSettings
{
    public GenerationVisualSemanticSettings(
        VisualSemanticPromptManifest prompt,
        VisualSemanticModelManifest model,
        VisualSemanticVideoInputPolicy videoPolicy,
        int maximumCandidateCount = 8)
    {
        Prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        Model = model ?? throw new ArgumentNullException(nameof(model));
        VideoPolicy = videoPolicy ??
            throw new ArgumentNullException(nameof(videoPolicy));
        if (maximumCandidateCount is < 1 or > 8 ||
            !string.Equals(
                prompt.Version,
                VisualSemanticPromptManifest.QualifiedEditorialVersion,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Thorough visual review requires the frozen qualified prompt and one to eight candidates.");
        }

        MaximumCandidateCount = maximumCandidateCount;
    }

    public VisualSemanticPromptManifest Prompt { get; }

    public VisualSemanticModelManifest Model { get; }

    public VisualSemanticVideoInputPolicy VideoPolicy { get; }

    public int MaximumCandidateCount { get; }
}

public enum GenerationVisualSemanticPhase
{
    PreparingReviewVideo,
    ReviewingCandidates,
    FinishingVisualReview,
    Completed,
}

public sealed record GenerationVisualSemanticProgress
{
    public GenerationVisualSemanticProgress(
        GenerationVisualSemanticPhase phase,
        string title,
        string detail,
        int completedCases,
        int totalCases,
        bool isIndeterminate,
        double? overallPercentage = null)
    {
        if (!Enum.IsDefined(phase) ||
            string.IsNullOrWhiteSpace(title) ||
            string.IsNullOrWhiteSpace(detail) ||
            totalCases <= 0 ||
            completedCases < 0 ||
            completedCases > totalCases ||
            overallPercentage is < 0 or > 100)
        {
            throw new ArgumentException(
                "Visual-review progress must be typed, bounded, and truthful.");
        }

        Phase = phase;
        Title = title.Trim();
        Detail = detail.Trim();
        CompletedCases = completedCases;
        TotalCases = totalCases;
        IsIndeterminate = isIndeterminate;
        OverallPercentage = overallPercentage;
    }

    public GenerationVisualSemanticPhase Phase { get; }
    public string Title { get; }
    public string Detail { get; }
    public int CompletedCases { get; }
    public int TotalCases { get; }
    public bool IsIndeterminate { get; }
    public double? OverallPercentage { get; }
}

public sealed class GenerationVisualSemanticCandidateObservation
{
    public GenerationVisualSemanticCandidateObservation(
        MomentCandidate candidate,
        AnalyzedGenerationSource source,
        TimeSpan reviewedSourceStart,
        TimeSpan reviewedSourceEnd,
        string reviewVideoSha256,
        VisualSemanticEditorialObservation observation,
        VisualSemanticEditorialCanonicalizationAudit canonicalizationAudit,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(canonicalizationAudit);
        if (!source.Evidence.FullPath.Equals(
                source.PreparedSource.Media.FullPath,
                StringComparison.OrdinalIgnoreCase) ||
            reviewedSourceStart < TimeSpan.Zero ||
            reviewedSourceEnd <= reviewedSourceStart ||
            reviewedSourceEnd > source.PreparedSource.Media.Duration ||
            elapsed < TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A visual observation requires one bounded candidate/source identity.");
        }

        Candidate = candidate;
        Source = source;
        ReviewedSourceStart = reviewedSourceStart;
        ReviewedSourceEnd = reviewedSourceEnd;
        ReviewVideoSha256 = ModelArtifactManifest.Sha256Value(
            reviewVideoSha256,
            nameof(reviewVideoSha256));
        Observation = observation;
        CanonicalizationAudit = canonicalizationAudit;
        Elapsed = elapsed;
    }

    public MomentCandidate Candidate { get; }
    public AnalyzedGenerationSource Source { get; }
    public TimeSpan ReviewedSourceStart { get; }
    public TimeSpan ReviewedSourceEnd { get; }
    public string ReviewVideoSha256 { get; }
    public VisualSemanticEditorialObservation Observation { get; }
    public VisualSemanticEditorialCanonicalizationAudit CanonicalizationAudit { get; }
    public TimeSpan Elapsed { get; }
}

public sealed class GenerationVisualSemanticAnalysisResult : IDisposable
{
    private readonly ReadOnlyCollection<GenerationVisualSemanticCandidateObservation>
        _observations;
    private readonly IReadOnlyDictionary<
        string,
        MaterializedVisualSemanticReviewVideo> _reviewVideos;
    private bool _disposed;

    public GenerationVisualSemanticAnalysisResult(
        GenerationCandidateIntelligenceResult candidateIntelligence,
        InferenceProviderIdentity provider,
        IEnumerable<GenerationVisualSemanticCandidateObservation> observations,
        TimeSpan elapsed,
        long? peakAllocatedGpuBytes,
        IEnumerable<MaterializedVisualSemanticReviewVideo>? reviewVideos = null)
    {
        ArgumentNullException.ThrowIfNull(candidateIntelligence);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(observations);
        GenerationVisualSemanticCandidateObservation[] snapshot =
            observations.ToArray();
        MaterializedVisualSemanticReviewVideo[] reviewSnapshot =
            (reviewVideos ?? []).ToArray();
        MomentCandidate[] proposals = candidateIntelligence.BaseMoments.Sources
            .SelectMany(static source => source.Moments.Proposals)
            .ToArray();
        if (snapshot.Length is < 1 or > 8 ||
            snapshot.Any(static value => value is null) ||
            snapshot.Select(static value => value.Candidate)
                .Distinct(ReferenceEqualityComparer.Instance).Count() != snapshot.Length ||
            snapshot.Any(value => !proposals.Any(
                proposal => ReferenceEquals(proposal, value.Candidate))) ||
            reviewSnapshot.Any(static value => value is null) ||
            reviewSnapshot.Select(static value => value.Request.CandidateId)
                .Distinct(StringComparer.Ordinal).Count() !=
                reviewSnapshot.Length ||
            reviewSnapshot.Any(video => !snapshot.Any(observation =>
                observation.Candidate.Id.Equals(
                    video.Request.CandidateId,
                    StringComparison.Ordinal) &&
                observation.ReviewVideoSha256.Equals(
                    video.Input.ReviewVideoSha256,
                    StringComparison.Ordinal))) ||
            elapsed < TimeSpan.Zero ||
            peakAllocatedGpuBytes < 0)
        {
            throw new ArgumentException(
                "Visual-semantic analysis must preserve a unique bounded shortlist from the retained proposals.",
                nameof(observations));
        }

        CandidateIntelligence = candidateIntelligence;
        Provider = provider;
        _observations = Array.AsReadOnly(snapshot);
        _reviewVideos = reviewSnapshot.ToDictionary(
            static value => value.Request.CandidateId,
            StringComparer.Ordinal);
        Elapsed = elapsed;
        PeakAllocatedGpuBytes = peakAllocatedGpuBytes;
    }

    public GenerationCandidateIntelligenceResult CandidateIntelligence { get; }
    public InferenceProviderIdentity Provider { get; }
    public IReadOnlyList<GenerationVisualSemanticCandidateObservation> Observations =>
        _observations;
    public TimeSpan Elapsed { get; }
    public long? PeakAllocatedGpuBytes { get; }

    internal VisualSemanticInputManifest? FindReviewVideo(
        string candidateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        return !_disposed &&
               _reviewVideos.TryGetValue(candidateId, out var video)
            ? video.Input
            : null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (MaterializedVisualSemanticReviewVideo video in
                 _reviewVideos.Values.Reverse())
        {
            video.Dispose();
        }
    }
}

public interface IGenerationVisualSemanticAnalysisService
{
    Task<GenerationVisualSemanticAnalysisResult> AnalyzeAsync(
        GenerationCandidateIntelligenceResult candidateIntelligence,
        IProgress<GenerationVisualSemanticProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed class GenerationVisualSemanticAnalysisException : Exception
{
    public GenerationVisualSemanticAnalysisException(
        string message,
        string? diagnosticDetails = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        DiagnosticDetails = diagnosticDetails;
    }

    public string? DiagnosticDetails { get; }

    public override string ToString()
    {
        string standard = base.ToString();
        return string.IsNullOrWhiteSpace(DiagnosticDetails)
            ? standard
            : $"{standard}{Environment.NewLine}{Environment.NewLine}" +
              $"Provider diagnostics:{Environment.NewLine}{DiagnosticDetails}";
    }
}
