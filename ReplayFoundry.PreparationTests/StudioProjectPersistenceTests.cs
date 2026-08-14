using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Projects;
using ReplayFoundry.Desktop.Features.Studio.Rendering;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;
using ReplayFoundry.Desktop.Media.Intelligence.VisualText;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Platform.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReplayFoundry.PreparationTests;

internal static class StudioProjectPersistenceTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Studio project persistence round-trips authoritative edits and recovery", RoundTripsProject),
        new("Studio project persistence reads 1.0 metadata without reroll history", ReadsLegacyProjectWithoutHistory),
        new("Studio project persistence classifies changed and missing sources", ClassifiesFreshness),
        new("Studio project persistence recovers the previous atomic save", RecoversBackup),
        new("Recent projects reopen durable Studio state after restart", ReopensRecentProject),
        new("Studio persistence debounce cannot discard the latest revision", DebounceKeepsLatest),
        new("Studio persistence shutdown does not deadlock the UI synchronization context", DisposeDoesNotDeadlockUiContext),
        new("Studio persistence exposes actionable save failures", ExposesSaveFailure),
        new("Studio render recovery preserves interrupted queue entries", PreservesInterruptedQueue),
    ];

    private static Task RoundTripsProject()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject project = fixture.CreateProject();
        var recovery = new StudioProjectRecoveryState(
            project.PrimaryAsset.Id,
            [
                new StudioRenderQueueEntryDocument(
                    project.PrimaryAsset.Id,
                    StudioPersistedRenderState.Ready),
            ],
            TimeSpan.FromSeconds(17));
        fixture.Store.Save(project, revision: 1, recovery);

        StudioProjectLoadResult loaded = fixture.Store.Load(project.Id);
        TestAssert.Equal(
            StudioProjectLoadOutcome.Loaded,
            loaded.Outcome,
            "A valid project should load without analysis work.");
        TestAssert.True(loaded.Project is not null, "The project should rehydrate.");
        GenerationOutputAsset asset = loaded.Project!.PrimaryAsset;
        TestAssert.Equal(TimeSpan.FromSeconds(12), asset.SourceStart,
            "The edited source start should persist.");
        TestAssert.Equal(TimeSpan.FromSeconds(28), asset.SourceEnd,
            "The edited source end should persist.");
        TestAssert.Equal(TimeSpan.FromSeconds(10), asset.OriginalSourceStart,
            "The original candidate start should remain distinct from the edit.");
        TestAssert.Equal(StudioVideoEffectPreset.Noir, asset.Appearance.VideoEffect,
            "The selected visual effect should persist.");
        TestAssert.Equal(GenerationCaptionStylePreset.KaraokeSweep,
            asset.Captions!.RequestedStyle,
            "The caption presentation should persist.");
        TestAssert.Equal("A grounded title", asset.EditorialMetadata!.Title,
            "Editorial metadata should persist.");
        TestAssert.Equal("An earlier grounded title #TestGame",
            asset.EditorialMetadata.PriorAcceptedTitles.Single(),
            "Bounded reroll exclusions must survive a durable Studio round trip.");
        TestAssert.Equal(StudioPersistedRenderState.Ready,
            loaded.Document!.Recovery!.RenderQueue[0].State,
            "The render queue should remain separate recoverable state.");
        return Task.CompletedTask;
    }

    private static Task ReadsLegacyProjectWithoutHistory()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject project = fixture.CreateProject();
        fixture.Store.Save(project, revision: 1);
        string path = Path.Combine(
            fixture.Store.ResolveProjectDirectory(project.Id),
            "studio-project.json");
        JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["schemaVersion"] = StudioProjectDocument.PreviousSchemaVersion;
        foreach (JsonNode? asset in root["assets"]!.AsArray())
        {
            asset!["editorialMetadata"]!.AsObject().Remove(
                "priorAcceptedTitles");
        }
        File.WriteAllText(
            path,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        StudioProjectLoadResult loaded = fixture.Store.Load(project.Id);

        TestAssert.Equal(StudioProjectLoadOutcome.Loaded, loaded.Outcome,
            "A valid 1.0 Studio project must remain readable after the additive history field.");
        TestAssert.Equal(0,
            loaded.Project!.PrimaryAsset.EditorialMetadata!
                .PriorAcceptedTitles.Count,
            "Legacy projects must not invent prior accepted titles.");
        return Task.CompletedTask;
    }

    private static Task ClassifiesFreshness()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject project = fixture.CreateProject();
        fixture.Store.Save(project, revision: 1);

        File.AppendAllText(fixture.SourcePath, "changed");
        StudioProjectLoadResult changed = fixture.Store.Load(project.Id);
        TestAssert.Equal(StudioProjectLoadOutcome.ChangedSource, changed.Outcome,
            "Length or UTC write-time changes must prevent an ordinary reopen.");
        TestAssert.True(changed.HasRecoverableProject,
            "Changed sources must not discard the user's Studio edits.");

        File.Delete(fixture.SourcePath);
        StudioProjectLoadResult missing = fixture.Store.Load(project.Id);
        TestAssert.Equal(StudioProjectLoadOutcome.MissingSource, missing.Outcome,
            "Missing sources must receive a typed recovery outcome.");
        TestAssert.True(missing.HasRecoverableProject,
            "Missing sources must preserve the durable project.");
        return Task.CompletedTask;
    }

    private static Task RecoversBackup()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject first = fixture.CreateProject();
        fixture.Store.Save(first, revision: 1);
        GenerationOutputAsset editedAsset = first.PrimaryAsset.WithStudioEdits(
            TimeSpan.FromSeconds(14),
            TimeSpan.FromSeconds(27),
            first.PrimaryAsset.Appearance);
        GenerationOutputProject edited = first.ReplaceAsset(editedAsset);
        fixture.Store.Save(edited, revision: 2);

        string primary = Path.Combine(
            fixture.Store.ResolveProjectDirectory(first.Id),
            "studio-project.json");
        File.WriteAllText(primary, "{not-json");
        StudioProjectLoadResult recovered = fixture.Store.Load(first.Id);
        TestAssert.Equal(
            StudioProjectLoadOutcome.RecoveredPreviousSave,
            recovered.Outcome,
            "A corrupt primary should recover the last atomically retained save.");
        TestAssert.Equal(TimeSpan.FromSeconds(12),
            recovered.Project!.PrimaryAsset.SourceStart,
            "Recovery should use the previous complete revision, never a partial replacement.");
        TestAssert.Equal("{not-json", File.ReadAllText(primary),
            "Recovery must preserve the corrupt primary for diagnostics.");
        return Task.CompletedTask;
    }

    private static Task ReopensRecentProject()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject project = fixture.CreateProject();
        fixture.Store.Save(project, revision: 1);
        string recentPath = Path.Combine(fixture.Root, "recent.json");

        var firstSession = new GenerationOutputSession();
        using (var firstCatalog = new ReplayFoundry.Desktop.Features.Generate.RecentProjects.RecentGenerationProjectCatalog(
                   firstSession,
                   new ReplayFoundry.Desktop.Features.Generate.RecentProjects.JsonRecentGenerationProjectStore(recentPath),
                   fixture.Store))
        {
            firstSession.Publish(project);
        }

        var restartedSession = new GenerationOutputSession();
        using var restarted = new ReplayFoundry.Desktop.Features.Generate.RecentProjects.RecentGenerationProjectCatalog(
            restartedSession,
            new ReplayFoundry.Desktop.Features.Generate.RecentProjects.JsonRecentGenerationProjectStore(recentPath),
            fixture.Store);
        TestAssert.True(
            restarted.TryGetStudioProject(project.Id, out GenerationOutputProject? restored),
            "A new process-level catalog should resolve the durable project.");
        TestAssert.Equal(project.CandidateSetFingerprint,
            restored!.CandidateSetFingerprint,
            "Reopen should use retained Studio state without reconstructing Generate analysis.");
        fixture.Store.Delete(project.Id);
        using var afterDelete = new ReplayFoundry.Desktop.Features.Generate.RecentProjects.RecentGenerationProjectCatalog(
            new GenerationOutputSession(),
            new ReplayFoundry.Desktop.Features.Generate.RecentProjects.JsonRecentGenerationProjectStore(recentPath),
            fixture.Store);
        TestAssert.False(
            afterDelete.Projects.Single().IsStudioReady,
            "A persisted historical ready flag must not outlive its durable project document.");
        return Task.CompletedTask;
    }

    private static async Task DebounceKeepsLatest()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject first = fixture.CreateProject();
        GenerationOutputAsset latestAsset = first.PrimaryAsset.WithStudioEdits(
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(26),
            first.PrimaryAsset.Appearance);
        GenerationOutputProject latest = first.ReplaceAsset(latestAsset);
        var session = new GenerationOutputSession();
        var store = new RecordingProjectStore();
        using var coordinator = new StudioProjectPersistenceCoordinator(
            session,
            store,
            TimeSpan.FromMilliseconds(30));

        coordinator.ScheduleSave(first);
        await Task.Delay(20);
        coordinator.ScheduleSave(latest);
        await coordinator.FlushAsync();

        TestAssert.True(store.Saved.Count > 0,
            "The newest pending save should be flushed.");
        TestAssert.Equal(TimeSpan.FromSeconds(15),
            store.Saved[^1].Project.PrimaryAsset.SourceStart,
            "An obsolete debounce generation must never consume and drop the newer project.");
        TestAssert.True(store.Saved.Select(static value => value.Revision)
                .SequenceEqual(store.Saved.Select(static value => value.Revision).Order()),
            "Persisted revisions should remain monotonic.");
    }

    private static async Task DisposeDoesNotDeadlockUiContext()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject first = fixture.CreateProject();
        GenerationOutputProject latest = first.ReplaceAsset(
            first.PrimaryAsset.WithStudioEdits(
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(26),
                first.PrimaryAsset.Appearance));
        var session = new GenerationOutputSession();
        var store = new RecordingProjectStore();
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new NonPumpingSingleThreadSynchronizationContext());
            try
            {
                var coordinator = new StudioProjectPersistenceCoordinator(
                    session,
                    store,
                    TimeSpan.FromSeconds(30));
                coordinator.ScheduleSave(first);
                coordinator.ScheduleSave(latest);
                coordinator.Dispose();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Studio persistence UI-context regression",
        };
        thread.Start();

        Task completed = await Task.WhenAny(
            completion.Task,
            Task.Delay(TimeSpan.FromSeconds(5)));
        TestAssert.True(ReferenceEquals(completed, completion.Task),
            "Synchronous Studio shutdown must not wait for a continuation posted back to the blocked UI thread.");
        await completion.Task;
        TestAssert.Equal(1, store.Saved.Count,
            "Shutdown should flush the newest pending project exactly once.");
        TestAssert.Equal(TimeSpan.FromSeconds(15),
            store.Saved[0].Project.PrimaryAsset.SourceStart,
            "UI-thread shutdown must retain the latest scheduled Studio state.");
    }

    private static async Task ExposesSaveFailure()
    {
        using var fixture = new PersistenceFixture();
        var coordinator = new StudioProjectPersistenceCoordinator(
            new GenerationOutputSession(),
            new FailingProjectStore(),
            TimeSpan.Zero);
        int stateChanges = 0;
        coordinator.PersistenceStateChanged += (_, _) => stateChanges++;

        coordinator.ScheduleSave(fixture.CreateProject());
        await coordinator.FlushAsync();

        TestAssert.True(
            coordinator.LastError?.Contains(
                "diagnostic save failure",
                StringComparison.Ordinal) == true,
            "A swallowed project-store failure must remain visible to Studio and support diagnostics.");
        TestAssert.Equal(1, stateChanges,
            "Studio should receive one state change when a save first fails.");
        coordinator.Dispose();
    }

    private static Task PreservesInterruptedQueue()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject project = fixture.CreateProject();
        using var render = new StudioFinalRenderViewModel(
            outputEditor: null,
            renderingService: null,
            applyPendingEdit: static () => true,
            setHostBusy: static _ => { });
        render.Bind(project);
        render.RestoreRecoveryState(new StudioProjectRecoveryState(
            project.PrimaryAsset.Id,
            [
                new StudioRenderQueueEntryDocument(
                    project.PrimaryAsset.Id,
                    StudioPersistedRenderState.Interrupted),
            ]));

        StudioProjectRecoveryState captured = render.CaptureRecoveryState(
            project.PrimaryAsset.Id,
            TimeSpan.Zero);
        TestAssert.Equal(StudioPersistedRenderState.Interrupted,
            captured.RenderQueue[0].State,
            "A crash-time render must remain interrupted rather than being assumed complete.");
        TestAssert.Equal("INTERRUPTED", render.QueueItems[0].Status,
            "The restored queue should surface its interrupted state.");
        return Task.CompletedTask;
    }

    private sealed class PersistenceFixture : IDisposable
    {
        public PersistenceFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "ReplayFoundryStudioPersistenceTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            SourcePath = Path.Combine(Root, "source.mkv");
            File.WriteAllBytes(SourcePath, [1, 2, 3, 4, 5]);
            Store = new JsonStudioProjectStore(Path.Combine(Root, "projects"));
        }

        public string Root { get; }
        public string SourcePath { get; }
        public JsonStudioProjectStore Store { get; }

        public GenerationOutputProject CreateProject()
        {
            var media = TestMediaFactory.Create(
                SourcePath,
                duration: TimeSpan.FromMinutes(2),
                hasAudio: true);
            const string candidateId = "candidate-1";
            const string neighborhoodId = "caption-candidate-1";
            var sourceSelection = new GenerationCaptionSourceSelection(
                SourcePath,
                media.AudioStreams[0].Index,
                CaptionAudioContentRole.CreatorCommentary);
            var segment = new AudioTranscriptionSegment(
                "segment-1",
                neighborhoodId,
                "we found the hidden route",
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(11),
                TimeSpan.FromSeconds(13));
            GenerationCandidateCaptionTrack captions =
                GenerationCandidateCaptionTrack.RestoreStudioHandoff(
                    candidateId,
                    neighborhoodId,
                    sourceSelection,
                    GenerationCaptionStylePreset.KaraokeSweep,
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(20),
                    media.Duration,
                    [segment],
                    isUserEdited: true,
                    GenerationCaptionSuppressionReason.None);
            var context = new ClipEditorialContext(
                candidateId,
                SourcePath,
                "Test Game",
                TimeSpan.FromSeconds(12),
                TimeSpan.FromSeconds(28),
                media.Duration,
                91,
                "A retained deterministic moment.");
            context = context.WithVisualText(new ClipVisualTextContext(
                candidateId,
                SourcePath,
                NormalizedRectangle.FullFrame,
                frames: [],
                anchors:
                [
                    new VisualTextAnchor(
                        "hidden route",
                        "Hidden Route",
                        VisualTextAnchorAuthority.RepeatedAcrossFrames,
                        [TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(13)]),
                ]));
            var metadata = new ClipEditorialMetadataDraft(
                "A grounded title",
                "I found the hidden route during this run.",
                ["TestGame"],
                ClipEditorialMetadataOrigin.UserEdited,
                new ClipEditorialMetadataGeneratorIdentity("test", "1.0"),
                attempt: 1,
                readiness: ClipEditorialMetadataReadiness.UserApproved,
                priorAcceptedTitles:
                [
                    "An earlier grounded title #TestGame",
                ]);
            var features = new ClipPreferenceFeatureVector(
                [new ClipPreferenceFeature(
                    ClipPreferenceFeatureCode.Duration,
                    0.5)]);
            var appearance = new StudioClipAppearance(
                GenerationCaptionStylePreset.KaraokeSweep,
                72,
                StudioVideoEffectPreset.Noir,
                45,
                captionWordLimit: StudioCaptionWordLimitPreset.Streamlined,
                captionMaximumWidthPercent: 75,
                captionFontScalePercent: 110);
            GenerationOutputAsset asset =
                GenerationOutputAsset.RestoreStudioHandoff(
                    candidateId,
                    rank: 1,
                    media,
                    outputFullPath: null,
                    thumbnailFullPath: null,
                    TimeSpan.FromSeconds(12),
                    TimeSpan.FromSeconds(28),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(30),
                    score: 91,
                    qualityTarget: 80,
                    GenerationCandidateSelectionReason.QualityQualified,
                    "A retained deterministic moment.",
                    captions,
                    appearance,
                    context,
                    metadata,
                    features,
                    GenerationOutputAssetDisposition.IncludeInFinalRender);
            return new GenerationOutputProject(
                "project-persistence",
                GenerationMode.IndividualClips,
                Path.Combine(Root, "output"),
                requestedCount: 1,
                ClipFulfillmentPreference.QualityFirst,
                GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
                [asset],
                DateTimeOffset.UtcNow,
                resultCountMode: GenerationResultCountMode.Exact,
                candidateSetFingerprint: "candidates-test");
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class RecordingProjectStore : IStudioProjectStore
    {
        public List<(GenerationOutputProject Project, long Revision)> Saved { get; } = [];

        public void Save(GenerationOutputProject project, long revision,
            StudioProjectRecoveryState? recovery = null) =>
            Saved.Add((project, revision));

        public StudioProjectLoadResult Load(string projectId) =>
            new(StudioProjectLoadOutcome.NotFound, "not found");

        public bool Exists(string projectId) => false;

        public void Delete(string projectId)
        {
        }
    }

    private sealed class FailingProjectStore : IStudioProjectStore
    {
        public void Save(
            GenerationOutputProject project,
            long revision,
            StudioProjectRecoveryState? recovery = null) =>
            throw new IOException("diagnostic save failure");

        public StudioProjectLoadResult Load(string projectId) =>
            new(StudioProjectLoadOutcome.NotFound, "not found");

        public bool Exists(string projectId) => false;

        public void Delete(string projectId)
        {
        }
    }

    private sealed class NonPumpingSingleThreadSynchronizationContext :
        SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            // A WPF dispatcher cannot run posted work while its UI thread is
            // synchronously blocked in Dispose. Intentionally leave posted
            // callbacks pending so this test detects captured continuations.
        }
    }
}
