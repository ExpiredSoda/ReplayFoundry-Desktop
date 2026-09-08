using System.Collections.ObjectModel;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.Moments;
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
        int maximumCandidateCount = GenerationSemanticReviewBudgetPolicy.MaximumCandidates)
    {
        Prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        Model = model ?? throw new ArgumentNullException(nameof(model));
        VideoPolicy = videoPolicy ??
            throw new ArgumentNullException(nameof(videoPolicy));
        if (maximumCandidateCount is < 1 or > GenerationSemanticReviewBudgetPolicy.MaximumCandidates ||
            !string.Equals(
                prompt.Version,
                VisualSemanticPromptManifest.QualifiedEditorialVersion,
                StringComparison.Ordinal) && prompt.Version != VisualSemanticPromptManifest.GroundedSceneVersion)
        {
            throw new ArgumentException(
                "Thorough visual review requires the qualified prompt and a budget of one to thirty-two candidates in bounded batches.");
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

public enum GenerationVisualSemanticOutcome
{
    Completed,
    RetainedDeterministicCandidates,
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
        TimeSpan elapsed,
        double? neuralEditorialValue = null)
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
            elapsed < TimeSpan.Zero || neuralEditorialValue.HasValue && (!double.IsFinite(neuralEditorialValue.Value) || neuralEditorialValue is < 0 or > 1))
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
        NeuralEditorialValue = neuralEditorialValue;
    }

    public MomentCandidate Candidate { get; }
    public AnalyzedGenerationSource Source { get; }
    public TimeSpan ReviewedSourceStart { get; }
    public TimeSpan ReviewedSourceEnd { get; }
    public string ReviewVideoSha256 { get; }
    public VisualSemanticEditorialObservation Observation { get; }
    public VisualSemanticEditorialCanonicalizationAudit CanonicalizationAudit { get; }
    public TimeSpan Elapsed { get; }
    public double? NeuralEditorialValue { get; }
}

public sealed class GenerationVisualSemanticAnalysisResult : IDisposable
{
    private readonly ReadOnlyCollection<GenerationVisualSemanticCandidateObservation>
        _observations;
    private readonly IReadOnlyDictionary<
        string,
        MaterializedVisualSemanticReviewVideo> _reviewVideos;
    private bool _disposed;
    private GenerationVisualSemanticAnalysisResult[] _ownedReviews = [];

    public GenerationVisualSemanticAnalysisResult(
        GenerationCandidateIntelligenceResult candidateIntelligence,
        InferenceProviderIdentity provider,
        IEnumerable<GenerationVisualSemanticCandidateObservation> observations,
        TimeSpan elapsed,
        long? peakAllocatedGpuBytes,
        IEnumerable<MaterializedVisualSemanticReviewVideo>? reviewVideos = null,
        GenerationVisualSemanticOutcome outcome =
            GenerationVisualSemanticOutcome.Completed,
        string? fallbackReason = null,
        string? diagnosticDetails = null)
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
        if (!Enum.IsDefined(outcome) ||
            snapshot.Length > GenerationSemanticReviewBudgetPolicy.MaximumCandidates ||
            outcome == GenerationVisualSemanticOutcome.Completed &&
                snapshot.Length == 0 ||
            outcome == GenerationVisualSemanticOutcome
                    .RetainedDeterministicCandidates &&
                (snapshot.Length != 0 || reviewSnapshot.Length != 0 ||
                 string.IsNullOrWhiteSpace(fallbackReason)) ||
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
        Outcome = outcome;
        FallbackReason = string.IsNullOrWhiteSpace(fallbackReason)
            ? null
            : fallbackReason.Trim();
        DiagnosticDetails = string.IsNullOrWhiteSpace(diagnosticDetails)
            ? null
            : diagnosticDetails.Trim();
    }

    public GenerationCandidateIntelligenceResult CandidateIntelligence { get; }
    public InferenceProviderIdentity Provider { get; }
    public IReadOnlyList<GenerationVisualSemanticCandidateObservation> Observations =>
        _observations;
    public TimeSpan Elapsed { get; }
    public long? PeakAllocatedGpuBytes { get; }
    public GenerationVisualSemanticOutcome Outcome { get; }
    public bool NeedsReview => Outcome ==
        GenerationVisualSemanticOutcome.RetainedDeterministicCandidates || FallbackReason is not null;
    public string? FallbackReason { get; }
    public string? DiagnosticDetails { get; }
    internal bool SupplementalReviewAttempted { get; private set; }

    internal static GenerationVisualSemanticAnalysisResult Combine(
        GenerationVisualSemanticAnalysisResult previous, GenerationVisualSemanticAnalysisResult supplemental)
    {
        if (previous._disposed || supplemental._disposed ||
            !ReferenceEquals(previous.CandidateIntelligence, supplemental.CandidateIntelligence) ||
            previous.Outcome != GenerationVisualSemanticOutcome.Completed ||
            previous.SupplementalReviewAttempted || supplemental.Observations.Count > GenerationSemanticReviewBudgetPolicy.MaximumBatchSize)
            throw new ArgumentException("Supplemental review must extend one live initial review from the same intelligence.");
        var combined = new GenerationVisualSemanticAnalysisResult(previous.CandidateIntelligence, previous.Provider,
            previous.Observations.Concat(supplemental.Observations), previous.Elapsed + supplemental.Elapsed,
            previous.PeakAllocatedGpuBytes is null && supplemental.PeakAllocatedGpuBytes is null ? null :
                Math.Max(previous.PeakAllocatedGpuBytes ?? 0, supplemental.PeakAllocatedGpuBytes ?? 0),
            fallbackReason: previous.NeedsReview || supplemental.NeedsReview
                ? "Some picture checks could not finish. Automatic selection uses the successfully reviewed candidates only."
                : null,
            diagnosticDetails: string.Join(Environment.NewLine, new[] { previous.DiagnosticDetails, supplemental.DiagnosticDetails }
                .Where(value => !string.IsNullOrWhiteSpace(value))));
        combined._ownedReviews = [previous, supplemental];
        combined.SupplementalReviewAttempted = true;
        return combined;
    }

    internal VisualSemanticInputManifest? FindReviewVideo(
        string candidateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        if (_disposed) return null;
        if (_reviewVideos.TryGetValue(candidateId, out var video)) return video.Input;
        return _ownedReviews.Select(review => review.FindReviewVideo(candidateId)).FirstOrDefault(input => input is not null);
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
        foreach (GenerationVisualSemanticAnalysisResult review in _ownedReviews.Reverse()) review.Dispose();
    }
}

public interface IGenerationVisualSemanticAnalysisService
{
    Task<GenerationCandidateIntelligenceResult> IndexRecordingAsync(GenerationCandidateIntelligenceResult intelligence,
        IProgress<string>? progress, CancellationToken cancellationToken) => Task.FromResult(intelligence);

    Task<GenerationVisualSemanticAnalysisResult> AnalyzeAsync(
        GenerationCandidateIntelligenceResult candidateIntelligence,
        IProgress<GenerationVisualSemanticProgress>? progress,
        CancellationToken cancellationToken);

    Task<GenerationVisualSemanticAnalysisResult> ReviewPromotedAsync(
        GenerationCandidateIntelligenceResult baseline,
        IReadOnlyList<GenerationMomentCandidate> selected,
        GenerationVisualSemanticAnalysisResult previous,
        IProgress<GenerationVisualSemanticProgress>? progress,
        CancellationToken cancellationToken) => Task.FromResult(previous);
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
