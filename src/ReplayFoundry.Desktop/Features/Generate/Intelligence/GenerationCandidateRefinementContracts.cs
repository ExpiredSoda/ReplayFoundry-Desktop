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
    IncompleteSpeechEnding,
    VisualSemanticSupport,
    VisualSemanticEditorialPenalty,
    VisualSemanticActionEvidence,
    CorrelatedVisualSupportPenalty,
    PersonalPreference,
    IncompleteSpeechBeginning,
    GroundedVisualRejection,
    SemanticDiscoveryEvidence,
    SpokenIntentMatch,
    CreatorIntentMatch,
    CaptureContextPenalty,
    NonGameplayCapture,
    SemanticRetrievalRelevance,
    ApplicationStartupLeadIn,
    NeuralIndexCoverage,
    NeuralGameplay,
    NeuralHumor,
    NeuralCommentary,
    NeuralMenu,
    GameIdentityConflict,
    NeuralTimelineValue,
    NeuralSceneValue,
    NeuralPersonalValue,
    NeuralReviewPriority,
    NeuralRegionCoverage,
    NeuralLore,
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
        double refinementContribution = snapshot.Sum(
            static item => item.SignedContribution);
        _components = Array.AsReadOnly(snapshot);
        UnclampedScore = BaseScore + refinementContribution;
        FinalScore = Math.Clamp(UnclampedScore, 0, 100);
        RankingScore = candidate.Score.RawComponentTotal +
            refinementContribution;
        var neuralValue = snapshot.FirstOrDefault(item => item.Code == GenerationCandidateRefinementComponentCode.NeuralPersonalValue)
            ?? snapshot.FirstOrDefault(item => item.Code == GenerationCandidateRefinementComponentCode.NeuralSceneValue)
            ?? snapshot.FirstOrDefault(item => item.Code == GenerationCandidateRefinementComponentCode.NeuralTimelineValue);
        if (neuralValue is not null)
        {
            // This is the model's prediction expressed on the existing 0–100 UI scale.
            // Category labels and old detector penalties do not rewrite a neural judgment.
            UnclampedScore = FinalScore = RankingScore = neuralValue.RawValue * 100;
        }
        PolicyVersion = policyVersion.Trim();
    }

    public MomentCandidate Candidate { get; }
    public double BaseScore { get; }
    public IReadOnlyList<GenerationCandidateRefinementComponent> Components =>
        _components;
    public double UnclampedScore { get; }
    public double FinalScore { get; }
    public double RankingScore { get; }
    public string PolicyVersion { get; }

    public bool HasIncompleteSpeechEnding => _components.Any(
        static component =>
            component.Code ==
                GenerationCandidateRefinementComponentCode
                    .IncompleteSpeechEnding &&
            component.RawValue > 0);

    public bool HasIncompleteSpeechBeginning => _components.Any(
        static component => component.Code ==
            GenerationCandidateRefinementComponentCode.IncompleteSpeechBeginning &&
            component.RawValue > 0);

    public bool HasGroundedVisualRejection => _components.Any(
        static component => component.Code ==
            GenerationCandidateRefinementComponentCode.GroundedVisualRejection &&
            component.RawValue > 0);

    public bool HasNonGameplayCapture => _components.Any(static component =>
        component.Code == GenerationCandidateRefinementComponentCode.NonGameplayCapture && component.RawValue > 0);

    public bool HasNeuralSceneValue => _components.Any(item => item.Code == GenerationCandidateRefinementComponentCode.NeuralSceneValue);

    public bool HasApplicationStartupLeadIn => _components.Any(static component =>
        component.Code == GenerationCandidateRefinementComponentCode.ApplicationStartupLeadIn && component.RawValue > 0);

    public bool RequiresSemanticReview =>
        Candidate.ConstructionReason == MomentCandidateConstructionReason.SemanticExploration &&
        !_components.Any(static component => component.Code ==
            GenerationCandidateRefinementComponentCode.SemanticDiscoveryEvidence &&
            component.RawValue > 0);
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
        GenerationVisualSemanticAnalysisResult? visualSemantic = null,
        GenerationTranscriptAnalysisResult? transcripts = null)
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
            transcripts is not null &&
            (!ReferenceEquals(transcripts.ExpandedMoments.Request, baseMoments.Request) ||
                transcripts.Sources.Select(static source => source.SourceFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != transcripts.Sources.Count ||
                transcripts.Sources.Any(source => !baseMoments.Sources.Any(moment =>
                    moment.AnalyzedSource.PreparedSource.Media.FullPath.Equals(source.SourceFullPath, StringComparison.OrdinalIgnoreCase) &&
                    moment.AnalyzedSource.PreparedSource.Media.AudioStreams.Any(stream => stream.Index == source.AudioStreamIndex)))) ||
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
        Transcripts = transcripts;
    }

    public GenerationMomentFindingResult BaseMoments { get; }
    public GenerationSpeechActivityResult SpeechActivity { get; }
    public IReadOnlyList<GenerationCandidateRefinement> Refinements =>
        _refinements;
    public GenerationMomentFindingResult RefinedMoments { get; }
    public GenerationVisualSemanticAnalysisResult? VisualSemantic { get; }
    public GenerationTranscriptAnalysisResult? Transcripts { get; }

    public GenerationCandidateIntelligenceResult WithTranscripts(GenerationTranscriptAnalysisResult transcripts) =>
        GenerationDiscoveryIntentPolicy.ApplyTranscriptIntent(this, transcripts);
}
