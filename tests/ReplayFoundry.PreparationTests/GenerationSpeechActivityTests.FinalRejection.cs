using ReplayFoundry.Desktop.Features.Generate.Guidance;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Learning;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationSpeechActivityTests
{
    private static async Task FinalRejectionBlocksSelection()
    {
        foreach (bool explicitlyRequested in new[] { false, true })
        foreach (double? neuralValue in new double?[] { null, .9, .1 })
        {
            var guidance = explicitlyRequested ? new GenerationMomentGuidance([
                UserMomentGuidance.CreateRange(TestMediaFactory.CreateSourcePath("final-query.mkv"), TimeSpan.FromMinutes(5),
                    TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(40))]) : null;
            var fixture = FinalQueryFixture(scores: [98, 80], desired: 2, guidance: guidance,
                reviewFactory: (baseline, candidate, index) => Reviewed(baseline, candidate, index == 0 && neuralValue != .1
                    ? CreateVisualObservation(VisualSemanticEditorialDisposition.Reject,
                        VisualSemanticEditorialRejectReason.NoObservablePayoff, VisualSemanticTernary.No,
                        "No observable payoff is established in the candidate.") : QueryKeep(), neuralValue: index == 0 ? neuralValue : .9));
            using var visual = fixture.Visual;
            var result = GenerationReviewedSelectionPolicy.Apply(fixture.Intelligence, CancellationToken.None);
            var rejected = fixture.Intelligence.Refinements[0].Candidate;
            TestAssert.Equal(explicitlyRequested, result.RefinedMoments.SelectedCandidates.Any(x => ReferenceEquals(x.Candidate, rejected)),
                "A completed AI rejection must remain available only when the user explicitly requested that range.");
            var personalized = await new GenerationTasteRanking(new FavorEveryCandidate()).ApplyAsync(result.RefinedMoments, result, CancellationToken.None);
            TestAssert.Equal(explicitlyRequested, personalized.SelectedCandidates.Any(x => ReferenceEquals(x.Candidate, rejected)),
                "Neural re-ranking cannot undo the final review decision.");
        }
    }

    private static async Task LaterScreeningCannotReopenUnreviewedCandidates()
    {
        var fixture = FinalQueryFixture(intent: GenerationDiscoveryIntent.Default,
            scores: [98, 80, 99], similarities: [.1, .1, .1], desired: 3,
            reviewFactory: (baseline, candidate, index) => index == 2 ? null :
                Reviewed(baseline, candidate, QueryKeep(), neuralValue: index == 0 ? .1 : .9));
        using var visual = fixture.Visual;
        var reviewed = GenerationReviewedSelectionPolicy.Apply(fixture.Intelligence, CancellationToken.None);
        var expected = reviewed.RefinedMoments.SelectedCandidates.Single().Candidate;
        var screened = await new GenerationCaptureContextScreeningService(new StartupVisualText { GameplayOnly = true })
            .ScreenAsync(reviewed, null, CancellationToken.None);
        TestAssert.Same(expected, screened.RefinedMoments.SelectedCandidates.Single().Candidate,
            "Capture screening must preserve the picture-review boundary even when unchecked candidates have higher timing scores.");
        var personalized = await new GenerationTasteRanking(new FavorEveryCandidate())
            .ApplyAsync(screened.RefinedMoments, screened, CancellationToken.None);
        TestAssert.Same(expected, personalized.SelectedCandidates.Single().Candidate,
            "Personal ranking must retain the narrowed selection pool after capture screening.");

        var alternatives = FinalQueryFixture(intent: GenerationDiscoveryIntent.Default, scores: [98, 80], desired: 1,
            reviewFactory: (baseline, candidate, index) => Reviewed(baseline, candidate, QueryKeep(), neuralValue: index == 0 ? .9 : .8));
        using var alternativesVisual = alternatives.Visual;
        var original = alternatives.Intelligence;
        var mapped = original.Refinements.Select(item => new GenerationCandidateRefinement(item.Candidate,
            [.. item.Components, new(GenerationCandidateRefinementComponentCode.NeuralTimelineValue, .75, 0, "Fixture recording map.")],
            item.PolicyVersion)).ToDictionary(item => item.Candidate);
        var mappedMoments = new GenerationMomentFindingResult(original.RefinedMoments.Request, original.RefinedMoments.Sources,
            new GenerationMomentPortfolioSelector().Select(original.RefinedMoments.Request, original.RefinedMoments.Sources, mapped), mapped);
        var mappedIntelligence = new GenerationCandidateIntelligenceResult(original.BaseMoments, original.SpeechActivity,
            mapped.Values, mappedMoments, alternativesVisual, original.Transcripts);
        var approvedPool = GenerationReviewedSelectionPolicy.Apply(mappedIntelligence, CancellationToken.None);
        var afterScreen = await new GenerationCaptureContextScreeningService(new StartupVisualText { GameplayOnly = true })
            .ScreenAsync(approvedPool, null, CancellationToken.None);
        TestAssert.Equal(2, afterScreen.RefinedMoments.SelectionEligibleCandidates!.Count,
            "Candidates that already bypass OCR must remain available as reviewed alternatives.");
        string rejectedId = afterScreen.RefinedMoments.SelectedCandidates.Single().Id;
        var replacement = GenerationEditorialReplacementPolicy.RejectAutomaticCut(afterScreen, rejectedId, CancellationToken.None);
        TestAssert.True(replacement is not null && replacement.RefinedMoments.SelectedCandidates.Single().Id != rejectedId,
            "A later writing rejection can use the non-selected reviewed alternative without reopening the wider pool.");
    }

    private sealed class FavorEveryCandidate : ITasteLearningService
    {
        public TasteLearningStatus Status => new(true, false, true, 100, 100, 20, "Fixture");
        public event EventHandler? Changed { add { } remove { } }
        public void Observe(TasteClip clip, TasteSignal? signal) { }
        public Task<IReadOnlyDictionary<string, TastePrediction>> PredictAsync(IReadOnlyList<TasteClip> clips, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, TastePrediction>>(clips.ToDictionary(x => x.Id, _ => new TastePrediction(true, 1, 0, "fixture")));
        public Task TrainAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResetAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void SetEnabled(bool enabled) { }
    }
}
