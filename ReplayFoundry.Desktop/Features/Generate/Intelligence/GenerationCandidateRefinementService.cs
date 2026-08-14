using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;
using ReplayFoundry.Desktop.Media.Intelligence.SpeechActivity;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

public interface IGenerationCandidateRefinementService
{
    GenerationCandidateIntelligenceResult Refine(
        GenerationMomentFindingResult moments,
        GenerationSpeechActivityResult speechActivity,
        CancellationToken cancellationToken = default);

    GenerationCandidateIntelligenceResult ApplyVisualSemantic(
        GenerationCandidateIntelligenceResult candidateIntelligence,
        GenerationVisualSemanticAnalysisResult visualSemantic,
        CancellationToken cancellationToken = default);
}

public sealed class GenerationCandidateRefinementService :
    IGenerationCandidateRefinementService
{
    private const string PolicyVersion = "1.0";
    private const string VisualPolicyVersion = "1.1";
    private const string PreferencePolicyVersion = "1.2";
    private readonly GenerationMomentPortfolioSelector _portfolioSelector;
    private readonly IClipPreferenceProfileProvider? _preferenceProfiles;

    public GenerationCandidateRefinementService(
        GenerationMomentPortfolioSelector? portfolioSelector = null,
        IClipPreferenceProfileProvider? preferenceProfiles = null)
    {
        _portfolioSelector = portfolioSelector ??
            new GenerationMomentPortfolioSelector();
        _preferenceProfiles = preferenceProfiles;
    }

    public GenerationCandidateIntelligenceResult Refine(
        GenerationMomentFindingResult moments,
        GenerationSpeechActivityResult speechActivity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(moments);
        ArgumentNullException.ThrowIfNull(speechActivity);
        if (!ReferenceEquals(
                moments.Request.EvidenceAnalysis,
                speechActivity.Request.EvidenceAnalysis) ||
            !ReferenceEquals(
                moments.Request.Setup,
                speechActivity.Request.SetupOptions))
        {
            throw new ArgumentException(
                "Candidate refinement requires moments and speech activity from the same retained request.",
                nameof(speechActivity));
        }

        var refinements = new List<GenerationCandidateRefinement>();
        var byCandidate = new Dictionary<
            MomentCandidate,
            GenerationCandidateRefinement>(ReferenceEqualityComparer.Instance);

        foreach (GenerationSourceMomentResult source in moments.Sources)
        {
            GenerationSourceSpeechActivity sourceSpeech = speechActivity.Sources
                .Single(item => ReferenceEquals(item.Source, source.AnalyzedSource));
            foreach (MomentCandidate candidate in source.Moments.Proposals)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GenerationCandidateRefinement refinement = CreateRefinement(
                    candidate,
                    sourceSpeech,
                    moments.Request.Setup.ContentEmphasis);
                refinement = ApplyPreference(refinement);
                refinements.Add(refinement);
                byCandidate.Add(candidate, refinement);
            }
        }

        IReadOnlyList<GenerationMomentCandidate> selected =
            _portfolioSelector.Select(
                moments.Request,
                moments.Sources,
                byCandidate,
                cancellationToken);
        var refinedMoments = new GenerationMomentFindingResult(
            moments.Request,
            moments.Sources,
            selected);
        return new GenerationCandidateIntelligenceResult(
            moments,
            speechActivity,
            refinements,
            refinedMoments);
    }

    public GenerationCandidateIntelligenceResult ApplyVisualSemantic(
        GenerationCandidateIntelligenceResult candidateIntelligence,
        GenerationVisualSemanticAnalysisResult visualSemantic,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidateIntelligence);
        ArgumentNullException.ThrowIfNull(visualSemantic);
        if (!ReferenceEquals(
                visualSemantic.CandidateIntelligence,
                candidateIntelligence))
        {
            throw new ArgumentException(
                "Visual observations must describe the retained candidate-intelligence result.",
                nameof(visualSemantic));
        }

        var byCandidate = new Dictionary<MomentCandidate,
            GenerationCandidateRefinement>(ReferenceEqualityComparer.Instance);
        foreach (GenerationCandidateRefinement existing in
                 candidateIntelligence.Refinements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GenerationVisualSemanticCandidateObservation? reviewed =
                visualSemantic.Observations.SingleOrDefault(value =>
                    ReferenceEquals(value.Candidate, existing.Candidate));
            GenerationCandidateRefinement updated = reviewed is null
                ? existing
                : AddVisualComponents(
                    WithoutPreference(existing),
                    reviewed);
            updated = ApplyPreference(updated);
            byCandidate.Add(existing.Candidate, updated);
        }

        IReadOnlyList<GenerationMomentCandidate> selected =
            _portfolioSelector.Select(
                candidateIntelligence.BaseMoments.Request,
                candidateIntelligence.BaseMoments.Sources,
                byCandidate,
                cancellationToken);
        var refinedMoments = new GenerationMomentFindingResult(
            candidateIntelligence.BaseMoments.Request,
            candidateIntelligence.BaseMoments.Sources,
            selected);
        return new GenerationCandidateIntelligenceResult(
            candidateIntelligence.BaseMoments,
            candidateIntelligence.SpeechActivity,
            byCandidate.Values,
            refinedMoments,
            visualSemantic);
    }

    private static GenerationCandidateRefinement AddVisualComponents(
        GenerationCandidateRefinement existing,
        GenerationVisualSemanticCandidateObservation reviewed)
    {
        VisualSemanticEditorialObservation observation = reviewed.Observation;
        double support =
            TernarySupport(observation.HasDistinctEvent, 0.30, 0.12) +
            TernarySupport(observation.HasObservablePayoff, 0.25, 0.10) +
            InverseTernarySupport(observation.RoutineTraversalOrMenuOnly, 0.15, 0.05) +
            InverseTernarySupport(observation.CandidateRequiresMissingContext, 0.10, 0.03) +
            InverseTernarySupport(observation.CandidateContainsOnlyAmbientChange, 0.10, 0.03) +
            observation.EditorialDisposition switch
            {
                VisualSemanticEditorialDisposition.Keep => 0.10,
                VisualSemanticEditorialDisposition.Unsure => 0.05,
                VisualSemanticEditorialDisposition.Reject => 0,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(reviewed),
                    observation.EditorialDisposition,
                    "The visual-semantic editorial disposition is not supported."),
            };
        double penalty = observation.EditorialDisposition switch
        {
            VisualSemanticEditorialDisposition.Keep => 0,
            VisualSemanticEditorialDisposition.Unsure => 0.25,
            VisualSemanticEditorialDisposition.Reject => 1,
            _ => throw new ArgumentOutOfRangeException(
                nameof(reviewed),
                observation.EditorialDisposition,
                "The visual-semantic editorial disposition is not supported."),
        };
        string[] references = observation.EvidenceIntervals
            .Select(value =>
                $"qwen:{reviewed.Candidate.Id}:{value.Id}:{value.Start:c}")
            .ToArray();
        GenerationCandidateRefinementComponent[] components =
        [
            .. existing.Components,
            new GenerationCandidateRefinementComponent(
                GenerationCandidateRefinementComponentCode.VisualSemanticSupport,
                Math.Clamp(support, 0, 1),
                8,
                "Qualified visual observations support bounded distinct-event, payoff, context, and non-routine evidence; they do not assign the final rank.",
                references),
            new GenerationCandidateRefinementComponent(
                GenerationCandidateRefinementComponentCode.VisualSemanticEditorialPenalty,
                penalty,
                -7,
                "The deterministic refinement policy applies a bounded penalty when the qualified observation truth table reports Reject or Unsure.",
                references),
        ];
        return new GenerationCandidateRefinement(
            existing.Candidate,
            components,
            VisualPolicyVersion);
    }

    private static double TernarySupport(
        VisualSemanticTernary value,
        double yes,
        double unsure) => value switch
        {
            VisualSemanticTernary.Yes => yes,
            VisualSemanticTernary.Unsure => unsure,
            VisualSemanticTernary.No => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    private static double InverseTernarySupport(
        VisualSemanticTernary value,
        double no,
        double unsure) => value switch
        {
            VisualSemanticTernary.No => no,
            VisualSemanticTernary.Unsure => unsure,
            VisualSemanticTernary.Yes => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    private GenerationCandidateRefinement ApplyPreference(
        GenerationCandidateRefinement existing)
    {
        GenerationCandidateRefinement withoutPreference =
            WithoutPreference(existing);
        if (_preferenceProfiles?.Current is not ClipPreferenceProfile profile)
        {
            return withoutPreference;
        }
        ClipPreferenceEvaluation evaluation = profile.Evaluate(
            GenerationClipPreferenceFeatureExtractor.Create(
                existing.Candidate,
                withoutPreference));
        if (!evaluation.IsActive ||
            Math.Abs(evaluation.SignedContribution) < 0.000001)
        {
            return withoutPreference;
        }

        double maximum = ClipPreferenceProfile.MaximumAbsoluteContribution;
        var component = new GenerationCandidateRefinementComponent(
            GenerationCandidateRefinementComponentCode.PersonalPreference,
            Math.Abs(evaluation.SignedContribution) / maximum,
            Math.Sign(evaluation.SignedContribution) * maximum,
            evaluation.Explanation);
        return new GenerationCandidateRefinement(
            existing.Candidate,
            [.. withoutPreference.Components, component],
            PreferencePolicyVersion);
    }

    private static GenerationCandidateRefinement WithoutPreference(
        GenerationCandidateRefinement existing)
    {
        if (!existing.Components.Any(static value =>
                value.Code ==
                    GenerationCandidateRefinementComponentCode
                        .PersonalPreference))
        {
            return existing;
        }
        return new GenerationCandidateRefinement(
            existing.Candidate,
            existing.Components.Where(static value =>
                value.Code !=
                    GenerationCandidateRefinementComponentCode
                        .PersonalPreference),
            existing.PolicyVersion);
    }

    private static GenerationCandidateRefinement CreateRefinement(
        MomentCandidate candidate,
        GenerationSourceSpeechActivity speech,
        ContentEmphasis emphasis)
    {
        var allIntervals = new List<SpeechActivityInterval>();
        var creatorIntervals = new List<SpeechActivityInterval>();
        var gameIntervals = new List<SpeechActivityInterval>();
        var unknownIntervals = new List<SpeechActivityInterval>();
        var references = new Dictionary<AudioContentRole, List<string>>();

        foreach (GenerationSpeechStreamResult stream in speech.Streams)
        {
            foreach (SpeechActivityInterval interval in stream.Intervals.Where(
                         interval => Intersects(candidate.Window, interval)))
            {
                allIntervals.Add(interval);
                List<SpeechActivityInterval> roleIntervals = stream.Role.Role switch
                {
                    AudioContentRole.CreatorSpeech => creatorIntervals,
                    AudioContentRole.GameDialogue => gameIntervals,
                    _ => unknownIntervals,
                };
                roleIntervals.Add(interval);
                if (!references.TryGetValue(stream.Role.Role, out List<string>? roleReferences))
                {
                    roleReferences = [];
                    references.Add(stream.Role.Role, roleReferences);
                }
                roleReferences.Add(
                    $"vad:stream-{stream.AbsoluteAudioStreamIndex}:{interval.AbsoluteStart:c}-{interval.AbsoluteEnd:c}");
            }
        }

        double all = NormalizedCoverage(candidate.Window, allIntervals);
        double creator = NormalizedCoverage(candidate.Window, creatorIntervals);
        double game = NormalizedCoverage(candidate.Window, gameIntervals);
        double unknown = NormalizedCoverage(candidate.Window, unknownIntervals);
        (double creatorWeight, double gameWeight, double unknownWeight) =
            emphasis switch
            {
                ContentEmphasis.GameplayFocused => (-1d, 5d, 1d),
                ContentEmphasis.Balanced => (2.5d, 3d, 2d),
                ContentEmphasis.CommentaryFocused => (6d, 1d, 1.5d),
                _ => throw new ArgumentOutOfRangeException(nameof(emphasis)),
            };

        return new GenerationCandidateRefinement(
            candidate,
            [
                new GenerationCandidateRefinementComponent(
                    GenerationCandidateRefinementComponentCode.SpeechCoverage,
                    all,
                    2,
                    all > 0
                        ? "Speech is active during this moment, so it may work well as an alternate. Review the preview to decide."
                        : "No speech was detected during this moment.",
                    references.Values.SelectMany(static value => value).Distinct(StringComparer.Ordinal)),
                new GenerationCandidateRefinementComponent(
                    GenerationCandidateRefinementComponentCode.UserConfirmedCreatorSpeech,
                    creator,
                    creatorWeight,
                    "Creator-speech timing uses only the user's explicit audio-stream role selection.",
                    ReferencesFor(AudioContentRole.CreatorSpeech)),
                new GenerationCandidateRefinementComponent(
                    GenerationCandidateRefinementComponentCode.UserConfirmedGameDialogue,
                    game,
                    gameWeight,
                    "Game-dialogue timing uses only the user's explicit audio-stream role selection.",
                    ReferencesFor(AudioContentRole.GameDialogue)),
                new GenerationCandidateRefinementComponent(
                    GenerationCandidateRefinementComponentCode.UnknownSpeechActivity,
                    unknown,
                    unknownWeight,
                    "Speech from streams without a user-confirmed role contributes only neutral timing support.",
                    references.Where(static pair => pair.Key is not AudioContentRole.CreatorSpeech and not AudioContentRole.GameDialogue)
                        .SelectMany(static pair => pair.Value)
                        .Distinct(StringComparer.Ordinal)),
            ],
            PolicyVersion);

        IEnumerable<string> ReferencesFor(AudioContentRole role) =>
            references.TryGetValue(role, out List<string>? values)
                ? values.Distinct(StringComparer.Ordinal)
                : [];
    }

    private static bool Intersects(
        MomentCandidateWindow window,
        SpeechActivityInterval interval) =>
        interval.AbsoluteStart < window.End &&
        interval.AbsoluteEnd > window.Start;

    private static double NormalizedCoverage(
        MomentCandidateWindow window,
        IEnumerable<SpeechActivityInterval> intervals)
    {
        (TimeSpan Start, TimeSpan End)[] bounded = intervals
            .Select(interval => (
                interval.AbsoluteStart > window.Start ? interval.AbsoluteStart : window.Start,
                interval.AbsoluteEnd < window.End ? interval.AbsoluteEnd : window.End))
            .Where(static item => item.Item2 > item.Item1)
            .OrderBy(static item => item.Item1)
            .ThenBy(static item => item.Item2)
            .ToArray();
        if (bounded.Length == 0)
        {
            return 0;
        }

        long coveredTicks = 0;
        TimeSpan start = bounded[0].Start;
        TimeSpan end = bounded[0].End;
        for (int index = 1; index < bounded.Length; index++)
        {
            if (bounded[index].Start <= end)
            {
                if (bounded[index].End > end)
                {
                    end = bounded[index].End;
                }
                continue;
            }

            coveredTicks += (end - start).Ticks;
            start = bounded[index].Start;
            end = bounded[index].End;
        }
        coveredTicks += (end - start).Ticks;

        double occupancy = coveredTicks / (double)window.Duration.Ticks;
        return Math.Clamp(occupancy / 0.60, 0, 1);
    }
}
