using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.PreparationTests;

internal static class OutputLocationAndLibraryTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Generation output location persists without rewriting existing paths", OutputLocationPersists),
        new("Generation output location rejects unsafe roots", OutputLocationRejectsUnsafeRoots),
        new("Library relink preserves asset identity and metadata", LibraryRelinkPreservesIdentity),
        new("Library removal preserves the rendered media file", LibraryRemovalPreservesMedia),
        new("Library bulk selection removes entries in one confirmed catalog update", LibraryBulkRemovalIsConfirmedOnce),
        new("Library persistence retains exact rendered candidate identities", LibraryPersistenceRetainsCandidateIdentity),
        new("Library playback scrubs the exact completed video continuously", LibraryPlaybackScrubsCompletedVideo),
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
            library.LibraryNotice.Contains("identity", StringComparison.Ordinal),
            "Relink must explain that catalog identity was retained.");
        library.OpenSelectedFolderCommand.Execute(null);
        TestAssert.Equal(
            Path.GetDirectoryName(moved),
            folderLauncher.LastOpened,
            "Library must open the selected asset folder, not an arbitrary root.");

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
            sourceCandidateIds: ["candidate-exact"]);

        var store = new JsonLibraryCatalogStore(catalogPath);
        store.Replace([asset]);
        LibraryMediaAsset restored =
            new JsonLibraryCatalogStore(catalogPath).Current.Single();

        TestAssert.True(
            restored.SourceCandidateIds.SequenceEqual(
                new[] { "candidate-exact" },
                StringComparer.Ordinal),
            "A restart must retain the exact candidate identity used to reconcile Studio and Library state.");
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
        return Task.CompletedTask;
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
            projectGroups.All(static label => label.StartsWith("Render · ", StringComparison.Ordinal)),
            "Project identities must be presented as friendly render collections, not opaque IDs.");
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
            new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero));

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
}
