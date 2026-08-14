using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.RecentProjects;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Features.Research;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    private static Task StudioProjectSwitchCommitsPendingEdits()
    {
        GenerationOutputProject first = CreateStudioQueueProject(1);
        GenerationOutputProject second = CreateStudioQueueProject(1);
        string storePath = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundryProjectSwitch-" + Guid.NewGuid().ToString("N"),
            "recent.json");
        var session = new GenerationOutputSession();
        using var catalog = new RecentGenerationProjectCatalog(
            session,
            new JsonRecentGenerationProjectStore(storePath));
        session.Publish(first);
        using var studio = new StudioViewModel(
            session,
            session,
            new RecordingStudioClipRenderer());
        TimeSpan expectedStart = first.PrimaryAsset.SourceStart -
            TimeSpan.FromSeconds(5);

        studio.Inspector.Clip.StartAdjustmentSeconds = -5;
        studio.Inspector.Clip.SelectedVideoEffect =
            studio.Inspector.Clip.VideoEffectOptions.Single(option =>
                option.Value == StudioVideoEffectPreset.Noir);
        studio.Inspector.Clip.VideoEffectIntensityPercent = 64;
        studio.Inspector.Editorial.Title = "Saved before switching projects";

        StudioProjectSwitchResult result =
            studio.TrySwitchProject(second);

        TestAssert.Equal(
            StudioProjectSwitchOutcome.Switched,
            result.Outcome,
            "A valid pending Studio draft should commit before the project switch.");
        TestAssert.Equal(second.Id, session.Current?.Id,
            "The requested project should become current only after the commit succeeds.");
        TestAssert.True(catalog.TryGetStudioProject(
                first.Id,
                out GenerationOutputProject? savedFirst),
            "The recent cache should retain the committed outgoing project.");
        TestAssert.Equal(expectedStart, savedFirst!.PrimaryAsset.SourceStart,
            "Pending trim changes must survive the project switch.");
        TestAssert.Equal(
            StudioVideoEffectPreset.Noir,
            savedFirst.PrimaryAsset.Appearance.VideoEffect,
            "Pending appearance changes must survive the project switch.");
        TestAssert.Equal(
            64d,
            savedFirst.PrimaryAsset.Appearance.VideoEffectIntensityPercent,
            "The pending appearance intensity must be committed exactly.");
        TestAssert.Equal(
            "Saved before switching projects",
            savedFirst.PrimaryAsset.EditorialMetadata?.Title,
            "Unsaved metadata must be committed after the pending cut is applied.");
        TestAssert.True(
            savedFirst.PrimaryAsset.IsEditorialMetadataCurrentForCut,
            "Metadata saved during switching must bind to the committed cut.");

        string? directory = Path.GetDirectoryName(storePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
        return Task.CompletedTask;
    }

    private static async Task StudioProjectSwitchBlocksActiveRender()
    {
        GenerationOutputProject first = CreateStudioQueueProject(1);
        GenerationOutputProject second = CreateStudioQueueProject(1);
        var session = new GenerationOutputSession();
        session.Publish(first);
        var renderer = new BlockingStudioClipRenderer();
        using var studio = new StudioViewModel(session, session, renderer);
        studio.FinalRender.AddToQueueCommand.Execute(null);
        Task render = studio.FinalRender.FinalizeProjectAsync();
        await renderer.Started;

        StudioProjectSwitchResult result =
            studio.TrySwitchProject(second);

        TestAssert.Equal(
            StudioProjectSwitchOutcome.BlockedActiveRender,
            result.Outcome,
            "An active render must own the current Studio project until it completes or is cancelled.");
        TestAssert.False(result.Succeeded,
            "A blocked render-time switch must not report success.");
        TestAssert.Equal(first.Id, session.Current?.Id,
            "The active render's project must remain current.");
        TestAssert.Equal(first.PrimaryAsset.Id, studio.SelectedAsset?.Id,
            "The inspector must remain bound to the active render's project.");

        renderer.Release();
        await render;
    }

    private static Task StudioProjectSwitchBlocksQueuedDraft()
    {
        GenerationOutputProject first = CreateStudioQueueProject(1);
        GenerationOutputProject second = CreateStudioQueueProject(1);
        var session = new GenerationOutputSession();
        session.Publish(first);
        using var studio = new StudioViewModel(
            session,
            session,
            new RecordingStudioClipRenderer());
        studio.FinalRender.AddToQueueCommand.Execute(null);

        StudioProjectSwitchResult result =
            studio.TrySwitchProject(second);

        TestAssert.Equal(
            StudioProjectSwitchOutcome.BlockedUnsavedDraft,
            result.Outcome,
            "A prepared render queue must be resolved before another Studio " +
            "project can replace it.");
        TestAssert.True(
            result.Message.Contains(
                "render queue",
                StringComparison.OrdinalIgnoreCase),
            "The blocked switch must explain how to resolve the queued draft.");
        TestAssert.Equal(first.Id, session.Current?.Id,
            "A blocked queue switch must keep the outgoing project current.");
        TestAssert.Equal(
            1,
            studio.FinalRender.QueueItems.Count,
            "A blocked switch must preserve every queued clip.");

        return Task.CompletedTask;
    }

    private static Task StudioRenderQueueStartsEmptyAndPreservesClips()
    {
        GenerationOutputProject project = CreateStudioQueueProject(2);
        var session = new GenerationOutputSession();
        session.Publish(project);
        using var studio = new StudioViewModel(
            session,
            session,
            new RecordingStudioClipRenderer());

        TestAssert.Equal(
            0,
            studio.FinalRender.QueueItems.Count,
            "A fresh Studio project must not pretend its default kept set is already queued.");
        studio.FinalRender.AddToQueueCommand.Execute(null);
        TestAssert.Equal(
            1,
            studio.FinalRender.QueueItems.Count,
            "Add to render queue should add only the currently selected Browser clip.");
        TestAssert.False(
            studio.FinalRender.AddToQueueCommand.CanExecute(null),
            "Add to render queue should disable while the selected clip is already queued.");
        TestAssert.Equal(
            "Selected clip queued",
            studio.FinalRender.ButtonText,
            "The project-bar action should explain why the selected clip cannot be added twice.");
        studio.SelectBrowserAssetCommand.Execute(project.Assets[1].Id);
        studio.FinalRender.AddToQueueCommand.Execute(null);
        TestAssert.Equal(
            2,
            studio.FinalRender.QueueItems.Count,
            "Selecting another kept Browser clip should allow that clip to be added explicitly.");
        string firstId = project.Assets[0].Id;
        studio.SelectBrowserAssetCommand.Execute(firstId);
        studio.RemoveBrowserAssetCommand.Execute(firstId);

        TestAssert.False(
            session.Current!.Assets[0].IsIncludedInFinalRender,
            "The Browser trash action should non-destructively exclude the clip.");
        TestAssert.Equal(
            "Saved title 1",
            session.Current.Assets[0].EditorialMetadata!.Title,
            "Excluding a clip must retain its saved metadata.");
        TestAssert.Equal(
            1,
            studio.FinalRender.QueueItems.Count,
            "Excluding a Browser clip must atomically remove the same ID from the queue.");
        TestAssert.Equal(
            firstId,
            studio.Inspector.SelectedAsset?.Id,
            "Exclusion should rebind the selected clip by ID instead of clearing the preview.");

        studio.RestoreBrowserAssetCommand.Execute(firstId);
        TestAssert.True(
            session.Current.Assets[0].IsIncludedInFinalRender,
            "The retained clip should be restorable to the kept set.");
        TestAssert.Equal(
            1,
            studio.FinalRender.QueueItems.Count,
            "Restoring a clip must not silently add it to an explicit render queue.");
        StudioBrowserPreviewItem restored = studio.BrowserPreviewItems
            .Single(item => item.AssetId == firstId);
        TestAssert.True(restored.IsIncluded, "Restored Browser clip should be kept.");
        TestAssert.False(
            restored.IsQueued,
            "The Browser must distinguish kept clips from explicitly queued clips.");

        return Task.CompletedTask;
    }

    private static Task StudioBrowserInclusionPersistsFeedback()
    {
        GenerationOutputProject project = CreateStudioQueueProject(2);
        var session = new GenerationOutputSession();
        session.Publish(project);
        var decisions = new RecordingStudioCandidateDecisionStore();
        var participation = new ResearchParticipationState(
            new InMemoryResearchParticipationStore());
        participation.Enable(DateTimeOffset.UtcNow);
        var research = new InMemoryResearchFeedbackStore();
        using var studio = new StudioViewModel(
            session,
            session,
            new RecordingStudioClipRenderer(),
            new ClipEditorialMetadataGenerationService(
                new HeuristicClipEditorialMetadataGenerator()),
            new ClipEditorialProfileSession(),
            previewMediaService: null,
            preferenceService: null,
            decisionStore: decisions,
            researchFeedback: new ResearchFeedbackRecorder(
                participation,
                research));
        string assetId = project.Assets[1].Id;

        studio.RemoveBrowserAssetCommand.Execute(assetId);

        TestAssert.Equal(
            GenerationOutputAssetDisposition.ExcludeFromFinalRender,
            decisions.Find(assetId)!.Disposition,
            "Browser exclusion should persist the same candidate decision as the inspector.");
        ResearchFeedbackRecord excluded = research.Current.Single();
        TestAssert.Equal(
            ResearchFeedbackChannel.StudioSelection,
            excluded.Channel,
            "Browser exclusion should use the typed Studio-selection channel.");
        TestAssert.Equal(
            ResearchFeedbackValue.Excluded,
            excluded.Value,
            "Browser exclusion should persist excluded research feedback.");

        studio.RestoreBrowserAssetCommand.Execute(assetId);

        TestAssert.Equal(
            GenerationOutputAssetDisposition.IncludeInFinalRender,
            decisions.Find(assetId)!.Disposition,
            "Browser restoration should replace the persisted candidate disposition.");
        ResearchFeedbackRecord included = research.Current.Single();
        TestAssert.Equal(
            ResearchFeedbackChannel.StudioSelection,
            included.Channel,
            "Browser restoration should remain on the typed Studio-selection channel.");
        TestAssert.Equal(
            ResearchFeedbackValue.Included,
            included.Value,
            "Browser restoration should replace the research value with included.");

        return Task.CompletedTask;
    }

    private static async Task StudioRenderQueueFinalizesExactSubset()
    {
        GenerationOutputProject project = CreateStudioQueueProject(2);
        var session = new GenerationOutputSession();
        session.Publish(project);
        var renderer = new RecordingStudioClipRenderer();
        using var studio = new StudioViewModel(session, session, renderer);

        studio.FinalRender.AddToQueueCommand.Execute(null);
        studio.SelectBrowserAssetCommand.Execute(project.Assets[1].Id);
        studio.FinalRender.AddToQueueCommand.Execute(null);
        string removedId = project.Assets[0].Id;
        string queuedId = project.Assets[1].Id;
        studio.FinalRender.RemoveQueuedItemCommand.Execute(removedId);

        await studio.FinalRender.FinalizeProjectAsync();

        TestAssert.Equal(1, renderer.CallCount, "The queue should render one project batch.");
        TestAssert.Equal(
            1,
            renderer.LastDraft!.IncludedCount,
            "The rendering snapshot must include exactly the queue subset.");
        TestAssert.Equal(
            queuedId,
            renderer.LastDraft.IncludedAssets[0].Id,
            "The remaining queue ID must be the only rendered clip.");
        TestAssert.False(
            session.Current!.Assets.Single(asset => asset.Id == removedId).IsRendered,
            "Removing a queue item must retain it as an unrendered Studio asset.");
        TestAssert.Equal(
            "Saved title 1",
            session.Current.Assets.Single(asset => asset.Id == removedId)
                .EditorialMetadata!.Title,
            "Finalizing a subset must preserve excluded clip metadata.");
        TestAssert.Equal(
            1,
            renderer.AcceptCallCount,
            "A successful Library commit must release the renderer's completed-output ownership.");
        TestAssert.Equal(
            0,
            renderer.DiscardCallCount,
            "A successfully archived output must not be deleted.");
    }

    private static async Task
        StudioRenderCopiesRemainEditableAndLibraryAware()
    {
        GenerationOutputProject project = CreateStudioQueueProject(1);
        var session = new GenerationOutputSession();
        session.Publish(project);
        using var catalog = new GenerationLibraryCatalog(
            session,
            new InMemoryLibraryCatalogStore());
        var renderer = new RecordingStudioClipRenderer();
        using var studio = new StudioViewModel(
            session,
            session,
            renderer,
            new ClipEditorialMetadataGenerationService(
                new HeuristicClipEditorialMetadataGenerator()),
            new ClipEditorialProfileSession(),
            libraryCatalog: catalog);
        string assetId = project.PrimaryAsset.Id;

        studio.QueueBrowserAssetCommand.Execute(assetId);
        await studio.FinalRender.FinalizeProjectAsync();

        TestAssert.False(
            session.Current!.IsFinalized,
            "A completed Library copy must not lock the open Studio project.");
        TestAssert.Equal(1, catalog.Assets.Count,
            "The render commit should archive one exact Library copy.");
        TestAssert.True(
            catalog.Assets.Single().SourceCandidateIds.Contains(assetId),
            "The Library copy must retain the candidate identity needed for queue reconciliation.");
        TestAssert.True(
            studio.FinalRender.QueueItems.Single().IsCompleted,
            "The rendered queue item should report its Library copy.");
        TestAssert.True(
            studio.FinalRender.RemoveQueuedItemCommand.CanExecute(assetId),
            "A completed queue item must remain removable.");

        studio.FinalRender.RemoveQueuedItemCommand.Execute(assetId);
        studio.QueueBrowserAssetCommand.Execute(assetId);
        TestAssert.True(
            studio.FinalRender.QueueItems.Single().IsCompleted,
            "Re-adding a candidate with an existing Library copy should recognize that copy without rendering.");
        studio.FinalRender.RerenderQueuedItemCommand.Execute(assetId);
        TestAssert.False(
            studio.FinalRender.QueueItems.Single().IsCompleted,
            "The explicit re-render action should arm a new copy without deleting the existing one.");
        await studio.FinalRender.FinalizeProjectAsync();

        TestAssert.Equal(2, catalog.Assets.Count,
            "Re-rendering should add a secondary Library entry.");
        TestAssert.Equal(
            2,
            catalog.Assets.Select(static asset => asset.ProjectId)
                .Distinct(StringComparer.Ordinal).Count(),
            "Each re-render must use a unique immutable render-batch identity.");
        TestAssert.Equal(
            2,
            catalog.Assets.Select(static asset => asset.OutputFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "A re-render must never overwrite the first output path.");

        catalog.RemoveAssets(catalog.Assets.Select(static asset => asset.Id)
            .ToArray());
        TestAssert.False(
            studio.FinalRender.QueueItems.Single().IsCompleted,
            "Removing every matching Library copy must make the retained Studio queue item renderable again.");
        TestAssert.True(
            studio.FinalRender.RenderQueueCommand.CanExecute(null),
            "Library removal should immediately re-enable rendering for the retained queue item.");
    }

    private static async Task
        StudioRenderCommitFailureRollsBackAndDiscardsOutput()
    {
        GenerationOutputProject project = CreateStudioQueueProject(1);
        var session = new GenerationOutputSession();
        session.Publish(project);
        using var catalog = new GenerationLibraryCatalog(
            session,
            new FailingLibraryCatalogStore());
        var renderer = new RecordingStudioClipRenderer();
        using var studio = new StudioViewModel(session, session, renderer);

        studio.FinalRender.AddToQueueCommand.Execute(null);
        await studio.FinalRender.FinalizeProjectAsync();

        TestAssert.Same(
            project,
            session.Current!,
            "A failed persistent Library commit must roll the output session back to its exact draft.");
        TestAssert.False(
            session.Current!.IsFinalized,
            "Library persistence failure must keep Studio editable for retry.");
        TestAssert.Equal(
            0,
            catalog.Assets.Count,
            "A failed persistent commit must not publish an in-memory Library asset.");
        TestAssert.Equal(
            0,
            renderer.AcceptCallCount,
            "A failed Library commit must never accept the completed renderer output.");
        TestAssert.Equal(
            1,
            renderer.DiscardCallCount,
            "A failed Library commit must discard its now-orphaned completed output.");
        TestAssert.True(
            studio.FinalRender.HasError &&
            studio.FinalRender.QueueItems.Count == 1,
            "Commit failure must remain visible while preserving the explicit queue for retry.");
    }

    private static Task StudioHiddenMomentDoesNotAutoqueue()
    {
        GenerationOutputProject project =
            CreateStudioQueueProjectWithHiddenMoment(1);
        var session = new GenerationOutputSession();
        session.Publish(project);
        using var studio = new StudioViewModel(
            session,
            session,
            new RecordingStudioClipRenderer());

        studio.FinalRender.AddToQueueCommand.Execute(null);
        string queuedId = studio.FinalRender.QueueItems.Single().AssetId;
        string hiddenId = project.HiddenMoments.Single().Id;

        session.AcceptHiddenMoment(project.Id, hiddenId);

        TestAssert.Equal(
            2,
            session.Current!.Assets.Count,
            "The accepted Hidden Moment should be added to the open Studio project.");
        TestAssert.Equal(
            1,
            studio.FinalRender.QueueItems.Count,
            "Accepting a Hidden Moment must not silently expand an explicit render queue.");
        TestAssert.Equal(
            queuedId,
            studio.FinalRender.QueueItems.Single().AssetId,
            "The queue should retain only the ID the user explicitly added.");
        TestAssert.False(
            studio.FinalRender.QueueItems.Any(item => item.AssetId == hiddenId),
            "A newly accepted Hidden Moment needs a later explicit Add to render queue action.");

        return Task.CompletedTask;
    }

    private static Task StudioBrowserActionPreservesPendingEdit()
    {
        GenerationOutputProject project = CreateStudioQueueProject(2);
        var session = new GenerationOutputSession();
        session.Publish(project);
        using var studio = new StudioViewModel(
            session,
            session,
            new RecordingStudioClipRenderer());

        GenerationOutputAsset selected = project.Assets[0];
        string otherId = project.Assets[1].Id;
        studio.Inspector.Clip.StartAdjustmentSeconds = -5;
        studio.RemoveBrowserAssetCommand.Execute(otherId);

        GenerationOutputAsset savedSelected = session.Current!.Assets.Single(
            asset => asset.Id == selected.Id);
        TestAssert.Equal(
            selected.SourceStart - TimeSpan.FromSeconds(5),
            savedSelected.SourceStart,
            "Removing another card must first retain the visible selected-clip trim.");
        TestAssert.False(
            session.Current.Assets.Single(asset => asset.Id == otherId)
                .IsIncludedInFinalRender,
            "The requested card action should still apply after saving the pending edit.");

        return Task.CompletedTask;
    }

    private static async Task StudioMutationCommandsLockDuringRender()
    {
        GenerationOutputProject project =
            CreateStudioQueueProjectWithHiddenMoment(3);
        var session = new GenerationOutputSession();
        session.Publish(project);
        var renderer = new BlockingStudioClipRenderer();
        using var studio = new StudioViewModel(session, session, renderer);

        studio.HiddenMoments.OpenCommand.Execute(null);
        string excludedId = project.Assets[0].Id;
        string includedId = project.Assets[1].Id;
        studio.RemoveBrowserAssetCommand.Execute(excludedId);
        studio.SelectBrowserAssetCommand.Execute(includedId);
        studio.FinalRender.AddToQueueCommand.Execute(null);

        Task renderTask = studio.FinalRender.FinalizeProjectAsync();
        await renderer.Started.WaitAsync(TimeSpan.FromSeconds(5));
        GenerationOutputProject activeProject = session.Current!;
        string? selectedId = studio.Inspector.SelectedAsset?.Id;

        TestAssert.True(
            studio.FinalRender.IsRendering,
            "The controlled renderer should hold Studio in its active render state.");
        TestAssert.False(
            studio.SelectBrowserAssetCommand.CanExecute(includedId),
            "Selecting another clip must be blocked because it can commit a pending edit.");
        TestAssert.False(
            studio.RemoveBrowserAssetCommand.CanExecute(includedId),
            "Browser remove must be disabled while a render snapshot is active.");
        TestAssert.False(
            studio.RestoreBrowserAssetCommand.CanExecute(excludedId),
            "Browser restore must be disabled while a render snapshot is active.");
        TestAssert.False(
            studio.FinalRender.RemoveQueuedItemCommand.CanExecute(includedId),
            "Queue removal must stay disabled during its active render.");
        TestAssert.False(
            studio.FinalRender.AddToQueueCommand.CanExecute(null),
            "Queue additions must stay disabled during its active render.");
        TestAssert.False(
            studio.Inspector.Preference.CanChangeRenderInclusion,
            "Inspector render inclusion must receive the same active-render lock.");
        studio.Inspector.Preference.IsIncludedInFinalRender = false;
        TestAssert.True(
            studio.HiddenMoments.IsProjectMutationBlocked,
            "Hidden Moments should receive the same project-mutation lock.");
        TestAssert.False(
            studio.HiddenMoments.AcceptCommand.CanExecute(null) ||
            studio.HiddenMoments.SkipCommand.CanExecute(null) ||
            studio.HiddenMoments.OpenCommand.CanExecute(null),
            "Hidden Moment acceptance and decisions must be disabled during rendering.");
        TestAssert.Same(
            activeProject,
            session.Current!,
            "Checking disabled actions must leave the immutable session untouched.");
        TestAssert.Equal(
            selectedId,
            studio.Inspector.SelectedAsset?.Id,
            "The active render lock must not move the selected clip.");

        renderer.Release();
        await renderTask;
    }

    private static async Task StudioRenderPreservesConcurrentProjectMutation()
    {
        GenerationOutputProject project = CreateStudioQueueProject(2);
        var session = new GenerationOutputSession();
        session.Publish(project);
        var renderer = new BlockingStudioClipRenderer();
        using var studio = new StudioViewModel(session, session, renderer);

        studio.FinalRender.AddToQueueCommand.Execute(null);
        Task renderTask = studio.FinalRender.FinalizeProjectAsync();
        await renderer.Started.WaitAsync(TimeSpan.FromSeconds(5));

        GenerationOutputAsset newerAsset = session.Current!.Assets[0]
            .WithDisposition(
                GenerationOutputAssetDisposition.ExcludeFromFinalRender);
        session.ReplaceAsset(project.Id, newerAsset);
        renderer.Release();
        await renderTask;

        TestAssert.False(
            session.Current!.IsFinalized,
            "A completed stale render must not finalize over a newer Studio project version.");
        TestAssert.False(
            session.Current.Assets[0].IsIncludedInFinalRender,
            "The newer concurrent clip decision must remain intact.");
        TestAssert.True(
            studio.FinalRender.HasError &&
            studio.FinalRender.Error!.Contains(
                "Studio changed",
                StringComparison.Ordinal),
            "The user should receive an actionable changed-project render error.");
        TestAssert.Equal(
            1,
            renderer.DiscardCallCount,
            "A completed stale render must be discarded by the service that owns its output.");
        TestAssert.Equal(
            0,
            studio.FinalRender.QueueItems.Count,
            "The queue should rebind to the newer project and remove the concurrently excluded selected ID.");
    }

    private static async Task StudioRenderFailurePreservesQueue()
    {
        GenerationOutputProject cancelledProject = CreateStudioQueueProject(1);
        var cancelledSession = new GenerationOutputSession();
        cancelledSession.Publish(cancelledProject);
        var cancellingRenderer = new BlockingStudioClipRenderer();
        using (var studio = new StudioViewModel(
                   cancelledSession,
                   cancelledSession,
                   cancellingRenderer))
        {
            studio.FinalRender.AddToQueueCommand.Execute(null);
            Task renderTask = studio.FinalRender.FinalizeProjectAsync();
            await cancellingRenderer.Started.WaitAsync(TimeSpan.FromSeconds(5));
            studio.FinalRender.CancelCommand.Execute(null);
            await renderTask;

            TestAssert.Equal(
                1,
                studio.FinalRender.QueueItems.Count,
                "Cancellation must preserve the explicit queue for retry.");
            TestAssert.False(
                cancelledSession.Current!.IsFinalized,
                "A cancelled render must leave the Studio project editable.");
            TestAssert.True(
                studio.FinalRender.Status.Contains(
                    "cancelled",
                    StringComparison.OrdinalIgnoreCase),
                "Cancellation should be named clearly in render status.");
        }

        GenerationOutputProject failedProject = CreateStudioQueueProject(1);
        var failedSession = new GenerationOutputSession();
        failedSession.Publish(failedProject);
        var failingRenderer = new BlockingStudioClipRenderer(
            failAfterRelease: true);
        using (var studio = new StudioViewModel(
                   failedSession,
                   failedSession,
                   failingRenderer))
        {
            studio.FinalRender.AddToQueueCommand.Execute(null);
            Task renderTask = studio.FinalRender.FinalizeProjectAsync();
            await failingRenderer.Started.WaitAsync(TimeSpan.FromSeconds(5));
            failingRenderer.Release();
            await renderTask;

            TestAssert.Equal(
                1,
                studio.FinalRender.QueueItems.Count,
                "A render failure must preserve the explicit queue for retry.");
            TestAssert.False(
                failedSession.Current!.IsFinalized,
                "A failed render must leave the Studio project editable.");
            TestAssert.True(
                studio.FinalRender.HasError,
                "A render failure should remain visible instead of clearing the queue.");
        }
    }

    private static async Task StudioUnsavedMetadataBlocksQueue()
    {
        GenerationOutputProject project = CreateStudioQueueProject(1);
        var session = new GenerationOutputSession();
        session.Publish(project);
        var renderer = new RecordingStudioClipRenderer();
        using var studio = new StudioViewModel(session, session, renderer);

        studio.Inspector.Editorial.Title = "Visible saved title";

        TestAssert.True(
            studio.Inspector.Editorial.HasUnsavedChanges,
            "Typing in the visible metadata fields must create an explicit unsaved state.");
        TestAssert.False(
            studio.FinalRender.AddToQueueCommand.CanExecute(null),
            "Unsaved visible metadata must block Add to render queue.");
        TestAssert.True(
            studio.FinalRender.NeedsMetadataSave,
            "Render readiness should name the save requirement.");

        studio.Inspector.Editorial.SaveCommand.Execute(null);

        TestAssert.False(
            studio.Inspector.Editorial.HasUnsavedChanges,
            "A successful metadata save must update the saved baseline.");
        TestAssert.True(
            studio.FinalRender.AddToQueueCommand.CanExecute(null),
            "Saving metadata should immediately unlock queueing.");
        studio.FinalRender.AddToQueueCommand.Execute(null);
        await studio.FinalRender.FinalizeProjectAsync();

        TestAssert.Equal(
            "Visible saved title",
            renderer.LastDraft!.PrimaryAsset.EditorialMetadata!.Title,
            "The renderer must receive the saved value that was visible in Studio.");
    }

    private static Task StudioPendingTrimRevalidatesMetadata()
    {
        GenerationOutputProject project = CreateStudioQueueProject(1);
        var session = new GenerationOutputSession();
        session.Publish(project);
        using var studio = new StudioViewModel(
            session,
            session,
            new RecordingStudioClipRenderer());

        studio.Inspector.Clip.StartAdjustmentSeconds = -5;
        TestAssert.True(
            studio.FinalRender.AddToQueueCommand.CanExecute(null),
            "A saved pre-trim project starts ready to queue.");

        studio.FinalRender.AddToQueueCommand.Execute(null);

        TestAssert.Equal(
            1,
            studio.FinalRender.QueueItems.Count,
            "Applying a pending trim should queue the exact visible cut without requiring publish metadata.");
        TestAssert.False(
            session.Current!.PrimaryAsset.IsEditorialMetadataCurrentForCut,
            "The immutable edited asset must record that its old copy describes a different cut.");
        TestAssert.True(
            studio.FinalRender.IsReadyToRender,
            "Local rendering to Library must remain independent from later publish-metadata readiness.");

        return Task.CompletedTask;
    }

    private static Task StudioInvalidTrimCannotQueueOldCut()
    {
        GenerationOutputProject project = CreateStudioQueueProject(1);
        var session = new GenerationOutputSession();
        session.Publish(project);
        var renderer = new RecordingStudioClipRenderer();
        using var studio = new StudioViewModel(session, session, renderer);

        studio.Inspector.Clip.StartAdjustmentSeconds =
            studio.Inspector.Clip.StartAdjustmentMaximumSeconds;
        studio.Inspector.Clip.EndAdjustmentSeconds =
            studio.Inspector.Clip.EndAdjustmentMinimumSeconds;

        TestAssert.False(
            studio.Inspector.Clip.IsBoundaryDraftValid,
            "The focused fixture must cross the visible start and end boundaries.");
        TestAssert.True(
            studio.Inspector.Clip.HasPendingEdit,
            "The invalid visible range remains an explicit unsaved clip draft.");
        TestAssert.True(
            studio.FinalRender.NeedsValidClipEdit,
            "Render readiness should identify the invalid visible range.");
        TestAssert.False(
            studio.FinalRender.AddToQueueCommand.CanExecute(null),
            "An invalid visible range must not queue the previously saved cut.");
        TestAssert.Equal(
            0,
            studio.FinalRender.QueueItems.Count,
            "No old-cut queue entry may appear while the visible range is invalid.");
        TestAssert.Equal(
            0,
            renderer.CallCount,
            "Validation must stop before any renderer invocation.");

        studio.Inspector.Clip.ResetBoundaryDraftCommand.Execute(null);
        TestAssert.True(
            studio.FinalRender.AddToQueueCommand.CanExecute(null),
            "Resetting to the valid saved range should restore queue readiness.");
        TestAssert.Equal(
            project.PrimaryAsset.SourceStart.TotalSeconds,
            studio.Preview.PreviewPositionMinimumSeconds,
            "Resetting an invalid trim must restore the saved preview start.");
        TestAssert.Equal(
            project.PrimaryAsset.SourceEnd.TotalSeconds,
            studio.Preview.PreviewPositionMaximumSeconds,
            "Resetting an invalid trim must restore the saved preview end.");

        return Task.CompletedTask;
    }

    private static Task StudioTimeLabelsUseWholeSeconds()
    {
        TestAssert.Equal(
            "1:02:03",
            StudioTimeFormatter.FormatTime(
                TimeSpan.FromHours(1) +
                TimeSpan.FromMinutes(2) +
                TimeSpan.FromSeconds(3.9)),
            "Studio time labels should omit frames and milliseconds.");
        TestAssert.Equal(
            "0:00",
            StudioTimeFormatter.FormatAdjustment(-0.4),
            "A rounded zero adjustment must not display a negative-zero sign.");
        TestAssert.Equal(
            "+0:01",
            StudioTimeFormatter.FormatAdjustment(1.4),
            "Studio adjustment labels should use whole-second time notation.");
        return Task.CompletedTask;
    }

    private static Task StudioPreviewRejectsStaleSeekTicks()
    {
        GenerationOutputProject project = CreateStudioQueueProject(1);
        using var mediaService = new ImmediateStudioPreviewMediaService();
        using var preview = new StudioPreviewViewModel(mediaService);
        GenerationOutputAsset asset = project.PrimaryAsset;
        preview.Bind(hasProject: true, project, asset);
        TestAssert.True(
            preview.IsPreviewAvailable,
            "The synchronous test preview should be available before seeking.");

        double oldPosition = asset.SourceStart.TotalSeconds;
        double soughtPosition = oldPosition + 10;
        preview.PreviewPositionSeconds = soughtPosition;
        TestAssert.True(
            preview.RequiresPlaybackPositionSampling,
            "A paused user seek must keep sampling native playback until MediaElement converges.");
        for (int staleTick = 0; staleTick < 12; staleTick++)
        {
            preview.ReportPlaybackPosition(TimeSpan.FromSeconds(
                oldPosition - preview.PreviewSourceOffsetSeconds));
        }
        TestAssert.Equal(
            soughtPosition,
            preview.PreviewPositionSeconds,
            "Any number of stale MediaElement ticks must not snap a fresh user seek backward.");

        preview.ReportPlaybackPosition(TimeSpan.FromSeconds(
            soughtPosition - preview.PreviewSourceOffsetSeconds));
        TestAssert.False(
            preview.RequiresPlaybackPositionSampling,
            "A converged paused seek should stop background playback sampling.");
        preview.ReportPlaybackPosition(TimeSpan.FromSeconds(
            soughtPosition + 1 - preview.PreviewSourceOffsetSeconds));
        TestAssert.Equal(
            soughtPosition + 1,
            preview.PreviewPositionSeconds,
            "Once playback converges on the seek target, normal ticks should advance again.");

        preview.BeginScrub();
        preview.PreviewPositionSeconds = soughtPosition + 5;
        int versionBeforeEnd = preview.PreviewSeekVersion;
        preview.EndScrub();
        TestAssert.Equal(
            versionBeforeEnd + 1,
            preview.PreviewSeekVersion,
            "Ending a scrub should publish exactly one final seek version.");

        string code = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "ReplayFoundry.Desktop",
            "Features",
            "Studio",
            "Preview",
            "StudioPreviewView.xaml.cs"));
        int start = code.IndexOf(
            "private void EndScrub()",
            StringComparison.Ordinal);
        int end = code.IndexOf(
            "private void PreviewPlayer_OnMediaOpened",
            start,
            StringComparison.Ordinal);
        string endScrubBody = code[start..end];
        TestAssert.False(
            endScrubBody.Contains("ApplyPosition();", StringComparison.Ordinal),
            "The code-behind must not issue a second seek after the view model resumes playback.");
        int tickStart = code.IndexOf(
            "private void OnPositionTimerTick",
            StringComparison.Ordinal);
        int tickEnd = code.IndexOf(
            "private void PreviewPosition_OnPreviewMouseLeftButtonDown",
            tickStart,
            StringComparison.Ordinal);
        TestAssert.True(
            code[tickStart..tickEnd].Contains(
                "RequiresPlaybackPositionSampling",
                StringComparison.Ordinal),
            "The Studio playback timer must continue observing paused seeks until they converge.");

        return Task.CompletedTask;
    }

    private static Task StudioPreviewRecoversConsumedScrubRelease()
    {
        GenerationOutputProject project = CreateStudioQueueProject(1);
        using var mediaService = new ImmediateStudioPreviewMediaService();
        using var preview = new StudioPreviewViewModel(mediaService);
        GenerationOutputAsset asset = project.PrimaryAsset;
        preview.Bind(hasProject: true, project, asset);

        double selectedStart = asset.SourceStart.TotalSeconds;
        double selectedStartInProxy =
            selectedStart - preview.PreviewSourceOffsetSeconds;
        preview.ReportPlaybackPosition(TimeSpan.FromSeconds(
            selectedStartInProxy));
        TestAssert.True(
            preview.IsPreviewSynchronized,
            "The native preview must synchronize before exercising playback.");

        preview.BeginScrub();
        preview.PlayCommand.Execute(null);
        TestAssert.True(
            preview.IsPreviewPlaying,
            "Pressing Play after a consumed Slider release must still start native playback.");
        preview.ReportPlaybackPosition(TimeSpan.FromSeconds(
            selectedStartInProxy + 3));
        TestAssert.Equal(
            selectedStart + 3,
            preview.PreviewPositionSeconds,
            "A stale scrub latch must not discard the MediaElement clock after playback starts.");
        TestAssert.True(
            preview.PreviewTimecode.StartsWith("0:03", StringComparison.Ordinal),
            "Recovering the stale scrub latch must advance the visible clock used by live captions.");

        string viewCode = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "ReplayFoundry.Desktop",
            "Features",
            "Studio",
            "Preview",
            "StudioPreviewView.xaml.cs"));
        TestAssert.True(
            viewCode.Contains("handledEventsToo: true", StringComparison.Ordinal) &&
            viewCode.Contains("Mouse.PreviewMouseUpEvent", StringComparison.Ordinal) &&
            viewCode.Contains("Mouse.LostMouseCaptureEvent", StringComparison.Ordinal),
            "The Studio Slider must observe handled release and capture events emitted by its native Thumb.");

        return Task.CompletedTask;
    }

    private static Task StudioPreviewClockAdvancesWhenNativePositionStalls()
    {
        TestAssert.Equal(
            3d,
            StudioPreviewView.ResolvePlaybackPositionSeconds(
                playbackStartedProxySeconds: 0,
                elapsedSeconds: 3,
                nativePositionSeconds: 0,
                maximumProxySeconds: 47),
            "A visibly playing MediaElement whose native Position remains zero must still advance the Studio playhead and caption clock.");
        TestAssert.Equal(
            3.1d,
            StudioPreviewView.ResolvePlaybackPositionSeconds(
                playbackStartedProxySeconds: 0,
                elapsedSeconds: 3,
                nativePositionSeconds: 3.1,
                maximumProxySeconds: 47),
            "A native MediaElement clock that follows playback should remain authoritative.");
        TestAssert.Equal(
            3d,
            StudioPreviewView.ResolvePlaybackPositionSeconds(
                playbackStartedProxySeconds: 0,
                elapsedSeconds: 3,
                nativePositionSeconds: 20,
                maximumProxySeconds: 47),
            "An implausible native clock jump must not move the playhead or captions away from visible real time.");
        TestAssert.Equal(
            47d,
            StudioPreviewView.ResolvePlaybackPositionSeconds(
                playbackStartedProxySeconds: 45,
                elapsedSeconds: 5,
                nativePositionSeconds: 0,
                maximumProxySeconds: 47),
            "The monotonic fallback clock must stop at the bounded preview end.");
        return Task.CompletedTask;
    }

    private static Task StudioCaptionAppearanceEditsPreservePlayhead()
    {
        GenerationOutputProject project = CreateStudioQueueProject(1);
        using var mediaService = new ImmediateStudioPreviewMediaService();
        using var preview = new StudioPreviewViewModel(mediaService);
        GenerationOutputAsset asset = project.PrimaryAsset;
        preview.Bind(hasProject: true, project, asset);
        double editPoint = asset.SourceStart.TotalSeconds + 8;
        preview.PreviewPositionSeconds = editPoint;

        var captionOnlyAppearance = new StudioClipAppearance(
            GenerationCaptionStylePreset.Pop,
            42,
            asset.Appearance.VideoEffect,
            asset.Appearance.VideoEffectIntensityPercent,
            asset.Appearance.GraphicOverlays,
            asset.Appearance.CaptionWordLimit,
            64,
            135);
        GenerationOutputAsset captionOnlyAsset = asset.WithStudioEdits(
            asset.SourceStart,
            asset.SourceEnd,
            captionOnlyAppearance);
        GenerationOutputProject captionOnlyProject =
            project.ReplaceAsset(captionOnlyAsset);
        preview.Bind(
            hasProject: true,
            captionOnlyProject,
            captionOnlyAsset);

        TestAssert.Equal(
            1,
            mediaService.MaterializeCount,
            "Live caption-only edits must not rebuild unchanged preview media.");
        TestAssert.Equal(
            editPoint,
            preview.PreviewPositionSeconds,
            "Caption position, width, size, and style edits must retain the current playhead.");

        var effectAppearance = new StudioClipAppearance(
            captionOnlyAppearance.CaptionStyle,
            captionOnlyAppearance.CaptionVerticalPositionPercent,
            StudioVideoEffectPreset.Noir,
            40,
            captionOnlyAppearance.GraphicOverlays,
            captionOnlyAppearance.CaptionWordLimit,
            captionOnlyAppearance.CaptionMaximumWidthPercent,
            captionOnlyAppearance.CaptionFontScalePercent);
        GenerationOutputAsset effectAsset = captionOnlyAsset.WithStudioEdits(
            captionOnlyAsset.SourceStart,
            captionOnlyAsset.SourceEnd,
            effectAppearance);
        GenerationOutputProject effectProject =
            captionOnlyProject.ReplaceAsset(effectAsset);
        preview.Bind(hasProject: true, effectProject, effectAsset);

        TestAssert.Equal(
            2,
            mediaService.MaterializeCount,
            "A video-effect edit must still rebuild the bounded preview.");
        TestAssert.Equal(
            editPoint,
            preview.PreviewPositionSeconds,
            "A required preview-media refresh must return to the current edit point rather than zero.");
        return Task.CompletedTask;
    }

    private static Task StudioClipSelectionResetsPreviewPlayhead()
    {
        GenerationOutputProject project = CreateStudioQueueProject(2);
        using var preview = new StudioPreviewViewModel(mediaService: null);
        GenerationOutputAsset first = project.Assets[0];
        GenerationOutputAsset second = project.Assets[1];
        preview.Bind(hasProject: true, project, first);
        preview.PreviewPositionSeconds = first.SourceEnd.TotalSeconds;
        preview.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(
                    StudioPreviewViewModel.PreviewPositionMaximumSeconds))
            {
                // A two-way WPF Slider can synchronously report its coerced
                // old value while Minimum/Maximum are being replaced.
                preview.PreviewPositionSeconds = second.SourceEnd.TotalSeconds;
            }
        };

        preview.Bind(hasProject: true, project, second);

        TestAssert.Equal(
            second.SourceStart.TotalSeconds,
            preview.PreviewPositionSeconds,
            "Selecting a different Browser clip must open at that clip's start rather than inherit or clamp the previous playhead.");
        TestAssert.Equal(
            "0:00",
            preview.PreviewTimecode,
            "The newly selected clip must present an unambiguous zero-based preview clock.");
        return Task.CompletedTask;
    }

    private static Task StudioPreviewReportsNeverConvergingSeek()
    {
        GenerationOutputProject project = CreateStudioQueueProject(1);
        using var mediaService = new ImmediateStudioPreviewMediaService();
        var timeProvider = new ManualTimeProvider();
        using var preview = new StudioPreviewViewModel(
            mediaService,
            timeProvider: timeProvider);
        GenerationOutputAsset asset = project.PrimaryAsset;
        preview.Bind(hasProject: true, project, asset);

        double stalePosition = asset.SourceStart.TotalSeconds;
        double requestedPosition = stalePosition + 10;
        preview.PreviewPositionSeconds = requestedPosition;
        int initialSeekVersion = preview.PreviewSeekVersion;

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            timeProvider.Advance(TimeSpan.FromSeconds(1));
            preview.ReportPlaybackPosition(TimeSpan.FromSeconds(
                stalePosition - preview.PreviewSourceOffsetSeconds));
            TestAssert.Equal(
                initialSeekVersion + attempt,
                preview.PreviewSeekVersion,
                "A never-converging MediaElement seek should be reissued on an elapsed-time boundary.");
            TestAssert.Equal(
                requestedPosition,
                preview.PreviewPositionSeconds,
                "A retry must retain the user's requested scrubber position.");
        }

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        preview.ReportPlaybackPosition(TimeSpan.FromSeconds(
            stalePosition - preview.PreviewSourceOffsetSeconds));

        TestAssert.True(
            preview.HasPreviewError &&
            preview.PreviewError!.Contains(
                "could not reach",
                StringComparison.OrdinalIgnoreCase),
            "A seek that never converges should surface a reloadable preview error instead of jittering forever.");
        TestAssert.Equal(
            requestedPosition,
            preview.PreviewPositionSeconds,
            "Failure must not snap the visible scrubber back to an obsolete playback tick.");

        return Task.CompletedTask;
    }

    private static Task StudioPreviewMediaOpenedDoesNotResetSource()
    {
        GenerationOutputProject project = CreateStudioQueueProject(1);
        using var mediaService = new ImmediateStudioPreviewMediaService();
        using var preview = new StudioPreviewViewModel(mediaService);
        preview.Bind(hasProject: true, project, project.PrimaryAsset);

        var changed = new List<string>();
        preview.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is { } propertyName)
            {
                changed.Add(propertyName);
            }
        };
        preview.ReportOpened();

        TestAssert.False(
            changed.Contains(nameof(StudioPreviewViewModel.PreviewMediaPath)),
            "MediaOpened must update status without re-publishing the source URI and recursively rebuilding the native media graph.");
        TestAssert.True(
            changed.Contains(nameof(StudioPreviewViewModel.PreviewStatus)),
            "MediaOpened should still publish its bounded-positioning status.");

        string code = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "ReplayFoundry.Desktop",
            "Features",
            "Studio",
            "Preview",
            "StudioPreviewView.xaml.cs"));
        TestAssert.True(
            code.Contains(
                "Equals(PreviewPlayer.Source, requestedSource)",
                StringComparison.Ordinal) &&
            code.Contains(
                "new DispatcherTimer(",
                StringComparison.Ordinal) &&
            code.Contains(
                "DispatcherPriority.Normal",
                StringComparison.Ordinal) &&
            code.Contains(
                "_positionTimer.Stop();",
                StringComparison.Ordinal),
            "The Studio media surface must ignore an unchanged source and use a dependable UI-thread playback clock that is stopped with the native media graph.");
        return Task.CompletedTask;
    }

    private static Task StudioPreviewSynchronizesPrerollBeforePlayback()
    {
        GenerationOutputProject project = CreateStudioQueueProject(1);
        // The production preview request already begins at the earliest
        // one-minute edit context, before this selected clip's 20-second
        // start, so this fixture naturally contains pre-roll.
        using var mediaService = new ImmediateStudioPreviewMediaService();
        using var preview = new StudioPreviewViewModel(mediaService);
        GenerationOutputAsset asset = project.PrimaryAsset;
        preview.Bind(hasProject: true, project, asset);

        TestAssert.False(preview.IsPreviewSynchronized,
            "A bounded proxy with pre-roll must not enable playback before its selected-start seek is observed.");
        TestAssert.False(preview.PlayCommand.CanExecute(null),
            "Play must stay disabled while native playback is still at the proxy's pre-roll origin.");
        double selectedStart = asset.SourceStart.TotalSeconds;
        preview.ReportPlaybackPosition(TimeSpan.Zero);
        TestAssert.Equal(selectedStart, preview.PreviewPositionSeconds,
            "A stale pre-roll tick must not move the visible Studio clock or caption time backward.");

        double selectedStartInProxy =
            selectedStart - preview.PreviewSourceOffsetSeconds;
        preview.ReportPlaybackPosition(TimeSpan.FromSeconds(
            selectedStartInProxy));
        TestAssert.True(preview.IsPreviewSynchronized,
            "Observing the requested selected-start position should complete initial synchronization.");
        TestAssert.True(preview.PlayCommand.CanExecute(null),
            "Play should become available only after initial synchronization.");
        preview.PlayCommand.Execute(null);
        preview.ReportPlaybackPosition(TimeSpan.FromSeconds(
            selectedStartInProxy + 1));
        TestAssert.Equal(selectedStart + 1, preview.PreviewPositionSeconds,
            "Native playback ticks must advance the visible clock and the caption lookup position together.");
        TestAssert.True(
            preview.PreviewTimecode.StartsWith("0:01", StringComparison.Ordinal),
            "The on-screen selected-clip clock must advance while playback runs.");

        return Task.CompletedTask;
    }

    private static GenerationOutputProject CreateStudioQueueProject(int count)
    {
        string outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundryStudioQueueTests-" + Guid.NewGuid().ToString("N"));
        GenerationOutputAsset[] assets = Enumerable.Range(1, count)
            .Select(rank =>
            {
                string id = $"studio-queue-{rank}";
                var media = TestMediaFactory.Create(
                    TestMediaFactory.CreateSourcePath($"queue-{rank}.mkv"),
                    TimeSpan.FromMinutes(5),
                    hasAudio: true);
                TimeSpan start = TimeSpan.FromSeconds(rank * 20);
                TimeSpan end = start + TimeSpan.FromSeconds(30);
                var context = new ClipEditorialContext(
                    id,
                    media.FullPath,
                    "ExampleGame",
                    start,
                    end,
                    media.Duration,
                    85,
                    "A bounded visible action supports this saved copy.");
                var metadata = new ClipEditorialMetadataDraft(
                    $"Saved title {rank}",
                    $"Saved description {rank} for this bounded clip.",
                    ["ExampleGame"],
                    ClipEditorialMetadataOrigin.UserEdited,
                    new ClipEditorialMetadataGeneratorIdentity(
                        "Studio queue tests",
                        "1.0.0"),
                    attempt: 0,
                    readiness: ClipEditorialMetadataReadiness.UserApproved);
                return new GenerationOutputAsset(
                    id,
                    rank,
                    media,
                    outputFullPath: null,
                    start,
                    end,
                    85,
                    70,
                    GenerationCandidateSelectionReason.QualityQualified,
                    "test",
                    preferenceFeatures: new ClipPreferenceFeatureVector(
                    [
                        new ClipPreferenceFeature(
                            ClipPreferenceFeatureCode.Duration,
                            0.1),
                        new ClipPreferenceFeature(
                            ClipPreferenceFeatureCode.DeterministicScore,
                            0.85),
                    ]),
                    editorialContext: context,
                    editorialMetadata: metadata);
            })
            .ToArray();
        return new GenerationOutputProject(
            "studio-queue-project-" + Guid.NewGuid().ToString("N"),
            GenerationMode.IndividualClips,
            outputDirectory,
            count,
            ClipFulfillmentPreference.QualityFirst,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
            assets,
            DateTimeOffset.UnixEpoch);
    }

    private static GenerationOutputProject
        CreateStudioQueueProjectWithHiddenMoment(int count)
    {
        GenerationOutputProject project = CreateStudioQueueProject(count);
        GenerationMomentFindingRequest request =
            GenerationMomentFindingTests.CreateRequest(
                sourceCount: 1,
                desiredCount: 1,
                qualityThreshold: 70,
                fulfillmentPreference:
                    ClipFulfillmentPreference.QualityFirst);
        GenerationMomentFindingResult moments =
            new GenerationMomentFindingService(
                new GenerationMomentFindingTests.RecordingMomentFinder(
                    new IReadOnlyList<double>[]
                    {
                        new double[] { 90, 80 },
                    }))
            .Find(request);
        GenerationHiddenMomentDeck deck =
            GenerationHiddenMomentPlanner.Create(moments);

        return new GenerationOutputProject(
            project.Id,
            project.Mode,
            project.OutputDirectory,
            project.RequestedCount,
            project.FulfillmentPreference,
            project.FulfillmentOutcome,
            project.Assets,
            project.CreatedAtUtc,
            resultCountMode: project.ResultCountMode,
            hiddenMoments: deck.Moments);
    }

    private sealed class BlockingStudioClipRenderer :
        IStudioProjectRenderingService
    {
        private readonly bool _failAfterRelease;
        private readonly TaskCompletionSource<bool> _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingStudioClipRenderer(bool failAfterRelease = false) =>
            _failAfterRelease = failAfterRelease;

        public Task Started => _started.Task;
        public int AcceptCallCount { get; private set; }
        public int DiscardCallCount { get; private set; }

        public void Release() => _release.TrySetResult(true);

        public async Task<StudioProjectRenderResult> FinalizeAsync(
            GenerationOutputProject draft,
            IProgress<StudioProjectRenderProgress> progress,
            CancellationToken cancellationToken)
        {
            _started.TrySetResult(true);
            await _release.Task.WaitAsync(cancellationToken);
            if (_failAfterRelease)
            {
                throw new InvalidOperationException(
                    "Controlled render failure.");
            }

            GenerationOutputAsset[] rendered = draft.IncludedAssets
                .Select(asset => asset.WithRenderedOutput(
                    Path.Combine(
                        draft.OutputDirectory,
                        $"studio-{asset.Rank:D3}.mp4")))
                .ToArray();
            GenerationOutputProject finalized = draft.Finalize(
                rendered,
                DateTimeOffset.UtcNow);
            return new StudioProjectRenderResult(
                draft,
                finalized,
                TimeSpan.Zero);
        }

        public void AcceptCompletedRender(StudioProjectRenderResult result) =>
            AcceptCallCount++;

        public void DiscardCompletedRender(StudioProjectRenderResult result) =>
            DiscardCallCount++;
    }

    private sealed class FailingLibraryCatalogStore : ILibraryCatalogStore
    {
        public IReadOnlyList<LibraryMediaAsset> Current => [];

        public void Replace(IReadOnlyList<LibraryMediaAsset> assets) =>
            throw new IOException("Controlled Library catalog commit failure.");
    }

    private sealed class RecordingStudioCandidateDecisionStore :
        IStudioCandidateDecisionStore
    {
        private readonly Dictionary<string, StudioCandidateDecision> _values =
            new(StringComparer.Ordinal);

        public IReadOnlyList<StudioCandidateDecision> Current =>
            _values.Values.ToArray();

        public StudioCandidateDecision? Find(string candidateId) =>
            _values.GetValueOrDefault(candidateId);

        public void Upsert(StudioCandidateDecision decision) =>
            _values[decision.CandidateId] = decision;
    }

    private sealed class ImmediateStudioPreviewMediaService :
        IStudioPreviewMediaService,
        IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundryStudioPreview-" + Guid.NewGuid().ToString("N") +
            ".mp4");
        private readonly TimeSpan _preRoll;

        public ImmediateStudioPreviewMediaService(
            TimeSpan? preRoll = null)
        {
            _preRoll = preRoll ?? TimeSpan.Zero;
            File.WriteAllBytes(_path, [0x00]);
        }

        public int MaterializeCount { get; private set; }

        public Task<StudioPreviewMediaLease> MaterializeAsync(
            StudioPreviewMediaRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MaterializeCount++;
            return Task.FromResult(new StudioPreviewMediaLease(
                _path,
                request.SourceStart - _preRoll,
                request.Duration + _preRoll,
                static () => { }));
        }

        public void Dispose()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration) =>
            _timestamp += duration.Ticks;
    }
}
