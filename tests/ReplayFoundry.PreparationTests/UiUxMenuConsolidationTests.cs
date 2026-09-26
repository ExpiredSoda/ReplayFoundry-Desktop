using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.ClipGoals;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Publish;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Shell.Navigation;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    private static IReadOnlyList<TestCase> MenuConsolidationTests() =>
    [
        new("One discovery search preserves exact phrases and semantic descriptions", CombinedDiscoverySearch),
        new("Incomplete search quotes block Next without replacing a valid search", IncompleteDiscoverySearch),
        new("One release selector preserves all supported visibility and scheduling choices", CombinedPublishRelease),
        new("Library type navigation composes with search, date and status", LibraryHasOneTypeFilter),
        new("Graphics keeps clips available and output format covers every custom shape", ConsolidatedStudioFormats),
        new("Caption gallery keeps saved typography and independent effect edits", ConsolidatedCaptionLooks),
        new("Writing defaults navigation preserves the current title draft", WritingDefaultsKeepDraft),
    ];

    private static Task CombinedDiscoverySearch()
    {
        TestAssert.True(DiscoverySearchText.TryParse("funny reaction \"no way\" \"what happened\"", out string meaning, out string words, out _), "Meaning and multiple phrases can share one search.");
        TestAssert.Equal("funny reaction", meaning, "Unquoted words retain semantic retrieval.");
        TestAssert.Equal("no way, what happened", words, "Both exact matches reach the existing transcript search.");
        foreach (var input in new[] { ("", "no way, amazing"), ("he says \"wait\" near C:\\game", "let's go"), ("a comeback", "") })
        {
            string formatted = DiscoverySearchText.Format(input.Item1, input.Item2);
            TestAssert.True(DiscoverySearchText.TryParse(formatted, out meaning, out words, out _), "Existing searches must reopen without reinterpretation.");
            TestAssert.Equal(input.Item1, meaning, "Literal quotes and paths must survive reopening.");
            TestAssert.Equal(input.Item2, words, "Exact phrases must survive reopening.");
        }
        TestAssert.False(DiscoverySearchText.TryParse(new string('x', 241), out _, out _, out _), "Combined input retains the semantic length bound.");
        TestAssert.False(DiscoverySearchText.TryParse("\"" + new string('x', 241) + "\"", out _, out _, out _), "Exact phrases retain their separate length bound.");
        return Task.CompletedTask;
    }

    private static Task IncompleteDiscoverySearch()
    {
        string path = Path.GetTempFileName();
        try
        {
            var source = new PreparedGenerationSource(new SelectedVideoSource(path, true), TestMediaFactory.Create(path, TimeSpan.FromMinutes(2)), TestMediaFactory.CreateSnapshot(path, 0));
            var preparation = new GenerationSourcePreparationResult(new GenerationSourcePreparationRequest([source.Source]), [source]);
            var draft = new GenerationSetupDraft(new GenerationSetupRequest(GenerationMode.IndividualClips, preparation));
            var model = new ClipGoalsStepViewModel(draft) { SearchText = "a comeback \"let's go\"" };
            TestAssert.True(model.IsValid, "A complete search is usable.");
            model.SearchText = "a comeback \"let's";
            TestAssert.False(model.IsValid, "Next must not submit a half-edited query.");
            TestAssert.Equal("let's go", draft.DiscoveryIntent.SpokenTerms, "Partial typing preserves the last valid settings.");
            model.SearchText += " win\"";
            TestAssert.True(model.IsValid, "Finishing the phrase clears validation.");
            TestAssert.Equal("let's win", draft.DiscoveryIntent.SpokenTerms, "The completed edit reaches generation.");
            model.SearchText = "";
            TestAssert.False(draft.DiscoveryIntent.UsesSemanticRetrieval, "Clearing the shared search clears semantic retrieval.");
            TestAssert.Equal(0, draft.DiscoveryIntent.Phrases.Count, "Clearing also clears exact-word filtering.");
        }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    }

    private static Task CombinedPublishRelease()
    {
        using var model = new PublishViewModel();
        model.ScheduledDate = DateTime.Today.AddDays(3);
        model.ScheduledTimeText = "14:35";
        var schedule = model.ReleaseOptions.Single(value => value.Timing == YouTubePublishTiming.Schedule);
        foreach (var option in model.ReleaseOptions)
        {
            model.SelectedReleaseOption = schedule;
            model.SelectedReleaseOption = option;
            TestAssert.Equal(option.Timing, model.Timing, "Release choice updates timing.");
            TestAssert.Equal(option.Visibility, model.Visibility, "Release choice updates visibility without a conflicting selector.");
            TestAssert.Equal(option, model.SelectedReleaseOption, "Selection reflects restored domain values.");
            TestAssert.Equal("14:35", model.ScheduledTimeText, "Switching release types must keep the user's scheduled time.");
            TestAssert.Equal(DateTime.Today.AddDays(3), model.ScheduledDate!.Value, "The date must survive switching too.");
        }
        model.Timing = YouTubePublishTiming.PublishNow;
        var notifications = new HashSet<string>();
        model.PropertyChanged += (_, change) => notifications.Add(change.PropertyName ?? "");
        model.Visibility = YouTubeVideoVisibility.Unlisted;
        TestAssert.Equal(YouTubeVideoVisibility.Unlisted, model.SelectedReleaseOption.Visibility, "Loading existing drafts updates the shared selector.");
        TestAssert.True(notifications.Contains(nameof(model.ScheduleSummary)) && notifications.Contains(nameof(model.PublishCommandText)),
            "A visibility-only change must refresh the release explanation and action label.");
        return Task.CompletedTask;
    }

    private static Task LibraryHasOneTypeFilter()
    {
        string path = Path.GetTempFileName();
        try
        {
            var assets = new[] { GenerationMode.IndividualClips, GenerationMode.Montage }.Select((mode, i) =>
                new LibraryMediaAsset("menu-" + i, "menu-project", mode, 1, path, null, TimeSpan.FromSeconds(24),
                    1080, 1920, "matching video " + i, "", [], DateTimeOffset.UtcNow)).ToArray();
            using var library = new LibraryViewModel(new MenuLibraryCatalog(assets));
            library.SearchQuery = "matching"; library.StatusFilter = "Ready"; library.DateFilter = "Today";
            TestAssert.Equal(2, library.Items.Count, "All videos can contain both output types.");
            library.SelectedCategory = LibraryCategory.GeneratedClips;
            TestAssert.Equal(GenerationMode.IndividualClips, library.Items.Single().Asset!.Mode, "Clips narrows the same search.");
            library.SelectedCategory = LibraryCategory.Montages;
            TestAssert.Equal(GenerationMode.Montage, library.Items.Single().Asset!.Mode, "Changing type cannot retain an opposing hidden mode filter.");
            library.ClearFiltersCommand.Execute(null);
            TestAssert.Equal(LibraryCategory.Montages, library.SelectedCategory, "Clear filters retains the current collection.");
        }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    }

    private static Task ConsolidatedStudioFormats()
    {
        RunOnSta(() =>
        {
            string path = Path.GetTempFileName();
            try
            {
                var session = new GenerationOutputSession(); session.Publish(TaggedMomentsProject(path));
                using var studio = new StudioViewModel(session, session, new RecordingStudioClipRenderer());
                string clip = studio.BrowserPreviewItems.Single().AssetId!;
                studio.Inspector.SelectedInspector = StudioInspectorSection.Graphics;
                TestAssert.Equal(clip, studio.BrowserPreviewItems.Single().AssetId!, "Opening Graphics must leave clips reachable.");
                var output = studio.Inspector.Output;
                foreach (var format in output.Formats)
                {
                    output.SelectedFormat = format;
                    TestAssert.Equal(format.Canvas, session.Current!.PrimaryAsset.RenderSettings.Canvas, "Every custom and platform shape must remain editable.");
                    TestAssert.Equal(format.Platform, session.Current.PrimaryAsset.RenderSettings.PlatformPreset, "Format must retain the corresponding publishing package.");
                }
            }
            finally { File.Delete(path); }
        });
        return Task.CompletedTask;
    }

    private static Task ConsolidatedCaptionLooks()
    {
        var editor = new StudioClipEditorViewModel(null);
        editor.NamedCaptionLooks.Clear();
        var saved = new StudioCaptionLook(GenerationCaptionStylePreset.HighContrast, 37, StudioCaptionWordLimitPreset.Punchy,
            65, 120, new StudioCaptionTypography("Arial", accentColor: "#12AAEF"));
        editor.NamedCaptionLooks.Add(new("Tournament", saved));
        editor.CaptionLooks.SelectedChoice = editor.CaptionLooks.Choices.Single(choice => choice.IsSaved);
        TestAssert.Equal(saved.CaptionTypography, editor.CaptionTypography, "Choosing a saved look keeps all its typography.");
        TestAssert.Equal(37d, editor.CaptionVerticalPositionPercent, "Saved placement must not be reduced to an animation preset.");
        editor.CaptionLooks.SelectedChoice = editor.CaptionLooks.Choices.Single(choice => !choice.IsSaved && choice.Style == GenerationCaptionStylePreset.WordFocus);
        TestAssert.Equal(GenerationCaptionStylePreset.WordFocus, editor.SelectedCaptionStyle.Value, "The same chooser supports changing only the effect.");
        TestAssert.Equal(saved.CaptionTypography, editor.CaptionTypography, "An effect edit must preserve the independent custom font and colors.");
        return Task.CompletedTask;
    }

    private static Task WritingDefaultsKeepDraft()
    {
        using var studio = new StudioViewModel();
        var settings = new SettingsViewModel();
        using var shell = CreateShell(studio, settings: settings);
        studio.Inspector.Editorial.Title = "An unfinished title";
        shell.OpenWritingDefaultsCommand.Execute(null);
        TestAssert.Equal(SettingsSection.CreatorVoice, settings.SelectedSection, "The shortcut must open the canonical writing settings directly.");
        shell.NavigateCommand.Execute(ShellDestination.Studio);
        TestAssert.Equal("An unfinished title", studio.Inspector.Editorial.Title, "Returning from defaults must not reload or discard the current title draft.");
        return Task.CompletedTask;
    }

    private sealed class MenuLibraryCatalog(IReadOnlyList<LibraryMediaAsset> assets) : ILibraryCatalog
    {
        public IReadOnlyList<LibraryMediaAsset> Assets => assets;
        public event EventHandler? Changed { add { } remove { } }
    }
}
