using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Intelligence.Learning;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationSpeechActivityTests
{
    private static IEnumerable<TestCase> MomentDiscernmentTests()
    {
        yield return new("Moment discernment preserves the requested type through neural selection", NeuralSelectionRetainsRequestedType);
        yield return new("Moment discernment never promotes uncertain or rejected type matches", NeuralTypePreferenceRequiresCompleteEvidence);
        yield return new("Moment discernment preserves quality and default choices", NeuralTypePreferencePreservesQuality);
        yield return new("Moment discernment keeps action coverage out of explicit story requests", ExplicitStoriesDoNotAcquireAnActionQuota);
        yield return new("Moment discernment retains lore-only speech nominations within their source window", LoreSpeechNominationsRemainOwned);
        yield return new("Moment discernment preserves current intent through personal ranking", PersonalRankingRetainsCurrentIntent);
    }

    private static Task NeuralSelectionRetainsRequestedType()
    {
        foreach (var (intent, type) in new[]
        {
            (GenerationMomentIntent.Humor, VisualSemanticObservableContentType.Humor),
            (GenerationMomentIntent.Story, VisualSemanticObservableContentType.Story),
            (GenerationMomentIntent.Action, VisualSemanticObservableContentType.Action),
            (GenerationMomentIntent.Dialogue, VisualSemanticObservableContentType.Dialogue),
            (GenerationMomentIntent.Discovery, VisualSemanticObservableContentType.Discovery),
            (GenerationMomentIntent.Failure, VisualSemanticObservableContentType.Failure),
        })
        {
            var fixture = FinalQueryFixture(intent: new(intent), scores: [80, 82], reviewFactory: (baseline, candidate, index) =>
                Reviewed(baseline, candidate, TypedKeep(index == 0 ? type : VisualSemanticObservableContentType.Other),
                    neuralValue: index == 0 ? .80 : .82));
            using var visual = fixture.Visual;
            var first = fixture.Intelligence.Refinements[0];
            TestAssert.Same(fixture.Intelligence.Refinements[1].Candidate, fixture.Intelligence.RefinedMoments.SelectedCandidates.Single().Candidate,
                "Before applying the objective, the unrelated candidate leads on the model's score.");
            var result = GenerationReviewedSelectionPolicy.Apply(fixture.Intelligence, CancellationToken.None);
            TestAssert.True(ReferenceEquals(first.Candidate, result.RefinedMoments.SelectedCandidates.Single().Candidate),
                $"A complete {intent} match must influence the final selection after the neural score replaces detector bonuses.");
            TestAssert.Equal(80d, first.FinalScore, "Creator intent must not inflate model quality or its threshold.");
            TestAssert.Equal(80d, first.RankingScore, "Model ranking evidence remains separate from selection preference.");
            TestAssert.True(result.RefinedMoments.SelectionPreferences.ContainsKey(first.Candidate),
                "Subsequent screening must retain the explicit selection preference.");
        }
        return Task.CompletedTask;
    }

    private static Task NeuralTypePreferenceRequiresCompleteEvidence()
    {
        foreach (string condition in new[] { "partial", "uncertain", "unreliable", "reject", "missing-payoff", "foreign" })
        {
            var fixture = FinalQueryFixture(intent: new(GenerationMomentIntent.Humor), scores: [80, 82],
                reviewFactory: (baseline, candidate, index) =>
                {
                    if (index != 0) return Reviewed(baseline, candidate, TypedKeep(VisualSemanticObservableContentType.Other), neuralValue: .82);
                    var original = TypedKeep(VisualSemanticObservableContentType.Humor);
                    var observation = new VisualSemanticEditorialObservation(original.ObservableContentType, original.HasDistinctEvent,
                        condition == "missing-payoff" ? VisualSemanticTernary.No : original.HasObservablePayoff,
                        original.RoutineTraversalOrMenuOnly, original.CandidateRequiresMissingContext, original.CandidateContainsOnlyAmbientChange,
                        condition == "unreliable" ? VisualSemanticTranscriptContextSupport.UnreliableOrAmbiguous : original.TranscriptContextSupport,
                        original.ObservedChanges, original.EvidenceIntervals,
                        condition == "uncertain" ? [new(VisualSemanticEditorialUncertaintyCode.TranscriptMayBeInaccurate, "Uncertain punchline.")] : [],
                        condition == "reject" ? VisualSemanticEditorialDisposition.Reject : original.EditorialDisposition,
                        condition == "reject" ? VisualSemanticEditorialRejectReason.RoutineTraversal : original.RejectReason,
                        "A classification cannot prove a self-contained joke.");
                    var reviewed = Reviewed(baseline, candidate, observation, partial: condition == "partial", neuralValue: .80);
                    if (condition != "foreign") return reviewed;
                    var foreign = CreateCandidateIntelligence(CreateRequest(GenerationAnalysisDepth.Thorough,
                        [("final-query.mkv", 1)], sourceDuration: TimeSpan.FromMinutes(5), desiredCount: 1), [80]);
                    return new(candidate, foreign.BaseMoments.Sources.Single().AnalyzedSource,
                        reviewed.ReviewedSourceStart, reviewed.ReviewedSourceEnd, reviewed.ReviewVideoSha256,
                        observation, reviewed.CanonicalizationAudit, reviewed.Elapsed, .80);
                });
            using var visual = fixture.Visual;
            var result = GenerationReviewedSelectionPolicy.Apply(fixture.Intelligence, CancellationToken.None);
            TestAssert.False(result.RefinedMoments.SelectionPreferences.ContainsKey(fixture.Intelligence.Refinements[0].Candidate),
                $"The {condition} joke must not earn a category preference.");
            TestAssert.True(ReferenceEquals(fixture.Intelligence.Refinements[1].Candidate,
                result.RefinedMoments.SelectedCandidates.Single().Candidate), "The qualified alternative remains selectable.");
        }
        return Task.CompletedTask;
    }

    private static Task NeuralTypePreferencePreservesQuality()
    {
        foreach (var (intent, firstScore, secondScore) in new[]
        {
            (GenerationMomentIntent.Any, 80d, 82d),
            (GenerationMomentIntent.Humor, 80d, 90d),
            (GenerationMomentIntent.Humor, 60d, 64d),
            (GenerationMomentIntent.Clutch, 80d, 82d),
        })
        {
            var fixture = FinalQueryFixture(intent: new(intent), scores: [firstScore, secondScore],
                reviewFactory: (baseline, candidate, index) => Reviewed(baseline, candidate,
                    TypedKeep(index == 0 ? VisualSemanticObservableContentType.Humor : VisualSemanticObservableContentType.Other),
                    neuralValue: (index == 0 ? firstScore : secondScore) / 100));
            using var visual = fixture.Visual;
            var result = GenerationReviewedSelectionPolicy.Apply(fixture.Intelligence, CancellationToken.None);
            TestAssert.True(ReferenceEquals(fixture.Intelligence.Refinements[1].Candidate,
                result.RefinedMoments.SelectedCandidates.Single().Candidate),
                "Intent must preserve defaults, large quality differences, thresholds, and unverified subtype requests.");
        }
        return Task.CompletedTask;
    }

    private static Task ExplicitStoriesDoNotAcquireAnActionQuota()
    {
        var fixture = FinalQueryFixture(intent: new(GenerationMomentIntent.Story), scores: [90, 89, 88, 91],
            similarities: [0, 0, 0, 0], desired: 3, reviewFactory: (baseline, candidate, index) =>
                Reviewed(baseline, candidate, TypedKeep(index == 3 ? VisualSemanticObservableContentType.Action : VisualSemanticObservableContentType.Story)));
        using var visual = fixture.Visual;
        var result = GenerationReviewedSelectionPolicy.Apply(fixture.Intelligence, CancellationToken.None);
        TestAssert.Equal(3, result.RefinedMoments.SelectedCandidates.Count, "Three qualified stories are available.");
        TestAssert.False(result.RefinedMoments.SelectedCandidates.Any(item =>
            ReferenceEquals(item.Candidate, fixture.Intelligence.Refinements[3].Candidate)),
            "Balanced gameplay coverage must not replace an explicit story with a generic action clip.");
        return Task.CompletedTask;
    }

    private static Task LoreSpeechNominationsRemainOwned()
    {
        var transcript = new GenerationSourceTranscript("lore.mkv", 1,
            [SentenceSegment("setup", "source", "The archive explains why the city fell.", 10, 15),
             SentenceSegment("reveal", "source", "Its defenders opened the gates themselves.", 16, 21),
             SentenceSegment("elsewhere", "source", "A separate event much later.", 60, 65)], []);
        JsonElement Window(bool lore, bool funny, int[] ids) => JsonSerializer.SerializeToElement(new
        { start = 0, end = 45, prediction = new { lore, funny, commentary = false, speechMomentIds = ids } });
        var seeds = GenerationRecordingIndexService.SpeechSeeds([Window(true, false, [0, 1]), Window(true, false, [0, 1])], transcript);
        TestAssert.Equal(1, seeds.Count, "A lore reveal can nominate speech without creator commentary or humor; duplicate ranges collapse.");
        TestAssert.Equal(TimeSpan.FromSeconds(10), seeds[0].Start, "The spoken setup must be retained.");
        TestAssert.Equal(TimeSpan.FromSeconds(21), seeds[0].End, "The spoken payoff must be retained.");
        TestAssert.Equal(0, GenerationRecordingIndexService.SpeechSeeds([Window(true, false, [])], transcript).Count,
            "A coarse lore label alone must not invent a timed speech event.");
        foreach (int[] ids in new[] { new[] { 0, 2 }, new[] { 0, 99 }, new[] { -1, 1 } })
            TestAssert.Equal(0, GenerationRecordingIndexService.SpeechSeeds([Window(false, true, ids)], transcript).Count,
                "Invalid or foreign-window speech cannot be silently dropped to produce an incomplete nomination.");
        return Task.CompletedTask;
    }

    private static VisualSemanticEditorialObservation TypedKeep(VisualSemanticObservableContentType type) =>
        CreateVisualObservation(VisualSemanticEditorialDisposition.Keep, VisualSemanticEditorialRejectReason.None,
            VisualSemanticTernary.Yes, "The supplied cut contains the complete event and its payoff.", type);

    private static async Task PersonalRankingRetainsCurrentIntent()
    {
        foreach (var (intent, firstPrediction, expectedFirst) in new[]
        {
            (GenerationMomentIntent.Humor, .6, true),
            (GenerationMomentIntent.Any, .6, false),
            (GenerationMomentIntent.Humor, 0d, false),
        })
        {
            var fixture = FinalQueryFixture(intent: new(intent), scores: [80, 82],
                reviewFactory: (baseline, candidate, index) => Reviewed(baseline, candidate,
                    TypedKeep(index == 0 ? VisualSemanticObservableContentType.Humor : VisualSemanticObservableContentType.Other),
                    neuralValue: index == 0 ? .80 : .82));
            using var visual = fixture.Visual;
            var result = GenerationReviewedSelectionPolicy.Apply(fixture.Intelligence, CancellationToken.None);
            var selected = await new GenerationTasteRanking(new IntentPredictionFixture(firstPrediction))
                .ApplyAsync(result.RefinedMoments, result, CancellationToken.None);
            TestAssert.True(ReferenceEquals(fixture.Intelligence.Refinements[expectedFirst ? 0 : 1].Candidate,
                selected.SelectedCandidates.Single().Candidate),
                "Personal ranking must honor the current verified type preference while retaining default behavior and current quality gates.");
        }
    }

    private sealed class IntentPredictionFixture(double firstPreference) : ITasteLearningService
    {
        public TasteLearningStatus Status => new(true, false, true, 0, 0, 0, "Injected predictions, not a trained model.");
        public event EventHandler? Changed { add { } remove { } }
        public void Observe(TasteClip clip, TasteSignal? signal) { }
        public Task<IReadOnlyDictionary<string, TastePrediction>> PredictAsync(IReadOnlyList<TasteClip> clips, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, TastePrediction>>(clips.Select((clip, index) => (clip.Id,
                Prediction: new TastePrediction(true, index == 0 ? firstPreference : .64, 0, "intent-fixture")))
                .ToDictionary(item => item.Id, item => item.Prediction));
        public Task TrainAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void SetEnabled(bool enabled) { }
        public Task ResetAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
