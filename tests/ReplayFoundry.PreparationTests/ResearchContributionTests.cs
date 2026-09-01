using ReplayFoundry.Desktop.Features.Research;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    private static Task ResearchContributionDefaultsOff()
    {
        var participation = new ResearchParticipationState(
            new InMemoryResearchParticipationStore());
        var store = new InMemoryResearchFeedbackStore();
        var recorder = new ResearchFeedbackRecorder(participation, store);

        recorder.Record(
            "candidate-1",
            @"C:\Private\Source.mkv",
            TimeSpan.FromMinutes(4),
            CreateResearchFeatures(),
            ResearchFeedbackChannel.Satisfaction,
            ResearchFeedbackValue.Like);

        TestAssert.False(
            participation.IsEnabled,
            "Research participation must be opt-out by default.");
        TestAssert.Equal(
            0,
            store.Current.Count,
            "No contribution record may be prepared before explicit consent.");
        return Task.CompletedTask;
    }

    private static Task ResearchContributionIsPseudonymous()
    {
        var participation = new ResearchParticipationState(
            new InMemoryResearchParticipationStore());
        participation.Enable(DateTimeOffset.UtcNow);
        var store = new InMemoryResearchFeedbackStore();
        var recorder = new ResearchFeedbackRecorder(participation, store);
        const string path = @"C:\Private\Ghostwire\stream.mkv";

        recorder.Record(
            "candidate-1",
            path,
            TimeSpan.FromMinutes(4),
            CreateResearchFeatures(),
            ResearchFeedbackChannel.HiddenMomentReview,
            ResearchFeedbackValue.Accepted);
        recorder.Record(
            "candidate-1",
            path,
            TimeSpan.FromMinutes(4),
            CreateResearchFeatures(),
            ResearchFeedbackChannel.Satisfaction,
            ResearchFeedbackValue.Like);

        TestAssert.Equal(
            2,
            store.Current.Count,
            "Hidden-moment discovery and satisfaction must remain distinct feedback layers.");
        TestAssert.True(
            store.Current.All(record =>
                record.CandidateIdentity.Length == 64 &&
                record.SourceIdentity.Length == 64 &&
                !record.SourceIdentity.Contains("Ghostwire", StringComparison.OrdinalIgnoreCase)),
            "Contribution records must retain only pseudonymous identities.");
        TestAssert.True(
            store.Current.All(record =>
                record.Features is not List<ClipPreferenceFeature>),
            "Contribution feature snapshots must be immutable.");
        return Task.CompletedTask;
    }

    private static Task ResearchContributionPersistsAndDeletes()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundry-ResearchTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string consentPath = Path.Combine(root, "consent.json");
            string feedbackPath = Path.Combine(root, "feedback.json");
            var consent = new ResearchParticipationState(
                new JsonResearchParticipationStore(consentPath));
            consent.Enable(DateTimeOffset.UtcNow);
            var feedback = new JsonResearchFeedbackStore(feedbackPath);
            new ResearchFeedbackRecorder(consent, feedback).Record(
                "candidate-2",
                @"D:\Media\source.mkv",
                TimeSpan.FromMinutes(5),
                CreateResearchFeatures(),
                ResearchFeedbackChannel.StudioSelection,
                ResearchFeedbackValue.Excluded);

            TestAssert.True(
                new JsonResearchParticipationStore(consentPath)
                    .Current.IsEnabled,
                "Explicit consent should survive a local reload.");
            TestAssert.Equal(
                1,
                new JsonResearchFeedbackStore(feedbackPath).Current.Count,
                "Eligible feedback should survive a local reload.");

            feedback.Clear();
            TestAssert.Equal(
                0,
                new JsonResearchFeedbackStore(feedbackPath).Current.Count,
                "The user must be able to delete all prepared contribution data.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
        return Task.CompletedTask;
    }

    private static ClipPreferenceFeatureVector CreateResearchFeatures() =>
        new(
        [
            new ClipPreferenceFeature(
                ClipPreferenceFeatureCode.Duration,
                0.5),
            new ClipPreferenceFeature(
                ClipPreferenceFeatureCode.DeterministicScore,
                0.75),
        ]);
}
