using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Personalization;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.Intelligence.Learning;

namespace ReplayFoundry.PreparationTests;

internal static class TasteIntegrationTests
{
    internal static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Taste successful renders and publishes retain the exact historical source cut", RenderAndPublishProvenance),
        new("Taste manual and Find More clips contribute distinct actions and remain rateable", ManualAndRecovered),
        new("Taste rating UI does not carry a judgment onto a changed cut", RatingCutIdentity),
        new("Taste source grouping recognizes copies while clip identities distinguish cuts", SourceIdentity),
        new("Taste personalized generation preserves quality and never rewrites evidence scores", GenerationRanking),
        new("Taste inactive learning leaves generation selection untouched", InactiveRanking),
    ];
    private static Task RenderAndPublishProvenance() => Sta(() =>
    {
        using var scratch = new TasteScratch(); var session = new GenerationOutputSession();
        var original = Asset(scratch.Path, "candidate", 10, 40); var project = Project(scratch.Path, original);
        session.Publish(project);
        using var library = new GenerationLibraryCatalog(session, new InMemoryLibraryCatalogStore());
        var firstRender = project.Finalize([original.WithRenderedOutput(Path.Combine(scratch.Path, "first.mp4"))], DateTimeOffset.UtcNow);
        session.CommitRenderedOutput(firstRender); var firstLibraryAsset = library.Assets.Single();
        using var studio = new StudioViewModel(session); var learning = new Recorder();
        using var interactions = new TasteInteractionRecorder(learning, session, library, studio);
        var revised = original.WithStudioEdits(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(130), original.Appearance);
        session.ReplaceAsset(project.Id, revised); learning.Events.Clear();
        TestAssert.False(learning.Events.Any(x => x.Signal == TasteSignal.Rendered), "Editing a draft is not successful rendering.");
        var secondRender = session.Current!.Finalize([revised.WithRenderedOutput(Path.Combine(scratch.Path, "second.mp4"))], DateTimeOffset.UtcNow);
        session.CommitRenderedOutput(secondRender);
        TestAssert.True(learning.Events.Any(x => x.Signal == TasteSignal.Rendered && x.Clip.Id == TasteClipFactory.FromAsset(revised).Id),
            "A committed render must teach the exact rendered source range.");
        learning.Events.Clear();
        foreach (var outcome in new[] { YouTubePublishOutcome.Failed, YouTubePublishOutcome.Cancelled, YouTubePublishOutcome.Scheduled,
            YouTubePublishOutcome.UploadedPrivate }) interactions.Published(firstLibraryAsset, outcome);
        TestAssert.Equal(0, learning.Events.Count, "Failed, cancelled, private, and merely scheduled uploads are not published outcomes.");
        interactions.Published(firstLibraryAsset, YouTubePublishOutcome.Published);
        TestAssert.Equal(TasteClipFactory.FromAsset(original).Id, learning.Events.Single().Clip.Id,
            "Publishing an older render must not teach the newer draft's source cut.");
        TestAssert.Equal(TasteSignal.Published, learning.Events.Single().Signal, "A completed publication must be recorded.");
        learning.Events.Clear();
        interactions.Published(library.Assets.Single(), YouTubePublishOutcome.UploadedUnlisted);
        TestAssert.Equal(TasteClipFactory.FromAsset(revised).Id, learning.Events.Single().Clip.Id, "Unlisted publication must use its own rendered snapshot.");
        learning.Events.Clear();
        interactions.ImportPublishHistory([new YouTubePublishHistoryEntry("past", firstLibraryAsset.Id, "Historical clip", "video", null,
            YouTubePublishOutcome.Published, YouTubeVideoVisibility.Public, DateTimeOffset.UtcNow, null, provenance: firstLibraryAsset.SourceProvenance)]);
        TestAssert.Equal(TasteClipFactory.FromAsset(original).Id, learning.Events.Single().Clip.Id,
            "History reconciliation must retain the submitted cut even after the Library ID is reused.");
    });
    private static Task ManualAndRecovered() => Sta(() =>
    {
        using var scratch = new TasteScratch(); var session = new GenerationOutputSession();
        using var library = new GenerationLibraryCatalog(session, new InMemoryLibraryCatalogStore());
        using var studio = new StudioViewModel(session); var learning = new Recorder();
        using var interactions = new TasteInteractionRecorder(learning, session, library, studio);
        var manual = Asset(scratch.Path, "manual", 10, 40, GenerationCandidateSelectionReason.ManualSourceCut);
        session.Publish(Project(scratch.Path, manual));
        TestAssert.True(learning.Events.Any(x => x.Signal == TasteSignal.ManualCreated), "Custom cuts must enter the observed-action task.");
        var recovered = Asset(scratch.Path, "recovered", 100, 130, GenerationCandidateSelectionReason.HiddenMomentRecovery);
        session.Publish(Project(scratch.Path, recovered));
        TestAssert.True(learning.Events.Any(x => x.Signal == TasteSignal.HiddenAccepted), "Accepted Find More moments must enter learning.");
        var ratings = new NeuralStudioClipPreferenceService(learning);
        TestAssert.True(ratings.CanRate(manual), "A custom clip without old heuristic preference features must still be rateable.");
        ratings.Update(manual, null, StudioClipPreferenceRating.Neutral);
        TestAssert.Equal(TasteSignal.Neutral, learning.Events.Last().Signal, "Neutral must be a first-class explicit judgment.");
        TestAssert.False(learning.Events.Any(x => x.Signal == TasteSignal.Rendered), "A generated or accepted draft is not a finished render.");
    });
    private static Task RatingCutIdentity() => Sta(() =>
    {
        using var scratch = new TasteScratch(); var asset = Asset(scratch.Path, "clip", 10, 40);
        var project = Project(scratch.Path, asset); var learning = new Recorder();
        using var view = new StudioClipPreferenceViewModel(new NeuralStudioClipPreferenceService(learning));
        view.Bind(project, asset); view.SetPreferenceCommand.Execute(StudioClipPreferenceRating.Like);
        TestAssert.True(view.IsLikeSelected, "An explicit Like should be shown for the rated cut.");
        var revised = asset.WithStudioEdits(TimeSpan.FromSeconds(70), TimeSpan.FromSeconds(100), asset.Appearance);
        view.Bind(project, revised);
        TestAssert.Null(view.SelectedPreference, "A different source cut with the same project asset ID needs its own judgment.");
        view.Bind(project, asset);
        TestAssert.True(view.IsLikeSelected, "Returning to the original range should recover that range's rating.");
    });
    private static Task SourceIdentity()
    {
        using var scratch = new TasteScratch(); string one = Path.Combine(scratch.Path, "one"), two = Path.Combine(scratch.Path, "two");
        Directory.CreateDirectory(one); Directory.CreateDirectory(two);
        File.WriteAllBytes(Path.Combine(one, "source.mp4"), Enumerable.Range(0, 512).Select(x => (byte)x).ToArray());
        File.Copy(Path.Combine(one, "source.mp4"), Path.Combine(two, "source.mp4"));
        var original = TasteClipFactory.FromAsset(Asset(one, "original", 10, 40));
        var copy = TasteClipFactory.FromAsset(Asset(two, "copy", 10, 40));
        var another = TasteClipFactory.FromAsset(Asset(one, "another", 70, 100));
        TestAssert.Equal(original.SourceGroup, copy.SourceGroup, "Copies cannot leak the same recording into training and testing.");
        TestAssert.Equal(original.Id, copy.Id, "Repeated copies of the same source cut cannot multiply feedback.");
        TestAssert.False(original.Id == another.Id, "Distinct cuts must remain distinct observations.");
        return Task.CompletedTask;
    }
    private static async Task GenerationRanking()
    {
        foreach (var mode in new[] { GenerationMode.IndividualClips, GenerationMode.Montage })
        {
        var request = new GenerationMomentFindingRequest(GenerationMomentFindingTests.CreateEvidence(1),
            GenerationMomentFindingTests.CreateSetup(mode, ContentEmphasis.Balanced, 1, 70, ClipFulfillmentPreference.QualityFirst));
        var original = new GenerationMomentFindingService(new GenerationMomentFindingTests.RecordingMomentFinder([[81, 80, 20]])).Find(request);
        var learning = new Recorder { Active = true };
        var result = await new GenerationTasteRanking(learning).ApplyAsync(original, null, CancellationToken.None);
        TestAssert.Equal(1, result.SelectedCount, "Personalization must preserve the requested result count.");
        TestAssert.NearlyEqual(80, result.SelectedCandidates.Single().Candidate.Score.HeuristicScore, 1e-9,
            "A learned preference may choose the other qualified moment without changing its evidence score.");
        TestAssert.False(result.SelectedCandidates.Any(x => x.Candidate.Score.HeuristicScore < 70), "Personalization cannot bypass the chosen quality floor.");
        TestAssert.NearlyEqual(81, original.SelectedCandidates.Single().Candidate.Score.HeuristicScore, 1e-9, "The original analysis must remain immutable.");
        }
    }
    private static async Task InactiveRanking()
    {
        var request = GenerationMomentFindingTests.CreateRequest(1, desiredCount: 1);
        var original = new GenerationMomentFindingService(new GenerationMomentFindingTests.RecordingMomentFinder([[81, 80]])).Find(request);
        var learning = new Recorder(); var result = await new GenerationTasteRanking(learning).ApplyAsync(original, null, CancellationToken.None);
        TestAssert.Same(original, result, "Learning without a qualified model must return the original selection.");
        TestAssert.Equal(0, learning.PredictionCalls, "Inactive learning must add no encoder or neural inference cost.");
    }
    private static GenerationOutputAsset Asset(string directory, string id, int start, int end,
        GenerationCandidateSelectionReason reason = GenerationCandidateSelectionReason.QualityQualified) =>
        new(id, 1, TestMediaFactory.Create(Path.Combine(directory, "source.mp4"), TimeSpan.FromMinutes(10)), null,
            TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), 80, 70, reason, "Test moment");
    private static GenerationOutputProject Project(string directory, GenerationOutputAsset asset) => new("taste-project", GenerationMode.IndividualClips,
        directory, 1, ClipFulfillmentPreference.QualityFirst, GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
        [asset], DateTimeOffset.UtcNow, sourceMedia: [asset.SourceMedia]);
    private static Task Sta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); completion.SetResult(); } catch (Exception e) { completion.SetException(e); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private sealed class Recorder : ITasteLearningService
    {
        internal List<(TasteClip Clip, TasteSignal? Signal)> Events { get; } = [];
        internal bool Active { get; init; }
        internal int PredictionCalls { get; private set; }
        public TasteLearningStatus Status => new(true, false, Active, 0, 0, 0, "Test profile");
        public event EventHandler? Changed { add { } remove { } }
        public void Observe(TasteClip clip, TasteSignal? signal) => Events.Add((clip, signal));
        public Task<IReadOnlyDictionary<string, TastePrediction>> PredictAsync(IReadOnlyList<TasteClip> clips, CancellationToken cancellationToken)
        {
            PredictionCalls++;
            return Task.FromResult<IReadOnlyDictionary<string, TastePrediction>>(clips.ToDictionary(x => x.Id,
                x => new TastePrediction(true, x.BaselineScore == 81 ? -1 : 1, 0, "ranking-fixture")));
        }
        public Task TrainAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void SetEnabled(bool enabled) { }
        public Task ResetAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
