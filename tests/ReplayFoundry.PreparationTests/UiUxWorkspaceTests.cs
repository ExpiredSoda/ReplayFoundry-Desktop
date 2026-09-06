using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using ReplayFoundry.Desktop;
using ReplayFoundry.Desktop.Features.Generate;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Features.Generate.Progress;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Library.Sections;
using ReplayFoundry.Desktop.Features.Publish;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Features.Publish.Sections;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Browser;
using ReplayFoundry.Desktop.Features.Studio.Inspector;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Presentation.Controls;
using ReplayFoundry.Desktop.Presentation.Converters;
using ReplayFoundry.Desktop.Presentation.Feedback;
using ReplayFoundry.Desktop.Presentation.Accessibility;
using ReplayFoundry.Desktop.Presentation.Workspaces;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Shell;
using ReplayFoundry.Desktop.Shell.Guidance;
using ReplayFoundry.Desktop.Shell.Navigation;
using ReplayFoundry.Desktop.Shell.Windowing;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    private static Task WorkspaceStatesAreExclusive()
    {
        foreach (WorkspaceSurfaceState state in Enum.GetValues<WorkspaceSurfaceState>())
        {
            var studio = new StudioViewModel(state);
            var library = new LibraryViewModel(state);
            var publish = new PublishViewModel(state);
            var settings = new SettingsViewModel(state);
            TestAssert.Equal(1, CountStates(studio.IsEmpty, studio.IsContentReady, studio.IsLoading, studio.IsError, studio.IsUnavailable), "Studio should expose one state.");
            TestAssert.Equal(1, CountStates(library.IsEmpty, library.IsContentReady, library.IsLoading, library.IsError, library.IsUnavailable), "Library should expose one state.");
            TestAssert.Equal(1, CountStates(publish.IsEmpty, publish.IsContentReady, publish.IsLoading, publish.IsError, publish.IsUnavailable), "Publish should expose one state.");
            TestAssert.Equal(1, CountStates(settings.IsEmpty, settings.IsContentReady, settings.IsLoading, settings.IsError, settings.IsUnavailable), "Settings should expose one state.");
        }
        return Task.CompletedTask;
    }

    private static Task LibraryDerivedStateWorks()
    {
        var library = new LibraryViewModel();
        library.SelectedCategory = LibraryCategory.GeneratedClips;
        library.SearchQuery = "clutch";
        library.StatusFilter = "Ready";
        TestAssert.True(library.HasActiveFilters, "Search and filter state should be tracked.");
        TestAssert.True(library.EmptyTitle.Contains("filters", StringComparison.OrdinalIgnoreCase), "Empty title should explain filtered state.");
        TestAssert.True(library.EmptyDescription.Contains("Clear", StringComparison.Ordinal), "Empty description should offer a clear-filter explanation.");
        return Task.CompletedTask;
    }

    private static Task LibraryClearFiltersUpdatesCanExecute()
    {
        var library = new LibraryViewModel { SearchQuery = "clip" };
        TestAssert.True(library.ClearFiltersCommand.CanExecute(null), "Clear Filters should enable when filters are active.");
        library.ClearFiltersCommand.Execute(null);
        TestAssert.False(library.ClearFiltersCommand.CanExecute(null), "Clear Filters should disable after clearing.");
        return Task.CompletedTask;
    }

    private static Task LibraryViewModeWorks()
    {
        var library = new LibraryViewModel { ViewMode = LibraryViewMode.List };
        TestAssert.True(library.IsListView, "List mode should be selectable.");
        library.SetGridViewCommand.Execute(null);
        TestAssert.True(library.IsGridView, "Grid mode should be selectable.");
        return Task.CompletedTask;
    }

    private static Task LibraryRuntimeCollectionRemainsEmpty()
    {
        TestAssert.Equal(0, new LibraryViewModel().Items.Count, "Runtime library items must remain empty.");
        return Task.CompletedTask;
    }

    private static Task LibraryOrganizationSelectorRendersLabel()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            using var viewModel = new LibraryViewModel();
            var view = new LibraryFilterBarView
            {
                Width = 760,
                Height = 180,
                DataContext = viewModel,
            };
            view.Measure(new Size(760, 180));
            view.Arrange(new Rect(0, 0, 760, 180));
            view.UpdateLayout();

            Expander filters = EnumerateVisualDescendants<Expander>(view)
                .Single(expander =>
                    System.Windows.Automation.AutomationProperties.GetName(expander)
                        .Equals("Library filters and organization", StringComparison.Ordinal));
            TestAssert.False(filters.IsExpanded,
                "Advanced Library filters should initially leave room for the finished clips.");
            filters.IsExpanded = true;
            view.UpdateLayout();

            ComboBox selector = EnumerateVisualDescendants<ComboBox>(view)
                .Single(combo =>
                    System.Windows.Automation.AutomationProperties.GetName(combo)
                        .Equals("Organize Library by", StringComparison.Ordinal));
            DataTemplate template = selector.ItemTemplate ??
                throw new InvalidOperationException(
                    "The organization selector needs one display template for both its popup and selected value.");
            TextBlock display = (TextBlock)template.LoadContent();
            Binding? labelBinding = BindingOperations.GetBinding(
                display,
                TextBlock.TextProperty);
            TestAssert.Equal(
                "Label",
                labelBinding?.Path.Path,
                "The organization selector must bind its popup and selected display to Label instead of the record's diagnostic ToString value.");

            Button clearFilters = EnumerateVisualDescendants<Button>(view)
                .Single(button =>
                    System.Windows.Automation.AutomationProperties
                        .GetName(button)
                        .Equals(
                            "Clear library filters",
                            StringComparison.Ordinal));
            TestAssert.Equal(
                Visibility.Collapsed,
                clearFilters.Visibility,
                "Library must not reserve a large clear-filter action when no filter is active.");
            viewModel.SearchQuery = "commentary";
            view.UpdateLayout();
            TestAssert.Equal(
                Visibility.Visible,
                clearFilters.Visibility,
                "The clear-filter action should appear as soon as filtering is active.");

            ComboBox sort = EnumerateVisualDescendants<ComboBox>(view)
                .Single(combo =>
                    System.Windows.Automation.AutomationProperties
                        .GetName(combo)
                        .Equals("Library sort order", StringComparison.Ordinal));
            TestAssert.True(
                sort.Width >= 150,
                "The default Recently modified sort label must remain readable without ellipsis.");
        });
        return Task.CompletedTask;
    }

    private static Task LibraryGridUsesSupportedGroupedVirtualization()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            using var viewModel = new LibraryViewModel();
            var view = new LibraryContentView
            {
                Width = 720,
                Height = 520,
                DataContext = viewModel,
            };
            view.Measure(new Size(720, 520));
            view.Arrange(new Rect(0, 0, 720, 520));
            view.UpdateLayout();

            ListBox grid = EnumerateVisualDescendants<ListBox>(view)
                .Single(list =>
                    System.Windows.Automation.AutomationProperties.GetName(list)
                        .Equals("Library grid items", StringComparison.Ordinal));
            object? panel = grid.ItemsPanel.LoadContent();
            TestAssert.True(
                panel is LibraryVirtualizingWrapPanel,
                "The flat grouped projection must use one generator-safe virtualizing wrap panel.");
            ScrollViewer scrollRegion =
                EnumerateVisualDescendants<ScrollViewer>(view)
                    .Single(scroll => AutomationProperties.GetName(scroll)
                        .Equals(
                            "Library grid scroll region",
                            StringComparison.Ordinal));
            TestAssert.Equal(
                ScrollBarVisibility.Auto,
                scrollRegion.VerticalScrollBarVisibility,
                "The Library grid must expose lower wrapped rows through its own vertical scroll boundary.");
            TestAssert.True(
                !scrollRegion.CanContentScroll &&
                VirtualizingPanel.GetIsVirtualizing(grid),
                "The flat projection must keep one pixel viewport and one virtualizing item generator.");

            LibraryItem[] groupedItems = Enumerable.Range(0, 405)
                .Select(index =>
                {
                    var item = new LibraryItem(
                        $"Clip {index + 1}",
                        "Clip",
                        "0:30",
                        "Today",
                        "Ready",
                        "9:16",
                        "Grouped virtualization probe",
                        "Icon.Media");
                    item.OrganizationGroup = index switch
                    {
                        < 400 => "Today",
                        < 403 => "This week",
                        _ => "Earlier",
                    };
                    return item;
                })
                .ToArray();
            var groupedProbe = new GroupedLibraryGridProbe(groupedItems);
            var groupedView = new LibraryContentView
            {
                Width = 720,
                Height = 520,
                DataContext = groupedProbe,
            };
            groupedView.Measure(new Size(720, 520));
            groupedView.Arrange(new Rect(0, 0, 720, 520));
            groupedView.UpdateLayout();
            ListBox groupedGrid = EnumerateVisualDescendants<ListBox>(
                    groupedView)
                .Single(list => AutomationProperties.GetName(list).Equals(
                    "Library grid items",
                    StringComparison.Ordinal));
            LibraryGridEntry[] headers = groupedProbe.GridEntries
                .Where(static entry => entry.IsHeader)
                .ToArray();
            TestAssert.Equal(3, headers.Length,
                "The flat projection must retain one explicit row for each visual group.");
            TestAssert.True(
                headers.Select(static entry => entry.GroupName).SequenceEqual(
                    ["Today", "This week", "Earlier"],
                    StringComparer.Ordinal),
                "Group headers must retain their first-seen organization order.");
            int initialContainers =
                EnumerateVisualDescendants<ListBoxItem>(groupedGrid).Count();
            TestAssert.True(initialContainers is > 0 and < 40,
                $"A large single Today group must realize only viewport-adjacent cards; realized {initialContainers}.");
            TestAssert.True(
                EnumerateVisualDescendants<TextBlock>(groupedGrid)
                    .Any(text => text.Text.Equals("Today", StringComparison.Ordinal)),
                "The first explicit group header must remain visible above its cards.");
            ScrollViewer groupedScroll =
                EnumerateVisualDescendants<ScrollViewer>(groupedView)
                    .Single(scroll => AutomationProperties.GetName(scroll)
                        .Equals(
                            "Library grid scroll region",
                            StringComparison.Ordinal));
            TestAssert.False(
                EnumerateVisualDescendants<ScrollViewer>(groupedGrid).Any(),
                "The Library grid ListBox must not retain an inactive inner scroller that traps mouse-wheel input.");
            double initialOffset = groupedScroll.VerticalOffset;
            ListBoxItem visibleCard =
                EnumerateVisualDescendants<ListBoxItem>(groupedGrid)
                    .First();
            var wheel = new MouseWheelEventArgs(
                Mouse.PrimaryDevice,
                Environment.TickCount,
                -120)
            {
                RoutedEvent = Mouse.MouseWheelEvent,
                Source = visibleCard,
            };
            visibleCard.RaiseEvent(wheel);
            Dispatcher.CurrentDispatcher.Invoke(
                static () => { },
                DispatcherPriority.ContextIdle);
            groupedView.UpdateLayout();
            TestAssert.True(
                groupedScroll.VerticalOffset > initialOffset,
                "Mouse-wheel input over a Library card must reach the outer grid viewport.");
            initialOffset = groupedScroll.VerticalOffset;
            LibraryGridEntry lastEntry = groupedProbe.GridEntries[^1];
            groupedGrid.SelectedValue = groupedItems[^1];
            groupedGrid.ScrollIntoView(lastEntry);
            groupedView.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(
                static () => { },
                DispatcherPriority.ContextIdle);
            groupedView.UpdateLayout();
            TestAssert.True(
                groupedScroll.VerticalOffset > initialOffset,
                "ScrollIntoView must move the marked outer viewport to an offscreen grouped card.");
            TestAssert.True(
                groupedGrid.ItemContainerGenerator.ContainerFromItem(lastEntry)
                    is ListBoxItem,
                "ScrollIntoView must realize the requested offscreen grouped card container.");
            TestAssert.True(ReferenceEquals(
                    groupedGrid.SelectedValue,
                    groupedItems[^1]),
                "Selection must remain exact after scrolling across multiple grouped rows.");
            TestAssert.True(ReferenceEquals(
                    groupedProbe.SelectedItem,
                    groupedItems[^1]),
                "The selected flat card must project its original Library item back to the view model.");
            int deepContainers =
                EnumerateVisualDescendants<ListBoxItem>(groupedGrid).Count();
            TestAssert.True(deepContainers is > 0 and < 40,
                $"Deep scrolling must recycle a bounded card set instead of materializing the large group; realized {deepContainers}.");
        });
        return Task.CompletedTask;
    }

    private static Task LibraryDetailsOwnTheirScrollBoundary()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var view = new LibraryDetailsView();
            TestAssert.False(
                view.DetailsScrollViewer.CanContentScroll,
                "Library details should use smooth pixel scrolling inside their fixed shell column.");
            TestAssert.Equal(
                ScrollBarVisibility.Auto,
                view.DetailsScrollViewer.VerticalScrollBarVisibility,
                "Long preview metadata must remain reachable without scrolling the entire Library workspace.");
            TestAssert.True(
                view.DetailsScrollViewer.Content is StackPanel,
                "The preview, playback controls, metadata, and file actions must share the internal scroll boundary.");
        });
        return Task.CompletedTask;
    }

    private static Task GenerateFailureDetailsDiscloseCleanly()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var view = new GenerationProgressView
            {
                Width = 820,
                Height = 620,
                DataContext = new
                {
                    Title = "Generation stopped",
                    Detail = "Replay Foundry could not finish generating.",
                    IsFaulted = true,
                    ErrorMessage = "What happened",
                    TechnicalDetails = string.Join(
                        Environment.NewLine,
                        Enumerable.Repeat(
                            "A long diagnostic line wraps without a horizontal scrollbar.",
                            12)),
                    ProgressPercent = 0,
                    IsIndeterminate = false,
                    IsCompleted = false,
                    IsCancelled = false,
                },
            };
            view.Measure(new Size(820, 620));
            view.Arrange(new Rect(0, 0, 820, 620));
            view.UpdateLayout();

            Expander disclosure = EnumerateVisualDescendants<Expander>(view)
                .Single(expander => Equals(expander.Header, "More details"));
            TextBox details = disclosure.Content as TextBox ??
                throw new InvalidOperationException(
                    "More details must remain selectable text.");
            TestAssert.False(
                disclosure.IsExpanded,
                "More details should start collapsed.");
            disclosure.IsExpanded = true;
            view.UpdateLayout();
            TestAssert.True(
                disclosure.IsExpanded && disclosure.IsKeyboardFocusWithin is false,
                "The disclosure must expand without transferring focus into the diagnostic text.");
            TestAssert.Equal(
                TextWrapping.Wrap,
                details.TextWrapping,
                "Long diagnostics must wrap inside the issue card.");
            TestAssert.Equal(
                ScrollBarVisibility.Disabled,
                details.HorizontalScrollBarVisibility,
                "The details view must not render a white scrollbar intersection square.");
            TestAssert.Equal(
                ScrollBarVisibility.Auto,
                details.VerticalScrollBarVisibility,
                "Long diagnostics must remain vertically reachable.");
        });
        return Task.CompletedTask;
    }

    private static Task WorkspaceMediaTimeFormattingIsConsistent()
    {
        TimeSpan duration = TimeSpan.FromHours(49) + TimeSpan.FromMilliseconds(900);
        var asset = new LibraryMediaAsset(
            "library-long-duration",
            "library-time-project",
            GenerationMode.IndividualClips,
            rank: 1,
            Path.Combine(Path.GetTempPath(), "library-long-duration.mp4"),
            thumbnailFullPath: null,
            duration,
            outputWidth: 1920,
            outputHeight: 1080,
            "Long clip",
            "Duration formatting fixture",
            ["gameplay"],
            DateTimeOffset.UnixEpoch);
        using var library = new LibraryViewModel(new FixedLibraryCatalog(asset));
        library.SelectedCategory = LibraryCategory.GeneratedClips;

        TestAssert.Equal(
            "49:00:00",
            library.Items.Single().Duration,
            "Library should use the shared whole-second formatter without wrapping after 24 hours.");
        TestAssert.Equal(
            "49:00:00",
            PublishPresentationRules.FormatDuration(duration),
            "Publish should display the exact same whole-second media time as Library, Generate, and Studio.");
        return Task.CompletedTask;
    }

    private static Task PublishSelectionWorks()
    {
        var publish = new PublishViewModel();
        TestAssert.Equal(PublishDestination.YouTube, publish.SelectedDestination, "YouTube should be the only publish destination.");
        TestAssert.Equal("YouTube", publish.SelectedDestinationLabel, "Destination label should stay YouTube-specific.");
        TestAssert.Equal(1, publish.Destinations.Count, "Publish should expose one explicit destination instead of inactive navigation choices.");
        return Task.CompletedTask;
    }

    private static Task PublishMetadataValidationWorks()
    {
        var publish = new PublishViewModel { Title = "Draft title", Description = "Description", Tags = "tag" };
        TestAssert.Equal("11/100", publish.TitleCharacterCount, "Title count should update.");
        TestAssert.Equal("3/500", publish.TagsCharacterCount, "Tag count should update.");
        TestAssert.True(publish.IsMetadataWithinLimits, "Short metadata should pass presentation validation.");
        publish.Title = new string('x', 101);
        TestAssert.False(publish.IsMetadataWithinLimits, "Over-limit metadata should fail presentation validation.");
        return Task.CompletedTask;
    }

    private static Task PublishCalendarDefaultsWork()
    {
        var publish = new PublishViewModel();
        TestAssert.Equal(42, publish.CalendarDays.Count, "Month view should expose a complete six-week calendar grid.");
        TestAssert.True(publish.SelectedCalendarDay is not null, "A planning day should be selected by default.");
        TestAssert.False(publish.CreatePlanCommand.CanExecute(null), "Scheduling must remain disabled until a finished Library video is selected.");
        return Task.CompletedTask;
    }

    private static Task PublishCalendarFilteringWorks()
    {
        var publish = new PublishViewModel();
        publish.SelectedCalendarPlatform = PublishCalendarPlatform.YouTube;
        TestAssert.True(publish.CalendarDays.SelectMany(day => day.Slots).All(slot => slot.Platform == PublishCalendarPlatform.YouTube), "Filtered calendar slots should remain YouTube-only.");
        publish.SelectedCalendarMode = PublishCalendarMode.Week;
        TestAssert.Equal(7, publish.CalendarDays.Count, "Week view should expose one complete week.");
        TestAssert.True(publish.CalendarDays.Any(day => day.IsToday), "Switching to week view should keep the selected current day in view.");
        string currentRange = publish.CalendarRangeTitle;
        publish.NextCalendarCommand.Execute(null);
        TestAssert.True(!string.Equals(currentRange, publish.CalendarRangeTitle, StringComparison.Ordinal), "Calendar navigation should advance the visible range in memory.");
        return Task.CompletedTask;
    }

    private static Task PublishOutputDraftUpdatesChecklist()
    {
        var publish = new PublishViewModel
        {
            Title = "Draft title",
            Description = "Grounded details",
            Tags = "gameplay, highlight",
            Timing = YouTubePublishTiming.Schedule,
            Visibility = YouTubeVideoVisibility.Public,
        };
        TestAssert.True(publish.ScheduleSummary.Contains("publish at", StringComparison.Ordinal), "Release summary should follow scheduled timing.");
        TestAssert.True(publish.Checklist.Any(item => item.Label == "Title and description" && item.State == "Ready"), "Checklist should reflect a valid title and description.");
        TestAssert.True(publish.Checklist.Any(item => item.Label == "Release"), "Checklist should expose the YouTube release decision.");
        return Task.CompletedTask;
    }

    private static Task PublishCommandsStayDisabled()
    {
        var publish = new PublishViewModel();
        TestAssert.False(publish.PublishCommand.CanExecute(null), "Publish must remain disabled without a provider.");
        return Task.CompletedTask;
    }

    private static Task PublishRuntimeCollectionsRemainEmpty()
    {
        var publish = new PublishViewModel();
        TestAssert.Equal(0, publish.QueueItems.Count, "Runtime queue must remain empty.");
        TestAssert.Equal(0, publish.History.TotalCount, "Runtime history must remain empty.");
        return Task.CompletedTask;
    }

    private static Task SettingsReachesEverySection()
    {
        var settings = new SettingsViewModel();
        foreach (SettingsSection section in Enum.GetValues<SettingsSection>())
        {
            settings.SelectedSection = section;
            TestAssert.Equal(section, settings.SelectedSection, $"Section {section} should be reachable.");
        }
        return Task.CompletedTask;
    }

    private static Task SettingsShowsOnlyFunctionalSections()
    {
        var settings = new SettingsViewModel();
        TestAssert.Equal(5, settings.Sections.Count, "Settings should expose only controls with a connected behavior.");
        TestAssert.True(settings.Sections.Any(item => item.Key == SettingsSection.Storage), "Saved output location should remain available.");
        TestAssert.True(settings.Sections.Any(item => item.Key == SettingsSection.CreatorVoice), "Shared creator wording should remain available.");
        TestAssert.True(settings.Sections.Any(item => item.Key == SettingsSection.AiModels), "Runtime maintenance should remain available.");
        TestAssert.True(settings.Sections.Any(item => item.Key == SettingsSection.PrivacyDiagnostics), "Connection and privacy controls should remain available.");
        TestAssert.True(settings.Sections.Any(item => item.Key == SettingsSection.About), "About should remain available.");
        TestAssert.True(
            settings.VersionText.StartsWith(
                "Replay Foundry Desktop · ",
                StringComparison.Ordinal) &&
            !settings.VersionText.Contains('+', StringComparison.Ordinal),
            "About should show the product version without a developer build hash.");
        return Task.CompletedTask;
    }

    private static Task PublishLibraryOrganizationWorks()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"ReplayFoundry-publish-library-{Guid.NewGuid():N}");
        string recentFolder = Path.Combine(root, "Recent renders");
        string archiveFolder = Path.Combine(root, "Archive renders");
        Directory.CreateDirectory(recentFolder);
        Directory.CreateDirectory(archiveFolder);
        string recentPath = Path.Combine(recentFolder, "recent.mp4");
        string archivePath = Path.Combine(archiveFolder, "archive.mp4");
        File.WriteAllBytes(recentPath, [1]);
        File.WriteAllBytes(archivePath, [2]);
        try
        {
            var recent = new LibraryMediaAsset(
                "publish-recent", "project-recent",
                GenerationMode.IndividualClips, 1,
                recentPath, null, TimeSpan.FromSeconds(42), 1080, 1920,
                "Recent vertical clip", string.Empty, [],
                new DateTimeOffset(2026, 8, 6, 10, 0, 0, TimeSpan.Zero));
            var archive = new LibraryMediaAsset(
                "publish-archive", "project-archive",
                GenerationMode.IndividualClips, 1,
                archivePath, null, TimeSpan.FromSeconds(31), 1920, 1080,
                "Archived landscape clip", string.Empty, [],
                new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero));
            using var publish = new PublishViewModel(
                new PublishLibraryCatalog(recent, archive),
                youtube: null,
                new InMemoryYouTubePublishPreferencesStore(),
                thumbnailPicker: null,
                WorkspaceSurfaceState.ContentReady,
                static () => new DateTimeOffset(
                    2026, 8, 6, 12, 0, 0, TimeSpan.Zero),
                TimeZoneInfo.Utc,
                drafts: new InMemoryYouTubePublishDraftStore());

            TestAssert.Equal(2, publish.LibraryItems.Count, "All finished videos should start visible.");
            TestAssert.Equal(3, publish.LibraryFolderOptions.Count, "All folders plus two real output folders should be available.");
            publish.LibraryDateFilter = "Today";
            TestAssert.Equal("publish-recent", publish.LibraryItems.Single().Asset.Id, "Date filtering must use the asset's local Library date.");
            publish.LibraryDateFilter = "Any date";
            publish.SelectedLibraryFolder = archiveFolder;
            TestAssert.Equal("publish-archive", publish.LibraryItems.Single().Asset.Id, "Folder filtering must compare the real output directory.");
            publish.LibrarySearchQuery = "not present";
            TestAssert.False(publish.HasVisibleLibraryItems, "Search should participate in the same filtered projection.");
            publish.ClearLibraryFiltersCommand.Execute(null);
            TestAssert.Equal(2, publish.LibraryItems.Count, "Clear must restore the complete finished-video collection.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
        return Task.CompletedTask;
    }

    private static Task PublishReviewControlsAreThemedAndContinuous()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var viewModel = new PublishViewModel
            {
                Timing = YouTubePublishTiming.Schedule,
            };
            var release = new PublishOutputSettingsView
            {
                DataContext = viewModel,
            };
            release.Measure(new Size(640, 900));
            release.Arrange(new Rect(0, 0, 640, 900));
            release.UpdateLayout();
            DatePicker? datePicker = FindVisualDescendant<DatePicker>(release);
            CheckBox? checkBox = FindVisualDescendant<CheckBox>(release);
            TimePickerField[] timePickers =
                EnumerateVisualDescendants<TimePickerField>(release)
                    .ToArray();
            TestAssert.True(datePicker?.CalendarStyle is not null, "The release date must open the shared themed calendar.");
            TestAssert.True(datePicker?.Template is not null, "The release date must use the themed DatePicker template.");
            TestAssert.True(checkBox?.Template is not null, "Publish checkboxes must use the shared themed selection template.");
            TestAssert.Equal(
                2,
                timePickers.Length,
                "Scheduled and preferred release times must share the same interactive picker instead of raw text fields.");
            TestAssert.True(
                timePickers.Any(static picker =>
                    picker.AutomationName == "Scheduled release time"),
                "The scheduled time picker must expose its purpose to assistive technology.");
            TimePickerField scheduledPicker = timePickers.Single(
                static picker =>
                    picker.AutomationName == "Scheduled release time");
            var hourList = (ListBox)scheduledPicker.FindName("HourList");
            var minuteList = (ListBox)scheduledPicker.FindName("MinuteList");
            var periodList = (ListBox)scheduledPicker.FindName("PeriodList");
            var doneButton = (Button)scheduledPicker.FindName("DoneButton");
            TestAssert.Equal(
                40d,
                doneButton.Height,
                "The compact time flyout action must preserve a full-sized interaction target.");
            hourList.SelectedItem = 7;
            minuteList.SelectedItem = "15";
            periodList.SelectedItem = "PM";
            TestAssert.Equal(
                "7:15 PM",
                viewModel.ScheduledTimeText,
                "Time-part selection must update the exact schedule value without free-form parsing.");

            var window = new PublishPreparationWindow(viewModel);
            TestAssert.True(window.PreviewPlayer.ScrubbingEnabled, "The review MediaElement must render frames while the user scrubs.");
            TestAssert.False(window.PreviewPosition.IsSnapToTickEnabled, "The review scrubber must remain continuous rather than segment-snapped.");
            TestAssert.False(
                EnumerateVisualDescendants<ComboBox>(window).Any(combo =>
                    AutomationProperties.GetName(combo).Equals(
                        "Finalized Studio video to review",
                        StringComparison.Ordinal)),
                "The focused Publish review must not offer a second video selector after the user already chose or dragged a Library video.");
            TestAssert.True(
                AutomationProperties.GetName(window.SelectedAssetTitle).Equals(
                    "Video being prepared for YouTube",
                    StringComparison.Ordinal),
                "The focused Publish review must identify the chosen video with non-interactive text.");
            TestAssert.False(
                EnumerateVisualDescendants<Button>(window).Any(button =>
                    Equals(button.Content, "Use for future clips")),
                "Publish must keep rerolling one-click and leave reusable writing-profile editing in Settings or Studio.");
            window.Close();
        });
        return Task.CompletedTask;
    }

    private static Task PublishLibraryDragStartsOnlyFromAssetCard()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var dragSurface = new Border();
            var titleRun = new System.Windows.Documents.Run("Finished clip");
            var title = new TextBlock(titleRun);
            var scheduleButton = new Button { Content = "Schedule" };
            var row = new Grid();
            row.Children.Add(dragSurface);
            row.Children.Add(title);
            row.Children.Add(scheduleButton);
            var item = new ListBoxItem { Content = row };
            var listBox = new ListBox();
            listBox.Items.Add(item);
            listBox.Measure(new Size(420, 180));
            listBox.Arrange(new Rect(0, 0, 420, 180));
            listBox.UpdateLayout();

            TestAssert.True(
                PublishLibraryBrowserView.IsAssetDragOrigin(dragSurface),
                "Pointer input inside a Library asset card should remain draggable and retain its real drag ghost.");
            TestAssert.True(
                PublishLibraryBrowserView.IsAssetDragOrigin(titleRun),
                "Text content origins must traverse the logical tree without throwing or losing the containing asset card.");
            TestAssert.False(
                PublishLibraryBrowserView.IsAssetDragOrigin(scheduleButton),
                "The Schedule button is interactive card chrome and must not start a drag.");
            TestAssert.False(
                PublishLibraryBrowserView.IsAssetDragOrigin(new ScrollBar()),
                "A scrollbar origin must not start a Library asset drag.");
            TestAssert.False(
                PublishLibraryBrowserView.IsAssetDragOrigin(listBox),
                "Blank ListBox chrome must not drag whichever asset happened to be selected.");
        });
        return Task.CompletedTask;
    }

    private static Task PublishPlannerDragFeedbackIsExplicit()
    {
        RunOnSta(() =>
        {
            var target = new Border();
            TestAssert.False(
                PublishCalendarView.GetIsDropTargetActive(target),
                "Calendar days must begin without a stale drag target state.");
            PublishCalendarView.SetIsDropTargetActive(target, true);
            TestAssert.True(
                PublishCalendarView.GetIsDropTargetActive(target),
                "A valid dragged Library asset must be able to activate clear day feedback.");
            PublishCalendarView.SetIsDropTargetActive(target, false);
            TestAssert.False(
                PublishCalendarView.GetIsDropTargetActive(target),
                "Leaving or dropping must clear the day feedback state.");
        });

        string publishView = File.ReadAllText(Path.Combine(
            RepositoryLayout.Root,
            "src",
            "ReplayFoundry.Desktop",
            "Features",
            "Publish",
            "PublishView.xaml"));
        int decorator = publishView.IndexOf(
            "<AdornerDecorator Grid.Row=\"1\"",
            StringComparison.Ordinal);
        int library = publishView.IndexOf(
            "<sections:PublishLibraryBrowserView",
            decorator < 0 ? 0 : decorator,
            StringComparison.Ordinal);
        int calendar = publishView.IndexOf(
            "<sections:PublishCalendarView",
            library < 0 ? 0 : library,
            StringComparison.Ordinal);
        int decoratorEnd = publishView.IndexOf(
            "</AdornerDecorator>",
            calendar < 0 ? 0 : calendar,
            StringComparison.Ordinal);
        TestAssert.True(
            decorator >= 0 &&
            library > decorator &&
            calendar > library &&
            decoratorEnd > calendar,
            "One planner-wide AdornerDecorator must contain both the drag source and calendar target so the ghost follows the cursor across their boundary.");

        string dragCode = File.ReadAllText(Path.Combine(
            RepositoryLayout.Root,
            "src",
            "ReplayFoundry.Desktop",
            "Features",
            "Publish",
            "Sections",
            "PublishLibraryBrowserView.xaml.cs"));
        TestAssert.True(
            dragCode.Contains(
                "GetCursorPos(out NativePoint cursor)",
                StringComparison.Ordinal) &&
            dragCode.Contains(
                "AdornedElement.PointFromScreen(",
                StringComparison.Ordinal),
            "The drag ghost must use the current OLE cursor and transform it into DPI-aware planner coordinates rather than remain at the drag origin or stop at the Library list edge.");
        return Task.CompletedTask;
    }

    private static Task PublishManualPreviewPrimesAssignedSource()
    {
        string code = File.ReadAllText(Path.Combine(
            RepositoryLayout.Root,
            "src",
            "ReplayFoundry.Desktop",
            "Features",
            "Publish",
            "PublishPreparationWindow.xaml.cs"));
        int assignment = code.IndexOf(
            "PreviewPlayer.Source = new Uri(path, UriKind.Absolute);",
            StringComparison.Ordinal);
        int pause = code.IndexOf(
            "PreviewPlayer.Pause();",
            assignment < 0 ? 0 : assignment,
            StringComparison.Ordinal);
        TestAssert.True(
            assignment >= 0 && pause > assignment,
            "A Manual MediaElement must receive the finalized file first and then be primed with Pause so MediaOpened, duration, and the first frame become available without audible autoplay.");
        return Task.CompletedTask;
    }

    private static Task PublishSchedulingTemplatesRetainRequiredShape()
    {
        string formStylesPath = Path.Combine(
            RepositoryLayout.Root,
            "src",
            "ReplayFoundry.Desktop",
            "Resources",
            "Controls",
            "FormStyles.xaml");
        var document = System.Xml.Linq.XDocument.Load(formStylesPath);
        System.Xml.Linq.XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        System.Xml.Linq.XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        System.Xml.Linq.XElement dayTitleTemplate = document.Root!
            .Elements(presentation + "DataTemplate")
            .Single(element =>
                (string?)element.Attribute(xaml + "Key") ==
                "{x:Static CalendarItem.DayTitleTemplateResourceKey}");
        TestAssert.True(
            dayTitleTemplate.Descendants(presentation + "TextBlock").Any(),
            "Calendar weekday titles must use WPF's culture-aware CalendarItem resource so they share the exact month-grid columns.");

        System.Xml.Linq.XElement calendarItemStyle = document.Root!
            .Elements(presentation + "Style")
            .Single(element =>
                (string?)element.Attribute(xaml + "Key") ==
                "Control.ThemedCalendarItem");
        System.Xml.Linq.XElement monthView = calendarItemStyle
            .Descendants(presentation + "Grid")
            .Single(element =>
                (string?)element.Attribute(xaml + "Name") ==
                "PART_MonthView");
        System.Xml.Linq.XElement calendarRoot = calendarItemStyle
            .Descendants(presentation + "Grid")
            .Single(element =>
                (string?)element.Attribute(xaml + "Name") ==
                "PART_Root");
        TestAssert.Equal(
            2,
            calendarRoot.Element(presentation + "Grid.RowDefinitions")!
                .Elements(presentation + "RowDefinition")
                .Count(),
            "The CalendarItem must have one navigation row and one month-grid row; a second hand-built weekday row causes duplicated, drifting labels.");
        TestAssert.Equal(
            "1",
            (string?)monthView.Attribute("Grid.Row"),
            "The culture-aware weekday labels and date buttons must remain in the same month grid.");
        TestAssert.Equal(
            7,
            monthView.Element(presentation + "Grid.ColumnDefinitions")!
                .Elements(presentation + "ColumnDefinition")
                .Count(),
            "The CalendarItem month view must retain seven weekday columns.");
        TestAssert.Equal(
            7,
            monthView.Element(presentation + "Grid.RowDefinitions")!
                .Elements(presentation + "RowDefinition")
                .Count(),
            "The CalendarItem month view must retain enough bounded rows to prevent week overlays.");
        System.Xml.Linq.XElement dateTextBoxStyle = document.Root!
            .Elements(presentation + "Style")
            .Single(element =>
                (string?)element.Attribute(xaml + "Key") ==
                "Control.ThemedDatePickerTextBox");
        System.Xml.Linq.XElement watermark = dateTextBoxStyle
            .Descendants(presentation + "ContentControl")
            .Single(element =>
                (string?)element.Attribute(xaml + "Name") ==
                "PART_Watermark");
        TestAssert.Equal(
            "Collapsed",
            (string?)watermark.Attribute("Visibility"),
            "The DatePicker watermark must be collapsed unless its empty-text trigger activates it.");

        System.Xml.Linq.XElement[] textTriggers = dateTextBoxStyle
            .Descendants(presentation + "Trigger")
            .Where(element => (string?)element.Attribute("Property") == "Text")
            .ToArray();
        TestAssert.Equal(
            2,
            textTriggers.Length,
            "The DatePicker watermark must explicitly handle both empty and null Text values.");
        TestAssert.True(
            textTriggers.Any(element =>
                (string?)element.Attribute("Value") == string.Empty),
            "The DatePicker watermark must become visible for empty Text.");
        TestAssert.True(
            textTriggers.Any(element =>
                (string?)element.Attribute("Value") == "{x:Null}"),
            "The DatePicker watermark must become visible for null Text.");
        TestAssert.True(
            textTriggers.All(element => element
                .Elements(presentation + "Setter")
                .Any(setter =>
                    (string?)setter.Attribute("TargetName") == "PART_Watermark" &&
                    (string?)setter.Attribute("Property") == "Visibility" &&
                    (string?)setter.Attribute("Value") == "Visible")),
            "Both empty/null Text triggers must reveal only the DatePicker watermark.");

        System.Xml.Linq.XElement contentHost = dateTextBoxStyle
            .Descendants(presentation + "ScrollViewer")
            .Single(element =>
                (string?)element.Attribute(xaml + "Name") ==
                "PART_ContentHost");
        TestAssert.Equal(
            "{TemplateBinding Padding}",
            (string?)contentHost.Attribute("Margin"),
            "Date text and watermark must share the same inset rather than render at different horizontal positions.");
        return Task.CompletedTask;
    }

    private static Task PublishCheckboxGlyphIsCenteredAndDpiStable()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var checkBox = new CheckBox
            {
                Content = "Notify subscribers",
                IsChecked = true,
                Style = (Style)Application.Current.FindResource(
                    "Control.FormCheckBox"),
            };
            checkBox.Measure(new Size(320, 48));
            checkBox.Arrange(new Rect(0, 0, 320, 48));
            checkBox.ApplyTemplate();
            checkBox.UpdateLayout();
            var checkMark = checkBox.Template.FindName(
                "CheckMark",
                checkBox) as System.Windows.Shapes.Path;

            TestAssert.True(
                checkBox.SnapsToDevicePixels && checkBox.UseLayoutRounding,
                "The checkbox surface must round its layout and snap strokes across DPI scales.");
            TestAssert.True(
                checkMark is not null,
                "The themed checkbox must retain a recognizable check glyph.");
            TestAssert.Equal(
                HorizontalAlignment.Center,
                checkMark!.HorizontalAlignment,
                "The check glyph must remain horizontally centered in its box.");
            TestAssert.Equal(
                VerticalAlignment.Center,
                checkMark.VerticalAlignment,
                "The check glyph must remain vertically centered in its box.");
            TestAssert.Equal(
                Stretch.Uniform,
                checkMark.Stretch,
                "The check geometry must scale uniformly instead of clipping or distorting.");
            TestAssert.Equal(
                Visibility.Visible,
                checkMark.Visibility,
                "A checked control must visibly render the centered glyph.");
        });
        return Task.CompletedTask;
    }

    private static IEnumerable<T> EnumerateVisualDescendants<T>(
        DependencyObject root)
        where T : DependencyObject
    {
        for (int index = 0;
             index < VisualTreeHelper.GetChildrenCount(root);
             index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }
            foreach (T descendant in EnumerateVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class PublishLibraryCatalog(
        params LibraryMediaAsset[] assets) : ILibraryCatalog
    {
        public IReadOnlyList<LibraryMediaAsset> Assets { get; } = assets;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }

    private sealed class GroupedLibraryGridProbe(
        IReadOnlyList<LibraryItem> items)
    {
        public IReadOnlyList<LibraryItem> Items { get; } = items;
        public IReadOnlyList<LibraryGridEntry> GridEntries { get; } =
            LibraryGridProjection.Create(items);
        public bool IsGridView => true;
        public bool IsSelectionMode => false;
        public LibraryItem? SelectedItem { get; set; }
    }

    private static Task SettingsPreviewDoesNotInventCapabilities()
    {
        var settings = new SettingsViewModel();
        TestAssert.Equal(0, settings.AiCapabilities.Count, "A design or disconnected preview must not fabricate installed AI tools.");
        TestAssert.True(settings.AiStatus.Contains("unavailable", StringComparison.OrdinalIgnoreCase), "Unavailable runtime discovery should be explained plainly.");
        return Task.CompletedTask;
    }

    private static Task PublishDestinationGlyphsAreSemantic()
    {
        var publish = new PublishViewModel();
        TestAssert.True(publish.Destinations.All(item => item.Glyph.StartsWith("Icon.", StringComparison.Ordinal)), "Publish destinations should use semantic icon keys.");
        return Task.CompletedTask;
    }

}
