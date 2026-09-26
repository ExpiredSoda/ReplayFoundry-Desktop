using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Guidance;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationSpeechActivityTests
{
    // Measured gameplay-region ratios for the development recording's
    // 197–235-second chapter-menu/shader-preparation window.
    private const double RecordedSetupBlackRatio = 0.6508684210526315;
    private const double RecordedSetupFreezeRatio = 0.8543947368421053;

    private static Task UnavailableGameplayExcludesCountFill()
    {
        var request = CreateRequest(GenerationAnalysisDepth.Thorough, [("unavailable-gameplay.mkv", 1)], desiredCount: 2);
        var moments = WithFirstCandidateIntegrity(CreateMoments(request, [100, 78]),
            RecordedSetupBlackRatio, RecordedSetupFreezeRatio);
        var intelligence = new GenerationCandidateRefinementService().Refine(moments,
            CreateSpeech(request, new AudioContentRoleAssignment(AudioContentRole.CreatorSpeech, AudioContentRoleSource.UserConfirmed),
                TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(25)));
        var first = intelligence.Refinements.First();
        using var visual = new GenerationVisualSemanticAnalysisResult(intelligence,
            new InferenceProviderIdentity("fake-review", "1", "1"),
            [Reviewed(intelligence, first.Candidate, NoPayoffObservation())], TimeSpan.Zero, null);
        var result = new GenerationCandidateRefinementService().ApplyVisualSemantic(intelligence, visual);
        var rejected = result.Refinements.Single(value => ReferenceEquals(value.Candidate, first.Candidate));
        TestAssert.True(rejected.HasGroundedVisualRejection,
            "Predominantly black, nearly static gameplay plus a complete no-payoff review must exclude default automatic gaming clips even with creator speech.");
        TestAssert.True(rejected.FinalScore >= request.SetupOptions.QualityThreshold,
            "The fixture must show that the eligibility gate matters while the score remains above the quality threshold.");
        TestAssert.Equal(1, result.RefinedMoments.SelectedCandidates.Count,
            "Neither normal selection nor count filling may resurrect corroborated unavailable gameplay.");
        var hidden = GenerationHiddenMomentPlanner.Create(result.RefinedMoments, result).Moments.Single();
        TestAssert.Equal(GenerationHiddenMomentReason.GroundedVisualRejection, hidden.Reason,
            "The excluded window must retain a typed manual-review route.");
        TestAssert.True(hidden.Explanation.Contains("predominantly black", StringComparison.Ordinal) &&
            !hidden.Explanation.Contains("OCR", StringComparison.OrdinalIgnoreCase),
            "The explanation must identify the real combined evidence without claiming OCR recognized a loading label.");
        return Task.CompletedTask;
    }

    private static Task UnavailableGameplayRequiresJointEvidence()
    {
        var request = CreateRequest(GenerationAnalysisDepth.Thorough, [("joint-unavailable-evidence.mkv", 1)], desiredCount: 1);
        var original = CreateMoments(request, [100]);
        foreach ((double black, double freeze, bool excluded) in new[]
        {
            (RecordedSetupBlackRatio, RecordedSetupFreezeRatio, true),
            (.5, .8, true),
            (.499, RecordedSetupFreezeRatio, false),
            (RecordedSetupBlackRatio, .799, false),
            (0.0, 1.0, false),
            (1.0, 0.0, false),
            (0.0, 0.0, false),
        })
        {
            var moments = WithFirstCandidateIntegrity(original, black, freeze);
            var quiet = new GenerationCandidateRefinementService().Refine(moments, CreateSpeech(request, AudioContentRoleAssignment.Unknown, []));
            var candidate = quiet.Refinements.Single().Candidate;
            TestAssert.Equal(excluded, GenerationGroundedVisualRejectionPolicy.HasCorroboratedUnavailableGameplay(
                Reviewed(quiet, candidate, NoPayoffObservation()), request.SetupOptions),
                $"Joint signal policy for black={black}, freeze={freeze}.");
        }

        var complete = new GenerationCandidateRefinementService().Refine(
            WithFirstCandidateIntegrity(original, RecordedSetupBlackRatio, RecordedSetupFreezeRatio),
            CreateSpeech(request, AudioContentRoleAssignment.Unknown, []));
        var reviewedCandidate = complete.Refinements.Single().Candidate;
        TestAssert.False(GenerationGroundedVisualRejectionPolicy.HasCorroboratedUnavailableGameplay(
            Reviewed(complete, reviewedCandidate, NoPayoffObservation(), partial: true), request.SetupOptions),
            "A partial observation cannot exclude the whole candidate.");
        foreach (var observation in new[]
        {
            NoPayoffObservation(uncertain: true),
            NoPayoffObservation(withEvidence: false),
            NoPayoffObservation(disposition: VisualSemanticEditorialDisposition.Unsure),
            NoPayoffObservation(disposition: VisualSemanticEditorialDisposition.Keep),
            NoPayoffObservation(payoff: VisualSemanticTernary.Unsure),
            NoPayoffObservation(payoff: VisualSemanticTernary.Yes),
        })
            TestAssert.False(GenerationGroundedVisualRejectionPolicy.HasCorroboratedUnavailableGameplay(
                Reviewed(complete, reviewedCandidate, observation), request.SetupOptions),
                "Uncertainty, missing evidence, a Keep, or a payoff cannot be upgraded into a combined rejection.");

        var assumedComposition = new GenerationCompositionReviewResult(new(request.Preparation),
            [new PreparedSourceCompositionPlan(request.ReferencePreparedSource,
                ManualCompositionPlanFactory.CreateFullFrameGameplay(request.ReferenceSource.FullPath,
                    request.ReferencePreparedSource.Media.Duration, DateTimeOffset.UnixEpoch))]);
        var assumedRequest = PreparedGenerationWorkflowTests.CreateGenerationRequest(request.Preparation, request.SetupOptions, assumedComposition);
        var assumed = new GenerationCandidateRefinementService().Refine(
            WithFirstCandidateIntegrity(CreateMoments(assumedRequest, [100]), RecordedSetupBlackRatio, RecordedSetupFreezeRatio),
            CreateSpeech(assumedRequest, AudioContentRoleAssignment.Unknown, []));
        TestAssert.False(GenerationGroundedVisualRejectionPolicy.HasCorroboratedUnavailableGameplay(
            Reviewed(assumed, assumed.Refinements.Single().Candidate, NoPayoffObservation()), assumedRequest.SetupOptions),
            "Black/freeze evidence in an assumed gameplay region cannot establish unavailable gameplay.");
        return Task.CompletedTask;
    }

    private static Task UnavailableGameplayPreservesExplicitIntent()
    {
        const string fileName = "requested-unavailable-gameplay.mkv";
        var request = CreateRequest(GenerationAnalysisDepth.Thorough, [(fileName, 1)], desiredCount: 1);
        var intelligence = new GenerationCandidateRefinementService().Refine(
            WithFirstCandidateIntegrity(CreateMoments(request, [100]), RecordedSetupBlackRatio, RecordedSetupFreezeRatio),
            CreateSpeech(request, AudioContentRoleAssignment.Unknown, []));
        var candidate = intelligence.Refinements.Single().Candidate;
        var observation = Reviewed(intelligence, candidate, NoPayoffObservation());
        foreach (var intent in new[]
        {
            new GenerationDiscoveryIntent(GenerationMomentIntent.Story),
            new GenerationDiscoveryIntent(GenerationMomentIntent.Discovery),
            new GenerationDiscoveryIntent(GenerationMomentIntent.Dialogue),
            new GenerationDiscoveryIntent(GenerationMomentIntent.Humor),
            new GenerationDiscoveryIntent(GenerationMomentIntent.Any, "compiling shaders"),
            new GenerationDiscoveryIntent(GenerationMomentIntent.Action, "first launch"),
        })
        {
            var explicitRequest = CreateRequest(GenerationAnalysisDepth.Thorough, [(fileName, 1)], discoveryIntent: intent);
            TestAssert.False(GenerationGroundedVisualRejectionPolicy.HasCorroboratedUnavailableGameplay(observation, explicitRequest.SetupOptions),
                "Explicit spoken or narrative objectives must preserve creator-requested material.");
        }
        var commentary = CreateRequest(GenerationAnalysisDepth.Thorough, [(fileName, 1)], contentEmphasis: ContentEmphasis.CommentaryFocused);
        TestAssert.False(GenerationGroundedVisualRejectionPolicy.HasCorroboratedUnavailableGameplay(observation, commentary.SetupOptions),
            "Explicit creator-commentary emphasis must remain authoritative.");
        foreach (var intent in new[] { GenerationMomentIntent.Any, GenerationMomentIntent.Action, GenerationMomentIntent.Failure })
        {
            var gameplay = CreateRequest(GenerationAnalysisDepth.Thorough, [(fileName, 1)], discoveryIntent: new(intent));
            TestAssert.True(GenerationGroundedVisualRejectionPolicy.HasCorroboratedUnavailableGameplay(observation, gameplay.SetupOptions),
                "The bounded gate applies to unqualified default gaming, action, and failure objectives.");
        }
        foreach (var guidance in new[]
        {
            UserMomentGuidance.CreatePoint(request.ReferenceSource.FullPath, request.ReferencePreparedSource.Media.Duration, candidate.Window.Start + TimeSpan.FromSeconds(1)),
            UserMomentGuidance.CreateRange(request.ReferenceSource.FullPath, request.ReferencePreparedSource.Media.Duration, candidate.Window.Start, candidate.Window.End),
        })
        {
            var guided = CreateRequest(GenerationAnalysisDepth.Thorough, [(fileName, 1)], momentGuidance: new([guidance]));
            TestAssert.False(GenerationGroundedVisualRejectionPolicy.HasCorroboratedUnavailableGameplay(observation, guided.SetupOptions),
                "An explicit marker or range must protect the chosen interval.");
        }
        return Task.CompletedTask;
    }

    private static VisualSemanticEditorialObservation NoPayoffObservation(
        bool uncertain = false, bool withEvidence = true,
        VisualSemanticEditorialDisposition disposition = VisualSemanticEditorialDisposition.Reject,
        VisualSemanticTernary payoff = VisualSemanticTernary.No) =>
        new(VisualSemanticObservableContentType.Action, VisualSemanticTernary.Yes,
            disposition == VisualSemanticEditorialDisposition.Keep ? VisualSemanticTernary.Yes : payoff,
            VisualSemanticTernary.No, VisualSemanticTernary.No, VisualSemanticTernary.No,
            VisualSemanticTranscriptContextSupport.Supports,
            withEvidence ? [new("A visual evidence point supports the observation.", VisualSemanticEvidenceBasis.Visual, ["e0"])] : [],
            withEvidence ? [new("e0", TimeSpan.Zero, TimeSpan.Zero, "A visual evidence point supports the observation.", VisualSemanticEvidenceBasis.Visual)] : [],
            uncertain ? [new(VisualSemanticEditorialUncertaintyCode.InsufficientVisualEvidence, "The sampled picture is ambiguous.")] : [],
            disposition, disposition == VisualSemanticEditorialDisposition.Keep ? VisualSemanticEditorialRejectReason.None : VisualSemanticEditorialRejectReason.NoObservablePayoff,
            "The observation records whether a payoff is visible.");

    private static GenerationMomentFindingResult WithFirstCandidateIntegrity(
        GenerationMomentFindingResult original, double black, double freeze)
    {
        var source = original.Sources.Single();
        var first = source.Moments.Proposals[0];
        var replacement = new MomentCandidate(first.Id, first.Window, first.ConstructionReason, first.EventNeighborhood,
            first.Anchors, first.Score, first.Disposition, first.FullFrameBlackOverlapRatio, first.FullFrameFreezeOverlapRatio,
            black, freeze, first.IntegrityEvidenceReferences, first.Episode, first.ContextAllocation,
            first.MontageObjective, first.MontageSelectionReason, first.EpisodeFeatures, first.StandaloneFeatures, first.MontageFeatures);
        MomentCandidate Replace(MomentCandidate candidate) => ReferenceEquals(candidate, first) ? replacement : candidate;
        var mediaMoments = new MediaMomentFindingResult(source.Moments.Request, source.Moments.Proposals.Select(Replace),
            source.Moments.SelectedCandidates.Select(Replace), source.Moments.Warnings, source.Moments.Manifest,
            source.Moments.ActivationSeries, source.Moments.Episodes);
        var replacedSource = new GenerationSourceMomentResult(source.AnalyzedSource, mediaMoments);
        return new(original.Request, [replacedSource], new GenerationMomentPortfolioSelector().Select(original.Request, [replacedSource]));
    }
}
