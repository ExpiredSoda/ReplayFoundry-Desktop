using ReplayFoundry.Desktop.Features.Generate.Guidance;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Learning;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationSpeechActivityTests
{
    private static async Task FinalRejectionBlocksSelection()
    {
        foreach (bool explicitlyRequested in new[] { false, true })
        {
            var guidance = explicitlyRequested ? new GenerationMomentGuidance([
                UserMomentGuidance.CreateRange(TestMediaFactory.CreateSourcePath("final-query.mkv"), TimeSpan.FromMinutes(5),
                    TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(40))]) : null;
            var fixture = FinalQueryFixture(scores: [98, 80], desired: 2, guidance: guidance,
                reviewFactory: (baseline, candidate, index) => Reviewed(baseline, candidate, index == 0
                    ? CreateVisualObservation(VisualSemanticEditorialDisposition.Reject,
                        VisualSemanticEditorialRejectReason.NoObservablePayoff, VisualSemanticTernary.No,
                        "No observable payoff is established in the candidate.") : QueryKeep()));
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
