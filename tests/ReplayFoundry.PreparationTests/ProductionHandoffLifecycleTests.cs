using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.HiddenMoments;
using ReplayFoundry.Desktop.Platform.Storage;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.PreparationTests;

internal static class ProductionHandoffLifecycleTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Maximum candidate duration is a bounded upper limit", MaximumDurationIsUpperLimit),
        new("Studio excludes candidates without losing their metadata", StudioExclusionPreservesMetadata),
        new("Library archives only finalized included outputs", LibraryArchivesFinalOutputs),
        new("Studio candidate decisions persist independently of preference aggregates", CandidateDecisionsPersist),
        new("Hidden Moments decisions remain separate from satisfaction ratings", HiddenMomentDecisionsRemainSeparate),
    ];

    private static Task MaximumDurationIsUpperLimit()
    {
        var setup = new GenerationSetupOptions(
            GenerationMode.IndividualClips,
            DetectionMethod.Heuristics,
            AudioSelectionMode.Auto,
            5,
            70,
            ContentEmphasis.Balanced,
            maximumClipDuration: TimeSpan.FromMinutes(3));
        GenerationMomentFindingSettings settings =
            GenerationMomentFindingSettings.FromSetup(setup);

        TestAssert.Equal(
            TimeSpan.FromMinutes(3),
            settings.Options.MaximumDuration,
            "The user selection must map to the exact deterministic cap.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(30),
            settings.Options.TargetDuration,
            "A three-minute cap must not force every candidate to three minutes.");

        var tenSeconds = new GenerationSetupOptions(
            GenerationMode.IndividualClips,
            DetectionMethod.Heuristics,
            AudioSelectionMode.Auto,
            5,
            70,
            ContentEmphasis.Balanced,
            maximumClipDuration: TimeSpan.FromSeconds(10));
        MediaMomentFindingOptions options =
            GenerationMomentFindingSettings.FromSetup(tenSeconds).Options;
        TestAssert.Equal(TimeSpan.FromSeconds(10), options.MinimumDuration, "Ten-second minimum.");
        TestAssert.Equal(TimeSpan.FromSeconds(10), options.TargetDuration, "Ten-second target cap.");
        TestAssert.Equal(TimeSpan.FromSeconds(10), options.MaximumDuration, "Ten-second maximum.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => new GenerationSetupOptions(
                GenerationMode.IndividualClips,
                DetectionMethod.Heuristics,
                AudioSelectionMode.Auto,
                5,
                70,
                ContentEmphasis.Balanced,
                maximumClipDuration: TimeSpan.FromSeconds(9)),
            "Durations below ten seconds must be rejected.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => new GenerationSetupOptions(
                GenerationMode.IndividualClips,
                DetectionMethod.Heuristics,
                AudioSelectionMode.Auto,
                5,
                70,
                ContentEmphasis.Balanced,
                maximumClipDuration: TimeSpan.FromSeconds(181)),
            "Durations above three minutes must be rejected.");
        return Task.CompletedTask;
    }

    private static Task StudioExclusionPreservesMetadata()
    {
        using var fixture = new LifecycleFixture();
        GenerationOutputAsset excluded = fixture.Assets[1].WithDisposition(
            GenerationOutputAssetDisposition.ExcludeFromFinalRender);
        GenerationOutputProject draft = fixture.Project.ReplaceAsset(excluded);
        GenerationOutputAsset rendered = draft.IncludedAssets[0]
            .WithRenderedOutput(fixture.OutputPath, fixture.ThumbnailPath);
        GenerationOutputProject finalized = draft.Finalize(
            [rendered],
            DateTimeOffset.UtcNow);

        TestAssert.True(finalized.IsFinalized, "Project must finalize.");
        TestAssert.Equal(1, finalized.IncludedCount, "Included count.");
        TestAssert.Equal(1, finalized.ExcludedCount, "Excluded count.");
        TestAssert.False(
            finalized.ExcludedAssets[0].IsRendered,
            "Rejected candidates must not be rendered.");
        TestAssert.Equal(
            fixture.Assets[1].Explanation,
            finalized.ExcludedAssets[0].Explanation,
            "Rejected candidate provenance must remain available.");
        return Task.CompletedTask;
    }

    private static Task LibraryArchivesFinalOutputs()
    {
        using var fixture = new LifecycleFixture();
        var session = new GenerationOutputSession();
        string catalogPath = Path.Combine(fixture.Root, "library.json");
        using var catalog = new GenerationLibraryCatalog(
            session,
            new JsonLibraryCatalogStore(catalogPath));
        session.Publish(fixture.Project);
        GenerationOutputProject draft = fixture.Project.ReplaceAsset(
            fixture.Assets[1].WithDisposition(
                GenerationOutputAssetDisposition.ExcludeFromFinalRender));
        session.Publish(draft);
        session.FinalizeProject(draft.Finalize(
            [draft.IncludedAssets[0].WithRenderedOutput(
                fixture.OutputPath,
                fixture.ThumbnailPath)],
            DateTimeOffset.UtcNow));

        TestAssert.Equal(1, catalog.Assets.Count, "Only included output enters Library.");
        TestAssert.Equal(
            fixture.Project.Id + "-" + fixture.Assets[0].Id,
            catalog.Assets[0].Id,
            "Library scopes the candidate identity to its finalized project.");
        TestAssert.Equal(
            1,
            new JsonLibraryCatalogStore(catalogPath).Current.Count,
            "Library catalog must survive a new store instance.");

        using var secondFixture = new LifecycleFixture();
        GenerationOutputProject repeatedCandidateProject =
            new GenerationOutputProject(
                "project-lifecycle-second",
                GenerationMode.IndividualClips,
                Path.Combine(secondFixture.Root, "rendered-project"),
                2,
                ClipFulfillmentPreference.FillRequestedCount,
                GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
                secondFixture.Assets,
                DateTimeOffset.UtcNow);
        session.Publish(repeatedCandidateProject);
        GenerationOutputProject secondDraft = repeatedCandidateProject.ReplaceAsset(
            secondFixture.Assets[1].WithDisposition(
                GenerationOutputAssetDisposition.ExcludeFromFinalRender));
        session.Publish(secondDraft);
        session.FinalizeProject(secondDraft.Finalize(
            [secondDraft.IncludedAssets[0].WithRenderedOutput(
                secondFixture.OutputPath,
                secondFixture.ThumbnailPath)],
            DateTimeOffset.UtcNow));

        TestAssert.Equal(
            2,
            catalog.Assets.Count,
            "Repeated deterministic candidate identities must remain distinct across projects.");
        TestAssert.Equal(
            2,
            catalog.Assets.Select(static asset => asset.Id)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            "Every archived Library asset requires a globally unique project-scoped identity.");
        return Task.CompletedTask;
    }

    private static Task CandidateDecisionsPersist()
    {
        using var fixture = new LifecycleFixture();
        string path = Path.Combine(fixture.Root, "decisions.json");
        var store = new JsonStudioCandidateDecisionStore(path);
        var decision = new StudioCandidateDecision(
            fixture.Assets[1].Id,
            fixture.Project.Id,
            new string('A', 64),
            fixture.Assets[1].SourceStart,
            fixture.Assets[1].SourceEnd,
            GenerationOutputAssetDisposition.ExcludeFromFinalRender,
            StudioClipPreferenceRating.Dislike,
            DateTimeOffset.UtcNow);
        store.Upsert(decision);

        StudioCandidateDecision loaded =
            new JsonStudioCandidateDecisionStore(path).Find(decision.CandidateId)!;
        TestAssert.Equal(decision.Disposition, loaded.Disposition, "Disposition persists.");
        TestAssert.Equal(decision.Rating, loaded.Rating, "Preference persists.");
        TestAssert.Equal(decision.SourceStart, loaded.SourceStart, "Timing persists.");
        return Task.CompletedTask;
    }

    private static Task HiddenMomentDecisionsRemainSeparate()
    {
        using var fixture = new LifecycleFixture();
        string path = Path.Combine(fixture.Root, "hidden-decisions.json");
        var store = new JsonStudioHiddenMomentDecisionStore(path);
        var decision = new StudioHiddenMomentDecision(
            fixture.Project.Id,
            "hidden-candidate",
            new string('B', 64),
            TimeSpan.FromSeconds(120),
            TimeSpan.FromSeconds(150),
            StudioHiddenMomentReviewDecision.SkippedForProject,
            DateTimeOffset.UtcNow);
        store.Upsert(decision);

        StudioHiddenMomentDecision loaded =
            new JsonStudioHiddenMomentDecisionStore(path).Find(
                decision.ProjectId,
                decision.CandidateId)!;
        TestAssert.Equal(
            StudioHiddenMomentReviewDecision.SkippedForProject,
            loaded.Decision,
            "Discovery choice persists with its own vocabulary.");
        TestAssert.True(
            typeof(StudioHiddenMomentDecision).GetProperty("Rating") is null,
            "Hidden Moments decisions must not masquerade as Like or Dislike.");
        return Task.CompletedTask;
    }

    private sealed class LifecycleFixture : IDisposable
    {
        public LifecycleFixture()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "ReplayFoundryLifecycleTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            string source = Path.Combine(Root, "source.mkv");
            File.WriteAllBytes(source, [1, 2, 3]);
            OutputPath = Path.Combine(Root, "final.mp4");
            ThumbnailPath = Path.Combine(Root, "final.jpg");
            File.WriteAllBytes(OutputPath, [4, 5, 6]);
            File.WriteAllBytes(ThumbnailPath, [7, 8, 9]);
            var media = TestMediaFactory.Create(source, TimeSpan.FromMinutes(5));
            Assets =
            [
                CreateAsset("candidate-1", 1, media, 10, 40),
                CreateAsset("candidate-2", 2, media, 60, 95),
            ];
            Project = new GenerationOutputProject(
                "project-lifecycle",
                GenerationMode.IndividualClips,
                Path.Combine(Root, "rendered-project"),
                2,
                ClipFulfillmentPreference.FillRequestedCount,
                GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
                Assets,
                DateTimeOffset.UtcNow);
        }

        public string Root { get; }
        public string OutputPath { get; }
        public string ThumbnailPath { get; }
        public GenerationOutputAsset[] Assets { get; }
        public GenerationOutputProject Project { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }

        private static GenerationOutputAsset CreateAsset(
            string id,
            int rank,
            MediaProbeResult media,
            int start,
            int end) => new(
                id,
                rank,
                media,
                outputFullPath: null,
                TimeSpan.FromSeconds(start),
                TimeSpan.FromSeconds(end),
                80,
                70,
                GenerationCandidateSelectionReason.QualityQualified,
                "Retained deterministic candidate metadata.");
    }
}
