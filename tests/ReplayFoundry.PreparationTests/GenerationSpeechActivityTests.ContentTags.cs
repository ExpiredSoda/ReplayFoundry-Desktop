using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationSpeechActivityTests
{
    private static Task ContentTagsRequireGroundedReview()
    {
        var request = CreateRequest(GenerationAnalysisDepth.Thorough, [("content-tags.mkv", 1)], desiredCount: 1);
        var intelligence = CreateCandidateIntelligence(request, [90]);
        var candidate = intelligence.Refinements.Single().Candidate;
        var humor = CreateVisualObservation(VisualSemanticEditorialDisposition.Keep,
            VisualSemanticEditorialRejectReason.None, VisualSemanticTernary.Yes, "An unexpected comic reaction is visible.",
            VisualSemanticObservableContentType.Humor);
        ClipPreferenceFeature[] speech = [new(ClipPreferenceFeatureCode.CreatorSpeech, .6)];
        var complete = GenerationMomentContentClassifier.Classify(candidate, speech, Reviewed(intelligence, candidate, humor));
        TestAssert.True(complete.Funny && !complete.Commentary && complete.VisualReviewCompleted,
            "Visual humor can be labelled, but an audio track assignment cannot establish creator commentary.");
        var partial = GenerationMomentContentClassifier.Classify(candidate, speech, Reviewed(intelligence, candidate, humor, partial: true));
        TestAssert.True(!partial.Commentary && !partial.Funny && !partial.VisualReviewCompleted,
            "A partial visual review cannot classify the whole moment as funny.");
        var action = CreateVisualObservation(VisualSemanticEditorialDisposition.Keep, VisualSemanticEditorialRejectReason.None,
            VisualSemanticTernary.Yes, "A bounded action event is visible.");
        TestAssert.True(GenerationMomentContentClassifier.Classify(candidate, [], Reviewed(intelligence, candidate, action)).Gameplay,
            "Complete action evidence can expose gameplay moments independently of the requested discovery intent.");
        TestAssert.False(GenerationMomentContentClassifier.Classify(candidate, [], null).HasLabels,
            "An unreviewed high-scoring proposal must not acquire guessed labels.");
        return Task.CompletedTask;
    }
}
