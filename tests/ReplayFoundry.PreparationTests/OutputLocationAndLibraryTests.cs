using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Platform.Storage;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.Processes;
using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.PreparationTests;

internal static class OutputLocationAndLibraryTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Generation output location persists without rewriting existing paths", OutputLocationPersists),
        new("Generation output location rejects unsafe roots", OutputLocationRejectsUnsafeRoots),
        new("Local JSON stores share immutable responsibility-specific policies", LocalJsonPoliciesAreSharedAndReadOnly),
        new("Library relink preserves asset identity and metadata", LibraryRelinkPreservesIdentity),
        new("Library removal preserves the rendered media file", LibraryRemovalPreservesMedia),
        new("Library bulk selection removes entries in one confirmed catalog update", LibraryBulkRemovalIsConfirmedOnce),
        new("Library persistence retains exact rendered candidate identities", LibraryPersistenceRetainsCandidateIdentity),
        new("Library migrates legacy publish titles into durable navigation labels", LegacyLibraryLabelsMigrateDurably),
        new("Finalized renders keep publish titles separate from literal Library labels", FinalizedRenderDerivesLiteralLibraryLabel),
        new("Library playback scrubs the exact completed video continuously", LibraryPlaybackScrubsCompletedVideo),
        new("Library file observation detects a video restored at the same path", LibraryFileObservationDetectsRestore),
        new("Library restores preview and thumbnail state without a manual refresh", LibraryRestoresPreviewAndThumbnailAutomatically),
        new("Library rebuilds a missing thumbnail when only its video returns", LibraryRebuildsThumbnailWhenOnlyVideoReturns),
        new("Library thumbnail recovery reuses atomic FFmpeg extraction", LibraryThumbnailRecoveryUsesAtomicFfmpegExtraction),
        new("Library organization groups real assets by date folder and project", LibraryOrganizationUsesCatalogIdentity),
        new("Settings and Library expose bounded local file actions", ViewModelsExposeBoundedActions),
    ];

    private static Task OutputLocationPersists()
    {
        using var directory = new TemporaryDirectory();
        string settingsPath = Path.Combine(directory.Root, "output-location.json");
        string defaultRoot = Path.Combine(directory.Root, "default");
        string customRoot = Path.Combine(directory.Root, "custom");
        var state = new GenerationOutputLocationState(
            new JsonGenerationOutputLocationStore(settingsPath),
            defaultRoot);

        TestAssert.Equal(
            defaultRoot,
            state.OutputRootDirectory,
            "A new state must use its default root.");
        state.SetCustomRoot(customRoot);

        var reloaded = new GenerationOutputLocationState(
            new JsonGenerationOutputLocationStore(settingsPath),
            defaultRoot);
        TestAssert.Equal(
            customRoot,
            reloaded.OutputRootDirectory,
            "A custom output root must survive a new store instance.");
        TestAssert.True(
            reloaded.UsesCustomRoot,
            "The persisted root must remain explicitly custom.");

        reloaded.UseDefaultRoot();
        var restored = new GenerationOutputLocationState(
            new JsonGenerationOutputLocationStore(settingsPath),
            defaultRoot);
        TestAssert.Equal(
            defaultRoot,
            restored.OutputRootDirectory,
            "Using the default must remove the custom override.");
        return Task.CompletedTask;
    }

    private static Task OutputLocationRejectsUnsafeRoots()
    {
        using var directory = new TemporaryDirectory();
        TestAssert.Throws<ArgumentException>(
            () => new GenerationOutputLocationState(
                new InMemoryGenerationOutputLocationStore(),
                "relative-output"),
            "Relative output roots must be rejected.");
        TestAssert.Throws<ArgumentException>(
            () => new JsonGenerationOutputLocationStore(
                    Path.Combine(directory.Root, "location.json"))
                .Save("relative-output"),
            "The persistent store must not accept a relative root.");

        string filePath = Path.Combine(directory.Root, "not-a-folder");
        File.WriteAllText(filePath, "test");
        var state = new GenerationOutputLocationState(
            new InMemoryGenerationOutputLocationStore(),
            Path.Combine(directory.Root, "default"));
        TestAssert.Throws<IOException>(
            () => state.SetCustomRoot(filePath),
            "A file cannot be selected as an output root.");
        return Task.CompletedTask;
    }

    private static Task LocalJsonPoliciesAreSharedAndReadOnly()
    {
        System.Text.Json.JsonSerializerOptions camelCase =
            ReplayFoundryLocalJsonPolicy.IndentedCamelCase;
        System.Text.Json.JsonSerializerOptions declaredNames =
            ReplayFoundryLocalJsonPolicy.IndentedDeclaredPropertyNames;
        TestAssert.True(
            camelCase.IsReadOnly && camelCase.WriteIndented &&
            ReferenceEquals(
                camelCase.PropertyNamingPolicy,
                System.Text.Json.JsonNamingPolicy.CamelCase),
            "The shared local camel-case JSON policy is mutable or changed shape.");
        TestAssert.True(
            declaredNames.IsReadOnly && declaredNames.WriteIndented &&
            declaredNames.PropertyNamingPolicy is null,
            "The shared declared-name JSON policy is mutable or changed shape.");
        TestAssert.True(
            ReferenceEquals(
                StaticJsonOptions(typeof(JsonLibraryCatalogStore), "JsonOptions"),
                camelCase) &&
            ReferenceEquals(
                StaticJsonOptions(typeof(JsonYouTubePublishDraftStore), "JsonOptions"),
                camelCase) &&
            ReferenceEquals(
                StaticJsonOptions(typeof(JsonGenerationGameContextMemory), "JsonOptions"),
                declaredNames),
            "Local stores recreated an identical serializer policy instead of sharing its owner.");
        System.Text.Json.JsonSerializerOptions visualRequests =
            VisualSemanticRequestJsonPolicy.IndentedCamelCase;
        TestAssert.True(
            visualRequests.IsReadOnly && visualRequests.WriteIndented &&
            ReferenceEquals(
                visualRequests.PropertyNamingPolicy,
                System.Text.Json.JsonNamingPolicy.CamelCase) &&
            ReferenceEquals(
                StaticJsonOptions(
                    typeof(Qwen3VlBatchRequestJsonWriter),
                    "JsonOptions"),
                visualRequests) &&
            ReferenceEquals(
                StaticJsonOptions(
                    typeof(Qwen3VlGroundedMetadataExecutor),
                    "RequestJsonOptions"),
                visualRequests),
            "Visual-semantic request writers recreated their identical serializer policy.");
        return Task.CompletedTask;
    }

    private static object? StaticJsonOptions(Type owner, string fieldName) =>
        owner.GetField(
            fieldName,
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static)?.GetValue(null);

    private static Task LibraryRelinkPreservesIdentity()
    {
        using var directory = new TemporaryDirectory();
        LibraryMediaAsset original = CreateAsset(
            Path.Combine(directory.Root, "missing", "clip.mp4"));
        var store = new InMemoryLibraryCatalogStore();
        store.Replace([original]);
        using var catalog = new GenerationLibraryCatalog(
            new GenerationOutputSession(),
            store);
        string replacement = Path.Combine(directory.Root, "moved", "clip.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(replacement)!);
        File.WriteAllBytes(replacement, [0, 1, 2, 3]);
        string thumbnail = Path.ChangeExtension(
            replacement,
            ".thumbnail.jpg");
        File.WriteAllBytes(thumbnail, [4, 5, 6]);

        LibraryMediaAsset rebound = catalog.RelinkMissingAsset(
            original.Id,
            replacement);

        TestAssert.Equal(original.Id, rebound.Id, "Asset ID must be preserved.");
        TestAssert.Equal(original.ProjectId, rebound.ProjectId, "Project ID must be preserved.");
        TestAssert.Equal(original.Title, rebound.Title, "Title must be preserved.");
        TestAssert.Equal(
            original.LibraryLabel,
            rebound.LibraryLabel,
            "The Library navigation label must be preserved independently from the publish title.");
        TestAssert.Equal(original.Description, rebound.Description, "Description must be preserved.");
        TestAssert.Equal(original.Duration, rebound.Duration, "Duration must be preserved.");
        TestAssert.Equal(replacement, rebound.OutputFullPath, "Only the media path should change.");
        TestAssert.Equal(thumbnail, rebound.ThumbnailFullPath, "A moved sibling thumbnail should be rebound.");
        TestAssert.Equal(
            replacement,
            store.Current.Single().OutputFullPath,
            "The relink must be persisted atomically through the catalog store.");
        TestAssert.Throws<InvalidOperationException>(
            () => catalog.RelinkMissingAsset(original.Id, replacement),
            "An available asset must not be rebound casually.");
        return Task.CompletedTask;
    }

    private static Task ViewModelsExposeBoundedActions()
    {
        using var directory = new TemporaryDirectory();
        string defaultRoot = Path.Combine(directory.Root, "default");
        string customRoot = Path.Combine(directory.Root, "custom");
        var outputLocation = new GenerationOutputLocationState(
            new InMemoryGenerationOutputLocationStore(),
            defaultRoot);
        var folderLauncher = new RecordingFolderLauncher();
        using var settings = new SettingsViewModel(
            outputLocation,
            new FixedOutputFolderPicker(customRoot),
            folderLauncher);

        settings.ChooseOutputFolderCommand.Execute(null);
        TestAssert.Equal(
            customRoot,
            settings.OutputRootDirectory,
            "Settings must update the shared output-location state.");
        TestAssert.True(
            settings.StorageNotice.Contains(
                "Existing Library videos were not moved",
                StringComparison.Ordinal),
            "Settings must state the future-project boundary.");
        settings.SelectedSectionItem = settings.Sections.Single(section =>
            section.Key == SettingsSection.Storage);
        TestAssert.Equal(
            SettingsSection.Storage,
            settings.SelectedSection,
            "The selected Settings item must project its typed section key.");
        settings.OpenOutputFolderCommand.Execute(null);
        TestAssert.Equal(
            customRoot,
            folderLauncher.LastOpened,
            "Settings must open the exact current output root.");

        LibraryMediaAsset missing = CreateAsset(
            Path.Combine(directory.Root, "old", "clip.mp4"));
        var store = new InMemoryLibraryCatalogStore();
        store.Replace([missing]);
        using var catalog = new GenerationLibraryCatalog(
            new GenerationOutputSession(),
            store);
        string moved = Path.Combine(directory.Root, "new", "clip.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        File.WriteAllBytes(moved, [7, 8, 9]);
        var libraryPicker = new FixedLibraryMediaFilePicker(moved);
        using var library = new LibraryViewModel(
            catalog,
            catalog,
            libraryPicker,
            folderLauncher);

        TestAssert.True(
            library.RelinkMissingFileCommand.CanExecute(null),
            "A missing selected asset must expose the explicit relink action.");
        library.RelinkMissingFileCommand.Execute(null);
        TestAssert.Equal("Ready", library.SelectedItem!.Status, "Relink must refresh availability.");
        TestAssert.True(
            library.LibraryNotice.Contains(
                "title and details did not change",
                StringComparison.OrdinalIgnoreCase),
            "Relink must explain that the visible Library information was retained.");
        library.OpenSelectedFolderCommand.Execute(null);
        TestAssert.Equal(
            Path.GetDirectoryName(moved),
            folderLauncher.LastOpened,
            "Library must open the selected asset folder, not an arbitrary root.");
        TestAssert.Equal(
            missing.Title,
            library.SelectedItem!.Title,
            "Library must preserve the publishing title when a video is relinked.");

        File.Delete(moved);
        library.RefreshLibraryCommand.Execute(null);
        TestAssert.Equal(
            "Missing locally",
            library.SelectedItem!.Status,
            "Refresh must revalidate known catalog paths.");
        TestAssert.Equal(
            1,
            library.Items.Count,
            "Refresh must not discover or fabricate arbitrary media entries.");
        return Task.CompletedTask;
    }

    private static Task LibraryPersistenceRetainsCandidateIdentity()
    {
        using var directory = new TemporaryDirectory();
        string catalogPath = Path.Combine(directory.Root, "library.json");
        var asset = new LibraryMediaAsset(
            "render-asset",
            "render-project",
            GenerationMode.IndividualClips,
            1,
            Path.Combine(directory.Root, "render.mp4"),
            null,
            TimeSpan.FromSeconds(18),
            1080,
            1920,
            "Rendered title",
            "Rendered description",
            ["tag"],
            DateTimeOffset.UnixEpoch,
            sourceCandidateIds: ["candidate-exact"],
            libraryLabel: "Control — document at 1:05");

        var store = new JsonLibraryCatalogStore(catalogPath);
        store.Replace([asset]);
        LibraryMediaAsset restored =
            new JsonLibraryCatalogStore(catalogPath).Current.Single();

        TestAssert.True(
            restored.SourceCandidateIds.SequenceEqual(
                new[] { "candidate-exact" },
                StringComparer.Ordinal),
            "A restart must retain the exact candidate identity used to reconcile Studio and Library state.");
        TestAssert.Equal(
            "Rendered title",
            restored.Title,
            "Persistence must retain the independent publish title.");
        TestAssert.Equal(
            "Control — document at 1:05",
            restored.LibraryLabel,
            "Persistence must retain the durable Library navigation label.");
        return Task.CompletedTask;
    }

    private static Task LegacyLibraryLabelsMigrateDurably()
    {
        using var directory = new TemporaryDirectory();
        string catalogPath = Path.Combine(directory.Root, "legacy-library.json");
        const string legacyTitle = "Legacy Publish Title #Control";
        File.WriteAllText(
            catalogPath,
            JsonSerializer.Serialize(
                new
                {
                    schemaVersion = "replayfoundry-library-catalog-1.1",
                    assets = new[]
                    {
                        new
                        {
                            id = "legacy-asset",
                            projectId = "legacy-project",
                            mode = GenerationMode.IndividualClips.ToString(),
                            rank = 1,
                            outputFullPath = Path.Combine(
                                directory.Root,
                                "legacy.mp4"),
                            thumbnailFullPath = (string?)null,
                            durationTicks = TimeSpan.FromSeconds(20).Ticks,
                            outputWidth = 1080,
                            outputHeight = 1920,
                            title = legacyTitle,
                            description = "Legacy description",
                            tags = new[] { "Control" },
                            addedAtUtc = DateTimeOffset.UnixEpoch,
                            contributingCandidateCount = 1,
                            sourceCandidateIds = new[] { "legacy-candidate" },
                        },
                    },
                }));

        LibraryMediaAsset migrated =
            new JsonLibraryCatalogStore(catalogPath).Current.Single();
        TestAssert.Equal(
            legacyTitle,
            migrated.LibraryLabel,
            "A legacy asset without retained context must safely fall back to its existing title.");

        using JsonDocument rewritten = JsonDocument.Parse(
            File.ReadAllText(catalogPath));
        TestAssert.Equal(
            "replayfoundry-library-catalog-1.2",
            rewritten.RootElement.GetProperty("schemaVersion").GetString(),
            "Loading a legacy catalog must durably migrate it to the Library-label schema.");
        TestAssert.Equal(
            legacyTitle,
            rewritten.RootElement.GetProperty("assets")[0]
                .GetProperty("libraryLabel").GetString(),
            "The migrated label must be written atomically instead of existing only in memory.");
        return Task.CompletedTask;
    }

    private static Task FinalizedRenderDerivesLiteralLibraryLabel()
    {
        using var directory = new TemporaryDirectory();
        string source = Path.Combine(directory.Root, "control-session.mkv");
        string output = Path.Combine(directory.Root, "control-clip.mp4");
        File.WriteAllBytes(source, [1, 2, 3]);
        File.WriteAllBytes(output, [4, 5, 6]);
        var media = TestMediaFactory.Create(
            source,
            TimeSpan.FromMinutes(10));
        TimeSpan sourceStart = TimeSpan.FromSeconds(65);
        TimeSpan sourceEnd = TimeSpan.FromSeconds(85);
        var claim = new GroundedGameContextClaim(
            "stable-ocr-hotline",
            GroundedGameContextClaimKind.StableReadableText,
            "HOTLINE",
            GroundedGameContextClaimAuthority.StableLocalOcr,
            GroundedGameContextClaimState.Supported,
            localEvidenceIds: ["ocr-hotline"],
            fieldAuthorizations:
            [
                GroundedEditorialField.Title,
                GroundedEditorialField.Description,
            ]);
        var brief = new GroundedEditorialBrief(
            "candidate-control-document",
            sourceStart,
            sourceEnd,
            [claim],
            canonicalIdentity: "Control",
            primaryGameplayBeat: "An in-game document was opened.",
            presentationKind: GroundedEditorialPresentationKind.DocumentOrLore,
            momentKind: GroundedEditorialMomentKind.Exposition);
        var context = new ClipEditorialContext(
            "candidate-control-document",
            source,
            "Control session",
            sourceStart,
            sourceEnd,
            media.Duration,
            82,
            "A document interaction was retained.",
            gameContext: new ClipEditorialGameContext(
                "Control",
                "#Control",
                contextNotes: null,
                ClipEditorialGameContextSource.UserConfirmed),
            editorialBrief: brief);
        var audit = new GameKnowledgeInfluenceAudit(
            brief.Fingerprint,
            ClipEditorialRevisionKind.InitialDraft,
            usedClaimIds: [claim.Id],
            usedEvidenceIds: ["ocr-hotline"],
            resolvedBrief: brief,
            sourceBindings: brief.SourceBindings);
        var metadata = new ClipEditorialMetadataDraft(
            "A New Piece of the Story Emerged #Control",
            "The document added more context.",
            ["Control", "document"],
            ClipEditorialMetadataOrigin.Heuristic,
            new ClipEditorialMetadataGeneratorIdentity(
                "Library label fixture",
                "1.0"),
            attempt: 0,
            groundingAudit: audit);
        var draftAsset = new GenerationOutputAsset(
            context.CandidateId,
            rank: 1,
            media,
            outputFullPath: null,
            sourceStart,
            sourceEnd,
            score: 82,
            qualityTarget: 70,
            GenerationCandidateSelectionReason.QualityQualified,
            "A retained document interaction.",
            editorialContext: context,
            editorialMetadata: metadata);
        var project = new GenerationOutputProject(
            "project-library-label",
            GenerationMode.IndividualClips,
            Path.Combine(directory.Root, "project-output"),
            requestedCount: 1,
            ClipFulfillmentPreference.FillRequestedCount,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
            [draftAsset],
            DateTimeOffset.UnixEpoch);
        var session = new GenerationOutputSession();
        var store = new InMemoryLibraryCatalogStore();
        using var catalog = new GenerationLibraryCatalog(session, store);
        session.Publish(project);
        session.FinalizeProject(project.Finalize(
            [draftAsset.WithRenderedOutput(output)],
            DateTimeOffset.UnixEpoch.AddMinutes(1)));

        LibraryMediaAsset archived = catalog.Assets.Single();
        TestAssert.Equal(
            metadata.Title,
            archived.Title,
            "The audience-facing title must remain unchanged for publishing.");
        TestAssert.Equal(
            "Control — document · HOTLINE",
            archived.LibraryLabel,
            "The render handoff must derive a literal label from confirmed game and used document evidence.");
        TestAssert.False(
            archived.LibraryLabel.Contains(
                "story",
                StringComparison.OrdinalIgnoreCase),
            "Abstract publish wording must never leak into the Library label.");

        var ungrounded = new GenerationOutputAsset(
            "candidate-ungrounded",
            rank: 2,
            media,
            outputFullPath: null,
            sourceStart,
            sourceEnd,
            score: 75,
            qualityTarget: 70,
            GenerationCandidateSelectionReason.QualityQualified,
            "A retained gameplay interval.");
        TestAssert.Equal(
            "Gameplay — 1:05",
            LibraryLabelPolicy.CreateForClip(ungrounded),
            "Low-evidence labels must state only the generic game class and exact source timestamp.");

        using var library = new LibraryViewModel(catalog);
        TestAssert.Equal(
            archived.Title,
            library.SelectedItem!.Title,
            "Library must display the same title as Studio and publishing, including older entries with a different navigation label.");
        TestAssert.Equal(archived.Title, archived.DisplayName,
            "Every Library display surface must use the audience-facing title.");
        return Task.CompletedTask;
    }

    private static Task LibraryRemovalPreservesMedia()
    {
        using var directory = new TemporaryDirectory();
        string media = Path.Combine(directory.Root, "rendered.mp4");
        File.WriteAllBytes(media, [1, 2, 3, 4]);
        LibraryMediaAsset asset = CreateAsset(media);
        var store = new InMemoryLibraryCatalogStore();
        store.Replace([asset]);
        using var catalog = new GenerationLibraryCatalog(
            new GenerationOutputSession(),
            store);
        var confirmation = new FixedLibraryRemovalConfirmation(confirm: true);
        using var library = new LibraryViewModel(
            catalog,
            catalog,
            new FixedLibraryMediaFilePicker(media),
            new RecordingFolderLauncher(),
            catalog,
            confirmation);

        TestAssert.True(
            library.RemoveSelectedFromLibraryCommand.CanExecute(null),
            "A selected Library entry should expose catalog removal.");
        library.RemoveSelectedFromLibraryCommand.Execute(null);

        TestAssert.Equal(1, confirmation.CallCount, "Removal must require one explicit confirmation.");
        TestAssert.Equal(0, catalog.Assets.Count, "The entry must leave the live catalog.");
        TestAssert.Equal(0, store.Current.Count, "Removal must persist through the catalog store.");
        TestAssert.True(File.Exists(media), "Removing a Library entry must never delete the rendered video.");
        TestAssert.True(
            library.LibraryNotice.Contains("remains on disk", StringComparison.Ordinal),
            "The UI must explain that media was preserved.");
        return Task.CompletedTask;
    }

    private static Task LibraryBulkRemovalIsConfirmedOnce()
    {
        using var directory = new TemporaryDirectory();
        string firstPath = Path.Combine(directory.Root, "first.mp4");
        string secondPath = Path.Combine(directory.Root, "second.mp4");
        File.WriteAllBytes(firstPath, [1]);
        File.WriteAllBytes(secondPath, [2]);
        LibraryMediaAsset first = CreateAsset(firstPath);
        LibraryMediaAsset second = new(
            "asset-2", "project-2", GenerationMode.IndividualClips, 1,
            secondPath, null, TimeSpan.FromSeconds(18), 1080, 1920,
            "Second title", string.Empty, [],
            new DateTimeOffset(2026, 8, 5, 13, 0, 0, TimeSpan.Zero));
        var store = new InMemoryLibraryCatalogStore();
        store.Replace([first, second]);
        using var catalog = new GenerationLibraryCatalog(
            new GenerationOutputSession(),
            store);
        var confirmation = new FixedLibraryRemovalConfirmation(confirm: true);
        using var library = new LibraryViewModel(
            catalog, catalog, new FixedLibraryMediaFilePicker(firstPath),
            new RecordingFolderLauncher(), catalog, confirmation);

        library.BeginSelectionCommand.Execute(null);
        library.SelectAllVisibleCommand.Execute(null);
        TestAssert.Equal(2, library.MarkedCount, "Select all must select the current filtered result set.");
        library.RemoveMarkedCommand.Execute(null);

        TestAssert.Equal(1, confirmation.CallCount, "Bulk removal must use one confirmation dialog.");
        TestAssert.Equal(2, confirmation.LastAssetCount, "The confirmation must describe the entire selected batch.");
        TestAssert.Equal(0, store.Current.Count, "The entire selected batch must persist in one catalog replacement.");
        TestAssert.True(File.Exists(firstPath) && File.Exists(secondPath), "Bulk catalog removal must preserve every rendered file.");
        TestAssert.False(library.IsSelectionMode, "Successful bulk removal must leave selection mode.");
        return Task.CompletedTask;
    }

    private static Task LibraryPlaybackScrubsCompletedVideo()
    {
        using var directory = new TemporaryDirectory();
        string media = Path.Combine(directory.Root, "preview.mp4");
        File.WriteAllBytes(media, [0, 1, 2, 3]);
        LibraryMediaAsset asset = CreateAsset(media);
        var store = new InMemoryLibraryCatalogStore();
        store.Replace([asset]);
        using var catalog = new GenerationLibraryCatalog(
            new GenerationOutputSession(),
            store);
        using var library = new LibraryViewModel(catalog);
        LibraryPlaybackViewModel playback = library.Playback;

        TestAssert.Equal(
            media,
            playback.MediaFullPath,
            "Library playback must use the exact completed render instead of extracting another preview.");
        TestAssert.True(
            playback.PlayPauseCommand.CanExecute(null),
            "An available rendered video must be playable.");
        int initialSeekVersion = playback.SeekVersion;
        playback.BeginScrub();
        playback.PositionSeconds = 12.875;
        TestAssert.True(
            playback.SeekVersion > initialSeekVersion,
            "Dragging the Library timeline must request a native seek continuously, not only after release.");
        TestAssert.Equal(
            "0:12",
            playback.PositionText,
            "The live scrub label must use whole seconds while retaining the exact internal position.");
        TestAssert.Equal(
            12.875,
            playback.PositionSeconds,
            "Whole-second display must not round the underlying seek position.");
        playback.EndScrub();

        playback.PlayPauseCommand.Execute(null);
        TestAssert.True(playback.IsPlaying, "Play must start the selected Library preview.");
        playback.ReportPlaybackPosition(TimeSpan.FromSeconds(18.25));
        TestAssert.Equal("0:18", playback.PositionText, "Playback ticks must update the visible current time.");
        playback.ReportEnded();
        TestAssert.False(playback.IsPlaying, "Media completion must stop the transport state.");
        TestAssert.Equal("0:24", playback.PositionText, "Media completion must move to the catalog duration.");
        int completedSeekVersion = playback.SeekVersion;
        playback.PlayPauseCommand.Execute(null);
        TestAssert.True(
            playback.IsPlaying,
            "Play after media completion must start a new Library preview pass.");
        TestAssert.Equal(
            0d,
            playback.PositionSeconds,
            "Play after media completion must rewind the Library preview to zero.");
        TestAssert.True(
            playback.SeekVersion > completedSeekVersion,
            "Replay must publish the zero-position seek before native playback starts.");
        return Task.CompletedTask;
    }

    private static async Task LibraryFileObservationDetectsRestore()
    {
        using var directory = new TemporaryDirectory();
        string media = Path.Combine(directory.Root, "observed.mp4");
        File.WriteAllBytes(media, [0, 1, 2, 3]);
        LibraryMediaAsset asset = CreateAsset(media);
        using var monitor = new LibraryFileAvailabilityMonitor(
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(50));
        monitor.Watch([asset]);

        var deletionObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<LibraryFilesChangedEventArgs> deletionHandler =
            (_, change) =>
            {
                if (change.Touches(media) && !File.Exists(media))
                {
                    deletionObserved.TrySetResult();
                }
            };
        monitor.FilesChanged += deletionHandler;
        File.Delete(media);
        await deletionObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        monitor.FilesChanged -= deletionHandler;

        var restorationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<LibraryFilesChangedEventArgs> restorationHandler =
            (_, change) =>
            {
                if (change.Touches(media) && File.Exists(media))
                {
                    restorationObserved.TrySetResult();
                }
            };
        monitor.FilesChanged += restorationHandler;
        File.WriteAllBytes(media, [4, 5, 6, 7]);
        await restorationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        TestAssert.True(
            File.Exists(media),
            "The availability observer must report the restored path, not only its deletion.");
    }

    private static Task LibraryRestoresPreviewAndThumbnailAutomatically()
    {
        using var directory = new TemporaryDirectory();
        string media = Path.Combine(directory.Root, "restored.mp4");
        string thumbnail = Path.Combine(
            directory.Root,
            "restored.thumbnail.jpg");
        File.WriteAllBytes(media, [0, 1, 2, 3]);
        File.WriteAllBytes(thumbnail, [4, 5, 6, 7]);
        var asset = new LibraryMediaAsset(
            "restored-asset",
            "restored-project",
            GenerationMode.IndividualClips,
            1,
            media,
            thumbnail,
            TimeSpan.FromSeconds(24),
            1080,
            1920,
            "Restored title",
            "Restored description",
            ["tag"],
            new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero));
        var store = new InMemoryLibraryCatalogStore();
        store.Replace([asset]);
        using var catalog = new GenerationLibraryCatalog(
            new GenerationOutputSession(),
            store);
        var monitor = new RecordingLibraryFileAvailabilityMonitor();
        using var library = new LibraryViewModel(catalog, monitor);
        LibraryItem selected = library.SelectedItem!;
        int initialThumbnailRevision = selected.ThumbnailRevision;

        TestAssert.Equal(
            media,
            library.Playback.MediaFullPath,
            "An initially available Library video must prime its exact preview path.");
        TestAssert.True(
            selected.HasThumbnail,
            "An existing retained thumbnail must start visible.");

        File.Delete(media);
        File.Delete(thumbnail);
        monitor.ReportChanged(media, thumbnail);

        TestAssert.Equal(
            "Missing locally",
            selected.Status,
            "Deleting the rendered video must update the existing Library item automatically.");
        TestAssert.False(
            library.Playback.IsAvailable,
            "Deleting the selected render must retire its stale preview source.");
        TestAssert.False(
            selected.HasThumbnail,
            "Deleting the thumbnail must replace it with the Library placeholder.");
        TestAssert.True(
            selected.ThumbnailRevision > initialThumbnailRevision,
            "A thumbnail file event must invalidate the retained image load.");
        int missingThumbnailRevision = selected.ThumbnailRevision;

        File.WriteAllBytes(media, [8, 9, 10, 11]);
        File.WriteAllBytes(thumbnail, [12, 13, 14, 15]);
        monitor.ReportChanged(media, thumbnail);

        TestAssert.Equal(
            "Ready",
            selected.Status,
            "Restoring the rendered video at its retained path must make the item ready again.");
        TestAssert.Equal(
            media,
            library.Playback.MediaFullPath,
            "The selected preview must be re-primed as soon as its video returns.");
        TestAssert.True(
            selected.HasThumbnail,
            "The thumbnail must return without restarting or reimporting the Library entry.");
        TestAssert.True(
            selected.ThumbnailRevision > missingThumbnailRevision,
            "Restoring the thumbnail must request a fresh decoded image.");
        TestAssert.True(
            monitor.WatchedAssets.Single().Id.Equals(
                asset.Id,
                StringComparison.Ordinal),
            "The Library must register every retained asset with its file observer.");
        return Task.CompletedTask;
    }

    private static Task LibraryRebuildsThumbnailWhenOnlyVideoReturns()
    {
        Task? verification = null;
        UiUxApplicationSurfaceTests.RunOnSta(() =>
            verification = LibraryRebuildsThumbnailOnDispatcherAsync());
        return verification!;
    }

    private static async Task LibraryRebuildsThumbnailOnDispatcherAsync()
    {
        TestAssert.True(
            SynchronizationContext.Current is
                System.Windows.Threading.DispatcherSynchronizationContext,
            "Thumbnail notifications must be observed on a pumping UI dispatcher.");
        int ownerThreadId = Environment.CurrentManagedThreadId;
        using var directory = new TemporaryDirectory();
        string media = Path.Combine(directory.Root, "returned.mp4");
        string thumbnail = Path.Combine(
            directory.Root,
            "returned.thumbnail.jpg");
        var asset = new LibraryMediaAsset(
            "returned-asset",
            "returned-project",
            GenerationMode.IndividualClips,
            1,
            media,
            thumbnail,
            TimeSpan.FromSeconds(24),
            1080,
            1920,
            "Returned title",
            "Returned description",
            ["tag"],
            new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero));
        var store = new InMemoryLibraryCatalogStore();
        store.Replace([asset]);
        using var catalog = new GenerationLibraryCatalog(
            new GenerationOutputSession(),
            store);
        var monitor = new RecordingLibraryFileAvailabilityMonitor();
        var recovery = new ControlledLibraryThumbnailRecovery();
        using var library = new LibraryViewModel(
            catalog,
            monitor,
            recovery);
        LibraryItem selected = library.SelectedItem!;
        int missingRevision = selected.ThumbnailRevision;
        var thumbnailRestored = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        selected.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(LibraryItem.HasThumbnail) &&
                selected.HasThumbnail)
            {
                thumbnailRestored.TrySetResult(
                    Environment.CurrentManagedThreadId);
            }
        };

        File.WriteAllBytes(media, [0, 1, 2, 3]);
        monitor.ReportChanged(media);
        await recovery.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        TestAssert.Equal(
            "Ready",
            selected.Status,
            "A returned video must become usable while thumbnail repair continues in the background.");
        TestAssert.False(
            selected.HasThumbnail,
            "Library should keep its placeholder until the repaired image is complete.");
        recovery.Release();
        int notificationThreadId = await thumbnailRestored.Task.WaitAsync(
            TimeSpan.FromSeconds(2));

        TestAssert.Equal(
            ownerThreadId,
            notificationThreadId,
            "Background thumbnail repair must notify the owning UI thread.");
        TestAssert.Equal(
            1,
            recovery.CallCount,
            "One video-return event must queue one deduplicated thumbnail repair.");
        TestAssert.True(
            File.Exists(thumbnail),
            "Thumbnail repair must recreate the retained sidecar path.");
        TestAssert.True(
            selected.ThumbnailRevision > missingRevision,
            "Completing repair must force the same-path image control to reload.");
    }

    private static async Task LibraryThumbnailRecoveryUsesAtomicFfmpegExtraction()
    {
        using var directory = new TemporaryDirectory();
        string media = Path.Combine(directory.Root, "available.mp4");
        string thumbnail = Path.Combine(
            directory.Root,
            "available.thumbnail.jpg");
        File.WriteAllBytes(media, [0, 1, 2, 3]);
        LibraryMediaAsset asset = CreateAsset(media).Relink(
            media,
            thumbnail);
        var runner = new ThumbnailWritingProcessRunner();
        var recovery = new FfmpegLibraryThumbnailRecoveryService(
            runner,
            new FixedLibraryFfmpegToolLocator(
                Path.Combine(directory.Root, "ffmpeg.exe")));

        bool recovered = await recovery.TryRecoverAsync(
            asset,
            CancellationToken.None);

        TestAssert.True(
            recovered && File.Exists(thumbnail),
            "A successful FFmpeg extraction must publish the retained thumbnail sidecar.");
        TestAssert.True(
            runner.Requests.Single().Arguments.Contains("-frames:v"),
            "Thumbnail repair must reuse the bounded one-frame FFmpeg command.");
        TestAssert.False(
            Directory.EnumerateFiles(
                    directory.Root,
                    "*.recovery-*.jpg")
                .Any(),
            "The atomic repair must not leave a staging image beside the video.");
    }

    private static Task LibraryOrganizationUsesCatalogIdentity()
    {
        using var directory = new TemporaryDirectory();
        string firstFolder = Path.Combine(directory.Root, "Session-A");
        string secondFolder = Path.Combine(directory.Root, "Session-B");
        Directory.CreateDirectory(firstFolder);
        Directory.CreateDirectory(secondFolder);
        string firstPath = Path.Combine(firstFolder, "first.mp4");
        string secondPath = Path.Combine(firstFolder, "second.mp4");
        string thirdPath = Path.Combine(secondFolder, "third.mp4");
        File.WriteAllBytes(firstPath, [1]);
        File.WriteAllBytes(secondPath, [2]);
        File.WriteAllBytes(thirdPath, [3]);
        DateTimeOffset today = new(
            DateTime.Today.AddHours(10),
            TimeZoneInfo.Local.GetUtcOffset(DateTime.Today.AddHours(10)));
        DateTimeOffset earlier = today.AddDays(-2);
        LibraryMediaAsset[] assets =
        [
            CreateOrganizedAsset("asset-a", "project-a", firstPath, today),
            CreateOrganizedAsset("asset-b", "project-a", secondPath, today),
            CreateOrganizedAsset("asset-c", "project-b", thirdPath, earlier),
        ];
        var store = new InMemoryLibraryCatalogStore();
        store.Replace(assets);
        using var catalog = new GenerationLibraryCatalog(
            new GenerationOutputSession(),
            store);
        using var library = new LibraryViewModel(catalog);

        TestAssert.Equal(
            3,
            library.OrganizationOptions.Count,
            "Library must expose focused date, folder, and project organization choices.");
        TestAssert.Equal(
            2,
            library.Items.Select(static item => item.OrganizationGroup).Distinct().Count(),
            "Date organization must form groups from retained catalog timestamps.");

        library.OrganizationMode = LibraryOrganizationMode.Folder;
        TestAssert.Equal(
            2,
            library.Items.Select(static item => item.OrganizationGroup).Distinct().Count(),
            "Folder organization must use the exact output parent directories.");
        TestAssert.Equal(
            2,
            library.Items.Count(item => item.OrganizationGroup == "Session-A"),
            "Videos from the same output folder must stay in one visible group.");

        library.OrganizationMode = LibraryOrganizationMode.Project;
        string[] projectGroups = library.Items
            .Select(static item => item.OrganizationGroup)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        TestAssert.Equal(
            2,
            projectGroups.Length,
            "Project organization must derive collections from durable project identity.");
        TestAssert.True(
            projectGroups.All(static label => label.StartsWith("Finished · ", StringComparison.Ordinal)),
            "Project identities must be presented as friendly finished-video groups, not opaque IDs.");
        return Task.CompletedTask;
    }

    private static LibraryMediaAsset CreateOrganizedAsset(
        string id,
        string projectId,
        string outputPath,
        DateTimeOffset addedAtLocal) =>
        new(
            id,
            projectId,
            GenerationMode.IndividualClips,
            1,
            outputPath,
            null,
            TimeSpan.FromSeconds(24),
            1080,
            1920,
            $"Title {id}",
            string.Empty,
            [],
            addedAtLocal.ToUniversalTime());

    private static LibraryMediaAsset CreateAsset(string outputPath) =>
        new(
            "asset-1",
            "project-1",
            GenerationMode.IndividualClips,
            1,
            outputPath,
            null,
            TimeSpan.FromSeconds(24),
            1080,
            1920,
            "Preserved title",
            "Preserved description",
            ["tag-one", "tag-two"],
            new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero),
            libraryLabel: "Literal Library label");

    private sealed class FixedOutputFolderPicker(string selected) :
        IOutputFolderPicker
    {
        public string? PickOutputFolder(string currentRootDirectory) => selected;
    }

    private sealed class FixedLibraryMediaFilePicker(string selected) :
        ILibraryMediaFilePicker
    {
        public string? PickReplacementMedia(LibraryMediaAsset asset) => selected;
    }

    private sealed class RecordingFolderLauncher : ILocalFolderLauncher
    {
        public string? LastOpened { get; private set; }
        public void OpenFolder(string fullPath) => LastOpened = fullPath;
    }

    private sealed class FixedLibraryRemovalConfirmation(bool confirm) :
        ILibraryRemovalConfirmation
    {
        public int CallCount { get; private set; }
        public int LastAssetCount { get; private set; }
        public bool ConfirmRemoveFromLibrary(IReadOnlyList<LibraryMediaAsset> assets)
        {
            CallCount++;
            LastAssetCount = assets.Count;
            return confirm;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "ReplayFoundryOutputLocationTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class RecordingLibraryFileAvailabilityMonitor :
        ILibraryFileAvailabilityMonitor
    {
        public IReadOnlyList<LibraryMediaAsset> WatchedAssets { get; private set; } = [];

        public event EventHandler<LibraryFilesChangedEventArgs>? FilesChanged;

        public void Watch(IReadOnlyList<LibraryMediaAsset> assets) =>
            WatchedAssets = assets.ToArray();

        public void ReportChanged(params string[] fullPaths) =>
            FilesChanged?.Invoke(
                this,
                new LibraryFilesChangedEventArgs(fullPaths));

        public void Dispose()
        {
        }
    }

    private sealed class ControlledLibraryThumbnailRecovery :
        ILibraryThumbnailRecoveryService
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        public void Release() => _release.TrySetResult();

        public async Task<bool> TryRecoverAsync(
            LibraryMediaAsset asset,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            File.WriteAllBytes(
                asset.ThumbnailFullPath!,
                [4, 5, 6, 7]);
            return true;
        }
    }

    private sealed class ThumbnailWritingProcessRunner : IProcessRunner
    {
        public List<ProcessRunRequest> Requests { get; } = [];

        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            File.WriteAllBytes(request.Arguments[^1], [4, 5, 6, 7]);
            return Task.FromResult(new ProcessRunResult(
                0,
                string.Empty,
                string.Empty,
                TimeSpan.FromMilliseconds(20)));
        }
    }

    private sealed class FixedLibraryFfmpegToolLocator(
        string executablePath) : IFfmpegToolLocator
    {
        public string LocateFfmpeg() => executablePath;

        public string LocateFfprobe() => executablePath;
    }
}
