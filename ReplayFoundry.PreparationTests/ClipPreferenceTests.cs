using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.PreparationTests;

internal static class ClipPreferenceTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Preference vectors snapshot neutral numeric features", VectorIsImmutable),
        new("Preference store replaces ratings using aggregate-only data", StoreReplacesRating),
        new("Preference profile activates only after balanced coverage", ProfileNeedsCoverage),
        new("Studio exposes Like Neutral and Dislike without semantic storage", StudioRecordsFeedback),
    ];

    private static Task VectorIsImmutable()
    {
        var source = new List<ClipPreferenceFeature>
        {
            new(ClipPreferenceFeatureCode.Duration, 0.25),
            new(ClipPreferenceFeatureCode.DeterministicScore, 0.8),
        };
        var vector = new ClipPreferenceFeatureVector(source);
        source.Clear();

        TestAssert.Equal(2, vector.Features.Count, "Feature snapshot.");
        TestAssert.Throws<NotSupportedException>(
            () => ((IList<ClipPreferenceFeature>)vector.Features).Clear(),
            "Preference vectors must be read-only.");
        return Task.CompletedTask;
    }

    private static Task StoreReplacesRating()
    {
        string root = CreateRoot();
        string path = Path.Combine(root, "preferences.json");
        try
        {
            var store = new JsonClipPreferenceFeedbackStore(path);
            ClipPreferenceFeatureVector vector = CreateVector(0.2, 0.8);
            store.Update(vector, previous: null, ClipPreferenceRating.Like);
            ClipPreferenceProfile profile = store.Update(
                vector,
                ClipPreferenceRating.Like,
                ClipPreferenceRating.Dislike);

            TestAssert.Equal(0, profile.LikeCount, "Old Like removed.");
            TestAssert.Equal(1, profile.DislikeCount, "New Dislike added.");
            string json = File.ReadAllText(path);
            TestAssert.False(
                json.Contains("game", StringComparison.OrdinalIgnoreCase) ||
                json.Contains("source", StringComparison.OrdinalIgnoreCase) ||
                json.Contains("title", StringComparison.OrdinalIgnoreCase) ||
                json.Contains("transcript", StringComparison.OrdinalIgnoreCase) ||
                json.Contains("description", StringComparison.OrdinalIgnoreCase),
                "The preference store must contain aggregates, not semantic context.");
            TestAssert.True(
                Directory.GetFiles(root, "*.tmp").Length == 0,
                "Atomic preference writes clean staging files.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
        return Task.CompletedTask;
    }

    private static Task ProfileNeedsCoverage()
    {
        string root = CreateRoot();
        try
        {
            var store = new JsonClipPreferenceFeedbackStore(
                Path.Combine(root, "preferences.json"));
            ClipPreferenceFeatureVector liked = CreateVector(0.2, 0.9);
            ClipPreferenceFeatureVector disliked = CreateVector(0.9, 0.2);
            for (int index = 0; index < 4; index++)
            {
                store.Update(liked, null, ClipPreferenceRating.Like);
                store.Update(disliked, null, ClipPreferenceRating.Dislike);
            }

            TestAssert.True(store.Current.IsReady, "Balanced history ready.");
            TestAssert.True(
                store.Current.Evaluate(liked).SignedContribution > 0,
                "A like-shaped neutral vector should receive bounded support.");
            TestAssert.True(
                store.Current.Evaluate(disliked).SignedContribution < 0,
                "A dislike-shaped neutral vector should receive a bounded penalty.");
            TestAssert.True(
                Math.Abs(store.Current.Evaluate(liked).SignedContribution) <=
                    ClipPreferenceProfile.MaximumAbsoluteContribution,
                "Preference contribution remains bounded.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
        return Task.CompletedTask;
    }

    private static Task StudioRecordsFeedback()
    {
        string root = CreateRoot();
        try
        {
            var store = new JsonClipPreferenceFeedbackStore(
                Path.Combine(root, "preferences.json"));
            var asset = new GenerationOutputAsset(
                "candidate-neutral-feedback",
                1,
                TestMediaFactory.Create(
                    TestMediaFactory.CreateSourcePath("preference-source.mkv"),
                    TimeSpan.FromMinutes(5)),
                outputFullPath: null,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(1.5),
                82,
                70,
                GenerationCandidateSelectionReason.QualityQualified,
                "Deterministic evidence selected this interval.",
                preferenceFeatures: CreateVector(0.25, 0.82));
            var project = new GenerationOutputProject(
                "project-neutral-feedback",
                GenerationMode.IndividualClips,
                Path.Combine(root, "outputs"),
                1,
                ClipFulfillmentPreference.QualityFirst,
                GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
                [asset],
                DateTimeOffset.UtcNow);
            var session = new GenerationOutputSession();
            session.Publish(project);
            using var studio = new StudioViewModel(
                session,
                session,
                new UnusedRenderer(),
                new ClipEditorialMetadataGenerationService(
                    new HeuristicClipEditorialMetadataGenerator()),
                new ClipEditorialProfileSession(),
                previewMediaService: null,
                new StudioClipPreferenceService(store));

            studio.Inspector.Preference.SetPreferenceCommand.Execute(
                StudioClipPreferenceRating.Like);

            TestAssert.True(
                studio.Inspector.Preference.IsLikeSelected,
                "Studio Like selection.");
            TestAssert.Equal(1, store.Current.LikeCount, "Stored Like count.");
            TestAssert.True(
                studio.Inspector.Preference.PreferenceLearningStatus.Contains(
                    "ranking is unchanged",
                    StringComparison.OrdinalIgnoreCase),
                "Sparse feedback must not change ranking.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
        return Task.CompletedTask;
    }

    private static ClipPreferenceFeatureVector CreateVector(
        double duration,
        double score) =>
        new(
        [
            new(ClipPreferenceFeatureCode.Duration, duration),
            new(ClipPreferenceFeatureCode.DeterministicScore, score),
        ]);

    private static string CreateRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundryPreferenceTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class UnusedRenderer : IStudioProjectRenderingService
    {
        public Task<StudioProjectRenderResult> FinalizeAsync(
            GenerationOutputProject draft,
            IProgress<StudioProjectRenderProgress> progress,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The preference test must not render media.");

        public void AcceptCompletedRender(StudioProjectRenderResult result) =>
            throw new InvalidOperationException(
                "The preference test must not accept rendered media.");

        public void DiscardCompletedRender(StudioProjectRenderResult result) =>
            throw new InvalidOperationException(
                "The preference test must not discard rendered media.");
    }
}
