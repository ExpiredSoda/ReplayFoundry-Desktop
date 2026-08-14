using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Publish;
using ReplayFoundry.Desktop.Features.Publish.Editorial;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Platform.Storage;
using ReplayFoundry.Desktop.Platform.YouTube;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.PreparationTests;

internal static class YouTubePublishingTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("YouTube OAuth uses browser PKCE and one exact scope", OAuthUsesPkceAndExactScope),
        new("YouTube OAuth preserves an omitted unchanged scope", OAuthPreservesOmittedScope),
        new("YouTube OAuth callback is branded and privacy clear", OAuthCallbackIsBranded),
        new("YouTube publish request snapshots tags immutably", RequestSnapshotsTags),
        new("YouTube scheduled release requires future public timing", ScheduleValidationIsStrict),
        new("Preferred YouTube slots resolve in local time", PreferredScheduleResolves),
        new("YouTube history validates outcomes and persists atomically", HistoryPersists),
        new("YouTube preferred slots persist as immutable values", PreferencesPersist),
        new("YouTube preparation drafts persist without uploading", DraftsPersistLocally),
        new("Legacy YouTube drafts omit reroll cadence safely", LegacyDraftRerollCadenceIsOptional),
        new("Publish rerolls grounded metadata before upload and retains the draft", PublishRerollRetainsDraft),
        new("Publish reroll cadence survives asset rebind and app reload", PublishRerollCadenceSurvivesRebindAndReload),
        new("Stale Publish rerolls do not consume cadence", StalePublishRerollDoesNotConsumeCadence),
        new("Same-asset Publish edits preserve newer metadata during reroll", SameAssetPublishEditsPreserveNewerMetadata),
        new("Same-asset Publish context changes do not consume reroll cadence", SameAssetPublishContextChangesDoNotConsumeCadence),
        new("Unchanged Publish rerolls apply and persist once", UnchangedPublishRerollAppliesAndPersists),
        new("Publish rerolls through the retained finalized Studio context", PublishRerollUsesRetainedContext),
        new("Publish rerolls a Library video without an active Studio session", PublishRerollUsesDurableContext),
        new("Publish opens a focused preparation dialog for a Library video", PreparationUsesFocusedDialog),
        new("YouTube connection permission persists and defaults off", ConnectionPermissionPersists),
        new("Disabled YouTube permission prevents connection attempts", DisabledPermissionPreventsNetworkCalls),
        new("YouTube upload uses one resumable session and bounded chunks", ResumableUploadUsesChunks),
        new("YouTube upload rejects an untrusted resumable endpoint", UploadRejectsUntrustedSession),
        new("YouTube upload detects a changed Studio asset", UploadDetectsChangedAsset),
        new("YouTube publishing applies optional follow-up steps", PublishingAppliesFollowUps),
        new("YouTube optional follow-up failure preserves core upload", OptionalFailureBecomesWarning),
        new("YouTube publishing prevents concurrent uploads", ConcurrentUploadsAreRejected),
        new("YouTube history reconciliation flags only recorded inaccessible IDs", HistoryReconciliationIsExplicit),
        new("YouTube selectors display friendly labels", SelectorItemsUseFriendlyLabels),
    ];

    private static Task DraftsPersistLocally()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "publish-drafts.json");
        var store = new JsonYouTubePublishDraftStore(path);
        var scheduled = new DateTimeOffset(2026, 8, 9, 18, 0, 0, TimeSpan.Zero);
        store.Upsert(new YouTubePublishDraft(
            "asset-1", "Reviewed title", "Reviewed description", "tag-one, tag-two",
            YouTubeVideoVisibility.Public, YouTubePublishTiming.Schedule,
            YouTubeAudience.NotMadeForKids, false, true, scheduled,
            null, "20", null,
            new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero),
            "Everyone",
            "Name the supported action directly.",
            "Thanks for watching.",
            lastCompletedEditorialRerollAttempt: 7,
            priorAcceptedTitles:
            [
                "Earlier title #TestGame",
                "Another title #TestGame",
            ]));

        var reloaded = new JsonYouTubePublishDraftStore(path);
        TestAssert.Equal(1, reloaded.Current.Count, "A local preparation draft must survive a new store instance.");
        TestAssert.Equal("Reviewed title", reloaded.Current[0].Title, "Draft metadata must survive exactly.");
        TestAssert.Equal(scheduled, reloaded.Current[0].ScheduledForUtc, "Draft release time must remain absolute UTC.");
        TestAssert.Equal("Everyone", reloaded.Current[0].AudienceAddress, "Draft-specific audience context must survive exactly.");
        TestAssert.Equal("Name the supported action directly.", reloaded.Current[0].NamingGuidance, "Draft-specific naming context must survive exactly.");
        TestAssert.Equal("Thanks for watching.", reloaded.Current[0].DescriptionSignature, "Draft-specific description context must survive exactly.");
        TestAssert.Equal(7, reloaded.Current[0].LastCompletedEditorialRerollAttempt, "The last successful editorial reroll must survive a JSON round trip.");
        TestAssert.Equal(2, reloaded.Current[0].PriorAcceptedTitles.Count,
            "Every accepted Publish reroll title must survive a JSON round trip.");
        reloaded.Remove("asset-1");
        TestAssert.Equal(0, new JsonYouTubePublishDraftStore(path).Current.Count, "Removing a draft must persist without touching media.");
        return Task.CompletedTask;
    }

    private static Task LegacyDraftRerollCadenceIsOptional()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "publish-drafts.json");
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": "replayfoundry-youtube-publish-drafts-1.1",
              "drafts": [
                {
                  "assetId": "legacy-asset",
                  "title": "Legacy reviewed title",
                  "description": "Legacy reviewed description.",
                  "tags": "gameplay",
                  "visibility": "Private",
                  "timing": "PublishNow",
                  "audience": "NotMadeForKids",
                  "containsSyntheticMedia": false,
                  "notifySubscribers": true,
                  "scheduledForUtc": null,
                  "savedAtUtc": "2026-08-06T12:00:00+00:00"
                }
              ]
            }
            """);

        var store = new JsonYouTubePublishDraftStore(path);

        TestAssert.Equal(
            1,
            store.Current.Count,
            "A valid pre-cadence draft must remain readable.");
        TestAssert.Null(
            store.Current[0].LastCompletedEditorialRerollAttempt,
            "An old draft must resume from retained metadata rather than inventing a completed reroll.");
        TestAssert.Equal(0, store.Current[0].PriorAcceptedTitles.Count,
            "An old draft must not invent accepted title history.");
        TestAssert.Throws<ArgumentException>(
            () => new YouTubePublishDraft(
                "asset",
                "Title",
                string.Empty,
                string.Empty,
                YouTubeVideoVisibility.Private,
                YouTubePublishTiming.PublishNow,
                YouTubeAudience.NotMadeForKids,
                false,
                true,
                null,
                null,
                null,
                null,
                DateTimeOffset.UnixEpoch,
                lastCompletedEditorialRerollAttempt: -1),
            "A persisted reroll attempt must never be negative.");
        return Task.CompletedTask;
    }

    private static async Task PublishRerollRetainsDraft()
    {
        using var fixture = new PublishAssetFixture(32);
        var catalog = new FixedCatalog(fixture.Asset);
        var drafts = new InMemoryYouTubePublishDraftStore();
        var editorial = new FakePublishEditorialMetadataService();
        var rerollPreference = new EditorialRerollPreferenceState(
            new InMemoryEditorialRerollPreferenceStore(
                new EditorialRerollPreferenceSnapshot(
                    UseLocalAi: true)));
        using var publish = new PublishViewModel(
            catalog,
            youtube: null,
            new InMemoryYouTubePublishPreferencesStore(),
            new NullThumbnailPicker(),
            connectionPermission: null,
            drafts,
            preparationDialog: null,
            bulkConfirmation: null,
            editorialMetadata: editorial,
            editorialRerollPreference: rerollPreference);
        publish.Editorial.AudienceAddress = "Viewers";
        publish.Editorial.NamingGuidance =
            "Lead with the specific supported action.";
        publish.Editorial.DescriptionSignature = "More clips every week.";

        await ((AsyncDelegateCommand)publish.Editorial.RerollCommand)
            .ExecuteAsync();

        TestAssert.Equal(1, editorial.CallCount, "One explicit Publish reroll must invoke the shared metadata boundary once.");
        TestAssert.True(editorial.LastRequireAi, "The single Publish reroll must require the qualified provider when the saved preference is on, rather than silently substituting heuristics.");
        TestAssert.Equal("A different grounded title", publish.Title, "The completed reroll must update the editable Publish title field.");
        TestAssert.Equal("A different grounded description.", publish.Description, "The completed reroll must update the editable Publish description field.");
        TestAssert.Equal(1, drafts.Current.Count, "A completed reroll must be retained in the local Publish draft without uploading.");
        TestAssert.Equal(publish.Title, drafts.Current[0].Title, "The local draft must retain the exact rerolled title.");
        TestAssert.Equal("Viewers", drafts.Current[0].AudienceAddress, "The local draft must retain the wording context used for the reroll.");

        string acceptedRerollTitle = publish.Title;
        publish.Title = "A manually refined grounded title";
        publish.SaveDraftCommand.Execute(null);
        TestAssert.True(
            drafts.Current[0].PriorAcceptedTitles.Contains(
                acceptedRerollTitle,
                StringComparer.Ordinal),
            "Replacing a Publish title manually must retain the accepted wording as a future exclusion.");

        await ((AsyncDelegateCommand)publish.Editorial.RerollCommand)
            .ExecuteAsync();
        TestAssert.True(
            editorial.PriorTitleHistories[1].Contains(
                acceptedRerollTitle,
                StringComparer.Ordinal),
            "A Publish reroll must receive accepted titles retained across manual edits.");
        TestAssert.Equal(
            2,
            editorial.PreviousCompletedAttempts[1],
            "A consecutive Publish reroll must continue from the last completed attempt so variant cadence advances.");

        editorial.FailNext = true;
        await ((AsyncDelegateCommand)publish.Editorial.RerollCommand)
            .ExecuteAsync();
        await ((AsyncDelegateCommand)publish.Editorial.RerollCommand)
            .ExecuteAsync();
        TestAssert.Equal(
            editorial.PreviousCompletedAttempts[2],
            editorial.PreviousCompletedAttempts[3],
            "A failed Publish reroll must not consume or advance the last completed variant attempt.");
    }

    private static async Task PublishRerollCadenceSurvivesRebindAndReload()
    {
        using var fixture = new PublishAssetFixture(32);
        var catalog = new MutableCatalog(fixture.Asset);
        var drafts = new InMemoryYouTubePublishDraftStore();
        var firstService = new FakePublishEditorialMetadataService();
        using (var first = new PublishViewModel(
                   catalog,
                   youtube: null,
                   new InMemoryYouTubePublishPreferencesStore(),
                   new NullThumbnailPicker(),
                   connectionPermission: null,
                   drafts,
                   preparationDialog: null,
                   bulkConfirmation: null,
                   editorialMetadata: firstService))
        {
            await ((AsyncDelegateCommand)first.Editorial.RerollCommand)
                .ExecuteAsync();
            TestAssert.Equal(
                2,
                drafts.Current[0].LastCompletedEditorialRerollAttempt,
                "A successful reroll must persist its completed attempt with the draft.");

            LibraryMediaAsset rebound = fixture.Asset.Relink(
                fixture.Asset.OutputFullPath,
                fixture.Asset.ThumbnailFullPath);
            catalog.Replace(rebound);
            await ((AsyncDelegateCommand)first.Editorial.RerollCommand)
                .ExecuteAsync();
            TestAssert.Equal(
                2,
                firstService.PreviousCompletedAttempts[1],
                "Replacing a Library object with the same logical asset must resume the saved cadence.");
        }

        var reloadedService = new FakePublishEditorialMetadataService();
        using var reloaded = new PublishViewModel(
            catalog,
            youtube: null,
            new InMemoryYouTubePublishPreferencesStore(),
            new NullThumbnailPicker(),
            connectionPermission: null,
            drafts,
            preparationDialog: null,
            bulkConfirmation: null,
            editorialMetadata: reloadedService);
        await ((AsyncDelegateCommand)reloaded.Editorial.RerollCommand)
            .ExecuteAsync();

        TestAssert.Equal(
            3,
            reloadedService.PreviousCompletedAttempts[0],
            "A new Publish view-model must restore the last successful attempt from its local draft.");
        TestAssert.Equal(
            4,
            drafts.Current[0].LastCompletedEditorialRerollAttempt,
            "The first reroll after reload must persist the next completed attempt.");
    }

    private static async Task StalePublishRerollDoesNotConsumeCadence()
    {
        using var fixture = new PublishAssetFixture(32);
        LibraryMediaAsset other = new(
            "asset-2",
            fixture.Asset.ProjectId,
            fixture.Asset.Mode,
            2,
            fixture.Asset.OutputFullPath,
            fixture.Asset.ThumbnailFullPath,
            fixture.Asset.Duration,
            fixture.Asset.OutputWidth,
            fixture.Asset.OutputHeight,
            "Other title",
            string.Empty,
            [],
            fixture.Asset.AddedAtUtc.AddSeconds(1));
        var service = new FakePublishEditorialMetadataService();
        using var viewModel = new PublishEditorialMetadataViewModel(
            service,
            _ => { },
            () => new PublishEditorialMetadataSnapshot(
                "Current title",
                "Current description.",
                "current, tags"),
            () => { },
            () => false);
        viewModel.BindAsset(fixture.Asset, lastCompletedAttempt: 5);
        service.BeforeReturn = () => viewModel.BindAsset(other);

        await ((AsyncDelegateCommand)viewModel.RerollCommand).ExecuteAsync();

        service.BeforeReturn = null;
        viewModel.BindAsset(fixture.Asset, lastCompletedAttempt: 5);
        await ((AsyncDelegateCommand)viewModel.RerollCommand).ExecuteAsync();
        TestAssert.Equal(
            service.PreviousCompletedAttempts[0],
            service.PreviousCompletedAttempts[1],
            "A result completed for a different selected asset must not consume the saved attempt.");
    }

    private static async Task SameAssetPublishEditsPreserveNewerMetadata()
    {
        using var fixture = new PublishAssetFixture(32);
        var service = new FakePublishEditorialMetadataService();
        var current = new PublishEditorialMetadataSnapshot(
            "Original title",
            "Original description.",
            "original, tags");
        int applyCount = 0;
        int persistCount = 0;
        using var viewModel = new PublishEditorialMetadataViewModel(
            service,
            result =>
            {
                applyCount++;
                current = new PublishEditorialMetadataSnapshot(
                    result.Title,
                    result.Description,
                    result.Tags);
            },
            () => current,
            () => persistCount++,
            () => false);
        viewModel.BindAsset(
            fixture.Asset,
            lastCompletedAttempt: 5,
            priorAcceptedTitles: ["Earlier accepted title"]);
        service.BeforeReturn = () =>
            current = new PublishEditorialMetadataSnapshot(
                "My newer title edit",
                "My newer description edit.",
                "my, newer, tags");

        await ((AsyncDelegateCommand)viewModel.RerollCommand).ExecuteAsync();

        TestAssert.Equal(
            "My newer title edit",
            current.Title,
            "A same-asset title edit made while rerolling must win over the older result.");
        TestAssert.Equal(
            "My newer description edit.",
            current.Description,
            "A same-asset description edit made while rerolling must win over the older result.");
        TestAssert.Equal(
            "my, newer, tags",
            current.Tags,
            "Same-asset tag edits made while rerolling must win over the older result.");
        TestAssert.Equal(0, applyCount,
            "A stale same-asset result must not invoke the metadata apply boundary.");
        TestAssert.Equal(0, persistCount,
            "A stale same-asset result must not persist a draft.");
        TestAssert.Equal(5, viewModel.LastCompletedAttempt,
            "A stale same-asset result must not advance the retained attempt.");
        TestAssert.Equal(1, viewModel.PriorAcceptedTitles.Count,
            "A stale same-asset result must not change accepted-title history.");
        TestAssert.True(
            viewModel.Status.Contains("kept your edits", StringComparison.Ordinal),
            "The stale-result status must explain that the newer edits were preserved.");
    }

    private static async Task SameAssetPublishContextChangesDoNotConsumeCadence()
    {
        using var fixture = new PublishAssetFixture(32);
        var service = new FakePublishEditorialMetadataService();
        var current = new PublishEditorialMetadataSnapshot(
            "Current title",
            "Current description.",
            "current, tags");
        int applyCount = 0;
        int persistCount = 0;
        PublishEditorialMetadataViewModel? viewModel = null;
        viewModel = new PublishEditorialMetadataViewModel(
            service,
            _ => applyCount++,
            () => current,
            () => persistCount++,
            () => false);
        using (viewModel)
        {
            viewModel.BindAsset(
                fixture.Asset,
                lastCompletedAttempt: 8,
                priorAcceptedTitles: ["Earlier accepted title"]);
            service.BeforeReturn = () =>
            {
                viewModel.AudienceAddress = "Regular viewers";
                viewModel.NamingGuidance =
                    "Prefer the supported outcome over the setup.";
                viewModel.DescriptionSignature = "New signature.";
            };

            await ((AsyncDelegateCommand)viewModel.RerollCommand)
                .ExecuteAsync();

            TestAssert.Equal(0, applyCount,
                "Changing reroll context in flight must discard the result rather than overwrite newer intent.");
            TestAssert.Equal(0, persistCount,
                "A result created from superseded context must not persist.");
            TestAssert.Equal(8, viewModel.LastCompletedAttempt,
                "A context-stale result must not consume reroll cadence.");
            TestAssert.Equal(1, viewModel.PriorAcceptedTitles.Count,
                "A context-stale result must not advance accepted-title history.");
            TestAssert.Equal(
                "Regular viewers",
                viewModel.AudienceAddress,
                "The newer audience wording must remain visible after the stale result completes.");
            TestAssert.Equal(
                "Prefer the supported outcome over the setup.",
                viewModel.NamingGuidance,
                "The newer naming guidance must remain visible after the stale result completes.");
            TestAssert.Equal(
                "New signature.",
                viewModel.DescriptionSignature,
                "The newer description signature must remain visible after the stale result completes.");

            service.BeforeReturn = null;
            await ((AsyncDelegateCommand)viewModel.RerollCommand)
                .ExecuteAsync();
            TestAssert.Equal(
                service.PreviousCompletedAttempts[0],
                service.PreviousCompletedAttempts[1],
                "The first clean retry after a context-stale result must reuse the unconsumed attempt.");
        }
    }

    private static async Task UnchangedPublishRerollAppliesAndPersists()
    {
        using var fixture = new PublishAssetFixture(32);
        var service = new FakePublishEditorialMetadataService();
        var current = new PublishEditorialMetadataSnapshot(
            "Current title",
            "Current description.",
            "current, tags");
        int applyCount = 0;
        int persistCount = 0;
        using var viewModel = new PublishEditorialMetadataViewModel(
            service,
            result =>
            {
                applyCount++;
                current = new PublishEditorialMetadataSnapshot(
                    result.Title,
                    result.Description,
                    result.Tags);
            },
            () => current,
            () => persistCount++,
            () => false);
        viewModel.BindAsset(
            fixture.Asset,
            lastCompletedAttempt: 3,
            priorAcceptedTitles: ["Earlier accepted title"]);

        await ((AsyncDelegateCommand)viewModel.RerollCommand).ExecuteAsync();

        TestAssert.Equal(1, applyCount,
            "An unchanged reroll must apply exactly once.");
        TestAssert.Equal(1, persistCount,
            "An unchanged reroll must persist exactly once.");
        TestAssert.Equal(
            "A different grounded title",
            current.Title,
            "The clean reroll must apply its generated title.");
        TestAssert.Equal(4, viewModel.LastCompletedAttempt,
            "A clean reroll must advance cadence exactly once.");
    }

    private static async Task PublishRerollUsesRetainedContext()
    {
        using var fixture = new PublishAssetFixture(32);
        var media = TestMediaFactory.Create(
            fixture.SourcePath,
            TimeSpan.FromSeconds(30));
        var context = new ClipEditorialContext(
            "candidate-1",
            fixture.SourcePath,
            "Test Game",
            TimeSpan.Zero,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30),
            82,
            "Deterministic scene and audio support.");
        var retained = new ClipEditorialMetadataDraft(
            "Retained title",
            "Retained description.",
            ["retained"],
            ClipEditorialMetadataOrigin.UserEdited,
            new ClipEditorialMetadataGeneratorIdentity("retained", "1.0"),
            attempt: 4);
        var output = new GenerationOutputAsset(
            "candidate-1",
            1,
            media,
            fixture.Asset.OutputFullPath,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(30),
            82,
            70,
            GenerationCandidateSelectionReason.QualityQualified,
            "Deterministic scene and audio support.",
            editorialContext: context,
            editorialMetadata: retained);
        var project = new GenerationOutputProject(
            "project-1",
            GenerationMode.IndividualClips,
            Path.GetDirectoryName(fixture.Asset.OutputFullPath)!,
            1,
            ClipFulfillmentPreference.QualityFirst,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
            [output],
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddMinutes(1));
        var session = new GenerationOutputSession();
        session.Publish(project);
        var generator = new RecordingEditorialGenerationService();
        var service = new PublishEditorialMetadataService(
            session,
            generator,
            new ClipEditorialProfileSession());

        PublishEditorialRerollResult result = await service.RerollAsync(
            fixture.Asset,
            "Viewers",
            "Use exact supported actions.",
            string.Empty,
            previousCompletedAttempt: null,
            currentTitle: retained.Title,
            priorAcceptedTitles: [],
            requireAi: true,
            CancellationToken.None);
        PublishEditorialRerollResult next = await service.RerollAsync(
            fixture.Asset,
            "Viewers",
            "Use exact supported actions.",
            string.Empty,
            previousCompletedAttempt: result.Attempt,
            currentTitle: result.Title,
            priorAcceptedTitles: result.PriorAcceptedTitles,
            requireAi: true,
            CancellationToken.None);

        TestAssert.Equal(2, generator.CallCount, "Two Publish rerolls must invoke the existing shared generation boundary exactly twice.");
        TestAssert.True(ReferenceEquals(context, generator.Requests[0].Context), "Publish must preserve the retained editorial context by identity.");
        TestAssert.True(ReferenceEquals(media, generator.Requests[0].SourceMedia), "Local-AI rerolls must receive the exact retained source inspection.");
        TestAssert.Equal(5, generator.Requests[0].Attempt, "The first Publish reroll must advance the retained attempt.");
        TestAssert.Equal(6, generator.Requests[1].Attempt, "The next Publish reroll must advance from the last completed attempt rather than repeating a variant.");
        TestAssert.Equal(6, next.Attempt, "Publish must return the actual completed attempt for the view-model cadence owner.");
        TestAssert.Equal(1,
            generator.Requests[0].PriorAcceptedTitleExclusions.Count,
            "The first Publish reroll must exclude the retained Studio title.");
        TestAssert.Equal(2,
            generator.Requests[1].PriorAcceptedTitleExclusions.Count,
            "The next Publish reroll must exclude every earlier accepted title, not only the latest.");
        TestAssert.Equal(ClipEditorialGenerationPreference.AiRequired, generator.Requests[1].Preference, "The saved AI choice must never silently choose another generator.");
        TestAssert.Equal("Shared service title", result.Title, "The shared generator result must flow back without semantic rewriting.");
    }

    private static async Task PublishRerollUsesDurableContext()
    {
        using var fixture = new PublishAssetFixture(32);
        using var projectDirectory = new TemporaryDirectory();
        var media = TestMediaFactory.Create(
            fixture.SourcePath,
            TimeSpan.FromSeconds(30));
        var context = new ClipEditorialContext(
            "candidate-1",
            fixture.SourcePath,
            "Test Game",
            TimeSpan.Zero,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30),
            82,
            "Durably retained scene and audio support.");
        var retained = new ClipEditorialMetadataDraft(
            "Durably retained title",
            "Durably retained description.",
            ["retained"],
            ClipEditorialMetadataOrigin.UserEdited,
            new ClipEditorialMetadataGeneratorIdentity("retained", "1.0"),
            attempt: 4);
        var output = new GenerationOutputAsset(
            "candidate-1",
            1,
            media,
            fixture.Asset.OutputFullPath,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(30),
            82,
            70,
            GenerationCandidateSelectionReason.QualityQualified,
            "Durably retained scene and audio support.",
            editorialContext: context,
            editorialMetadata: retained);
        const string sourceProjectId = "project-1";
        const string renderedProjectId =
            sourceProjectId + "-render-abcdef12";
        var project = new GenerationOutputProject(
            sourceProjectId,
            GenerationMode.IndividualClips,
            Path.GetDirectoryName(fixture.Asset.OutputFullPath)!,
            1,
            ClipFulfillmentPreference.QualityFirst,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
            [output],
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddMinutes(1));
        var store = new JsonStudioProjectStore(projectDirectory.Path);
        store.Save(project, revision: 1);
        var renderedAsset = new LibraryMediaAsset(
            "rendered-asset-1",
            renderedProjectId,
            fixture.Asset.Mode,
            fixture.Asset.Rank,
            fixture.Asset.OutputFullPath,
            fixture.Asset.ThumbnailFullPath,
            fixture.Asset.Duration,
            fixture.Asset.OutputWidth,
            fixture.Asset.OutputHeight,
            fixture.Asset.Title,
            fixture.Asset.Description,
            fixture.Asset.Tags,
            fixture.Asset.AddedAtUtc,
            sourceCandidateIds: [output.Id]);
        var generator = new RecordingEditorialGenerationService();
        var service = new PublishEditorialMetadataService(
            new GenerationOutputSession(),
            generator,
            new ClipEditorialProfileSession(),
            store);

        TestAssert.True(
            service.CanReroll(renderedAsset),
            "A Library video must remain rerollable from its durable project after the active Studio session ends.");
        PublishEditorialRerollResult result = await service.RerollAsync(
            renderedAsset,
            "Viewers",
            "Use a different supported narrative angle.",
            string.Empty,
            previousCompletedAttempt: null,
            currentTitle: retained.Title,
            priorAcceptedTitles: [],
            requireAi: true,
            CancellationToken.None);

        TestAssert.Equal(
            1,
            generator.CallCount,
            "A Library reroll must invoke the shared metadata generator exactly once.");
        TestAssert.Equal(
            fixture.SourcePath,
            generator.LastRequest!.SourceMedia!.FullPath,
            "A Library reroll must use the exact verified source retained by the durable project.");
        TestAssert.Equal(
            5,
            result.Attempt,
            "A Library reroll must continue the retained editorial cadence.");
    }

    private static Task PreparationUsesFocusedDialog()
    {
        using var fixture = new PublishAssetFixture(32);
        var catalog = new FixedCatalog(fixture.Asset);
        var dialog = new RecordingPreparationDialog();
        using var publish = new PublishViewModel(
            catalog,
            youtube: null,
            new InMemoryYouTubePublishPreferencesStore(),
            new NullThumbnailPicker(),
            connectionPermission: null,
            drafts: new InMemoryYouTubePublishDraftStore(),
            preparationDialog: dialog,
            bulkConfirmation: null);

        publish.PrepareAssetCommand.Execute(fixture.Asset);

        TestAssert.Equal(1, dialog.CallCount, "Preparing a Library video must open one focused review dialog.");
        TestAssert.True(ReferenceEquals(fixture.Asset, publish.SelectedAsset), "The exact Library asset must remain selected for preparation.");
        TestAssert.Equal(YouTubePublishTiming.Schedule, publish.Timing, "The calendar preparation path must default to a scheduled release.");
        return Task.CompletedTask;
    }

    private static async Task HistoryReconciliationIsExplicit()
    {
        using var fixture = new PublishAssetFixture(32);
        var api = new FakeApi
        {
            ExistingVideoIds = new HashSet<string>(StringComparer.Ordinal),
        };
        var history = new InMemoryYouTubePublishHistoryStore();
        var service = new YouTubePublishingService(
            new FakeAuthorization(),
            api,
            history);
        await service.PublishAsync(
            fixture.CreateRequest(),
            progress: null,
            CancellationToken.None);

        int flagged = await service.ReconcileHistoryAsync(
            CancellationToken.None);

        TestAssert.Equal(1, flagged, "The absent recorded ID should be flagged.");
        TestAssert.Equal(
            YouTubeRemoteVideoStatus.NotFoundOrInaccessible,
            history.Current[0].RemoteStatus,
            "An empty videos.list response is not proof of deletion; it must retain the inaccessible wording.");
        TestAssert.True(
            history.Current[0].RemoteCheckedAtUtc.HasValue,
            "A successful explicit check must record when it ran.");
    }

    private static Task OAuthUsesPkceAndExactScope()
    {
        var configuration = new YouTubeOAuthClientConfiguration(
            "123456.apps.googleusercontent.com",
            "desktop-client-secret");
        Uri uri = GoogleYouTubeAuthorizationService.BuildAuthorizationUri(
            configuration,
            new Uri("http://127.0.0.1:53121/oauth2/callback"),
            "state-value",
            "challenge-value");
        string text = Uri.UnescapeDataString(uri.Query);
        TestAssert.Equal(1, configuration.Scopes.Count, "OAuth should request one canonical scope.");
        TestAssert.Equal(
            YouTubeOAuthClientConfiguration.ManageYouTubeScope,
            configuration.Scopes[0],
            "Playlist-capable publishing should use the documented YouTube management scope.");
        TestAssert.True(text.Contains("code_challenge=challenge-value", StringComparison.Ordinal), "OAuth must use PKCE.");
        TestAssert.True(text.Contains("code_challenge_method=S256", StringComparison.Ordinal), "OAuth must use SHA-256 PKCE.");
        TestAssert.True(text.Contains("access_type=offline", StringComparison.Ordinal), "OAuth must request a refresh credential.");
        TestAssert.False(text.Contains("client_secret", StringComparison.Ordinal), "The paired desktop client secret must never appear in the browser authorization URL.");
        TestAssert.Throws<ArgumentException>(
            () => new YouTubeOAuthClientConfiguration(
                "123456.apps.googleusercontent.com",
                " "),
            "An incomplete desktop OAuth pair must fail before a browser is opened.");
        return Task.CompletedTask;
    }

    private static Task OAuthPreservesOmittedScope()
    {
        IReadOnlyList<string> requested = Array.AsReadOnly(
            [YouTubeOAuthClientConfiguration.ManageYouTubeScope]);
        IReadOnlyList<string> resolved =
            GoogleYouTubeAuthorizationService.ResolveGrantedScopes(
                Array.Empty<string>(),
                requested);
        TestAssert.Equal(1, resolved.Count, "An omitted token-response scope must retain the exact requested grant.");
        TestAssert.Equal(requested[0], resolved[0], "The fallback scope must not broaden or rewrite the requested scope.");
        TestAssert.False(ReferenceEquals(requested, resolved), "The stored grant must be an immutable snapshot.");
        return Task.CompletedTask;
    }

    private static Task OAuthCallbackIsBranded()
    {
        string success =
            GoogleYouTubeAuthorizationService.BuildLoopbackResponseHtml(
                success: true);
        string failure =
            GoogleYouTubeAuthorizationService.BuildLoopbackResponseHtml(
                success: false);
        TestAssert.True(success.Contains("ReplayFoundry", StringComparison.Ordinal), "The local success page should carry the product identity.");
        TestAssert.True(success.Contains("YouTube is connected", StringComparison.Ordinal), "The success state should be unmistakable.");
        TestAssert.True(success.Contains("Authorization received", StringComparison.Ordinal), "The callback should describe the completed authorization without implying Google endorsement.");
        TestAssert.False(success.Contains("Approved by Google", StringComparison.Ordinal), "The callback must not imply that Google endorses ReplayFoundry.");
        TestAssert.True(success.Contains("Windows Credential Manager", StringComparison.Ordinal), "The success page should explain local credential protection.");
        TestAssert.True(success.Contains("no video or channel data", StringComparison.Ordinal), "The local callback must set a narrow privacy expectation.");
        TestAssert.True(failure.Contains("Nothing was connected", StringComparison.Ordinal), "The stopped state must not imply that access was granted.");
        TestAssert.False(success.Contains("http://", StringComparison.OrdinalIgnoreCase), "The callback page must not fetch insecure external content.");
        TestAssert.False(success.Contains("https://", StringComparison.OrdinalIgnoreCase), "The callback page must be completely self-contained.");
        return Task.CompletedTask;
    }

    private static Task RequestSnapshotsTags()
    {
        using var fixture = new PublishAssetFixture(32);
        var tags = new List<string> { "Gameplay", " gameplay ", "Moment" };
        YouTubePublishRequest request = fixture.CreateRequest(tags: tags);
        tags.Add("mutated");
        TestAssert.Equal(2, request.Tags.Count, "Tags should be trimmed and deduplicated case-insensitively.");
        TestAssert.False(request.Tags.Contains("mutated"), "The caller collection must not remain mutable.");
        var secured = new YouTubePublishRequest(
            fixture.Asset,
            "Safe\u202E title\r\ncontinuation",
            "First line\r\nSecond\u2066 line",
            ["tag\u200Bvalue"],
            "20",
            YouTubeVideoVisibility.Private,
            YouTubePublishTiming.PublishNow,
            YouTubeAudience.NotMadeForKids,
            false,
            true,
            createdAtUtc: new DateTimeOffset(
                2026, 8, 1, 12, 0, 0, TimeSpan.Zero));
        TestAssert.Equal(
            "Safe title continuation",
            secured.Title,
            "Public title text must not carry line or bidi control syntax.");
        TestAssert.Equal(
            "First line\nSecond line",
            secured.Description,
            "Public descriptions may retain normalized line breaks but not bidi controls.");
        TestAssert.Equal(
            "tagvalue",
            secured.Tags.Single(),
            "Public tags must not carry zero-width control syntax.");
        TestAssert.Throws<ArgumentException>(
            () => fixture.CreateRequest(playlistId: "playlist?redirect=other"),
            "Playlist identifiers must not smuggle URL syntax into a Google API path.");
        TestAssert.Throws<ArgumentException>(
            () => fixture.CreateRequest(thumbnailFullPath: "relative.png"),
            "Relative thumbnail paths must be rejected before normalization.");
        return Task.CompletedTask;
    }

    private static Task ScheduleValidationIsStrict()
    {
        using var fixture = new PublishAssetFixture(32);
        DateTimeOffset now = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        TestAssert.Throws<ArgumentException>(
            () => fixture.CreateRequest(
                timing: YouTubePublishTiming.Schedule,
                visibility: YouTubeVideoVisibility.Unlisted,
                scheduledForUtc: now.AddHours(2),
                createdAtUtc: now),
            "Scheduled releases must become public.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => fixture.CreateRequest(
                timing: YouTubePublishTiming.Schedule,
                visibility: YouTubeVideoVisibility.Public,
                scheduledForUtc: now.AddMinutes(29),
                createdAtUtc: now),
            "Scheduling needs an honest upload and processing lead time.");
        YouTubePublishRequest valid = fixture.CreateRequest(
            timing: YouTubePublishTiming.Schedule,
            visibility: YouTubeVideoVisibility.Public,
            scheduledForUtc: now.AddHours(2),
            createdAtUtc: now);
        TestAssert.Equal(now.AddHours(2), valid.ScheduledForUtc, "The exact UTC release should be retained.");
        return Task.CompletedTask;
    }

    private static Task PreferredScheduleResolves()
    {
        var slots = new[]
        {
            new YouTubePreferredScheduleSlot(DayOfWeek.Friday, new TimeOnly(18, 30)),
            new YouTubePreferredScheduleSlot(DayOfWeek.Monday, new TimeOnly(9, 0)),
        };
        DateTimeOffset next = YouTubeSchedulePlanner.FindNext(
            slots,
            new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            TimeZoneInfo.Utc,
            TimeSpan.FromMinutes(30))!.Value;
        TestAssert.Equal(
            new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
            next,
            "The earliest preferred wall-clock slot should win without an engagement claim.");
        return Task.CompletedTask;
    }

    private static Task HistoryPersists()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "history.json");
        var store = new JsonYouTubePublishHistoryStore(path);
        var entry = new YouTubePublishHistoryEntry(
            "history-1",
            "asset-1",
            "A real title",
            "video-1",
            "https://youtu.be/video-1",
            YouTubePublishOutcome.Published,
            YouTubeVideoVisibility.Public,
            DateTimeOffset.UnixEpoch,
            scheduledForUtc: null);
        DateTimeOffset checkedAt = DateTimeOffset.UnixEpoch.AddHours(1);
        store.Append(entry.WithRemoteStatus(
            YouTubeRemoteVideoStatus.Exists,
            checkedAt));
        var loaded = new JsonYouTubePublishHistoryStore(path);
        TestAssert.Equal(1, loaded.Current.Count, "History should survive a process-local reload.");
        TestAssert.Equal(entry.Title, loaded.Current[0].Title, "Persisted metadata should round-trip.");
        TestAssert.Equal(
            YouTubeRemoteVideoStatus.Exists,
            loaded.Current[0].RemoteStatus,
            "Remote reconciliation status should round-trip.");
        TestAssert.Equal(
            checkedAt,
            loaded.Current[0].RemoteCheckedAtUtc,
            "The remote check time should round-trip exactly.");
        TestAssert.Throws<ArgumentException>(
            () => new YouTubePublishHistoryEntry(
                "bad", "asset", "title", null, null,
                YouTubePublishOutcome.Failed,
                YouTubeVideoVisibility.Private,
                DateTimeOffset.UnixEpoch,
                null),
            "A failure should require a diagnostic code.");
        loaded.Clear();
        TestAssert.False(File.Exists(path), "Clear should remove the local history file.");
        return Task.CompletedTask;
    }

    private static Task PreferencesPersist()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "preferences.json");
        var store = new JsonYouTubePublishPreferencesStore(path);
        var caller = new List<YouTubePreferredScheduleSlot>
        {
            new(DayOfWeek.Wednesday, new TimeOnly(19, 15)),
        };
        store.Replace(caller);
        caller.Clear();
        var loaded = new JsonYouTubePublishPreferencesStore(path);
        TestAssert.Equal(1, loaded.PreferredSlots.Count, "Preferred slots should be snapshotted and persisted.");
        TestAssert.Equal(new TimeOnly(19, 15), loaded.PreferredSlots[0].LocalTime, "The exact local time should round-trip.");
        return Task.CompletedTask;
    }

    private static Task ConnectionPermissionPersists()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "youtube-permission.json");
        var store = new JsonYouTubeConnectionPermissionStore(path);
        TestAssert.False(
            store.Current.IsEnabled,
            "YouTube network access must default off.");

        DateTimeOffset enabledAt =
            new(2026, 8, 2, 1, 2, 3, TimeSpan.Zero);
        var state = new YouTubeConnectionPermissionState(store);
        state.Enable(enabledAt);

        var reloaded = new JsonYouTubeConnectionPermissionStore(path);
        TestAssert.True(
            reloaded.Current.IsEnabled,
            "The explicit permission should survive restart locally.");
        TestAssert.Equal(
            enabledAt,
            reloaded.Current.EnabledAtUtc,
            "The exact UTC enable time should round-trip.");

        new YouTubeConnectionPermissionState(reloaded).Disable();
        TestAssert.False(
            new JsonYouTubeConnectionPermissionStore(path)
                .Current.IsEnabled,
            "Disabling should durably restore local-only mode.");
        TestAssert.Throws<ArgumentException>(
            () => state.Enable(enabledAt.ToOffset(TimeSpan.FromHours(-4))),
            "Permission provenance must use UTC.");
        return Task.CompletedTask;
    }

    private static async Task DisabledPermissionPreventsNetworkCalls()
    {
        var permission = new YouTubeConnectionPermissionState(
            new InMemoryYouTubeConnectionPermissionStore());
        var authorization = new FakeAuthorization();
        var api = new FakeApi();
        var service = new YouTubePublishingService(
            authorization,
            api,
            new InMemoryYouTubePublishHistoryStore(),
            permission);

        YouTubeAccountConnection? disconnected =
            await service.GetConnectionAsync(CancellationToken.None);
        TestAssert.True(
            disconnected is null,
            "A disabled connection check should remain local and disconnected.");
        TestAssert.Equal(
            0,
            authorization.GetCalls,
            "Initialization must not read or refresh an OAuth credential while disabled.");

        YouTubePublishingException exception =
            await TestAssert.ThrowsAsync<YouTubePublishingException>(
                () => service.ConnectAsync(CancellationToken.None),
                "A direct caller must not bypass the persisted permission gate.");
        TestAssert.Equal(
            "youtube.connection.disabled",
            exception.DiagnosticCode,
            "The disabled state should remain actionable.");
        TestAssert.Equal(
            0,
            authorization.ConnectCalls,
            "A rejected connection must not open OAuth or call Google.");

        using (var fixture = new PublishAssetFixture(32))
        {
            YouTubePublishingException publishFailure =
                await TestAssert.ThrowsAsync<YouTubePublishingException>(
                    () => service.PublishAsync(
                        fixture.CreateRequest(),
                        progress: null,
                        CancellationToken.None),
                    "Publishing must use the same hard permission gate.");
            TestAssert.Equal(
                "youtube.connection.disabled",
                publishFailure.DiagnosticCode,
                "The upload gate should retain the same actionable reason.");
            TestAssert.Equal(
                0,
                api.UploadCalls,
                "A disabled publish must not create a YouTube upload session.");
        }

        await TestAssert.ThrowsAsync<YouTubePublishingException>(
            () => service.ReconcileHistoryAsync(CancellationToken.None),
            "Recorded-video status checks must also require explicit permission.");

        permission.Enable(DateTimeOffset.UtcNow);
        YouTubeAccountConnection? connected =
            await service.GetConnectionAsync(CancellationToken.None);
        TestAssert.True(
            connected is not null,
            "The same service may check a credential only after explicit enablement.");
        TestAssert.Equal(
            1,
            authorization.GetCalls,
            "Exactly one enabled credential check should occur.");
    }

    private static async Task ResumableUploadUsesChunks()
    {
        using var fixture = new PublishAssetFixture((8 * 1024 * 1024) + 19);
        var handler = new RecordingYouTubeHandler();
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var api = new YouTubeDataApiClient(client);
        YouTubePublishRequest request = fixture.CreateRequest(
            timing: YouTubePublishTiming.Schedule,
            visibility: YouTubeVideoVisibility.Public,
            scheduledForUtc: new DateTimeOffset(2026, 8, 1, 14, 0, 0, TimeSpan.Zero),
            createdAtUtc: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero));
        string id = await api.UploadVideoAsync("token", request, null, CancellationToken.None);
        TestAssert.Equal("video-123", id, "The final YouTube video identifier should be returned.");
        TestAssert.Equal(1, handler.SessionRequests, "All chunks should share one resumable session.");
        TestAssert.Equal(2, handler.ChunkRanges.Count, "An 8 MiB boundary should create exactly two chunks.");
        TestAssert.Equal("bytes 0-8388607/8388627", handler.ChunkRanges[0], "The first byte range should be exact.");
        TestAssert.Equal("bytes 8388608-8388626/8388627", handler.ChunkRanges[1], "The final byte range should be exact.");
        TestAssert.True(handler.SessionJson.Contains("\"privacyStatus\":\"private\"", StringComparison.Ordinal), "Scheduled videos must upload privately.");
        TestAssert.True(handler.SessionJson.Contains("\"publishAt\":\"2026-08-01T14:00:00", StringComparison.Ordinal), "The exact scheduled instant should be sent.");
        TestAssert.True(handler.SessionQuery.Contains("notifySubscribers=true", StringComparison.Ordinal), "Subscriber notification should remain explicit.");
    }

    private static async Task UploadRejectsUntrustedSession()
    {
        using var fixture = new PublishAssetFixture(32);
        var handler = new RecordingYouTubeHandler
        {
            SessionLocation = new Uri("https://example.com/upload"),
        };
        using var client = new HttpClient(handler);
        var api = new YouTubeDataApiClient(client);
        YouTubePublishingException exception = await TestAssert.ThrowsAsync<YouTubePublishingException>(
            () => api.UploadVideoAsync("token", fixture.CreateRequest(), null, CancellationToken.None),
            "A resumable upload must remain on an approved Google HTTPS host.");
        TestAssert.Equal("youtube.upload.invalid-session-location", exception.DiagnosticCode, "The endpoint failure should stay actionable.");
    }

    private static async Task UploadDetectsChangedAsset()
    {
        using var fixture = new PublishAssetFixture((8 * 1024 * 1024) + 10);
        var handler = new RecordingYouTubeHandler
        {
            AfterFirstChunk = () => File.AppendAllBytes(fixture.Asset.OutputFullPath!, [42]),
        };
        using var client = new HttpClient(handler);
        var api = new YouTubeDataApiClient(client);
        YouTubePublishingException exception = await TestAssert.ThrowsAsync<YouTubePublishingException>(
            () => api.UploadVideoAsync("token", fixture.CreateRequest(), null, CancellationToken.None),
            "A changed rendered video must stop the upload path.");
        TestAssert.Equal("youtube.upload.asset-changed", exception.DiagnosticCode, "Source mutation should have a stable code.");
    }

    private static async Task PublishingAppliesFollowUps()
    {
        using var fixture = new PublishAssetFixture(32);
        var authorization = new FakeAuthorization();
        var api = new FakeApi();
        var history = new InMemoryYouTubePublishHistoryStore();
        var service = new YouTubePublishingService(authorization, api, history);
        YouTubePublishResult result = await service.PublishAsync(
            fixture.CreateRequest(playlistId: "playlist-1", thumbnailFullPath: fixture.ThumbnailPath),
            null,
            CancellationToken.None);
        TestAssert.Equal(1, api.UploadCalls, "The service should upload exactly once.");
        TestAssert.Equal(1, api.ThumbnailCalls, "The requested thumbnail should be applied once.");
        TestAssert.Equal(1, api.PlaylistCalls, "The requested playlist should be applied once.");
        TestAssert.True(result.ThumbnailApplied, "The result should retain thumbnail provenance.");
        TestAssert.Equal(1, history.Current.Count, "Only the complete result should enter history.");
    }

    private static async Task OptionalFailureBecomesWarning()
    {
        using var fixture = new PublishAssetFixture(32);
        var api = new FakeApi { FailThumbnail = true };
        var service = new YouTubePublishingService(
            new FakeAuthorization(),
            api,
            new InMemoryYouTubePublishHistoryStore());
        YouTubePublishResult result = await service.PublishAsync(
            fixture.CreateRequest(thumbnailFullPath: fixture.ThumbnailPath),
            null,
            CancellationToken.None);
        TestAssert.Equal(1, result.Warnings.Count, "A follow-up failure should not mislabel the uploaded video as failed.");
        TestAssert.False(result.ThumbnailApplied, "Failed thumbnail provenance must stay truthful.");
    }

    private static async Task ConcurrentUploadsAreRejected()
    {
        using var fixture = new PublishAssetFixture(32);
        var api = new FakeApi { BlockUpload = true };
        var service = new YouTubePublishingService(
            new FakeAuthorization(),
            api,
            new InMemoryYouTubePublishHistoryStore());
        Task<YouTubePublishResult> first = service.PublishAsync(
            fixture.CreateRequest(), null, CancellationToken.None);
        await api.UploadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestAssert.ThrowsAsync<InvalidOperationException>(
            () => service.PublishAsync(fixture.CreateRequest(), null, CancellationToken.None),
            "A second upload should not race the active operation.");
        api.ReleaseUpload.SetResult();
        await first;
        TestAssert.Equal(1, api.UploadCalls, "Only the accepted upload should reach the API.");
    }

    private static Task SelectorItemsUseFriendlyLabels()
    {
        TestAssert.Equal(
            "Upload now",
            new PublishChoiceItem<YouTubePublishTiming>(
                YouTubePublishTiming.PublishNow,
                "Upload now",
                "Upload immediately.").ToString(),
            "Choice controls should never expose record-debug text to users.");
        TestAssert.Equal(
            "No playlist",
            new PublishPlaylistItem(null, "No playlist", false).ToString(),
            "Playlist controls should display their human-facing labels.");
        TestAssert.Equal(
            "Gaming",
            new YouTubeVideoCategory("20", "Gaming").ToString(),
            "Category controls should display their human-facing titles.");
        return Task.CompletedTask;
    }

    private sealed class RecordingYouTubeHandler : HttpMessageHandler
    {
        public Uri SessionLocation { get; init; } =
            new("https://upload.youtube.googleapis.com/session/test");
        public Action? AfterFirstChunk { get; init; }
        public int SessionRequests { get; private set; }
        public string SessionJson { get; private set; } = string.Empty;
        public string SessionQuery { get; private set; } = string.Empty;
        public List<string> ChunkRanges { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post &&
                request.RequestUri!.AbsolutePath.EndsWith("/videos", StringComparison.Ordinal))
            {
                SessionRequests++;
                SessionJson = await request.Content!.ReadAsStringAsync(cancellationToken);
                SessionQuery = request.RequestUri.Query;
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Headers.Location = SessionLocation;
                return response;
            }

            ContentRangeHeaderValue range = request.Content!.Headers.ContentRange!;
            ChunkRanges.Add(range.ToString());
            _ = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            if (ChunkRanges.Count == 1)
            {
                AfterFirstChunk?.Invoke();
                var response = new HttpResponseMessage((HttpStatusCode)308);
                response.Headers.TryAddWithoutValidation("Range", $"bytes=0-{range.To!.Value}");
                return response;
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"video-123\"}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class FakeAuthorization : IYouTubeAuthorizationService
    {
        private static readonly YouTubeAccessCredential Credential = new(
            "access-token",
            DateTimeOffset.UtcNow.AddHours(1),
            "refresh-token",
            Array.AsReadOnly([YouTubeOAuthClientConfiguration.ManageYouTubeScope]));

        public int GetCalls { get; private set; }
        public int ConnectCalls { get; private set; }
        public int DisconnectCalls { get; private set; }

        public Task<YouTubeAccessCredential?> GetAccessCredentialAsync(
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            GetCalls++;
            return Task.FromResult<YouTubeAccessCredential?>(Credential);
        }

        public Task<YouTubeAccessCredential> ConnectAsync(
            CancellationToken cancellationToken)
        {
            ConnectCalls++;
            return Task.FromResult(Credential);
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            DisconnectCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeApi : IYouTubeDataApiClient
    {
        public int UploadCalls { get; private set; }
        public int ThumbnailCalls { get; private set; }
        public int PlaylistCalls { get; private set; }
        public bool FailThumbnail { get; init; }
        public bool BlockUpload { get; init; }
        public IReadOnlySet<string> ExistingVideoIds { get; init; } =
            new HashSet<string>(["video-123"], StringComparer.Ordinal);
        public TaskCompletionSource UploadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseUpload { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<YouTubeAccountConnection> GetChannelAsync(string accessToken, CancellationToken cancellationToken) =>
            Task.FromResult(new YouTubeAccountConnection("channel", "Test channel", DateTimeOffset.UnixEpoch));
        public Task<IReadOnlyList<YouTubePlaylist>> GetPlaylistsAsync(string accessToken, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<YouTubePlaylist>>([]);
        public Task<IReadOnlyList<YouTubeVideoCategory>> GetCategoriesAsync(string accessToken, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<YouTubeVideoCategory>>([new("20", "Gaming")]);

        public async Task<string> UploadVideoAsync(string accessToken, YouTubePublishRequest request, IProgress<YouTubePublishProgress>? progress, CancellationToken cancellationToken)
        {
            UploadCalls++;
            UploadStarted.TrySetResult();
            if (BlockUpload)
            {
                await ReleaseUpload.Task.WaitAsync(cancellationToken);
            }
            return "video-123";
        }

        public Task SetThumbnailAsync(string accessToken, string videoId, string thumbnailFullPath, CancellationToken cancellationToken)
        {
            ThumbnailCalls++;
            return FailThumbnail
                ? Task.FromException(new YouTubePublishingException("Thumbnail rejected.", "youtube.thumbnail.test"))
                : Task.CompletedTask;
        }

        public Task AddToPlaylistAsync(string accessToken, string videoId, string playlistId, CancellationToken cancellationToken)
        {
            PlaylistCalls++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlySet<string>> GetExistingVideoIdsAsync(
            string accessToken,
            IReadOnlyList<string> videoIds,
            CancellationToken cancellationToken) =>
            Task.FromResult(ExistingVideoIds);
    }

    private sealed class FixedCatalog(LibraryMediaAsset asset) : ILibraryCatalog
    {
        public IReadOnlyList<LibraryMediaAsset> Assets { get; } = [asset];
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }

    private sealed class MutableCatalog(
        params LibraryMediaAsset[] assets) : ILibraryCatalog
    {
        private LibraryMediaAsset[] _assets = assets.ToArray();

        public IReadOnlyList<LibraryMediaAsset> Assets =>
            Array.AsReadOnly(_assets.ToArray());

        public event EventHandler? Changed;

        public void Replace(params LibraryMediaAsset[] assets)
        {
            _assets = assets.ToArray();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class NullThumbnailPicker : IThumbnailFilePicker
    {
        public string? PickThumbnail() => null;
    }

    private sealed class RecordingPreparationDialog :
        IPublishPreparationDialogService
    {
        public int CallCount { get; private set; }
        public void Show(PublishViewModel viewModel) => CallCount++;
    }

    private sealed class FakePublishEditorialMetadataService :
        IPublishEditorialMetadataService
    {
        public int CallCount { get; private set; }
        public bool LastRequireAi { get; private set; }
        public bool FailNext { get; set; }
        public Action? BeforeReturn { get; set; }
        public List<int?> PreviousCompletedAttempts { get; } = [];
        public List<IReadOnlyList<string>> PriorTitleHistories { get; } = [];
        public bool IsAiAvailable => true;

        public PublishEditorialProfileSnapshot LoadProfile() =>
            new(
                "Chat",
                ClipEditorialProfile.DefaultNamingGuidance,
                string.Empty);

        public bool CanReroll(LibraryMediaAsset asset) => true;

        public Task<PublishEditorialRerollResult> RerollAsync(
            LibraryMediaAsset asset,
            string audienceAddress,
            string namingGuidance,
            string descriptionSignature,
            int? previousCompletedAttempt,
            string currentTitle,
            IReadOnlyList<string> priorAcceptedTitles,
            bool requireAi,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastRequireAi = requireAi;
            PreviousCompletedAttempts.Add(previousCompletedAttempt);
            PriorTitleHistories.Add(priorAcceptedTitles.ToArray());
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException(
                    "Qualified local AI failed for this test.");
            }
            int attempt = (previousCompletedAttempt ?? 1) + 1;
            var draft = new ClipEditorialMetadataDraft(
                "A different grounded title",
                "A different grounded description.",
                ["different", "grounded"],
                ClipEditorialMetadataOrigin.AiAssisted,
                new ClipEditorialMetadataGeneratorIdentity(
                    "fake-qualified-local-provider",
                    "1.0.0"),
                attempt);
            BeforeReturn?.Invoke();
            return Task.FromResult(
                new PublishEditorialRerollResult(
                    draft.Title,
                    draft.Description,
                    draft.TagsText,
                    draft.Attempt,
                    priorAcceptedTitles,
                    "Grounded local-AI wording is ready."));
        }

    }

    private sealed class RecordingEditorialGenerationService :
        IClipEditorialMetadataGenerationService
    {
        public bool IsAiAvailable => true;
        public int CallCount { get; private set; }
        public ClipEditorialMetadataRequest? LastRequest { get; private set; }
        public List<ClipEditorialMetadataRequest> Requests { get; } = [];

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastRequest = request;
            Requests.Add(request);
            return Task.FromResult(
                new ClipEditorialMetadataDraft(
                    "Shared service title",
                    "Shared service description.",
                    ["shared"],
                    ClipEditorialMetadataOrigin.AiAssisted,
                    new ClipEditorialMetadataGeneratorIdentity(
                        "shared-test-generator",
                        "1.0"),
                    request.Attempt,
                    priorAcceptedTitles: request.PriorAcceptedTitleExclusions
                        .Select(static value => value.Title)));
        }
    }

    private sealed class PublishAssetFixture : IDisposable
    {
        private readonly TemporaryDirectory _directory = new();

        public PublishAssetFixture(long outputSize)
        {
            SourcePath = Path.Combine(_directory.Path, "source.mkv");
            File.WriteAllBytes(SourcePath, [1, 2, 3, 4]);
            string output = Path.Combine(_directory.Path, "final.mp4");
            using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                stream.SetLength(outputSize);
            }
            ThumbnailPath = Path.Combine(_directory.Path, "thumbnail.png");
            File.WriteAllBytes(ThumbnailPath, TestMediaFactory.CreatePngHeader(1280, 720));
            Asset = new LibraryMediaAsset(
                "asset-1",
                "project-1",
                GenerationMode.IndividualClips,
                1,
                output,
                ThumbnailPath,
                TimeSpan.FromSeconds(30),
                1080,
                1920,
                "A grounded test title",
                "A description grounded in the finalized test video.",
                ["gameplay", "test"],
                new DateTimeOffset(
                    2026,
                    8,
                    1,
                    12,
                    0,
                    0,
                    TimeSpan.Zero));
        }

        public LibraryMediaAsset Asset { get; }
        public string SourcePath { get; }
        public string ThumbnailPath { get; }

        public YouTubePublishRequest CreateRequest(
            IEnumerable<string>? tags = null,
            YouTubePublishTiming timing = YouTubePublishTiming.PublishNow,
            YouTubeVideoVisibility visibility = YouTubeVideoVisibility.Private,
            DateTimeOffset? scheduledForUtc = null,
            DateTimeOffset? createdAtUtc = null,
            string? playlistId = null,
            string? thumbnailFullPath = null) =>
            new(
                Asset,
                "A grounded test title",
                "A description grounded in the finalized test video.",
                tags ?? ["gameplay", "test"],
                "20",
                visibility,
                timing,
                YouTubeAudience.NotMadeForKids,
                containsSyntheticMedia: false,
                notifySubscribers: true,
                scheduledForUtc,
                playlistId,
                thumbnailFullPath,
                createdAtUtc ?? new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero));

        public void Dispose() => _directory.Dispose();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "ReplayFoundryYouTubeTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
