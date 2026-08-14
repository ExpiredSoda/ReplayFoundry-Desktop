using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
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
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.MomentGuidance;
using ReplayFoundry.Desktop.Features.Generate.Guidance;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Library.Sections;
using ReplayFoundry.Desktop.Features.Publish;
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
    private static Task DropDownFieldsUseCompleteHitTarget()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var combo = new ComboBox
            {
                Width = 280,
                Height = 40,
                ItemsSource = new[] { "Balanced", "Thorough" },
                SelectedIndex = 0,
                Style = Application.Current?.TryFindResource(
                    "Control.ThemedComboBox") as Style,
            };
            combo.Measure(new Size(280, 40));
            combo.Arrange(new Rect(0, 0, 280, 40));
            combo.ApplyTemplate();
            combo.UpdateLayout();

            var toggle = combo.Template.FindName(
                    "DropDownToggle",
                    combo) as ToggleButton ??
                throw new InvalidOperationException(
                    "The shared ComboBox template is missing its drop-down toggle.");
            TestAssert.True(
                toggle.ActualWidth >= 270,
                "The shared drop-down toggle must cover the selected value and padding, not only the chevron.");
        });
        return Task.CompletedTask;
    }

    private static Task StudioCaptionScriptUsesSharedEditor()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var view = new StudioCaptionEditorView
            {
                Width = 340,
                Height = 720,
            };
            view.Measure(new Size(340, 720));
            view.Arrange(new Rect(0, 0, 340, 720));
            view.UpdateLayout();

            TestAssert.Equal(
                0,
                EnumerateVisualDescendants<DataGrid>(view).Count(),
                "The caption script must not fall back to an unthemed system DataGrid.");
            TestAssert.True(
                EnumerateVisualDescendants<ItemsControl>(view).Any(items =>
                    System.Windows.Automation.AutomationProperties.GetName(items)
                        .Equals(
                            "Caption text and timing segments",
                            StringComparison.Ordinal)),
                "The caption script should use the themed stacked editor that fits the narrow Inspector.");

            ItemsControl segments = EnumerateVisualDescendants<ItemsControl>(view)
                .Single(items =>
                    System.Windows.Automation.AutomationProperties.GetName(items)
                        .Equals(
                            "Caption text and timing segments",
                            StringComparison.Ordinal));
            FrameworkElement segment = (FrameworkElement)segments.ItemTemplate.LoadContent();
            segment.Measure(new Size(300, 220));
            segment.Arrange(new Rect(0, 0, 300, 220));
            segment.UpdateLayout();
            TextBox start = EnumerateVisualDescendants<TextBox>(segment)
                .Single(field =>
                    System.Windows.Automation.AutomationProperties.GetName(field)
                        .Equals(
                            "Caption segment start time",
                            StringComparison.Ordinal));
            TestAssert.Equal(
                32d,
                start.Height,
                "Clip-relative times should use compact fields instead of full-size form buttons.");
            TestAssert.False(
                EnumerateVisualDescendants<TextBlock>(view).Any(text =>
                    text.Text.Equals(
                        "Clip-relative seconds",
                        StringComparison.Ordinal)),
                "The caption editor should explain relative timing in its guidance instead of an unexplained decorative pill.");
        });
        return Task.CompletedTask;
    }

    private static Task StudioBrowserCardsPreserveReadableIdentity()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var view = new StudioBrowserView
            {
                Width = 288,
                Height = 680,
            };
            view.Measure(new Size(288, 680));
            view.Arrange(new Rect(0, 0, 288, 680));
            view.UpdateLayout();

            ItemsControl cards = EnumerateVisualDescendants<ItemsControl>(view)
                .Single(items =>
                    items.GetType() == typeof(ItemsControl) &&
                    items.ItemTemplate is not null);
            FrameworkElement card = (FrameworkElement)cards.ItemTemplate.LoadContent();
            card.Measure(new Size(250, 180));
            card.Arrange(new Rect(0, 0, 250, 180));
            card.UpdateLayout();

            TextBlock title = EnumerateVisualDescendants<TextBlock>(card)
                .Single(text =>
                    System.Windows.Automation.AutomationProperties.GetName(text)
                        .Equals(
                            "Studio Browser clip name",
                            StringComparison.Ordinal));
            TextBlock details = EnumerateVisualDescendants<TextBlock>(card)
                .Single(text =>
                    System.Windows.Automation.AutomationProperties.GetName(text)
                        .Equals(
                            "Studio Browser clip source and duration",
                            StringComparison.Ordinal));
            TestAssert.Equal(
                TextWrapping.Wrap,
                title.TextWrapping,
                "Clip names need their own wrapping row instead of competing with quality and action controls.");
            TestAssert.Equal(
                TextWrapping.Wrap,
                details.TextWrapping,
                "Source and duration details should wrap at the real Browser width rather than disappear behind ellipses.");
        });
        return Task.CompletedTask;
    }

    private static Task Ui03ThemeResourcesLoad()
    {
        RunOnSta(() =>
        {
            var app = EnsureApplication();
            foreach (string key in new[]
                     {
                         "Control.ThemedButton", "Control.ThemedTextBox", "Control.ThemedComboBox",
                         "Control.ThemedListBox", "Control.ScrollThumb", "Control.PopupSurface",
                         "Control.RangeThumb", "Control.ValidationErrorTemplate", "ReplayFoundry.WindowChrome",
                         "Control.CaptionButton", "Text.CaptionGlyph", "Motion.Hover", "Motion.Press",
                         "Motion.Reduced", "Icon.Project", "Icon.Glyph.ChromeClose"
                     })
            {
                TestAssert.True(app.TryFindResource(key) is not null, $"UI-03 resource {key} should load.");
            }
        });
        return Task.CompletedTask;
    }

    private static Task Ui03IconShapeResolves()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var icon = new IconPath { IconKey = "Icon.Project" };
            TestAssert.True(icon.IconKey == "Icon.Project", "Icon shape should retain its semantic key.");
            TestAssert.True(Application.Current?.TryFindResource("Icon.Project") is Geometry geometry && !geometry.IsEmpty(), "Icon resource should resolve a non-empty geometry.");

            Geometry edit = Application.Current?.TryFindResource("Icon.Edit") as Geometry ??
                throw new InvalidOperationException("The shared edit geometry is missing.");
            Rect bounds = edit.Bounds;
            TestAssert.True(
                !bounds.IsEmpty &&
                Math.Abs(bounds.Width - bounds.Height) <= 0.01d &&
                Math.Abs((bounds.Left + bounds.Right) / 2d - 7d) <= 0.5d &&
                Math.Abs((bounds.Top + bounds.Bottom) / 2d - 7d) <= 0.5d,
                "The shared pencil must retain a square, centered semantic geometry instead of shifting its eraser or tip inside icon buttons.");
        });
        return Task.CompletedTask;
    }

    private static Task CustomShellChromeIsConfigured()
    {
        RunOnSta(() =>
        {
            var window = new MainWindow(CreateShell());
            TestAssert.Equal(WindowStyle.None, window.WindowStyle, "Shell should use custom chrome.");
            TestAssert.False(window.AllowsTransparency, "Shell should preserve native composition.");
            TestAssert.True(WindowChrome.GetWindowChrome(window) is not null, "Shell should apply WindowChrome.");
            TestAssert.True(window.FindName("CaptionMinimizeButton") is Button, "Minimize caption button should exist.");
            TestAssert.True(window.FindName("CaptionMaximizeButton") is Button, "Maximize caption button should exist.");
            TestAssert.True(window.FindName("CaptionCloseButton") is Button, "Close caption button should exist.");
            TestAssert.Equal(
                56d,
                WindowChrome.GetWindowChrome(window)?.CaptionHeight ?? 0,
                "The shared chrome should remain compact.");
            TestAssert.True(
                window.FindName("WorkspaceChromeEyebrow") is TextBlock,
                "The blue workspace identity should remain in shared chrome.");
            TestAssert.True(
                window.FindName("WorkspaceChromeDescription") is TextBlock,
                "The workspace description should remain in shared chrome.");

            static void AssertCaptionButtons(
                FrameworkElement chromeRoot,
                string context)
            {
                foreach (string name in new[]
                         {
                             "CaptionMinimizeButton",
                             "CaptionMaximizeButton",
                             "CaptionCloseButton",
                         })
                {
                    Button button = chromeRoot.FindName(name) as Button ??
                        throw new InvalidOperationException(
                            $"{context} did not create {name}.");
                    TestAssert.True(
                        button.Width is >= 40d and <= 46d &&
                        button.Height is >= 40d and <= 46d &&
                        button.MinWidth >= 40d &&
                        button.MinHeight >= 40d,
                        $"{context} {name} must retain a 40-46 DIP hit target.");
                    TestAssert.True(
                        button.Content is TextBlock glyph && glyph.FontSize == 8d,
                        $"{context} {name} should use the compact shared caption glyph.");
                    button.ApplyTemplate();
                    Border captionVisual = button.Template?.FindName(
                        "CaptionVisual",
                        button) as Border ??
                        throw new InvalidOperationException(
                            $"{context} {name} did not create its compact visual surface.");
                    TestAssert.True(
                        captionVisual.Width == 28d &&
                        captionVisual.Height == 28d,
                        $"{context} {name} must paint only a compact 28-DIP visual surface inside its larger hit target.");
                }

                TestAssert.True(
                    chromeRoot.FindName("CaptionMaximizeGlyph") is TextBlock,
                    $"{context} must retain a text glyph for native maximize/restore updates.");
            }

            AssertCaptionButtons(window, "Main shell chrome");
            AssertCaptionButtons(new WindowTitleBar(), "Dialog chrome");

            string shellXaml = File.ReadAllText(
                Path.Combine(
                    FindRepositoryRoot(),
                    "ReplayFoundry.Desktop",
                    "Shell",
                    "MainWindow.xaml"));
            TestAssert.False(
                shellXaml.Contains(
                    "CurrentWorkspaceChrome.WorkspaceTitle",
                    StringComparison.Ordinal),
                "Shared chrome should not repeat a bold white workspace title beside the blue identity.");
        });
        return Task.CompletedTask;
    }

    private static Task SharedChromeAndSelectionControlsStayAligned()
    {
        RunOnSta(() =>
        {
            Application app = EnsureApplication();
            var titleStatus = new TextBlock
            {
                Style = (Style)app.FindResource("Text.TitleBarStatus"),
            };
            TestAssert.Equal(
                VerticalAlignment.Center,
                titleStatus.VerticalAlignment,
                "Shared title-bar status text should align to the middle of fixed-height chrome.");
            TestAssert.Equal(
                TextWrapping.NoWrap,
                titleStatus.TextWrapping,
                "Shared title-bar status text should not create a second row inside fixed-height chrome.");

            var checkBox = new CheckBox
            {
                Width = 240,
                Height = 40,
                Content = "Include captions",
                Style = (Style)app.FindResource("Control.ThemedCheckBox"),
            };
            var radioButton = new RadioButton
            {
                Width = 240,
                Height = 40,
                Content = "Gameplay focus",
                Style = (Style)app.FindResource("Control.ThemedRadioButton"),
            };

            foreach ((System.Windows.Controls.Primitives.ToggleButton control, string indicatorName) in
                     new (System.Windows.Controls.Primitives.ToggleButton, string)[]
                     {
                         (checkBox, "Box"),
                         (radioButton, "Outer"),
                     })
            {
                control.ApplyTemplate();
                control.Measure(new Size(240, 40));
                control.Arrange(new Rect(0, 0, 240, 40));
                control.UpdateLayout();

                TestAssert.Equal(
                    VerticalAlignment.Center,
                    control.VerticalContentAlignment,
                    "Selection content should stay vertically centered in its 40 DIP target.");
                Border indicator = control.Template.FindName(indicatorName, control) as Border ??
                    throw new InvalidOperationException(
                        $"The shared selection template did not create {indicatorName}.");
                TestAssert.Equal(
                    VerticalAlignment.Center,
                    indicator.VerticalAlignment,
                    "Selection indicators should align with their labels rather than the control top edge.");
            }
        });

        return Task.CompletedTask;
    }

    private static Task WorkspaceContinuationCueTracksScrollExtent()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var fittingViewport = new WorkspaceScrollViewport
            {
                Width = 320,
                Height = 160,
                Content = new Border { Height = 80 },
            };

            fittingViewport.ApplyTemplate();
            fittingViewport.Measure(new Size(320, 160));
            fittingViewport.Arrange(new Rect(0, 0, 320, 160));
            fittingViewport.UpdateLayout();

            ScrollViewer fittingScrollViewer =
                FindVisualDescendant<ScrollViewer>(fittingViewport) ??
                throw new InvalidOperationException(
                    "The fitting workspace viewport did not create its ScrollViewer.");
            TestAssert.Equal(
                0d,
                fittingScrollViewer.ScrollableHeight,
                "A workspace that fits must not gain a phantom vertical scroll range.");
            TestAssert.Equal(
                Visibility.Collapsed,
                fittingScrollViewer.ComputedVerticalScrollBarVisibility,
                "A workspace that fits must not show an unnecessary vertical scrollbar.");
            TestAssert.False(
                fittingViewport.HasMoreBelow,
                "A workspace that fits must not show the continuation cue.");

            var viewport = new WorkspaceScrollViewport
            {
                Width = 320,
                Height = 160,
                CueText = "More tools below",
                Content = new Border { Height = 480 },
            };

            viewport.ApplyTemplate();
            viewport.Measure(new Size(320, 160));
            viewport.Arrange(new Rect(0, 0, 320, 160));
            viewport.UpdateLayout();

            TestAssert.True(
                viewport.HasMoreBelow,
                "The continuation cue should appear while content remains below the viewport.");

            ScrollViewer scrollViewer =
                FindVisualDescendant<ScrollViewer>(viewport) ??
                throw new InvalidOperationException(
                    "The workspace viewport template did not create its ScrollViewer.");
            Border continuationCue =
                viewport.Template.FindName("ContinuationCue", viewport) as
                    Border ??
                throw new InvalidOperationException(
                    "The workspace viewport template did not create its continuation cue.");
            TestAssert.Equal(
                0,
                Grid.GetRow(scrollViewer),
                "Scrollable content should occupy the viewport's content row.");
            TestAssert.Equal(
                1,
                Grid.GetRow(continuationCue),
                "The continuation cue must reserve its own row instead of obscuring workspace controls.");
            scrollViewer.ScrollToEnd();
            viewport.UpdateLayout();

            TestAssert.False(
                viewport.HasMoreBelow,
                "The continuation cue should clear at the final scroll extent.");
        });

        return Task.CompletedTask;
    }

    private static Task SettingsNavigationStaysAlignedWhileContentScrolls()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            using var viewModel = new SettingsViewModel();
            var settings = new SettingsView
            {
                Width = 1266,
                Height = 620,
                DataContext = viewModel,
            };
            settings.SetResponsiveWidthForTest(settings.Width);
            settings.Measure(new Size(settings.Width, settings.Height));
            settings.Arrange(new Rect(0, 0, settings.Width, settings.Height));
            settings.UpdateLayout();

            var sectionList = settings.FindName("StandardSectionList") as
                    ListBox ??
                throw new InvalidOperationException(
                    "The standard Settings navigation list is missing.");
            var sectionViewport =
                settings.FindName("StandardSectionScrollViewport") as
                    WorkspaceScrollViewport ??
                throw new InvalidOperationException(
                    "The standard Settings section viewport is missing.");
            sectionViewport.ApplyTemplate();
            ScrollViewer sectionScroller =
                FindVisualDescendant<ScrollViewer>(sectionViewport) ??
                throw new InvalidOperationException(
                    "The Settings section viewport must contain its scroll viewer.");
            TestAssert.True(
                sectionScroller.ScrollableHeight > 0,
                "The regression surface must have enough section content to scroll.");

            var privacyItem =
                sectionList.ItemContainerGenerator.ContainerFromIndex(3) as
                    ListBoxItem ??
                throw new InvalidOperationException(
                    "The Privacy & connections navigation item is missing.");
            Point beforeScroll = privacyItem.TranslatePoint(
                new Point(
                    privacyItem.ActualWidth / 2d,
                    privacyItem.ActualHeight / 2d),
                settings);

            sectionScroller.ScrollToVerticalOffset(
                Math.Min(160d, sectionScroller.ScrollableHeight));
            settings.UpdateLayout();

            Point afterScroll = privacyItem.TranslatePoint(
                new Point(
                    privacyItem.ActualWidth / 2d,
                    privacyItem.ActualHeight / 2d),
                settings);
            TestAssert.True(
                Math.Abs(beforeScroll.Y - afterScroll.Y) <= 0.01d,
                "Scrolling a Settings page must not move the navigation hit targets.");

            DependencyObject hit = VisualTreeHelper.HitTest(
                    settings,
                    afterScroll)?.VisualHit ??
                throw new InvalidOperationException(
                    "The visible Settings navigation item was not hit-testable.");
            TestAssert.True(
                ReferenceEquals(
                    FindVisualAncestor<ListBoxItem>(hit),
                    privacyItem),
                "The visible Privacy & connections row must hit-test to itself after the page scrolls.");

            sectionList.SelectedIndex = 3;
            settings.UpdateLayout();
            TestAssert.True(
                Math.Abs(sectionScroller.VerticalOffset) <= 0.01d,
                "Changing Settings sections must reset the new page to the top.");
        });
        return Task.CompletedTask;
    }

    private static Task PriorityMomentMarksOccupyTimelineTrack()
    {
        string xaml = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                "ReplayFoundry.Desktop",
                "Features",
                "Generate",
                "GenerationSetup",
                "Steps",
                "MomentGuidance",
                "MomentGuidanceStepView.xaml"));

        TestAssert.True(
            xaml.Contains("x:Name=\"TimelineGuidanceOverlay\"", StringComparison.Ordinal) &&
            xaml.Contains("<local:MomentGuidanceTimelineOverlay", StringComparison.Ordinal) &&
            xaml.Contains("AutomationProperties.Name=\"Priority moment timeline position\"", StringComparison.Ordinal) &&
            xaml.Contains("Items=\"{Binding SelectedSource.Items}\"", StringComparison.Ordinal) &&
            xaml.Contains("PointBrush=\"{DynamicResource Brush.StatusInfo}\"", StringComparison.Ordinal) &&
            xaml.Contains("RangeBrush=\"{DynamicResource Brush.StatusWarning}\"", StringComparison.Ordinal),
            "Priority ticks and ranges must arrange against the timeline's full visual width.");

        string sourcePath = Path.Combine(
            Path.GetTempPath(),
            "priority-moment-overlay-source.mkv");
        var point = new UserMomentGuidanceItemViewModel(
            UserMomentGuidance.CreatePoint(
                sourcePath,
                TimeSpan.FromSeconds(12),
                TimeSpan.FromSeconds(6)),
            static _ => { });
        var range = new UserMomentGuidanceItemViewModel(
            UserMomentGuidance.CreateRange(
                sourcePath,
                TimeSpan.FromSeconds(12),
                TimeSpan.FromSeconds(6),
                TimeSpan.FromSeconds(12)),
            static _ => { });

        Rect pointBounds = MomentGuidanceTimelineOverlay.MarkerBounds(
            point,
            durationSeconds: 12,
            width: 100,
            height: 14);
        Rect rangeBounds = MomentGuidanceTimelineOverlay.MarkerBounds(
            range,
            durationSeconds: 12,
            width: 100,
            height: 14);
        TestAssert.Equal(
            new Rect(50, 0, 3, 14),
            pointBounds,
            "A guidance item at the midpoint should start halfway across the timeline track.");
        TestAssert.Equal(
            new Rect(50, 3, 50, 8),
            rangeBounds,
            "A guidance range should visibly span its complete timeline interval.");

        return Task.CompletedTask;
    }

    private static T? FindVisualDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (int index = 0;
             index < VisualTreeHelper.GetChildrenCount(root);
             index++)
        {
            DependencyObject child =
                VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                return match;
            }

            T? descendant = FindVisualDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject child)
        where T : DependencyObject
    {
        for (DependencyObject? current = child;
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static Task Ui04StartupPolicyIsExplicit()
    {
        RunOnSta(() =>
        {
            var window = new MainWindow(CreateShell());
            TestAssert.Equal(WindowState.Maximized, window.WindowState, "The shell should start maximized.");
            TestAssert.Equal(WindowStartupPolicy.MinimumWidth, window.MinWidth, "The shell minimum width should support compact Snap layouts.");
            TestAssert.Equal(WindowStartupPolicy.MinimumHeight, window.MinHeight, "The shell minimum height should support compact work areas.");
            TestAssert.True(window.ShowInTaskbar, "The shell must remain represented in the taskbar.");
            TestAssert.False(window.Topmost, "The shell must not cover other applications.");
            TestAssert.Equal(ResizeMode.CanResize, window.ResizeMode, "The shell must remain resizable.");
        });
        return Task.CompletedTask;
    }

    private static Task Ui04CaptionHitTestingIsExplicit()
    {
        RunOnSta(() =>
        {
            var window = new MainWindow(CreateShell());
            foreach (string name in new[] { "CaptionMinimizeButton", "CaptionMaximizeButton", "CaptionCloseButton", "CaptionHelpButton" })
            {
                TestAssert.True(window.FindName(name) is Button button && WindowChrome.GetIsHitTestVisibleInChrome(button), $"{name} should be interactive in custom chrome.");
            }
            TestAssert.True(WindowWorkAreaCalculator.DipToPixels(40, 144) >= 40, "Caption targets should remain at least 40 physical pixels at 150 percent DPI.");
            string nativeBehavior = File.ReadAllText(
                Path.Combine(
                    FindRepositoryRoot(),
                    "ReplayFoundry.Desktop",
                    "Shell",
                    "Windowing",
                    "MainWindowNativeBehavior.cs"));
            TestAssert.False(
                nativeBehavior.Contains("HtMaxButton", StringComparison.Ordinal) ||
                nativeBehavior.Contains("WmNcHitTest", StringComparison.Ordinal),
                "Custom caption buttons must not advertise a native maximize hit zone that opens the oversized Windows Snap Layout flyout.");
        });
        return Task.CompletedTask;
    }

    private static Task Ui04WorkAreaBoundsPreserveWorkArea()
    {
        var monitor = new MonitorWorkArea(-1920, 0, 1920, 1080, -1920, 0, 1920, 1040, 144);
        WindowMaxBounds bounds = WindowWorkAreaCalculator.ForMonitor(monitor);
        TestAssert.Equal(0, bounds.X, "A full-work-area monitor should start at its monitor origin.");
        TestAssert.Equal(1040, bounds.Height, "The work area should reserve the taskbar.");
        TestAssert.Equal(1920, bounds.Width, "The work area should preserve the monitor width.");
        return Task.CompletedTask;
    }

    private static Task Ui04ResponsiveReadabilityIsExplicit()
    {
        ResponsiveReadabilityState compact = ResponsiveReadabilityState.Calculate(900, 620, 1.25, 120);
        ResponsiveReadabilityState largeText = ResponsiveReadabilityState.Calculate(1920, 1080, 2.25, 192);
        TestAssert.Equal(ResponsiveWidthBand.Compact, compact.Width, "Compact width should be explicit.");
        TestAssert.Equal(ResponsiveHeightBand.Short, compact.Height, "Short height should be explicit.");
        TestAssert.True(compact.NeedsProgressiveDisclosure, "Compact work areas should disclose secondary detail progressively.");
        TestAssert.Equal(ResponsiveWidthBand.Wide, largeText.Width, "Wide width should remain available at large text scale.");
        TestAssert.True(largeText.NeedsWrappedLabels, "225 percent text should wrap labels rather than clip them.");
        TestAssert.Equal((uint)192, largeText.Dpi, "DPI must remain part of the readability state.");
        return Task.CompletedTask;
    }

    private static Task Ui04GuidanceSurfacesAreSearchable()
    {
        var shell = CreateShell();
        shell.OpenGuideCommand.Execute(null);
        TestAssert.True(shell.ActiveOverlay is FoundryGuideViewModel, "F1/help should open the guide surface.");
        shell.Guide.SearchText = "access";
        TestAssert.True(shell.Guide.FilteredEntries.Count > 0, "Guide search should return accessibility guidance.");
        shell.OpenShortcutReferenceCommand.Execute(null);
        TestAssert.True(shell.ActiveOverlay is ShortcutReferenceViewModel, "Shortcut reference should be a reopenable overlay.");
        shell.ShortcutReference.SearchText = "palette";
        TestAssert.Equal(1, shell.ShortcutReference.FilteredEntries.Count, "Shortcut search should narrow the reference.");
        shell.OpenCommandPaletteCommand.Execute(null);
        TestAssert.True(shell.ActiveOverlay is CommandPaletteViewModel, "Ctrl+K should open the command palette.");
        shell.CommandPalette.SearchText = "studio";
        TestAssert.True(shell.CommandPalette.FilteredEntries.Count > 0, "Command palette search should find workspace navigation.");
        return Task.CompletedTask;
    }

    private static Task Ui04IssueReferencesAreStable()
    {
        TestAssert.True(IssueReference.IsValid("RF-STU-001"), "Stable issue references should accept RF-AREA-000.");
        TestAssert.False(IssueReference.IsValid("studio unavailable"), "Issue references should reject unstable prose.");
        var issue = new UserFacingIssue("RF-LIB-001", "Library is not available yet.", "Return to Generate to continue.", "Provider boundary.");
        TestAssert.Equal("RF-LIB-001", issue.Reference, "Issue reference should remain copyable.");
        TestAssert.Throws<ArgumentException>(() => new UserFacingIssue("LIB-1", "Bad", "Fix", "Details"), "Invalid issue references should fail explicitly.");
        return Task.CompletedTask;
    }

    private static Task Ui04AccessibilityResourcesArePresent()
    {
        RunOnSta(() =>
        {
            var app = EnsureApplication();
            TestAssert.Equal(40d, (double)app.Resources["Dimension.InteractiveTarget"], "Interactive target token should be 40.");
            TestAssert.True(app.TryFindResource("Brush.HighContrastWindow") is not null, "High-contrast system brush resources should load.");
            TestAssert.True(app.TryFindResource("Motion.IsReduced") is not null, "Reduced-motion state resource should load.");
            TestAssert.False(CursorPolicy.HasGlobalHandCursor, "The global cursor policy should not use a hand cursor.");
        });
        return Task.CompletedTask;
    }

    private static Task ResponsiveBreakpointsWork()
    {
        RunOnSta(() =>
        {
            var studio = new StudioView();
            var library = new LibraryView();
            var publish = new PublishView();
            var settings = new SettingsView();
            foreach (double width in new[] { 1000d, 1266d, 1920d })
            {
                studio.SetResponsiveWidthForTest(width);
                library.SetResponsiveWidthForTest(width);
                publish.SetResponsiveWidthForTest(width);
                settings.SetResponsiveWidthForTest(width);
                bool compact = width < 1120;
                bool standard = width >= 1120 && width < 1600;
                bool wide = width >= 1600;
                TestAssert.Equal(compact, studio.IsCompactLayout, "Studio compact breakpoint should match.");
                TestAssert.Equal(standard, studio.IsStandardLayout, "Studio standard breakpoint should match.");
                TestAssert.Equal(wide, studio.IsWideLayout, "Studio wide breakpoint should match.");
                TestAssert.Equal(compact, library.IsCompactLayout, "Library compact breakpoint should match.");
                TestAssert.Equal(compact, publish.IsCompactLayout, "Publish compact breakpoint should match.");
                TestAssert.Equal(compact, settings.IsCompactLayout, "Settings compact breakpoint should match.");
            }
        });
        return Task.CompletedTask;
    }

    private static Task ViewsInstantiateWithAppResources()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            _ = new StudioView();
            _ = new LibraryView();
            _ = new PublishView();
            _ = new SettingsView();
            foreach (UserControl section in new UserControl[]
            {
                new StudioBrowserView(),
                new StudioPreviewView(),
                new StudioInspectorView(),
                new LibraryCategoryRailView(),
                new LibraryFilterBarView(),
                new LibraryContentView(),
                new LibraryDetailsView(),
                new PublishAssetView(),
                new PublishChecklistView(),
                new PublishDestinationsView(),
                new PublishMetadataView(),
                new PublishOutputSettingsView(),
                new PublishQueueHistoryView(),
                new PublishCalendarView(),
                new PublishLibraryBrowserView(),
                new StorageSettingsView(),
                new CreatorVoiceSettingsView(),
                new AiModelsSettingsView(),
                new PrivacyDiagnosticsSettingsView(),
                new AboutSettingsView(),
            })
            {
                TestAssert.True(section.Content is not null, $"{section.GetType().Name} should initialize its XAML content.");
            }
            _ = new WindowTitleBar();
            _ = new EmptyState();
            _ = new StatusBadge();
            _ = new UnavailableBanner();
            var window = new MainWindow(CreateShell());
            window.Show();
            window.ApplyTemplate();
            window.UpdateLayout();
            var contentControl = window.FindName("WorkspaceContent") as ContentControl;
            if (contentControl is null) throw new InvalidOperationException("The shell should expose one workspace host.");
            TestAssert.True(contentControl.Content is GenerateViewModel, "The workspace host should start on Generate.");
            Dispatcher.CurrentDispatcher.Invoke(
                static () => { },
                DispatcherPriority.ContextIdle);
            window.Close();
            Dispatcher.CurrentDispatcher.Invoke(
                static () => { },
                DispatcherPriority.ContextIdle);
        });
        return Task.CompletedTask;
    }

    private static Task Ui04AutoHideTaskbarEdgeRemainsReachable()
    {
        var monitor = new MonitorWorkArea(
            -1920,
            -100,
            1920,
            1080,
            -1920,
            -100,
            1920,
            1080,
            144);
        WindowMaxBounds baseline =
            WindowWorkAreaCalculator.ForMonitor(monitor);
        WindowMaxBounds left = WindowWorkAreaCalculator.ForMonitor(
            monitor,
            AutoHideTaskbarEdge.Left);
        WindowMaxBounds top = WindowWorkAreaCalculator.ForMonitor(
            monitor,
            AutoHideTaskbarEdge.Top);
        WindowMaxBounds right = WindowWorkAreaCalculator.ForMonitor(
            monitor,
            AutoHideTaskbarEdge.Right);
        WindowMaxBounds bottom = WindowWorkAreaCalculator.ForMonitor(
            monitor,
            AutoHideTaskbarEdge.Bottom);

        TestAssert.Equal(
            baseline.X + 1,
            left.X,
            "A left auto-hidden taskbar requires one reachable physical edge pixel.");
        TestAssert.Equal(
            baseline.Y + 1,
            top.Y,
            "A top auto-hidden taskbar requires one reachable physical edge pixel.");
        TestAssert.Equal(
            baseline.Right - 1,
            right.Right,
            "A right auto-hidden taskbar requires one reachable physical edge pixel.");
        TestAssert.Equal(
            baseline.Bottom - 1,
            bottom.Bottom,
            "A bottom auto-hidden taskbar requires one reachable physical edge pixel.");
        TestAssert.Equal(
            baseline.Height,
            left.Height,
            "A vertical taskbar edge must not alter maximized height.");
        TestAssert.Equal(
            baseline.Width,
            bottom.Width,
            "A horizontal taskbar edge must not alter maximized width.");
        return Task.CompletedTask;
    }

    private static Task Ui04OffScreenRecoveryCentersRestoreBounds()
    {
        var monitor = new MonitorWorkArea(
            -1920,
            0,
            1920,
            1080,
            -1920,
            0,
            1920,
            1040,
            96);
        WindowRestoreBounds bounds =
            WindowWorkAreaCalculator.CenterRestoreBounds(
                monitor,
                WindowStartupPolicy.DefaultWidth,
                WindowStartupPolicy.DefaultHeight);

        TestAssert.Equal(-1600, bounds.X,
            "Recovery should center a normal window on the selected negative-coordinate monitor.");
        TestAssert.Equal(160, bounds.Y,
            "Recovery should center within the work area rather than the taskbar-inclusive monitor.");
        TestAssert.Equal(1280, bounds.Width,
            "Recovery should preserve the normal startup width when it fits.");
        TestAssert.Equal(720, bounds.Height,
            "Recovery should preserve the normal startup height when it fits.");

        WindowRestoreBounds compact =
            WindowWorkAreaCalculator.CenterRestoreBounds(
                new MonitorWorkArea(
                    0,
                    0,
                    800,
                    600,
                    0,
                    0,
                    800,
                    560,
                    144),
                WindowStartupPolicy.DefaultWidth,
                WindowStartupPolicy.DefaultHeight);
        TestAssert.Equal(800, compact.Width,
            "Recovery should clamp a DPI-scaled width to the available work area.");
        TestAssert.Equal(560, compact.Height,
            "Recovery should clamp a DPI-scaled height to the available work area.");
        return Task.CompletedTask;
    }

    private static Task Ui04OffScreenShellRecoversOnLoad()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var window = new MainWindow(CreateShell())
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                WindowState = WindowState.Normal,
                Left = -32000,
                Top = -32000,
                Width = WindowStartupPolicy.MinimumWidth,
                Height = WindowStartupPolicy.MinimumHeight,
                ShowActivated = false,
            };
            try
            {
                window.Show();
                var frame = new DispatcherFrame();
                window.Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);

                TestAssert.True(
                    window.Left > -30000 && window.Top > -30000,
                    "A visible shell must not retain the native minimized-window sentinel as its normal position.");
            }
            finally
            {
                window.Close();
            }
        });
        return Task.CompletedTask;
    }

    private static Task LibraryThumbnailConverterLoadsStream()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"ReplayFoundry-thumbnail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "thumbnail.png");
        File.WriteAllBytes(
            path,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));

        try
        {
            RunOnSta(() =>
            {
                var converter = new FileImageSourceConverter();
                object? result = converter.Convert(
                    path,
                    typeof(BitmapSource),
                    parameter: null,
                    System.Globalization.CultureInfo.InvariantCulture);
                TestAssert.True(
                    result is BitmapSource image && image.PixelWidth == 1 && image.PixelHeight == 1,
                    "A valid local thumbnail must load completely without a URI-backed image cache key.");
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static Task LibraryPopulatedDetailsBindOneWay()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var asset = new LibraryMediaAsset(
                "library-binding-asset",
                "library-binding-project",
                GenerationMode.IndividualClips,
                rank: 1,
                Path.Combine(Path.GetTempPath(), "library-binding-render.mp4"),
                thumbnailFullPath: null,
                TimeSpan.FromSeconds(38),
                outputWidth: 1080,
                outputHeight: 1920,
                "Rendered clip",
                "Rendered clip description",
                ["gameplay"],
                DateTimeOffset.UtcNow);
            using var viewModel = new LibraryViewModel(
                new FixedLibraryCatalog(asset));
            var view = new LibraryDetailsView
            {
                Width = 320,
                Height = 480,
                DataContext = viewModel,
            };

            view.ApplyTemplate();
            view.Measure(new Size(320, 480));
            view.Arrange(new Rect(0, 0, 320, 480));
            view.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(
                static () => { },
                DispatcherPriority.ContextIdle);

            foreach (Run valueRun in new[]
                     {
                         view.SelectedStatusRun,
                         view.SelectedAspectRatioRun,
                         view.SelectedDurationRun,
                         view.SelectedModifiedRun,
                     })
            {
                Binding? binding = BindingOperations.GetBinding(
                    valueRun,
                    Run.TextProperty);
                TestAssert.Equal(
                    BindingMode.OneWay,
                    binding?.Mode,
                    "Read-only Library detail text must never activate a TwoWay WPF binding.");
            }
        });

        return Task.CompletedTask;
    }

    private sealed class FixedLibraryCatalog(
        LibraryMediaAsset asset) : ILibraryCatalog
    {
        public IReadOnlyList<LibraryMediaAsset> Assets { get; } = [asset];

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }

}
