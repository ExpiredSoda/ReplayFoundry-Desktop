using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.RecentProjects;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.HiddenMoments;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Features.Research;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.Processes;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    private static async Task StudioProjectSwitchCommitsPendingEdits()
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
            await studio.TrySwitchProjectAsync(second);

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
            await studio.TrySwitchProjectAsync(second);

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

    private static async Task StudioProjectSwitchBlocksQueuedDraft()
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
            await studio.TrySwitchProjectAsync(second);

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
            "Queued",
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

    private static async Task StudioQueueCommitsVisibleMetadata()
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
        TestAssert.True(
            studio.FinalRender.AddToQueueCommand.CanExecute(null),
            "A valid visible title edit should be saved by Add instead of blocking the queue.");
        TestAssert.True(
            studio.FinalRender.NeedsMetadataSave,
            "Studio should still expose that the visible copy has not been saved yet.");
        TestAssert.False(
            studio.FinalRender.NeedsRenderAttention,
            "A valid pending title edit is not a render blocker because Add commits it.");

        studio.FinalRender.AddToQueueCommand.Execute(null);
        TestAssert.False(
            studio.Inspector.Editorial.HasUnsavedChanges,
            "Add must update the saved baseline after committing the visible fields.");
        TestAssert.Equal(
            1,
            studio.FinalRender.QueueItems.Count,
            "A valid visible metadata edit should queue in the same action.");
        TestAssert.Equal(
            ClipEditorialMetadataReadiness.UserEditedDraft,
            session.Current!.PrimaryAsset.EditorialMetadata!.Readiness,
            "Queue-time save must not silently mark ordinary edits as reviewed.");
        await studio.FinalRender.FinalizeProjectAsync();

        TestAssert.Equal(
            "Visible saved title",
            renderer.LastDraft!.PrimaryAsset.EditorialMetadata!.Title,
            "The renderer must receive the saved value that was visible in Studio.");

        GenerationOutputProject invalidProject = CreateStudioQueueProject(1);
        var invalidSession = new GenerationOutputSession();
        invalidSession.Publish(invalidProject);
        using var invalidStudio = new StudioViewModel(
            invalidSession,
            invalidSession,
            new RecordingStudioClipRenderer());
        invalidStudio.Inspector.Editorial.Title = string.Empty;

        TestAssert.True(
            invalidStudio.FinalRender.AddToQueueCommand.CanExecute(null),
            "A recoverable title problem should keep Add available so the click can explain what needs attention.");
        TestAssert.True(
            invalidStudio.FinalRender.NeedsMetadataFix,
            "Invalid visible copy should expose one actionable title-and-description requirement.");
        TestAssert.True(
            invalidStudio.QueueBrowserAssetCommand.CanExecute(
                invalidProject.PrimaryAsset.Id),
            "The clip-local Add action should remain available for a recoverable title problem.");
        invalidStudio.QueueBrowserAssetCommand.Execute(
            invalidProject.PrimaryAsset.Id);
        TestAssert.Equal(
            StudioInspectorSection.Metadata,
            invalidStudio.Inspector.SelectedInspector,
            "Clicking Add should open Title & description when the visible copy needs attention.");
        TestAssert.Equal(
            0,
            invalidStudio.FinalRender.QueueItems.Count,
            "Invalid visible copy must leave the queue unchanged.");
    }

    private static async Task StudioMetadataReadinessDoesNotGateWorkflow()
    {
        ClipEditorialMetadataReadiness[] states =
        [
            ClipEditorialMetadataReadiness.WorkingLabel,
            ClipEditorialMetadataReadiness.GroundedDraft,
            ClipEditorialMetadataReadiness.UserEditedDraft,
            ClipEditorialMetadataReadiness.UserApproved,
        ];

        foreach (ClipEditorialMetadataReadiness state in states)
        {
            GenerationOutputProject project = CreateStudioQueueProject(
                1,
                state);
            var session = new GenerationOutputSession();
            session.Publish(project);
            var renderer = new RecordingStudioClipRenderer();
            using var studio = new StudioViewModel(
                session,
                session,
                renderer);

            TestAssert.True(
                project.HasPublishReadyEditorialMetadata,
                $"Structurally valid {state} copy should complete the downstream workflow.");
            TestAssert.True(
                studio.FinalRender.AddToQueueCommand.CanExecute(null),
                $"{state} copy should be queueable without review approval.");
            studio.FinalRender.AddToQueueCommand.Execute(null);
            TestAssert.Equal(
                state == ClipEditorialMetadataReadiness.UserApproved
                    ? "Title reviewed"
                    : "Title ready",
                studio.FinalRender.QueueItems[0].MetadataState,
                "The queue should show that the title is usable without making optional review look required.");
            TestAssert.True(
                studio.FinalRender.RenderQueueCommand.CanExecute(null),
                $"{state} copy should be renderable without review approval.");

            await studio.FinalRender.FinalizeProjectAsync();

            TestAssert.True(
                renderer.LastDraft is not null,
                $"{state} copy should reach the renderer.");
        }
    }

    private static Task StudioBrowserExplainsClipSelectionPlainly()
    {
        (GenerationCandidateSelectionReason Reason, string Label)[] cases =
        [
            (GenerationCandidateSelectionReason.UserReservedRange, "Requested"),
            (GenerationCandidateSelectionReason.UserPriority, "Matches your request"),
            (GenerationCandidateSelectionReason.QualityQualified, "Strong match"),
            (GenerationCandidateSelectionReason.QualityQualifiedGameplayEventCoverage, "Gameplay moment"),
            (GenerationCandidateSelectionReason.CountFillBelowQualityTarget, "More variety"),
            (GenerationCandidateSelectionReason.CountFillRelaxedDiversity, "Similar option"),
            (GenerationCandidateSelectionReason.HiddenMomentRecovery, "Found later"),
        ];
        string[] internalTerms =
        [
            "deterministic",
            "candidate",
            "score",
            "prominence",
            "luma",
            "baseline",
            "quality target",
            "diversity",
        ];

        foreach ((GenerationCandidateSelectionReason reason, string label) in
                 cases)
        {
            GenerationOutputProject project = CreateStudioQueueProject(
                1,
                selectionReason: reason,
                explanation:
                    "Deterministic candidate score used prominence, luma, baseline, quality target, and diversity.");
            StudioBrowserPreviewItem item =
                StudioSurfaceCatalog.BuildBrowserPreviewItems(
                    StudioToolSection.MomentsClips,
                    project,
                    project.PrimaryAsset.Id)[0];

            TestAssert.Equal(label, item.Status,
                $"{reason} should map to a concise visible chip.");
            TestAssert.True(
                item.SourcePositionText?.StartsWith(
                    "Source ",
                    StringComparison.Ordinal) == true,
                "Browser cards should show the source position.");
            TestAssert.Equal("Pick 1 of 1", item.BatchRankText,
                "Browser cards should show rank within the generated batch.");
            TestAssert.True(item.HasClipRationale,
                "Browser cards should include a visible Why this clip explanation.");
            foreach (string term in internalTerms)
            {
                TestAssert.False(
                    item.WhyThisClip!.Contains(
                        term,
                        StringComparison.OrdinalIgnoreCase),
                    $"Browser rationale must not leak the internal term '{term}'.");
            }
        }

        return Task.CompletedTask;
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
        TestAssert.True(
            studio.FinalRender.AddToQueueCommand.CanExecute(null),
            "A recoverable start/end problem should keep Add available so the click can explain what needs attention.");
        TestAssert.True(
            studio.QueueBrowserAssetCommand.CanExecute(project.PrimaryAsset.Id),
            "The clip-local Add action should remain available for the selected clip.");
        studio.QueueBrowserAssetCommand.Execute(project.PrimaryAsset.Id);
        TestAssert.Equal(
            StudioInspectorSection.Clip,
            studio.Inspector.SelectedInspector,
            "Clicking Add should open Start & end when the visible range needs attention.");
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

    private static Task StudioHiddenMomentPreviewUsesExactSelection()
    {
        GenerationOutputProject project =
            CreateStudioQueueProjectWithHiddenMoment(1);
        var session = new GenerationOutputSession();
        session.Publish(project);
        using var mediaService = new ImmediateStudioPreviewMediaService();
        using var hiddenMoments = new StudioHiddenMomentsViewModel(
            session,
            mediaService,
            decisionStore: null);

        hiddenMoments.Bind(project);
        hiddenMoments.OpenCommand.Execute(null);

        GenerationHiddenMoment current = hiddenMoments.Current ??
            throw new InvalidOperationException(
                "The controlled project should expose one alternate moment.");
        StudioPreviewMediaRequest request = mediaService.LastRequest ??
            throw new InvalidOperationException(
                "Opening alternate-moment review should request its preview.");
        var editableRequest = new StudioPreviewMediaRequest(
            request.Asset,
            StudioPreviewRangeMode.EditableEnvelope);
        TestAssert.Equal(
            StudioPreviewRangeMode.ExactSelection,
            request.RangeMode,
            "Alternate review must not render the full editable trim envelope.");
        TestAssert.Equal(
            current.SourceStart,
            request.SourceStart,
            "Alternate review should begin at the moment's actual boundary.");
        TestAssert.Equal(
            current.SourceEnd,
            request.SourceEnd,
            "Alternate review should end at the moment's actual boundary.");
        TestAssert.Equal(
            current.Duration,
            request.Duration,
            "Alternate preview work should be proportional to visible review time.");
        TestAssert.True(
            request.Duration < editableRequest.Duration,
            "Review-only previewing should avoid encoding unused trim context.");
        return Task.CompletedTask;
    }

    private static Task StudioHiddenMomentNavigationIsNeutral()
    {
        GenerationOutputProject project =
            CreateStudioQueueProjectWithHiddenMoment(
                count: 1,
                hiddenMomentCount: 3);
        var session = new GenerationOutputSession();
        session.Publish(project);
        var decisions = new RecordingHiddenMomentDecisionStore();
        using var hiddenMoments = new StudioHiddenMomentsViewModel(
            session,
            previewMediaService: null,
            decisions);
        using var standardPreview = new StudioPreviewViewModel(
            mediaService: null);
        hiddenMoments.Bind(project);
        hiddenMoments.OpenCommand.Execute(null);
        string[] momentIds = project.HiddenMoments
            .OrderBy(static moment => moment.ReviewOrder)
            .Select(static moment => moment.Id)
            .ToArray();

        TestAssert.False(
            hiddenMoments.PreviousMomentCommand.CanExecute(null),
            "The first alternate must not offer a nonexistent previous moment.");
        TestAssert.True(
            hiddenMoments.NextMomentCommand.CanExecute(null),
            "A following alternate must be directly browseable.");
        hiddenMoments.NextMomentCommand.Execute(null);

        TestAssert.Equal(momentIds[1], hiddenMoments.Current?.Id,
            "Next must move to the following alternate without making a decision.");
        TestAssert.Equal("Moment 2 of 3", hiddenMoments.ProgressText,
            "The header must show the browsed moment's stable deck position.");
        TestAssert.Equal(0, hiddenMoments.ReviewedCount,
            "Browsing alone must not count as a review decision.");
        TestAssert.Equal(3, hiddenMoments.RemainingCount,
            "Browsing alone must not remove an alternate.");
        TestAssert.Equal(0, decisions.Current.Count,
            "Browsing alone must not persist a Hidden Moments choice.");
        TestAssert.Equal(project.Assets.Count, session.Current!.Assets.Count,
            "Browsing alone must not append a Studio clip.");

        hiddenMoments.NextMomentCommand.Execute(null);
        TestAssert.Equal(momentIds[2], hiddenMoments.Current?.Id,
            "Next must reach the final alternate in deck order.");
        TestAssert.False(hiddenMoments.NextMomentCommand.CanExecute(null),
            "The final alternate must not offer a nonexistent next moment.");
        TestAssert.True(hiddenMoments.PreviousMomentCommand.CanExecute(null),
            "The final alternate must keep Previous available.");
        hiddenMoments.PreviousMomentCommand.Execute(null);
        TestAssert.Equal(momentIds[1], hiddenMoments.Current?.Id,
            "Previous must return to the preceding alternate without recording a choice.");
        TestAssert.True(
            hiddenMoments.Preview.UsesSecondaryGuidancePlacement,
            "Hidden Moments must move transport guidance to its quieter review placement.");
        TestAssert.False(
            standardPreview.UsesSecondaryGuidancePlacement,
            "The regular Studio preview must retain its established guidance placement.");
        return Task.CompletedTask;
    }

    private static async Task StudioAcceptsReopenedCaptionedAlternate()
    {
        GenerationOutputProject project =
            CreateReopenedCaptionedHiddenMomentProject(
                hiddenMomentCount: 3);
        var session = new GenerationOutputSession();
        session.Publish(project);
        var captions = new RecordingRetainedCaptionPreparationService();
        var metadataGenerator =
            new RecordingAcceptedHiddenMetadataGenerationService(
                blockUntilReleased: true);
        var generationMetadata = new GenerationEditorialMetadataService(
            metadataGenerator,
            new ClipEditorialProfileSession());
        using var studio = new StudioViewModel(
            session,
            session,
            new RecordingStudioClipRenderer(),
            metadataGenerator,
            new ClipEditorialProfileSession(),
            captionPreparation: captions,
            generationEditorialMetadata: generationMetadata);
        studio.HiddenMoments.OpenCommand.Execute(null);
        string selectedBeforeBackgroundWork = studio.SelectedAsset!.Id;
        string[] momentIds = project.HiddenMoments
            .OrderBy(static moment => moment.ReviewOrder)
            .Select(static moment => moment.Id)
            .ToArray();

        studio.HiddenMoments.AcceptCommand.Execute(null);
        await metadataGenerator.WaitForStartedCountAsync(1);
        studio.HiddenMoments.AcceptCommand.Execute(null);
        studio.HiddenMoments.AcceptCommand.Execute(null);

        TestAssert.Equal(3, studio.HiddenMoments.QueueItems.Count,
            "Adding several alternates should enqueue every choice immediately instead of waiting for local AI.");
        TestAssert.True(studio.HiddenMoments.HasUnfinishedQueueItems,
            "The queue must remain visibly busy while its first item is held.");
        TestAssert.Equal(1, metadataGenerator.StartedCandidateIds.Count,
            "Only one local-AI request may run while later alternates wait in FIFO order.");
        TestAssert.Equal(1, metadataGenerator.MaxConcurrentCalls,
            "Hidden Moments must serialize local-AI work.");
        TestAssert.Equal(project.Assets.Count, session.Current!.Assets.Count,
            "Enqueueing must not append an unprepared clip.");

        StudioHiddenMomentQueueItem canceledWaiting =
            studio.HiddenMoments.QueueItems.Single(item =>
                item.CandidateId.Equals(momentIds[1], StringComparison.Ordinal));
        canceledWaiting.CancelCommand.Execute(null);

        TestAssert.Equal(momentIds[1], studio.HiddenMoments.Current?.Id,
            "Canceling a waiting item must return that alternate to review.");
        TestAssert.Equal(1, studio.HiddenMoments.RemainingCount,
            "A canceled waiting item must become reviewable again exactly once.");
        canceledWaiting.CancelCommand.Execute(null);
        TestAssert.Equal(1, studio.HiddenMoments.RemainingCount,
            "A repeated stale cancel must not return the same moment twice.");

        metadataGenerator.Release(momentIds[0]);
        await metadataGenerator.WaitForStartedCountAsync(2);
        TestAssert.Equal(
            momentIds[2],
            metadataGenerator.StartedCandidateIds[1],
            "The worker must continue with the next surviving queued item.");
        TestAssert.Equal(
            selectedBeforeBackgroundWork,
            studio.SelectedAsset?.Id,
            "A background completion must not steal the user's current Studio selection.");
        metadataGenerator.Release(momentIds[2]);
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            await studio.HiddenMoments.WaitForQueueIdleAsync(timeout.Token);
        }

        GenerationOutputProject updated = session.Current!;
        GenerationOutputAsset accepted = updated.Assets.Single(asset =>
            asset.Id.Equals(momentIds[0], StringComparison.Ordinal));
        TestAssert.Equal(
            project.Assets.Count + 2,
            updated.Assets.Count,
            "Two surviving queued acceptances must append exactly two Studio clips.");
        TestAssert.Equal(
            1,
            updated.Assets.Count(asset =>
                asset.Id.Equals(momentIds[0], StringComparison.Ordinal)),
            "The first accepted alternate must occur exactly once in the project.");
        TestAssert.Equal(
            1,
            updated.Assets.Count(asset =>
                asset.Id.Equals(momentIds[2], StringComparison.Ordinal)),
            "The later accepted alternate must occur exactly once in the project.");
        TestAssert.False(
            updated.Assets.Any(asset => asset.Id.Equals(
                momentIds[1],
                StringComparison.Ordinal)),
            "Canceling a waiting item must prevent it from reaching Studio.");
        TestAssert.True(
            accepted.Captions?.HasTimedWords == true,
            "A reopened captioned alternate must retain renderable word timing.");
        StudioCaptionLook establishedLook = project.CaptionLook ??
            throw new InvalidOperationException(
                "The fixture must establish a project caption look.");
        TestAssert.Equal(
            establishedLook.CaptionStyle,
            accepted.Appearance.CaptionStyle,
            "A later accepted alternate must inherit the current project's caption style.");
        TestAssert.Equal(
            establishedLook.CaptionWordLimit,
            accepted.Appearance.CaptionWordLimit,
            "A later accepted alternate must inherit the current project's phrase size instead of reverting to Streamlined.");
        TestAssert.Equal(
            establishedLook.CaptionVerticalPositionPercent,
            accepted.Appearance.CaptionVerticalPositionPercent,
            "A later accepted alternate must inherit the current project's caption position.");
        TestAssert.Equal(
            establishedLook.CaptionMaximumWidthPercent,
            accepted.Appearance.CaptionMaximumWidthPercent,
            "A later accepted alternate must inherit the current project's caption width.");
        TestAssert.Equal(
            establishedLook.CaptionFontScalePercent,
            accepted.Appearance.CaptionFontScalePercent,
            "A later accepted alternate must inherit the current project's caption size.");
        TestAssert.Equal(
            establishedLook.CaptionStyle,
            accepted.Captions!.RequestedStyle,
            "The retained caption track and inherited appearance must stay on the same effect.");
        TestAssert.True(
            accepted.EditorialContext?.Transcripts.Single().Text.Contains(
                "rubber duckies",
                StringComparison.OrdinalIgnoreCase) == true,
            "The same prepared transcript must ground accepted metadata.");
        TestAssert.Equal(
            2,
            captions.RetainedCalls,
            "Only surviving queued alternates should use the retained source-window caption path.");
        TestAssert.Equal(
            0,
            captions.LiveCandidateCalls,
            "A reopened alternate must not require the released Generate analysis graph.");
        TestAssert.Equal(
            2,
            metadataGenerator.Requests.Count,
            "Each surviving queued alternate must prepare required metadata exactly once.");
        TestAssert.Equal(
            2,
            studio.HiddenMoments.ReviewedCount,
            "Only committed queued moments count as reviewed.");
        TestAssert.Equal(
            1,
            studio.HiddenMoments.RemainingCount,
            "The canceled waiting alternate must remain available after the queue drains.");
        TestAssert.True(
            studio.HiddenMoments.Current is { } next &&
            next.Id.Equals(momentIds[1], StringComparison.Ordinal),
            "The canceled waiting alternate must remain selected after background work drains.");
        TestAssert.False(studio.HiddenMoments.HasUnfinishedQueueItems,
            "The queue must become idle after every surviving item reaches Studio.");
        TestAssert.Equal(
            selectedBeforeBackgroundWork,
            studio.SelectedAsset?.Id,
            "Finishing the background queue must preserve the user's Studio selection.");
        TestAssert.Equal(
            updated.Assets.Count,
            studio.BrowserPreviewItems.Count,
            "The Studio browser must refresh to include the accepted clip.");
    }

    private static async Task StudioHiddenMomentCloseCancelsCleanly()
    {
        GenerationOutputProject project =
            CreateReopenedCaptionedHiddenMomentProject(
                hiddenMomentCount: 2);
        var session = new GenerationOutputSession();
        session.Publish(project);
        var captions = new RecordingRetainedCaptionPreparationService();
        var metadataGenerator =
            new RecordingAcceptedHiddenMetadataGenerationService(
                blockUntilReleased: true);
        using var hiddenMoments = new StudioHiddenMomentsViewModel(
            session,
            previewMediaService: null,
            decisionStore: null,
            captionPreparation: captions,
            editorialMetadata: new GenerationEditorialMetadataService(
                metadataGenerator,
                new ClipEditorialProfileSession()));
        hiddenMoments.Bind(project);
        hiddenMoments.OpenCommand.Execute(null);
        string preparingId = hiddenMoments.Current!.Id;

        hiddenMoments.AcceptCommand.Execute(null);
        await metadataGenerator.WaitForStartedCountAsync(1);
        TestAssert.Equal(
            StudioHiddenMomentAcceptanceStage.PreparingTitleAndDescription,
            hiddenMoments.AcceptanceStage,
            "The background worker must expose its current local-AI stage.");
        TestAssert.True(
            hiddenMoments.IsAcceptanceProgressIndeterminate,
            "Unknown local-AI duration must use indeterminate progress.");
        StudioHiddenMomentQueueItem activeQueueItem =
            hiddenMoments.QueueItems.Single();
        TestAssert.True(
            activeQueueItem.IsProgressIndeterminate &&
            activeQueueItem.ProgressPercentage == 0d,
            "The visible queue row must animate unknown-duration local-AI work instead of inventing a percentage.");
        TestAssert.Equal(
            "Close",
            hiddenMoments.CloseButtonText,
            "Closing the review must remain separate from canceling background work.");
        TestAssert.Equal(
            "Close Hidden Moments",
            hiddenMoments.CloseButtonAutomationName,
            "Assistive technology must describe closing rather than canceling the queue.");
        TestAssert.Equal(project.HiddenMoments[1].Id, hiddenMoments.Current?.Id,
            "Enqueueing must advance immediately so another moment can be reviewed.");

        hiddenMoments.CloseCommand.Execute(null);
        TestAssert.False(
            hiddenMoments.IsOpen,
            "Close must dismiss the review without owning the queue lifetime.");
        TestAssert.True(hiddenMoments.HasUnfinishedQueueItems,
            "Closing must leave the queued operation running in the background.");
        TestAssert.Equal(0, metadataGenerator.CancellationCount,
            "Closing the review must not cancel active local-AI work.");
        TestAssert.True(hiddenMoments.OpenCommand.CanExecute(null),
            "The review must be reopenable while background work continues.");

        hiddenMoments.OpenCommand.Execute(null);
        TestAssert.True(hiddenMoments.IsOpen,
            "Reopening must restore the review while its queue remains active.");
        TestAssert.True(hiddenMoments.QueueItems.Any(item =>
                item.CandidateId.Equals(preparingId, StringComparison.Ordinal)),
            "Reopening must show the same active queued moment.");
        TestAssert.Equal(
            StudioHiddenMomentAcceptanceStage.PreparingTitleAndDescription,
            hiddenMoments.AcceptanceStage,
            "Reopening must preserve the real background preparation stage.");

        metadataGenerator.Release(preparingId);
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            await hiddenMoments.WaitForQueueIdleAsync(timeout.Token);
        }

        TestAssert.Equal(
            project.Assets.Count + 1,
            session.Current!.Assets.Count,
            "The operation must finish after the review is closed and reopened.");
        TestAssert.Equal(1, session.Current.Assets.Count(asset =>
                asset.Id.Equals(preparingId, StringComparison.Ordinal)),
            "Background completion must append the queued moment exactly once.");
        TestAssert.False(
            hiddenMoments.HasError,
            "Closing and reopening must not manufacture a cancellation failure.");
        TestAssert.Equal(
            project.HiddenMoments[1].Id,
            hiddenMoments.Current?.Id,
            "The next unqueued moment must remain available after background completion.");
    }

    private static async Task
        StudioHiddenMomentLongMetadataShowsElapsedLiveness()
    {
        GenerationOutputProject project =
            CreateReopenedCaptionedHiddenMomentProject(
                hiddenMomentCount: 1);
        var session = new GenerationOutputSession();
        session.Publish(project);
        var metadataGenerator =
            new RecordingAcceptedHiddenMetadataGenerationService(
                blockUntilCancelled: true);
        var timeProvider = new ManualTimeProvider();
        using var hiddenMoments = new StudioHiddenMomentsViewModel(
            session,
            previewMediaService: null,
            decisionStore: null,
            captionPreparation:
                new RecordingRetainedCaptionPreparationService(),
            editorialMetadata: new GenerationEditorialMetadataService(
                metadataGenerator,
                new ClipEditorialProfileSession()),
            timeProvider: timeProvider);
        hiddenMoments.Bind(project);
        hiddenMoments.OpenCommand.Execute(null);

        hiddenMoments.AcceptCommand.Execute(null);
        await metadataGenerator.Started.WaitAsync(TimeSpan.FromSeconds(5));

        TestAssert.Equal(
            StudioHiddenMomentAcceptanceStage
                .PreparingTitleAndDescription,
            hiddenMoments.AcceptanceStage,
            "The elapsed clock must describe the actual long-running AI stage.");
        TestAssert.True(
            hiddenMoments.IsAcceptanceProgressIndeterminate,
            "Unknown local-AI duration must not invent a percentage.");
        TestAssert.True(
            hiddenMoments.AcceptanceLivenessText.Contains(
                "0:00 elapsed",
                StringComparison.Ordinal),
            "The AI stage should begin with a visible elapsed clock.");

        timeProvider.Advance(TimeSpan.FromSeconds(46));
        hiddenMoments.RefreshAcceptanceLiveness();

        TestAssert.True(
            hiddenMoments.AcceptanceLivenessText.Contains(
                "0:46 elapsed",
                StringComparison.Ordinal),
            "Elapsed liveness must follow monotonic time while AI remains active.");
        TestAssert.True(
            hiddenMoments.IsAcceptanceTakingLong &&
            hiddenMoments.AcceptanceWaitGuidance.Contains(
                "several minutes",
                StringComparison.OrdinalIgnoreCase),
            "A sustained AI call must explain that the wait can be normal and remains bounded.");

        hiddenMoments.QueueItems.Single().CancelCommand.Execute(null);
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            await hiddenMoments.WaitForQueueIdleAsync(timeout.Token);
        }

        TestAssert.False(
            hiddenMoments.IsAcceptanceLivenessVisible ||
            hiddenMoments.IsAcceptanceTakingLong,
            "Canceling the active queue item must clear stale liveness.");
        TestAssert.Equal(
            string.Empty,
            hiddenMoments.AcceptanceLivenessText,
            "The elapsed clock must reset after cancellation.");
    }

    private static async Task StudioHiddenMomentCommitStageCancellationWins()
    {
        GenerationOutputProject project =
            CreateReopenedCaptionedHiddenMomentProject(
                hiddenMomentCount: 1);
        var session = new GenerationOutputSession();
        session.Publish(project);
        var metadataGenerator =
            new RecordingAcceptedHiddenMetadataGenerationService();
        using var hiddenMoments = new StudioHiddenMomentsViewModel(
            session,
            previewMediaService: null,
            decisionStore: null,
            captionPreparation:
                new RecordingRetainedCaptionPreparationService(),
            editorialMetadata: new GenerationEditorialMetadataService(
                metadataGenerator,
                new ClipEditorialProfileSession()));
        hiddenMoments.Bind(project);
        hiddenMoments.OpenCommand.Execute(null);
        bool canceledAtCommit = false;
        bool commitStageUsedDeterminateProgress = false;
        hiddenMoments.PropertyChanged += (_, args) =>
        {
            if (!canceledAtCommit &&
                args.PropertyName == nameof(
                    StudioHiddenMomentsViewModel.AcceptanceStage) &&
                hiddenMoments.AcceptanceStage ==
                    StudioHiddenMomentAcceptanceStage.AddingToStudio)
            {
                canceledAtCommit = true;
                StudioHiddenMomentQueueItem queueItem =
                    hiddenMoments.QueueItems.Single();
                commitStageUsedDeterminateProgress =
                    !queueItem.IsProgressIndeterminate &&
                    queueItem.ProgressPercentage == 100d;
                queueItem.CancelCommand.Execute(null);
            }
        };

        hiddenMoments.AcceptCommand.Execute(null);
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            await hiddenMoments.WaitForQueueIdleAsync(timeout.Token);
        }

        TestAssert.True(canceledAtCommit,
            "The regression must cancel after all preparation but before project commit.");
        TestAssert.True(
            commitStageUsedDeterminateProgress,
            "The visible queue row must finish its shared loading bar when preparation reaches the Studio commit stage.");
        TestAssert.Equal(
            project.Assets.Count,
            session.Current!.Assets.Count,
            "Cancellation observed at the commit boundary must win before the clip is appended.");
        TestAssert.Equal(
            project.HiddenMomentCount,
            session.Current.HiddenMomentCount,
            "A commit-boundary cancellation must leave the moment available.");
        TestAssert.False(hiddenMoments.HasError,
            "Canceling at the commit boundary must remain intentional, not an error.");
    }

    private static Task StudioAcceptedDecisionDoesNotHideUncommittedAlternate()
    {
        GenerationOutputProject project =
            CreateReopenedCaptionedHiddenMomentProject(
                hiddenMomentCount: 1);
        GenerationHiddenMoment hidden = project.HiddenMoments[0];
        var decisions = new RecordingHiddenMomentDecisionStore();
        decisions.Upsert(new StudioHiddenMomentDecision(
            project.Id,
            hidden.Id,
            new string('A', 64),
            hidden.SourceStart,
            hidden.SourceEnd,
            StudioHiddenMomentReviewDecision.AcceptedIntoStudio,
            DateTimeOffset.UnixEpoch));
        var session = new GenerationOutputSession();
        session.Publish(project);
        using var hiddenMoments = new StudioHiddenMomentsViewModel(
            session,
            previewMediaService: null,
            decisions);

        hiddenMoments.Bind(project);

        TestAssert.Equal(
            1,
            hiddenMoments.RemainingCount,
            "An accepted decision must not hide a moment still present in the durable project after an interrupted save.");
        hiddenMoments.OpenCommand.Execute(null);
        TestAssert.Equal(
            hidden.Id,
            hiddenMoments.Current?.Id,
            "The uncommitted alternate must remain reviewable after restart.");
        return Task.CompletedTask;
    }

    private static async Task
        StudioHiddenMomentDecisionFailureKeepsCommittedClip()
    {
        GenerationOutputProject project =
            CreateReopenedCaptionedHiddenMomentProject(
                hiddenMomentCount: 1);
        var session = new GenerationOutputSession();
        session.Publish(project);
        var metadataGenerator =
            new RecordingAcceptedHiddenMetadataGenerationService();
        using var hiddenMoments = new StudioHiddenMomentsViewModel(
            session,
            previewMediaService: null,
            new FailingHiddenMomentDecisionStore(),
            captionPreparation:
                new RecordingRetainedCaptionPreparationService(),
            editorialMetadata: new GenerationEditorialMetadataService(
                metadataGenerator,
                new ClipEditorialProfileSession()));
        hiddenMoments.Bind(project);
        hiddenMoments.OpenCommand.Execute(null);
        string acceptedId = hiddenMoments.Current!.Id;

        hiddenMoments.AcceptCommand.Execute(null);
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            await hiddenMoments.WaitForQueueIdleAsync(timeout.Token);
        }

        TestAssert.Equal(
            1,
            session.Current!.Assets.Count(asset =>
                asset.Id.Equals(acceptedId, StringComparison.Ordinal)),
            "A post-commit decision-store failure must not undo or duplicate the accepted clip.");
        TestAssert.False(
            hiddenMoments.HasUnfinishedQueueItems,
            "A committed final alternate must leave no unfinished queue work.");
        TestAssert.True(
            hiddenMoments.Error?.StartsWith(
                "The clip was added to Studio",
                StringComparison.Ordinal) == true,
            "Post-commit storage failure must accurately say that the clip was added.");
        TestAssert.False(
            hiddenMoments.Error!.Contains(
                "could not add this hidden moment",
                StringComparison.OrdinalIgnoreCase),
            "The UI must not report a failed add after the project commit succeeded.");
    }

    private static async Task
        StudioFirstAlternateWarmupWaitsForPrimarySynchronization()
    {
        GenerationOutputProject project =
            CreateStudioQueueProjectWithHiddenMoment(1);
        var session = new GenerationOutputSession();
        session.Publish(project);
        using var mediaService = new ImmediateStudioPreviewMediaService();
        using var studio = new StudioViewModel(
            session,
            session,
            new RecordingStudioClipRenderer(),
            new ClipEditorialMetadataGenerationService(
                new HeuristicClipEditorialMetadataGenerator()),
            new ClipEditorialProfileSession(),
            previewMediaService: mediaService);

        TestAssert.Equal(
            1,
            mediaService.MaterializeCount,
            "Alternate warmup must wait while the selected Studio preview is still synchronizing.");
        Synchronize(studio.Preview);
        await WaitForPreviewRequestCountAsync(mediaService, 2);

        StudioPreviewMediaRequest warmed = mediaService.Requests[1];
        TestAssert.Equal(
            project.HiddenMoments[0].Id,
            warmed.Asset.Id,
            "The first background request should prepare the first reviewable alternate.");
        TestAssert.Equal(
            StudioPreviewRangeMode.ExactSelection,
            warmed.RangeMode,
            "Warm-ahead must reuse the bounded alternate-preview cache identity.");
    }

    private static async Task StudioAlternateReviewWarmsOnlyNextMoment()
    {
        GenerationOutputProject project =
            CreateStudioQueueProjectWithHiddenMoment(
                count: 1,
                hiddenMomentCount: 3);
        var session = new GenerationOutputSession();
        session.Publish(project);
        using var mediaService = new ImmediateStudioPreviewMediaService();
        using var hiddenMoments = new StudioHiddenMomentsViewModel(
            session,
            mediaService,
            decisionStore: null);

        hiddenMoments.Bind(project);
        hiddenMoments.WarmFirstAlternatePreview();
        await WaitForPreviewRequestCountAsync(mediaService, 1);
        hiddenMoments.OpenCommand.Execute(null);

        TestAssert.Equal(
            2,
            mediaService.MaterializeCount,
            "Opening review should request the current alternate but not warm the remaining deck yet.");
        TestAssert.True(
            mediaService.Requests.Take(2).All(request =>
                request.Asset.Id == project.HiddenMoments[0].Id),
            "Foreground review should reuse the same first-alternate cache identity.");

        Synchronize(hiddenMoments.Preview);
        await WaitForPreviewRequestCountAsync(mediaService, 3);

        TestAssert.Equal(
            project.HiddenMoments[1].Id,
            mediaService.Requests[2].Asset.Id,
            "Once the current review settles, only its immediate successor should warm.");
        TestAssert.False(
            mediaService.Requests.Any(request =>
                request.Asset.Id == project.HiddenMoments[2].Id),
            "Bounded warm-ahead must not encode the rest of a large alternate deck.");
    }

    private static async Task StudioAlternateWarmupCancelsOnProjectChange()
    {
        GenerationOutputProject project =
            CreateStudioQueueProjectWithHiddenMoment(1);
        var session = new GenerationOutputSession();
        session.Publish(project);
        var mediaService = new CancellableStudioPreviewMediaService();
        using var hiddenMoments = new StudioHiddenMomentsViewModel(
            session,
            mediaService,
            decisionStore: null);

        hiddenMoments.Bind(project);
        hiddenMoments.WarmFirstAlternatePreview();
        await mediaService.Started.WaitAsync(TimeSpan.FromSeconds(5));
        hiddenMoments.Bind(null);
        await mediaService.CancellationObserved.WaitAsync(
            TimeSpan.FromSeconds(5));

        TestAssert.Equal(
            project.HiddenMoments[0].Id,
            mediaService.Request!.Asset.Id,
            "The cancelled work should belong only to the outgoing project's first alternate.");
    }

    private static void Synchronize(StudioPreviewViewModel preview)
    {
        double proxySeconds = preview.PreviewPositionSeconds -
            preview.PreviewSourceOffsetSeconds;
        preview.ReportPlaybackPosition(TimeSpan.FromSeconds(proxySeconds));
        TestAssert.True(
            preview.IsPreviewSynchronized,
            "The controlled player should settle at its requested source position.");
    }

    private static async Task WaitForPreviewRequestCountAsync(
        ImmediateStudioPreviewMediaService mediaService,
        int expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (mediaService.MaterializeCount < expected)
        {
            await Task.Delay(10, timeout.Token);
        }
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
            RepositoryLayout.Root,
            "src",
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

    private static Task StudioPreviewTrimClampSynchronizesNativePlayer()
    {
        GenerationOutputProject project = CreateStudioQueueProject(1);
        using var mediaService = new ImmediateStudioPreviewMediaService();
        using var preview = new StudioPreviewViewModel(mediaService);
        GenerationOutputAsset asset = project.PrimaryAsset;
        preview.Bind(hasProject: true, project, asset);
        double sourceOffset = preview.PreviewSourceOffsetSeconds;
        preview.ReportPlaybackPosition(TimeSpan.FromSeconds(
            asset.SourceStart.TotalSeconds - sourceOffset));
        preview.PlayCommand.Execute(null);
        TestAssert.True(
            preview.IsPreviewPlaying,
            "The synchronized selected clip should begin playback before its trim changes.");

        TimeSpan trimmedStart = asset.SourceStart + TimeSpan.FromSeconds(5);
        int seekVersion = preview.PreviewSeekVersion;
        preview.UpdateRange(trimmedStart, asset.SourceEnd);

        TestAssert.Equal(
            trimmedStart.TotalSeconds,
            preview.PreviewPositionSeconds,
            "Moving the trim start past the playhead must clamp the visible position to the new cut.");
        TestAssert.False(
            preview.IsPreviewSynchronized,
            "A trim clamp must wait for the native player to reach the same source position.");
        TestAssert.False(
            preview.IsPreviewPlaying,
            "The old native position must pause while the trim clamp is synchronizing.");
        TestAssert.Equal(
            seekVersion + 1,
            preview.PreviewSeekVersion,
            "A trim clamp must publish one native seek instead of changing only the Slider value.");

        preview.ReportPlaybackPosition(TimeSpan.FromSeconds(
            trimmedStart.TotalSeconds - sourceOffset));
        TestAssert.True(
            preview.IsPreviewPlaying,
            "Playback should resume only after the native player confirms the clamped trim start.");

        preview.ReportPlaybackPosition(TimeSpan.FromSeconds(
            asset.SourceEnd.TotalSeconds - sourceOffset));
        TestAssert.False(
            preview.IsPreviewPlaying,
            "The selected clip end must stop playback before the context proxy enters post-roll.");
        int replaySeekVersion = preview.PreviewSeekVersion;
        preview.PlayCommand.Execute(null);
        TestAssert.False(
            preview.IsPreviewPlaying,
            "Replay from the end must wait for the native rewind instead of presenting stale end frames.");
        TestAssert.Equal(
            trimmedStart.TotalSeconds,
            preview.PreviewPositionSeconds,
            "Replay from the selected end must visibly return to the clip's zero-time position.");
        TestAssert.True(
            preview.PreviewSeekVersion > replaySeekVersion,
            "Replay from the selected end must always publish a fresh native rewind.");
        preview.ReportPlaybackPosition(TimeSpan.FromSeconds(
            trimmedStart.TotalSeconds - sourceOffset));
        TestAssert.True(
            preview.IsPreviewPlaying,
            "Replay should start after the native player confirms the selected beginning.");
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
            RepositoryLayout.Root,
            "src",
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
            63d,
            StudioPreviewView.ResolvePlaybackPositionSeconds(
                playbackStartedProxySeconds: 60,
                elapsedSeconds: 3,
                nativePositionSeconds: 60,
                maximumProxySeconds: 100),
            "Visible playback must advance its bounded presentation clock when MediaElement.Position stalls.");
        TestAssert.Equal(
            3.1d,
            StudioPreviewView.ResolvePlaybackPositionSeconds(
                playbackStartedProxySeconds: 0,
                elapsedSeconds: 3,
                nativePositionSeconds: 3.1,
                maximumProxySeconds: 47),
            "Native media time should take over once it follows the bounded presentation clock.");
        TestAssert.Equal(
            63d,
            StudioPreviewView.ResolvePlaybackPositionSeconds(
                playbackStartedProxySeconds: 60,
                elapsedSeconds: 3,
                nativePositionSeconds: 90,
                maximumProxySeconds: 100),
            "A disconnected native timestamp must not jump the current selected clip into post-roll.");
        TestAssert.Equal(
            100d,
            StudioPreviewView.ResolvePlaybackPositionSeconds(
                playbackStartedProxySeconds: 60,
                elapsedSeconds: 45,
                nativePositionSeconds: 60,
                maximumProxySeconds: 100),
            "Fallback presentation time must stop at the selected clip end rather than enter trim post-roll.");

        string code = File.ReadAllText(Path.Combine(
            RepositoryLayout.Root,
            "src",
            "ReplayFoundry.Desktop",
            "Features",
            "Studio",
            "Preview",
            "StudioPreviewView.xaml.cs"));
        TestAssert.True(
            code.Contains("CreateNativeMediaPlayer()", StringComparison.Ordinal) &&
            code.Contains("ReferenceEquals(openedPlayer, PreviewPlayer)", StringComparison.Ordinal) &&
            code.Contains("MaximumMediaOpenRetries = 1", StringComparison.Ordinal) &&
            code.Contains("TryScheduleMediaRetry()", StringComparison.Ordinal),
            "Changing clips must create a session-owned MediaElement and reject callbacks from retired graphs.");
        TestAssert.True(
            code.Contains("PreviewPlayer.IsMuted = false;", StringComparison.Ordinal) &&
            code.Contains("PreviewPlayer.Volume = 1d;", StringComparison.Ordinal),
            "Each native graph must initialize audible, full-volume Studio playback.");
        TestAssert.True(
            code.Contains("PreviewPlayer.IsBuffering", StringComparison.Ordinal) &&
            code.Contains("RebaselinePlaybackClock", StringComparison.Ordinal),
            "Native buffering must suspend and re-anchor the fallback clock instead of inventing progress.");
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

    private static Task StudioPreviewClipSwitchRejectsSupersededEvents()
    {
        GenerationOutputProject project = CreateStudioQueueProject(2);
        using var mediaService = new ImmediateStudioPreviewMediaService();
        using var preview = new StudioPreviewViewModel(mediaService);
        GenerationOutputAsset first = project.Assets[0];
        GenerationOutputAsset second = project.Assets[1];

        preview.Bind(hasProject: true, project, first);
        int firstSession = preview.PreviewSessionVersion;
        preview.ReportPlaybackPosition(
            TimeSpan.FromSeconds(
                first.SourceStart.TotalSeconds -
                preview.PreviewSourceOffsetSeconds),
            firstSession);

        preview.Bind(hasProject: true, project, second);
        int secondSession = preview.PreviewSessionVersion;
        TestAssert.True(
            secondSession != firstSession,
            "Selecting another clip must establish a distinct native-preview session.");
        preview.ReportPlaybackPosition(
            TimeSpan.FromSeconds(first.SourceEnd.TotalSeconds),
            firstSession);
        TestAssert.Equal(
            second.SourceStart.TotalSeconds,
            preview.PreviewPositionSeconds,
            "A delayed position event from clip A must not move clip B's playhead.");

        preview.Bind(hasProject: true, project, first);
        int returnedSession = preview.PreviewSessionVersion;
        TestAssert.True(
            returnedSession != firstSession &&
            returnedSession != secondSession,
            "Returning A after A-to-B switching must create a fresh session even when the cache reuses a media path.");
        preview.ReportPlaybackPosition(
            TimeSpan.FromSeconds(second.SourceEnd.TotalSeconds),
            secondSession);
        TestAssert.Equal(
            first.SourceStart.TotalSeconds,
            preview.PreviewPositionSeconds,
            "A delayed event from clip B must not jump the returned clip A to its end.");

        preview.ReportPlaybackPosition(
            TimeSpan.FromSeconds(
                first.SourceStart.TotalSeconds -
                preview.PreviewSourceOffsetSeconds),
            returnedSession);
        TestAssert.True(
            preview.IsPreviewSynchronized,
            "The current A session must still synchronize and remain playable after stale events are discarded.");

        return Task.CompletedTask;
    }

    private static Task StudioPreviewManualReloadStartsNewSession()
    {
        GenerationOutputProject project = CreateStudioQueueProject(1);
        using var mediaService = new ImmediateStudioPreviewMediaService();
        using var preview = new StudioPreviewViewModel(mediaService);
        preview.Bind(hasProject: true, project, project.PrimaryAsset);
        string originalPath = preview.PreviewMediaPath ??
            throw new InvalidOperationException(
                "The initial preview path was not materialized.");
        int originalSession = preview.PreviewSessionVersion;

        preview.ReloadPreviewCommand.Execute(null);

        TestAssert.Equal(
            originalPath,
            preview.PreviewMediaPath,
            "A cache hit may legitimately reuse the same bounded media URI.");
        TestAssert.True(
            preview.PreviewSessionVersion > originalSession,
            "Manual Reload must retire the failed MediaElement session even when materialization reuses the same URI.");
        TestAssert.Equal(
            2,
            mediaService.MaterializeCount,
            "Manual Reload must request one fresh materialization lease.");

        return Task.CompletedTask;
    }

    private static Task StudioPreviewRetryRetiresFailedGraph()
    {
        string code = File.ReadAllText(Path.Combine(
            RepositoryLayout.Root,
            "src",
            "ReplayFoundry.Desktop",
            "Features",
            "Studio",
            "Preview",
            "StudioPreviewView.xaml.cs"));
        int replacementStart = code.IndexOf(
            "private void ReplaceNativeMediaSource",
            StringComparison.Ordinal);
        int replacementEnd = code.IndexOf(
            "private void ReleaseNativeMediaGraph",
            replacementStart,
            StringComparison.Ordinal);
        string replacement = code[replacementStart..replacementEnd];
        TestAssert.True(
            replacement.IndexOf("_mediaSourceVersion++", StringComparison.Ordinal) <
            replacement.IndexOf("ReleaseNativeMediaGraph();", StringComparison.Ordinal),
            "Every replacement must advance its graph generation before the failed graph is released.");

        int retryStart = code.IndexOf(
            "private void RetryMediaSource",
            StringComparison.Ordinal);
        int retryEnd = code.IndexOf(
            "private bool IsBoundToCurrentPreviewSession",
            retryStart,
            StringComparison.Ordinal);
        TestAssert.True(
            code[retryStart..retryEnd].Contains(
                "ReplaceNativeMediaSource(failedSource);",
                StringComparison.Ordinal),
            "Automatic recovery must pass through the generation-advancing graph replacement boundary.");
        return Task.CompletedTask;
    }

    private static Task StudioPreviewRejectsRetiredGraphEnd()
    {
        string code = File.ReadAllText(Path.Combine(
            RepositoryLayout.Root,
            "src",
            "ReplayFoundry.Desktop",
            "Features",
            "Studio",
            "Preview",
            "StudioPreviewView.xaml.cs"));
        int handlerStart = code.IndexOf(
            "private void PreviewPlayer_OnMediaEnded",
            StringComparison.Ordinal);
        int handlerEnd = code.IndexOf(
            "private void PreviewPlayer_OnMediaFailed",
            handlerStart,
            StringComparison.Ordinal);
        string handler = code[handlerStart..handlerEnd];

        TestAssert.True(
            handler.Contains(
                "ReferenceEquals(endedPlayer, PreviewPlayer)",
                StringComparison.Ordinal) &&
            handler.Contains(
                "IsBoundToCurrentPreviewSession()",
                StringComparison.Ordinal),
            "Studio must accept MediaEnded only from its current native graph and current preview session.");
        TestAssert.True(
            handler.Contains(
                "ReportPlaybackPosition(",
                StringComparison.Ordinal) &&
            !handler.Contains(
                "endedPlayer.Position",
                StringComparison.Ordinal),
            "The current graph's MediaEnded event must complete playback even when MediaElement has already reset Position to zero.");
        TestAssert.True(
            code.Contains(
                "retired.MediaEnded -= PreviewPlayer_OnMediaEnded;",
                StringComparison.Ordinal) &&
            code.Contains(
                "_previewPlayer = CreateNativeMediaPlayer();",
                StringComparison.Ordinal),
            "A retired graph must lose its end handler before Studio installs the next immutable event sender.");
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
            RepositoryLayout.Root,
            "src",
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

    private static GenerationOutputProject CreateStudioQueueProject(
        int count,
        ClipEditorialMetadataReadiness readiness =
            ClipEditorialMetadataReadiness.UserApproved,
        GenerationCandidateSelectionReason selectionReason =
            GenerationCandidateSelectionReason.QualityQualified,
        string explanation = "test")
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
                    readiness switch
                    {
                        ClipEditorialMetadataReadiness.WorkingLabel =>
                            ClipEditorialMetadataOrigin.Heuristic,
                        ClipEditorialMetadataReadiness.GroundedDraft =>
                            ClipEditorialMetadataOrigin.AiAssisted,
                        _ => ClipEditorialMetadataOrigin.UserEdited,
                    },
                    new ClipEditorialMetadataGeneratorIdentity(
                        "Studio queue tests",
                        "1.0.0"),
                    attempt: 0,
                    readiness: readiness);
                return new GenerationOutputAsset(
                    id,
                    rank,
                    media,
                    outputFullPath: null,
                    start,
                    end,
                    85,
                    70,
                    selectionReason,
                    explanation,
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
                    editorialMetadata: metadata)
                    .WithCurrentCutEditorialMetadata(context, metadata);
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

    private static async Task StudioPreviewCacheRetainsActiveLeases()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundryPreviewLeaseTests-" + Guid.NewGuid().ToString("N"));
        string executable = Path.Combine(root, "ffmpeg.exe");
        GenerationOutputProject project = CreateStudioQueueProject(2);
        string[] sources = project.Assets
            .Select(static asset => asset.SourceFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Directory.CreateDirectory(root);
        File.WriteAllBytes(executable, [0]);
        foreach (string source in sources)
        {
            File.WriteAllBytes(source, [1, 2, 3, 4]);
        }

        try
        {
            var service = new FfmpegStudioPreviewMediaService(
                new PreviewWritingProcessRunner(),
                new PreviewFfmpegToolLocator(executable),
                Path.Combine(root, "cache"),
                maximumCacheBytes: 1);
            var firstRequest = new StudioPreviewMediaRequest(project.Assets[0]);
            var secondRequest = new StudioPreviewMediaRequest(project.Assets[1]);
            using StudioPreviewMediaLease first = await service.MaterializeAsync(
                firstRequest,
                CancellationToken.None);
            using StudioPreviewMediaLease second = await service.MaterializeAsync(
                secondRequest,
                CancellationToken.None);
            string firstRoot = Path.GetDirectoryName(first.MediaPath)!;
            string secondRoot = Path.GetDirectoryName(second.MediaPath)!;

            await Task.Delay(100);
            TestAssert.True(File.Exists(first.MediaPath),
                "Pruning must retain the first preview while its playback lease is active.");
            TestAssert.True(File.Exists(second.MediaPath),
                "Pruning must retain every simultaneously active preview, not only the latest one.");

            File.Delete(Path.Combine(firstRoot, "identity.txt"));
            await TestAssert.ThrowsAsync<InvalidOperationException>(
                () => service.MaterializeAsync(firstRequest, CancellationToken.None),
                "An invalid active cache entry must be retried after playback releases it.");
            TestAssert.True(File.Exists(first.MediaPath),
                "Rebuilding an active cache identity must never delete the media being played.");

            first.Dispose();
            await WaitForFileRemovalAsync(firstRoot);
            TestAssert.False(Directory.Exists(firstRoot),
                "An over-budget cache entry should become evictable after its final lease releases it.");
            TestAssert.True(File.Exists(second.MediaPath),
                "Releasing one preview must not evict another active preview.");

            using StudioPreviewMediaLease replacement =
                await service.MaterializeAsync(
                    firstRequest,
                    CancellationToken.None);
            string replacementRoot = Path.GetDirectoryName(
                replacement.MediaPath)!;
            string lockedPath = Path.Combine(replacementRoot, "delete-blocker.bin");
            File.WriteAllBytes(lockedPath, [1]);
            File.SetAttributes(lockedPath, FileAttributes.ReadOnly);
            replacement.Dispose();
            string quarantinedRoot =
                await WaitForQuarantinedRootAsync(
                    Path.Combine(root, "cache", ".pruning"),
                    replacementRoot);
            await Task.Run(second.Dispose)
                .WaitAsync(TimeSpan.FromSeconds(1));
            TestAssert.True(Directory.Exists(quarantinedRoot),
                "A blocked recursive delete must stay isolated in quarantine while another playback lease releases.");
            foreach (string quarantinedFile in Directory.EnumerateFiles(
                         Path.Combine(root, "cache", ".pruning"),
                         "delete-blocker.bin",
                         SearchOption.AllDirectories))
            {
                File.SetAttributes(quarantinedFile, FileAttributes.Normal);
            }
        }
        finally
        {
            foreach (string source in sources)
            {
                if (File.Exists(source))
                {
                    File.Delete(source);
                }
            }
            if (Directory.Exists(root))
            {
                await DeleteDirectoryEventuallyAsync(root);
            }
        }
    }

    private static Task StudioPreviewReleasesAfterMediaPathNotification()
    {
        GenerationOutputProject project = CreateStudioQueueProject(1);
        using var mediaService = new OrderedStudioPreviewMediaService();
        using var preview = new StudioPreviewViewModel(mediaService);
        preview.Bind(hasProject: true, project, project.PrimaryAsset);
        string firstPath = preview.PreviewMediaPath ??
            throw new InvalidOperationException(
                "The initial preview media path was not published.");
        bool replacementNotified = false;
        bool releaseFollowedNotification = false;
        preview.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StudioPreviewViewModel.PreviewMediaPath) &&
                !string.Equals(
                    preview.PreviewMediaPath,
                    firstPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                replacementNotified = true;
            }
        };
        mediaService.FirstRelease = () =>
            releaseFollowedNotification = replacementNotified;

        GenerationOutputAsset edited = project.PrimaryAsset.WithStudioEdits(
            project.PrimaryAsset.SourceStart,
            project.PrimaryAsset.SourceEnd,
            new StudioClipAppearance(
                project.PrimaryAsset.Appearance.CaptionStyle,
                project.PrimaryAsset.Appearance.CaptionVerticalPositionPercent,
                StudioVideoEffectPreset.Noir,
                70,
                project.PrimaryAsset.Appearance.GraphicOverlays,
                project.PrimaryAsset.Appearance.CaptionWordLimit,
                project.PrimaryAsset.Appearance.CaptionMaximumWidthPercent,
                project.PrimaryAsset.Appearance.CaptionFontScalePercent));
        GenerationOutputProject editedProject = project.ReplaceAsset(edited);
        preview.Bind(hasProject: true, editedProject, edited);

        TestAssert.True(releaseFollowedNotification,
            "The MediaElement binding must observe the replacement path before the old cache root becomes evictable.");
        return Task.CompletedTask;
    }

    private static async Task WaitForFileRemovalAsync(string root)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        while (Directory.Exists(root) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }

    private static async Task<string> WaitForQuarantinedRootAsync(
        string quarantineRoot,
        string originalRoot)
    {
        string prefix = Path.GetFileName(originalRoot) + "-";
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            string? match = Directory.Exists(quarantineRoot)
                ? Directory.EnumerateDirectories(quarantineRoot)
                    .FirstOrDefault(path => Path.GetFileName(path).StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase))
                : null;
            if (match is not null)
            {
                return match;
            }
            await Task.Delay(20);
        }
        throw new InvalidOperationException(
            "The over-budget inactive preview was not reserved in the pruning quarantine.");
    }

    private static async Task DeleteDirectoryEventuallyAsync(string root)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        while (Directory.Exists(root))
        {
            try
            {
                Directory.Delete(root, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception exception) when (
                (exception is IOException or UnauthorizedAccessException) &&
                DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }
        }
    }

    private static GenerationOutputProject
        CreateStudioQueueProjectWithHiddenMoment(
            int count,
            int hiddenMomentCount = 1)
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
                        Enumerable.Range(0, hiddenMomentCount + 1)
                            .Select(index => 90d - index * 5d)
                            .ToArray(),
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

    private static GenerationOutputProject
        CreateReopenedCaptionedHiddenMomentProject(int hiddenMomentCount)
    {
        GenerationOutputProject project =
            CreateStudioQueueProjectWithHiddenMoment(
                count: 1,
                hiddenMomentCount);
        GenerationOutputAsset existing = project.PrimaryAsset;
        var existingSelection = new GenerationCaptionSourceSelection(
            existing.SourceFullPath,
            existing.SourceMedia.AudioStreams[0].Index,
            CaptionAudioContentRole.CreatorCommentary,
            GenerationCaptionLanguagePolicy.English);
        TimeSpan existingRelativeStart = TimeSpan.FromSeconds(1);
        TimeSpan existingRelativeEnd = TimeSpan.FromSeconds(2);
        var existingSegment = new AudioTranscriptionSegment(
            "existing-studio-caption",
            "existing-studio-caption-neighborhood",
            "existing caption",
            existingRelativeStart,
            existingRelativeEnd,
            existing.SourceStart + existingRelativeStart,
            existing.SourceStart + existingRelativeEnd,
            [
                new AudioTranscriptionWord(
                    "existing",
                    existingRelativeStart,
                    TimeSpan.FromSeconds(1.5),
                    existing.SourceStart + existingRelativeStart,
                    existing.SourceStart + TimeSpan.FromSeconds(1.5)),
                new AudioTranscriptionWord(
                    "caption",
                    TimeSpan.FromSeconds(1.5),
                    existingRelativeEnd,
                    existing.SourceStart + TimeSpan.FromSeconds(1.5),
                    existing.SourceStart + existingRelativeEnd),
            ]);
        GenerationCandidateCaptionTrack existingTrack =
            GenerationCandidateCaptionTrack.RestoreStudioHandoff(
                existing.Id,
                existingSegment.NeighborhoodId,
                existingSelection,
                GenerationCaptionStylePreset.KaraokeSweep,
                existing.SourceStart,
                existing.Duration,
                existing.SourceDuration,
                [existingSegment],
                isUserEdited: false,
                GenerationCaptionSuppressionReason.None);
        var establishedLook = new StudioCaptionLook(
            GenerationCaptionStylePreset.KaraokeSweep,
            captionVerticalPositionPercent: 48,
            StudioCaptionWordLimitPreset.Punchy,
            captionMaximumWidthPercent: 76,
            captionFontScalePercent: 115);
        GenerationOutputAsset captionedExisting = existing
            .WithCaptionTrack(existingTrack)
            .WithStudioEdits(
                existing.SourceStart,
                existing.SourceEnd,
                establishedLook.ApplyTo(existing.Appearance));
        project = project.ReplaceAsset(captionedExisting);
        GenerationHiddenMoment[] retained = project.HiddenMoments
            .Select(hidden =>
            {
                MediaProbeResult sourceMedia = TestMediaFactory.Create(
                    hidden.SourceFullPath,
                    hidden.SourceMedia.Duration,
                    hasAudio: true);
                var context = new ClipEditorialContext(
                    hidden.Id,
                    sourceMedia.FullPath,
                    "Control",
                    hidden.SourceStart,
                    hidden.SourceEnd,
                    sourceMedia.Duration,
                    hidden.FinalScore,
                    hidden.Explanation);
                var selection = new GenerationCaptionSourceSelection(
                    sourceMedia.FullPath,
                    sourceMedia.AudioStreams[0].Index,
                    CaptionAudioContentRole.CreatorCommentary,
                    GenerationCaptionLanguagePolicy.English);
                return GenerationHiddenMoment.RestoreStudioHandoff(
                    hidden.Id,
                    hidden.ReviewOrder,
                    hidden.SourceOrder,
                    sourceMedia,
                    hidden.SourceStart,
                    hidden.SourceEnd,
                    hidden.FinalScore,
                    hidden.QualityTarget,
                    hidden.Reason,
                    hidden.Explanation,
                    hidden.PreferenceFeatures,
                    ClipEditorialGenerationPreference.AiRequired,
                    context,
                    editorialMetadata: null,
                    selection,
                    GenerationCaptionStylePreset.KaraokeSweep);
            })
            .ToArray();
        TestAssert.True(
            retained.All(static hidden => !hidden.HasGenerationProvenance),
            "The acceptance fixture must model a reopened Studio project without the Generate analysis graph.");
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
            hiddenMoments: retained);
    }

    private sealed class RecordingRetainedCaptionPreparationService :
        IGenerationCaptionPreparationService
    {
        private readonly bool _blockUntilCancelled;
        private readonly TaskCompletionSource<bool> _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public RecordingRetainedCaptionPreparationService(
            bool blockUntilCancelled = false) =>
            _blockUntilCancelled = blockUntilCancelled;

        public int RetainedCalls { get; private set; }
        public int LiveCandidateCalls { get; private set; }
        public Task Started => _started.Task;

        public Task<GenerationCaptionPreparationResult> PrepareAsync(
            GenerationMomentFindingResult moments,
            IProgress<GenerationCaptionPreparationProgress> progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GenerationCandidateCaptionTrack> PrepareCandidateAsync(
            GenerationMomentCandidate candidate,
            GenerationCaptionSourceSelection selection,
            GenerationCaptionStylePreset style,
            CancellationToken cancellationToken)
        {
            LiveCandidateCalls++;
            throw new InvalidOperationException(
                "The reopened acceptance path must not request live Generate provenance.");
        }

        public async Task<GenerationCandidateCaptionTrack>
            PrepareRetainedCandidateAsync(
                string candidateId,
                MediaProbeResult sourceMedia,
                TimeSpan sourceStart,
                TimeSpan sourceEnd,
                GenerationCaptionSourceSelection selection,
                GenerationCaptionStylePreset style,
                CancellationToken cancellationToken)
        {
            RetainedCalls++;
            _started.TrySetResult(true);
            if (_blockUntilCancelled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            string neighborhoodId = "caption-" + candidateId;
            TimeSpan relativeStart = TimeSpan.FromSeconds(1);
            TimeSpan relativeMiddle = TimeSpan.FromSeconds(1.5);
            TimeSpan relativeEnd = TimeSpan.FromSeconds(2);
            AudioTranscriptionWord[] words =
            [
                new(
                    "rubber",
                    relativeStart,
                    relativeMiddle,
                    sourceStart + relativeStart,
                    sourceStart + relativeMiddle),
                new(
                    "duckies",
                    relativeMiddle,
                    relativeEnd,
                    sourceStart + relativeMiddle,
                    sourceStart + relativeEnd),
            ];
            var segment = new AudioTranscriptionSegment(
                "accepted-hidden-segment",
                neighborhoodId,
                "rubber duckies",
                relativeStart,
                relativeEnd,
                sourceStart + relativeStart,
                sourceStart + relativeEnd,
                words);
            return GenerationCandidateCaptionTrack.RestoreStudioHandoff(
                candidateId,
                neighborhoodId,
                selection,
                style,
                sourceStart,
                sourceEnd - sourceStart,
                sourceMedia.Duration,
                [segment],
                isUserEdited: false,
                GenerationCaptionSuppressionReason.None);
        }
    }

    private sealed class RecordingAcceptedHiddenMetadataGenerationService :
        IClipEditorialMetadataGenerationService
    {
        private readonly bool _blockUntilCancelled;
        private readonly bool _blockUntilReleased;
        private readonly object _sync = new();
        private readonly Dictionary<string, TaskCompletionSource<bool>>
            _releaseByCandidate = new(StringComparer.Ordinal);
        private readonly List<string> _startedCandidateIds = [];
        private readonly TaskCompletionSource<bool> _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeCalls;
        private int _maxConcurrentCalls;
        private int _cancellationCount;

        public RecordingAcceptedHiddenMetadataGenerationService(
            bool blockUntilCancelled = false,
            bool blockUntilReleased = false)
        {
            if (blockUntilCancelled && blockUntilReleased)
            {
                throw new ArgumentException(
                    "A controlled metadata fake cannot use two blocking modes at once.");
            }
            _blockUntilCancelled = blockUntilCancelled;
            _blockUntilReleased = blockUntilReleased;
        }

        public bool IsAiAvailable => true;
        public List<ClipEditorialMetadataRequest> Requests { get; } = [];
        public Task Started => _started.Task;
        public IReadOnlyList<string> StartedCandidateIds
        {
            get
            {
                lock (_sync)
                {
                    return _startedCandidateIds.ToArray();
                }
            }
        }
        public int MaxConcurrentCalls
        {
            get
            {
                lock (_sync)
                {
                    return _maxConcurrentCalls;
                }
            }
        }
        public int CancellationCount
        {
            get
            {
                lock (_sync)
                {
                    return _cancellationCount;
                }
            }
        }

        public void Release(string candidateId)
        {
            TaskCompletionSource<bool> release;
            lock (_sync)
            {
                if (!_releaseByCandidate.TryGetValue(candidateId, out release!))
                {
                    throw new InvalidOperationException(
                        "The requested candidate has not started metadata generation.");
                }
            }
            release.TrySetResult(true);
        }

        public async Task WaitForStartedCountAsync(int expected)
        {
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(5));
            while (StartedCandidateIds.Count < expected)
            {
                await Task.Delay(10, timeout.Token);
            }
        }

        public async Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string candidateId = request.Context.CandidateId;
            TaskCompletionSource<bool>? release = null;
            lock (_sync)
            {
                Requests.Add(request);
                _startedCandidateIds.Add(candidateId);
                _activeCalls++;
                _maxConcurrentCalls = Math.Max(
                    _maxConcurrentCalls,
                    _activeCalls);
                if (_blockUntilReleased)
                {
                    release = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    _releaseByCandidate.Add(candidateId, release);
                }
            }
            _started.TrySetResult(true);
            try
            {
                if (_blockUntilCancelled)
                {
                    await Task.Delay(
                        Timeout.InfiniteTimeSpan,
                        cancellationToken);
                }
                else if (release is not null)
                {
                    await release.Task.WaitAsync(cancellationToken);
                }
                string transcript = request.Context.Transcripts
                    .Select(static value => value.Text)
                    .FirstOrDefault() ?? "the selected moment";
                var provenance = new ClipEditorialAiProvenance(
                    "test-local-ai",
                    "1.0",
                    "test-runtime",
                    "test/model",
                    "test-revision",
                    new string('A', 64),
                    "accepted-hidden-test",
                    "1.0",
                    new string('B', 64),
                    TimeSpan.FromMilliseconds(1),
                    peakAllocatedGpuBytes: null);
                return new ClipEditorialMetadataDraft(
                    "Rubber duckies are not allowed in the FBC #Control",
                    $"The accepted clip follows the creator saying {transcript}.",
                    ["Control", "gaming"],
                    ClipEditorialMetadataOrigin.AiAssisted,
                    new ClipEditorialMetadataGeneratorIdentity(
                        "test-local-ai",
                        "1.0"),
                    request.Attempt,
                    aiProvenance: provenance);
            }
            catch (OperationCanceledException)
            {
                lock (_sync)
                {
                    _cancellationCount++;
                }
                throw;
            }
            finally
            {
                lock (_sync)
                {
                    _activeCalls--;
                }
            }
        }
    }

    private sealed class RecordingHiddenMomentDecisionStore :
        IStudioHiddenMomentDecisionStore
    {
        private readonly Dictionary<string, StudioHiddenMomentDecision>
            _decisions = new(StringComparer.Ordinal);

        public IReadOnlyList<StudioHiddenMomentDecision> Current =>
            _decisions.Values.ToArray();

        public StudioHiddenMomentDecision? Find(
            string projectId,
            string candidateId) =>
            _decisions.GetValueOrDefault(projectId + "|" + candidateId);

        public void Upsert(StudioHiddenMomentDecision decision) =>
            _decisions[decision.ProjectId + "|" + decision.CandidateId] =
                decision;

        public void ClearSkippedForProject(string projectId)
        {
            string[] keys = _decisions
                .Where(pair =>
                    pair.Value.ProjectId.Equals(
                        projectId,
                        StringComparison.Ordinal) &&
                    pair.Value.Decision ==
                        StudioHiddenMomentReviewDecision.SkippedForProject)
                .Select(static pair => pair.Key)
                .ToArray();
            foreach (string key in keys)
            {
                _decisions.Remove(key);
            }
        }
    }

    private sealed class FailingHiddenMomentDecisionStore :
        IStudioHiddenMomentDecisionStore
    {
        public IReadOnlyList<StudioHiddenMomentDecision> Current => [];

        public StudioHiddenMomentDecision? Find(
            string projectId,
            string candidateId) => null;

        public void Upsert(StudioHiddenMomentDecision decision) =>
            throw new IOException(
                "Controlled Hidden Moments decision-store failure.");

        public void ClearSkippedForProject(string projectId)
        {
        }
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
        private readonly object _sync = new();
        private readonly List<StudioPreviewMediaRequest> _requests = [];

        public ImmediateStudioPreviewMediaService(
            TimeSpan? preRoll = null)
        {
            _preRoll = preRoll ?? TimeSpan.Zero;
            File.WriteAllBytes(_path, [0x00]);
        }

        public int MaterializeCount
        {
            get
            {
                lock (_sync) return _requests.Count;
            }
        }
        public StudioPreviewMediaRequest? LastRequest
        {
            get
            {
                lock (_sync) return _requests.LastOrDefault();
            }
        }
        public IReadOnlyList<StudioPreviewMediaRequest> Requests
        {
            get
            {
                lock (_sync) return _requests.ToArray();
            }
        }

        public Task<StudioPreviewMediaLease> MaterializeAsync(
            StudioPreviewMediaRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync) _requests.Add(request);
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

    private sealed class CancellableStudioPreviewMediaService :
        IStudioPreviewMediaService
    {
        private readonly TaskCompletionSource<bool> _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _cancelled = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public StudioPreviewMediaRequest? Request { get; private set; }
        public Task Started => _started.Task;
        public Task CancellationObserved => _cancelled.Task;

        public async Task<StudioPreviewMediaLease> MaterializeAsync(
            StudioPreviewMediaRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            _started.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException(
                    "The controlled preview should be cancelled.");
            }
            finally
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _cancelled.TrySetResult(true);
                }
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

    private sealed class PreviewFfmpegToolLocator(string path) :
        IFfmpegToolLocator
    {
        public string LocateFfmpeg() => path;

        public string LocateFfprobe() => path;
    }

    private sealed class PreviewWritingProcessRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string output = request.Arguments[^1];
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllBytes(output, new byte[256]);
            return Task.FromResult(new ProcessRunResult(
                0,
                string.Empty,
                string.Empty,
                TimeSpan.Zero));
        }
    }

    private sealed class OrderedStudioPreviewMediaService :
        IStudioPreviewMediaService,
        IDisposable
    {
        private readonly List<string> _paths = [];
        private int _calls;

        public Action? FirstRelease { get; set; }

        public Task<StudioPreviewMediaLease> MaterializeAsync(
            StudioPreviewMediaRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int call = ++_calls;
            string path = Path.Combine(
                Path.GetTempPath(),
                $"ReplayFoundryOrderedPreview-{Guid.NewGuid():N}.mp4");
            File.WriteAllBytes(path, [0]);
            _paths.Add(path);
            return Task.FromResult(new StudioPreviewMediaLease(
                path,
                request.SourceStart,
                request.Duration,
                () =>
                {
                    if (call == 1)
                    {
                        FirstRelease?.Invoke();
                    }
                }));
        }

        public void Dispose()
        {
            foreach (string path in _paths.Where(File.Exists))
            {
                File.Delete(path);
            }
        }
    }
}
