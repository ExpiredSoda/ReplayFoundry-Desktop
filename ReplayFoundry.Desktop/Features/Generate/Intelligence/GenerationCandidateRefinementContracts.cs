using System.Collections.ObjectModel;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

public enum GenerationCandidateRefinementComponentCode
{
    SpeechCoverage,
    UserConfirmedCreatorSpeech,
    UserConfirmedGameDialogue,
    UnknownSpeechActivity,
    VisualSemanticSupport,
    VisualSemanticEditorialPenalty,
    CorrelatedVisualSupportPenalty,
    PersonalPreference,
}

public sealed record GenerationCandidateRefinementComponent
{
    private readonly ReadOnlyCollection<string> _evidenceReferences;

    public GenerationCandidateRefinementComponent(
        GenerationCandidateRefinementComponentCode code,
        double rawValue,
        double weight,
        string explanation,
        IEnumerable<string>? evidenceReferences = null)
    {
        if (!Enum.IsDefined(code) ||
            !double.IsFinite(rawValue) ||
            !double.IsFinite(weight) ||
            rawValue is < 0 or > 1 ||
            weight is < -100 or > 100 ||
            string.IsNullOrWhiteSpace(explanation))
        {
            throw new ArgumentException(
                "Candidate refinement components must be finite, bounded, typed, and explained.");
        }

        string[] references = evidenceReferences?.ToArray() ?? [];
        if (references.Any(string.IsNullOrWhiteSpace) ||
            references.Distinct(StringComparer.Ordinal).Count() != references.Length)
        {
            throw new ArgumentException(
                "Candidate refinement evidence references must be nonblank and unique.",
                nameof(evidenceReferences));
        }

        Code = code;
        RawValue = rawValue;
        Weight = weight;
        SignedContribution = rawValue * weight;
        Explanation = explanation.Trim();
        _evidenceReferences = Array.AsReadOnly(references);
    }

    public GenerationCandidateRefinementComponentCode Code { get; }
    public double RawValue { get; }
    public double Weight { get; }
    public double SignedContribution { get; }
    public string Explanation { get; }
    public IReadOnlyList<string> EvidenceReferences => _evidenceReferences;
}

public sealed class GenerationCandidateRefinement
{
    private readonly ReadOnlyCollection<GenerationCandidateRefinementComponent>
        _components;

    public GenerationCandidateRefinement(
        MomentCandidate candidate,
        IEnumerable<GenerationCandidateRefinementComponent> components,
        string policyVersion)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(components);
        if (string.IsNullOrWhiteSpace(policyVersion))
        {
            throw new ArgumentException(
                "Candidate refinement requires a policy version.",
                nameof(policyVersion));
        }

        GenerationCandidateRefinementComponent[] snapshot = components.ToArray();
        if (snapshot.Any(static item => item is null) ||
            snapshot.Select(static item => item.Code).Distinct().Count() != snapshot.Length)
        {
            throw new ArgumentException(
                "Candidate refinement components must be nonnull and uniquely typed.",
                nameof(components));
        }

        Candidate = candidate;
        BaseScore = candidate.HeuristicScore;
        _components = Array.AsReadOnly(snapshot);
        UnclampedScore = BaseScore + snapshot.Sum(static item => item.SignedContribution);
        FinalScore = Math.Clamp(UnclampedScore, 0, 100);
        PolicyVersion = policyVersion.Trim();
    }

    public MomentCandidate Candidate { get; }
    public double BaseScore { get; }
    public IReadOnlyList<GenerationCandidateRefinementComponent> Components =>
        _components;
    public double UnclampedScore { get; }
    public double FinalScore { get; }
    public string PolicyVersion { get; }
}

public sealed class GenerationCandidateIntelligenceResult
{
    private readonly ReadOnlyCollection<GenerationCandidateRefinement>
        _refinements;

    public GenerationCandidateIntelligenceResult(
        GenerationMomentFindingResult baseMoments,
        GenerationSpeechActivityResult speechActivity,
        IEnumerable<GenerationCandidateRefinement> refinements,
        GenerationMomentFindingResult refinedMoments,
        GenerationVisualSemanticAnalysisResult? visualSemantic = null)
    {
        ArgumentNullException.ThrowIfNull(baseMoments);
        ArgumentNullException.ThrowIfNull(speechActivity);
        ArgumentNullException.ThrowIfNull(refinements);
        ArgumentNullException.ThrowIfNull(refinedMoments);
        GenerationCandidateRefinement[] snapshot = refinements.ToArray();
        MomentCandidate[] proposals = baseMoments.Sources
            .SelectMany(static source => source.Moments.Proposals)
            .ToArray();
        if (!ReferenceEquals(baseMoments.Request.EvidenceAnalysis, speechActivity.Request.EvidenceAnalysis) ||
            !ReferenceEquals(baseMoments.Request.Setup, speechActivity.Request.SetupOptions) ||
            !ReferenceEquals(baseMoments.Request, refinedMoments.Request) ||
            visualSemantic is not null &&
            !ReferenceEquals(visualSemantic.CandidateIntelligence.BaseMoments, baseMoments) ||
            snapshot.Length != proposals.Length ||
            snapshot.Select(static item => item.Candidate).Distinct(ReferenceEqualityComparer.Instance).Count() != snapshot.Length ||
            proposals.Any(proposal => !snapshot.Any(item => ReferenceEquals(item.Candidate, proposal))))
        {
            throw new ArgumentException(
                "Candidate intelligence must refine every proposal from one coherent request exactly once.");
        }

        BaseMoments = baseMoments;
        SpeechActivity = speechActivity;
        _refinements = Array.AsReadOnly(snapshot);
        RefinedMoments = refinedMoments;
        VisualSemantic = visualSemantic;
    }

    public GenerationMomentFindingResult BaseMoments { get; }
    public GenerationSpeechActivityResult SpeechActivity { get; }
    public IReadOnlyList<GenerationCandidateRefinement> Refinements =>
        _refinements;
    public GenerationMomentFindingResult RefinedMoments { get; }
    public GenerationVisualSemanticAnalysisResult? VisualSemantic { get; }
}
