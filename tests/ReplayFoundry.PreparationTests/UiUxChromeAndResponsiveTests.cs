using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
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
using ReplayFoundry.Desktop.Features.Settings.Sections;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Browser;
using ReplayFoundry.Desktop.Features.Studio.Inspector;
using ReplayFoundry.Desktop.Features.Studio.HiddenMoments;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Presentation.Controls;
using ReplayFoundry.Desktop.Presentation.Commands;
using ReplayFoundry.Desktop.Presentation.Converters;
using ReplayFoundry.Desktop.Presentation.Feedback;
using ReplayFoundry.Desktop.Presentation.Accessibility;
using ReplayFoundry.Desktop.Presentation.Responsive;
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
            TestAssert.True(
                EnumerateVisualDescendants<TextBlock>(view).Any(text =>
                    text.Text.Equals(
                        "Caption phrase size",
                        StringComparison.Ordinal)),
                "The Studio control must describe static caption segmentation as phrase size, not words shown during animation.");
            TestAssert.True(
                EnumerateVisualDescendants<Border>(view).Any(border =>
                    System.Windows.Automation.AutomationProperties.GetName(border)
                        .Equals(
                            "Caption phrase size locked by Pop",
                            StringComparison.Ordinal)),
                "The caption editor must contain an explicit accessible Pop-locked phrase-size state.");

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
                40d,
                start.Height,
                "Clip-relative times should preserve a compact visual while retaining the shared hit-target height.");
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

            ListBox cards = EnumerateVisualDescendants<ListBox>(view)
                .Single(items =>
                    System.Windows.Automation.AutomationProperties.GetName(items)
                        .Equals(
                            "Studio clips",
                            StringComparison.Ordinal) &&
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
            TextBlock rank = EnumerateVisualDescendants<TextBlock>(card)
                .Single(text =>
                    System.Windows.Automation.AutomationProperties.GetName(text)
                        .Equals(
                            "Studio Browser clip batch rank",
                            StringComparison.Ordinal));
            TextBlock sourcePosition = EnumerateVisualDescendants<TextBlock>(card)
                .Single(text =>
                    System.Windows.Automation.AutomationProperties.GetName(text)
                        .Equals(
                            "Studio Browser clip source position",
                            StringComparison.Ordinal));
            TestAssert.Equal(
                TextWrapping.Wrap,
                title.TextWrapping,
                "Clip names need their own wrapping row instead of competing with quality and action controls.");
            TestAssert.True(
                title.Foreground is SolidColorBrush titleForeground &&
                Application.Current.FindResource("Brush.TextPrimary") is
                    SolidColorBrush textPrimaryBrush &&
                titleForeground.Color == textPrimaryBrush.Color,
                "Clip names must use the dark-theme primary text color instead of inheriting WPF's black ListBox foreground.");
            TestAssert.Equal(
                TextTrimming.None,
                rank.TextTrimming,
                "The stable batch rank must remain readable instead of disappearing behind an ellipsis.");
            TestAssert.Equal(
                TextTrimming.None,
                sourcePosition.TextTrimming,
                "The source position must remain readable instead of disappearing behind an ellipsis.");
            TestAssert.True(
                rank.Parent is WrapPanel && ReferenceEquals(rank.Parent, sourcePosition.Parent),
                "Rank and source position should reflow together at the real Browser width.");
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
                         "Control.ThemedListBox", "Control.ScrollThumb",
                         "Control.RangeThumb", "Control.ValidationErrorTemplate", "ReplayFoundry.WindowChrome",
                         "Control.CaptionButton", "Text.CaptionGlyph", "Motion.Hover", "Motion.Press",
                         "Icon.Project", "Icon.Glyph.ChromeClose", "Icon.Calendar", "Icon.Clock"
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
                    RepositoryLayout.Root,
                    "src",
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

    private static Task ShellAndLibraryStatusIconsAreCompact()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            using var shell = CreateShell(
                localAiCapabilities: new GenerationRuntimeCapabilities(
                    IsCaptionTranscriptionAvailable: true,
                    IsSpeechActivityAvailable: true,
                    IsVisualSemanticReviewAvailable: true,
                    IsEditorialAiAvailable: true));
            var window = new MainWindow(shell);
            window.Show();
            window.ApplyTemplate();
            window.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(
                static () => { },
                DispatcherPriority.ContextIdle);

            Label localAi = window.FindName("LocalAiStatusIndicator") as Label ??
                throw new InvalidOperationException(
                    "The shell must expose its compact Local AI indicator.");
            TestAssert.True(
                localAi.Width == 32d && localAi.Height == 32d,
                "The installed-tools indicator must stay compact instead of returning to a text pill.");
            TestAssert.True(
                !string.IsNullOrWhiteSpace(
                    System.Windows.Automation.AutomationProperties
                        .GetName(localAi)) &&
                !string.IsNullOrWhiteSpace(
                    System.Windows.Automation.AutomationProperties
                        .GetHelpText(localAi)) &&
                !string.IsNullOrWhiteSpace(
                    System.Windows.Automation.AutomationProperties
                        .GetItemStatus(localAi)),
                "The installed-tools indicator must expose its toolset and explanation to assistive technology.");
            TestAssert.True(
                localAi.ToolTip is ToolTip,
                "The installed-tools indicator must explain itself on hover.");
            var tooltip = (ToolTip)localAi.ToolTip;
            tooltip.PlacementTarget = localAi;
            tooltip.IsOpen = true;
            Dispatcher.CurrentDispatcher.Invoke(
                static () => { },
                DispatcherPriority.Loaded);
            TestAssert.True(
                tooltip.Content is TextBlock tooltipText &&
                string.Equals(
                    tooltipText.Text,
                    "Advanced AI is ready for picture review, title writing, captions, and speech timing. Local tools are also ready.",
                    StringComparison.Ordinal),
                "The AI hover explanation should identify the usable tier and its user-facing work.");
            tooltip.IsOpen = false;
            TestAssert.Equal(
                "✓",
                (window.FindName("LocalAiStatusMarker") as TextBlock)?.Text,
                "The AI-and-local marker must communicate the installed toolset without relying on color.");
            TestAssert.Equal(
                "Icon.Spark",
                (window.FindName("LocalAiStatusIcon") as
                    ReplayFoundry.Desktop.Presentation.Controls.IconPath)?.IconKey,
                "The AI-and-local projection should use the AI spark icon.");
            SolidColorBrush readyColor =
                (SolidColorBrush)Application.Current.FindResource(
                    "Brush.StatusSuccess");
            TestAssert.Equal(
                readyColor.Color,
                ((SolidColorBrush)localAi.Foreground).Color,
                "Usable AI must render with the bright ready color instead of the muted offline color.");
            Border readySignal =
                (Border)localAi.Template.FindName("ReadySignal", localAi);
            TestAssert.True(
                localAi.Opacity == 1d && readySignal.Opacity > 0d,
                "Usable AI must light the complete indicator and its bounded inner signal.");

            using (var offlineShell = CreateShell(
                       localAiCapabilities:
                           GenerationRuntimeCapabilities.DeterministicOnly))
            {
                var offlineWindow = new MainWindow(offlineShell);
                offlineWindow.Show();
                offlineWindow.ApplyTemplate();
                offlineWindow.UpdateLayout();
                Label offlineIndicator =
                    (Label)offlineWindow.FindName("LocalAiStatusIndicator");
                Border offlineSignal =
                    (Border)offlineIndicator.Template.FindName(
                        "ReadySignal",
                        offlineIndicator);
                SolidColorBrush offlineColor =
                    (SolidColorBrush)Application.Current.FindResource(
                        "Brush.TextMuted");
                TestAssert.True(
                    offlineIndicator.Opacity < 1d &&
                    offlineSignal.Opacity == 0d &&
                    ((SolidColorBrush)offlineIndicator.Foreground).Color ==
                    offlineColor.Color,
                    "An installation without usable AI must keep the icon quiet and unlit.");
                offlineWindow.Close();
            }
            TestAssert.True(
                window.FindName("WikidataStatusIndicator") is null,
                "Public game lookup must not appear globally as an offline service before the user reaches Game Details.");

            string gameContextXaml = File.ReadAllText(Path.Combine(
                RepositoryLayout.Root,
                "src",
                "ReplayFoundry.Desktop",
                "Features",
                "Generate",
                "GenerationSetup",
                "Steps",
                "GameContext",
                "GameContextStepView.xaml"));
            TestAssert.True(
                gameContextXaml.Contains(
                    "x:Name=\"PublicLookupStateRow\"",
                    StringComparison.Ordinal) &&
                gameContextXaml.Contains(
                    "SelectedSource.IsPublicLookupAvailable",
                    StringComparison.Ordinal) &&
                gameContextXaml.Contains(
                    "SelectedSource.PublicLookupStatusText",
                    StringComparison.Ordinal) &&
                gameContextXaml.Contains(
                    "SelectedSource.CanUsePublicLookup",
                    StringComparison.Ordinal),
                "Game Details must own a stable, capability-based public lookup status beside its opt-in control.");

            var library = new LibraryContentView();
            Style removeStyle = library.Resources["LibraryRemoveIconButton"] as Style ??
                throw new InvalidOperationException(
                    "Library remove buttons must use their local compact style.");
            var removeButton = new Button
            {
                Content = "Icon.Trash",
                Style = removeStyle,
            };
            removeButton.ApplyTemplate();
            TestAssert.True(
                removeButton.Width >= 40d &&
                removeButton.Height >= 40d &&
                removeButton.MinWidth >= 40d &&
                removeButton.MinHeight >= 40d,
                "A smaller Library trash glyph must retain the 40-DIP hit target.");
            DataTemplate removeTemplate = removeButton.ContentTemplate ??
                throw new InvalidOperationException(
                    "The Library remove style must provide a local icon template.");
            IconPath trash = (IconPath)removeTemplate.LoadContent();
            TestAssert.True(
                trash.Width == 12d && trash.Height == 12d,
                "Library trash glyphs must render smaller than the shared 16-DIP icon.");

            string libraryXaml = File.ReadAllText(
                Path.Combine(
                    RepositoryLayout.Root,
                    "src",
                    "ReplayFoundry.Desktop",
                    "Features",
                    "Library",
                    "Sections",
                    "LibraryContentView.xaml"));
            TestAssert.Equal(
                2,
                libraryXaml.Split(
                    "StringFormat=Remove {0} from Library",
                    StringSplitOptions.None).Length - 1,
                "Both Library trash buttons must retain a specific accessible label.");

            window.Close();
        });
        return Task.CompletedTask;
    }

    private static Task StudioClipCardsUseCompleteHitSurface()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var viewModel = new StudioBrowserHitSurfaceViewModel();
            var view = new StudioBrowserView
            {
                Width = 300,
                Height = 700,
                DataContext = viewModel,
            };
            var window = new Window
            {
                Width = 340,
                Height = 740,
                Content = view,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
            };
            try
            {
                window.Show();
                view.ApplyTemplate();
                view.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(
                    static () => { },
                    DispatcherPriority.ContextIdle);

                ListBox clips = view.FindName("BrowserItems") as ListBox ??
                    throw new InvalidOperationException(
                        "The Studio Browser complete-card selector is missing.");
                var first = clips.ItemContainerGenerator.ContainerFromIndex(0) as
                    ListBoxItem ??
                    throw new InvalidOperationException(
                        "The first Studio clip card was not generated.");
                var second = clips.ItemContainerGenerator.ContainerFromIndex(1) as
                    ListBoxItem ??
                    throw new InvalidOperationException(
                        "The second Studio clip card was not generated.");

                Point nonTitlePoint = second.TranslatePoint(
                    new Point(
                        Math.Max(12d, second.ActualWidth - 18d),
                        Math.Min(92d, second.ActualHeight / 2d)),
                    view);
                DependencyObject hit = VisualTreeHelper.HitTest(
                        view,
                        nonTitlePoint)?.VisualHit ??
                    throw new InvalidOperationException(
                        "The Studio clip body was not hit-testable.");
                TestAssert.True(
                    ReferenceEquals(
                        FindVisualAncestor<ListBoxItem>(hit),
                        second) &&
                    FindVisualAncestor<Button>(hit) is null,
                    "Status, rationale, and card whitespace must hit the selectable clip item rather than requiring the title button.");

                second.IsSelected = true;
                Dispatcher.CurrentDispatcher.Invoke(
                    static () => { },
                    DispatcherPriority.DataBind);
                TestAssert.Equal(
                    "clip-b",
                    viewModel.LastSelectedAssetId,
                    "Selecting the complete card surface must open that clip for preview and editing.");

                Button exclude = EnumerateVisualDescendants<Button>(first)
                    .Single(button =>
                        string.Equals(
                            button.Content as string,
                            "Exclude clip",
                            StringComparison.Ordinal));
                ICommand excludeCommand = exclude.Command ??
                    throw new InvalidOperationException(
                        "The nested Exclude command binding was not resolved.");
                TestAssert.True(
                    string.Equals(
                        exclude.CommandParameter as string,
                        "clip-a",
                        StringComparison.Ordinal),
                    "The nested Exclude surface must retain its own asset-scoped command binding.");
                excludeCommand.Execute(exclude.CommandParameter);
                TestAssert.Equal(
                    "clip-a",
                    viewModel.LastExcludedAssetId,
                    "The nested Exclude action must execute its own command.");
                TestAssert.Equal(
                    "clip-b",
                    viewModel.LastSelectedAssetId,
                    "A nested card action must not replace the clip selection command.");
                TestAssert.True(
                    exclude.MinHeight >= 40d &&
                    exclude.Foreground is SolidColorBrush excludeForeground &&
                    Application.Current.FindResource("Brush.StatusError") is
                        SolidColorBrush errorBrush &&
                    excludeForeground.Color == errorBrush.Color,
                    "Exclude must keep a 40-DIP target and use the destructive red semantic color.");

                TextBlock excludedState =
                    EnumerateVisualDescendants<TextBlock>(second)
                        .Single(text => string.Equals(
                            text.Text,
                            "EXCLUDED",
                            StringComparison.Ordinal));
                TestAssert.True(
                    excludedState.IsVisible &&
                    excludedState.Foreground is SolidColorBrush stateForeground &&
                    Application.Current.FindResource("Brush.StatusError") is
                        SolidColorBrush stateErrorBrush &&
                    stateForeground.Color == stateErrorBrush.Color,
                    "An excluded clip must retain a visible red state badge instead of looking selected or deleted.");
            }
            finally
            {
                window.Close();
            }
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

            var sectionList = EnumerateVisualDescendants<ListBox>(settings)
                    .SingleOrDefault(list =>
                        System.Windows.Automation.AutomationProperties.GetName(list)
                            .Equals("Settings section selector", StringComparison.Ordinal)) ??
                throw new InvalidOperationException(
                    "The standard Settings navigation list is missing.");
            var sectionViewport =
                settings.FindName("SectionScrollViewport") as
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
                RepositoryLayout.Root,
                "src",
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

    private sealed class StudioBrowserHitSurfaceViewModel :
        INotifyPropertyChanged
    {
        public StudioBrowserHitSurfaceViewModel()
        {
            BrowserPreviewItems =
            [
                new StudioBrowserPreviewItem(
                    "First clip",
                    "18 seconds · source.mp4",
                    "Strong match",
                    "Icon.Media",
                    "clip-a",
                    IsSelected: true,
                    IsIncluded: true,
                    WhyThisClip: "The reaction and spoken point land together."),
                new StudioBrowserPreviewItem(
                    "Second clip",
                    "16 seconds · source.mp4",
                    "Strong match",
                    "Icon.Media",
                    "clip-b",
                    IsSelected: false,
                    IsIncluded: false,
                    WhyThisClip: "The explanation resolves cleanly."),
            ];
            SelectedAsset = new StudioBrowserHitSurfaceAsset("clip-a");
            SelectBrowserAssetCommand = new DelegateCommand<string>(
                SelectAsset);
            QueueBrowserAssetCommand = new DelegateCommand<string>(
                static _ => { });
            RemoveBrowserAssetCommand = new DelegateCommand<string>(
                assetId => LastExcludedAssetId = assetId);
            RestoreBrowserAssetCommand = new DelegateCommand<string>(
                static _ => { });
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public IReadOnlyList<StudioToolItem> ToolSections { get; } = [];

        public StudioToolSection SelectedTool { get; set; }

        public IReadOnlyList<StudioBrowserPreviewItem> BrowserPreviewItems
        {
            get;
        }

        public StudioBrowserHitSurfaceAsset SelectedAsset { get; private set; }

        public ICommand SelectBrowserAssetCommand { get; }

        public ICommand QueueBrowserAssetCommand { get; }

        public ICommand RemoveBrowserAssetCommand { get; }

        public ICommand RestoreBrowserAssetCommand { get; }

        public string? LastSelectedAssetId { get; private set; }

        public string? LastExcludedAssetId { get; private set; }

        private void SelectAsset(string assetId)
        {
            LastSelectedAssetId = assetId;
            SelectedAsset = new StudioBrowserHitSurfaceAsset(assetId);
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(SelectedAsset)));
        }
    }

    private sealed record StudioBrowserHitSurfaceAsset(string Id);

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
                    RepositoryLayout.Root,
                    "src",
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

    private static Task Ui04AvoidsFalseTextScaleState()
    {
        RunOnSta(() =>
        {
            var app = EnsureApplication();
            TestAssert.True(
                app.TryFindResource("Accessibility.TextScale") is null,
                "The app must not publish a hard-coded text scale that no view consumes.");
            TestAssert.True(
                typeof(MainWindow).GetProperty("ReadabilityState") is null,
                "The shell must not expose an unobservable readability state that never reaches XAML.");
        });
        return Task.CompletedTask;
    }

    private static Task HighContrastPaletteReplacesSemanticBrushes()
    {
        RunOnSta(() =>
        {
            var app = EnsureApplication();
            object original = app.Resources["Brush.TextPrimary"];
            using var controller = new HighContrastThemeController(app.Resources);
            controller.Apply(true);
            TestAssert.True(
                ReferenceEquals(SystemColors.WindowTextBrush, app.Resources["Brush.TextPrimary"]),
                "High contrast must replace semantic text colors, not only the title-bar background.");
            controller.Apply(false);
            TestAssert.True(
                ReferenceEquals(original, app.Resources["Brush.TextPrimary"]),
                "Leaving high contrast must restore the brand palette.");
        });
        return Task.CompletedTask;
    }

    private static Task ResponsiveLayoutsAvoidDuplicateFeatureTrees()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var settings = new SettingsView { Width = 1266, Height = 720 };
            var publish = new PublishView { Width = 1266, Height = 720 };
            settings.Measure(new Size(settings.Width, settings.Height));
            settings.Arrange(new Rect(0, 0, settings.Width, settings.Height));
            settings.UpdateLayout();
            publish.Measure(new Size(publish.Width, publish.Height));
            publish.Arrange(new Rect(0, 0, publish.Width, publish.Height));
            publish.UpdateLayout();
            TestAssert.Equal(
                1,
                EnumerateVisualDescendants<SettingsSectionHostView>(settings).Count(),
                "Settings must not construct every settings section twice for responsive layout.");
            TestAssert.Equal(
                1,
                EnumerateVisualDescendants<PublishLibraryBrowserView>(publish).Count(),
                "Publish must keep one Library browser while changing its grid placement.");
            TestAssert.Equal(
                1,
                EnumerateVisualDescendants<PublishCalendarView>(publish).Count(),
                "Publish must keep one calendar while changing its grid placement.");
        });
        return Task.CompletedTask;
    }

    private static Task HiddenMomentsReflowsAndContainsFocus()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var view = new StudioHiddenMomentsView { Width = 820, Height = 700 };
            view.Measure(new Size(820, 700));
            view.Arrange(new Rect(0, 0, 820, 700));
            view.UpdateLayout();
            Border detail = (Border)view.FindName("DetailPanel");
            TestAssert.Equal(
                1,
                Grid.GetRow(detail),
                "Hidden Moments details must stack below preview at compact width.");
            TestAssert.Equal(
                KeyboardNavigationMode.Cycle,
                KeyboardNavigation.GetTabNavigation(view),
                "Modal keyboard navigation must remain inside Hidden Moments.");
            KeyBinding[] bindings = view.InputBindings
                .OfType<KeyBinding>()
                .ToArray();
            TestAssert.True(
                bindings.Any(binding =>
                    binding.Key == Key.PageUp &&
                    binding.Modifiers == ModifierKeys.Control) &&
                bindings.Any(binding =>
                    binding.Key == Key.PageDown &&
                    binding.Modifiers == ModifierKeys.Control),
                "Hidden Moments must expose non-conflicting keyboard browsing shortcuts.");
            Button[] navigationButtons =
                EnumerateVisualDescendants<Button>(view)
                    .Where(button =>
                        System.Windows.Automation.AutomationProperties
                            .GetName(button) is
                            "Previous hidden moment" or
                            "Next hidden moment")
                    .ToArray();
            TestAssert.Equal(
                2,
                navigationButtons.Length,
                "Neutral moment navigation must remain visible and named for assistive technology.");

            using var standardPreview = new StudioPreviewViewModel(
                mediaService: null);
            var standardPreviewView = new StudioPreviewView
            {
                Width = 700,
                Height = 620,
                DataContext = standardPreview,
            };
            standardPreviewView.Measure(new Size(700, 620));
            standardPreviewView.Arrange(new Rect(0, 0, 700, 620));
            standardPreviewView.UpdateLayout();
            TestAssert.Equal(
                Visibility.Visible,
                ((TextBlock)standardPreviewView.FindName(
                    "InlinePreviewGuidance")).Visibility,
                "The regular Studio preview must retain its established inline guidance.");
            TestAssert.Equal(
                Visibility.Collapsed,
                ((TextBlock)standardPreviewView.FindName(
                    "SecondaryPreviewGuidance")).Visibility,
                "The regular Studio preview must not gain an extra guidance row.");

            using var reviewPreview = new StudioPreviewViewModel(
                mediaService: null,
                showCaptionControls: false,
                rangeMode: StudioPreviewRangeMode.ExactSelection);
            var reviewPreviewView = new StudioPreviewView
            {
                Width = 700,
                Height = 620,
                DataContext = reviewPreview,
            };
            reviewPreviewView.Measure(new Size(700, 620));
            reviewPreviewView.Arrange(new Rect(0, 0, 700, 620));
            reviewPreviewView.UpdateLayout();
            TestAssert.Equal(
                Visibility.Collapsed,
                ((TextBlock)reviewPreviewView.FindName(
                    "InlinePreviewGuidance")).Visibility,
                "Hidden Moments must remove helper copy from beside the preview time.");
            TestAssert.Equal(
                Visibility.Visible,
                ((TextBlock)reviewPreviewView.FindName(
                    "SecondaryPreviewGuidance")).Visibility,
                "Hidden Moments must place preview guidance in its quieter secondary row.");
        });
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
            TestAssert.True(app.TryFindResource("Motion.Hover") is Duration, "Reduced motion must continue to use durations consumed by animations.");
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
                ResponsiveLayoutBands shared = ResponsiveLayout.ForWidth(width);
                studio.SetResponsiveWidthForTest(width);
                library.SetResponsiveWidthForTest(width);
                publish.SetResponsiveWidthForTest(width);
                settings.SetResponsiveWidthForTest(width);
                bool compact = width < 1120;
                bool standard = width >= 1120 && width < 1600;
                bool wide = width >= 1600;
                TestAssert.Equal(compact, shared.IsCompact, "Shared compact breakpoint should match.");
                TestAssert.Equal(standard, shared.IsStandard, "Shared standard breakpoint should match.");
                TestAssert.Equal(wide, shared.IsWide, "Shared wide breakpoint should match.");
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
        string largePath = Path.Combine(directory, "large-thumbnail.png");
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

                var source = new WriteableBitmap(
                    120,
                    60,
                    96,
                    96,
                    PixelFormats.Bgra32,
                    null);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));
                using (FileStream output = File.Create(largePath))
                {
                    encoder.Save(output);
                }

                object? downsampled = converter.Convert(
                    largePath,
                    typeof(BitmapSource),
                    parameter: "48",
                    System.Globalization.CultureInfo.InvariantCulture);
                object? cached = converter.Convert(
                    largePath,
                    typeof(BitmapSource),
                    parameter: "48",
                    System.Globalization.CultureInfo.InvariantCulture);
                TestAssert.True(
                    downsampled is BitmapSource preview && preview.PixelWidth == 48 && preview.PixelHeight == 24,
                    "Visible Library thumbnails should decode near their rendered size instead of at source resolution.");
                TestAssert.True(
                    ReferenceEquals(downsampled, cached),
                    "Repeated visible thumbnails should reuse the frozen weak-cache image while it remains alive.");
                TestAssert.True(
                    converter.Convert(
                        "C:\\invalid\0thumbnail.png",
                        typeof(BitmapSource),
                        parameter: "48",
                        System.Globalization.CultureInfo.InvariantCulture) is null,
                    "Malformed or racing thumbnail paths must fail as an empty binding value.");
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static Task CorruptLocalThumbnailIsNonfatal()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"ReplayFoundry-corrupt-thumbnail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "corrupt.png");
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x00]);

        try
        {
            RunOnSta(() =>
            {
                var image = new LocalThumbnailImage
                {
                    SourcePath = path,
                    DecodePixelWidth = 240,
                };
                var window = new Window
                {
                    Width = 320,
                    Height = 240,
                    Content = image,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                };
                Exception? dispatcherFailure = null;
                var frame = new DispatcherFrame();
                DispatcherUnhandledExceptionEventHandler handler = (_, e) =>
                {
                    dispatcherFailure = e.Exception;
                    e.Handled = true;
                    frame.Continue = false;
                };
                Dispatcher dispatcher = window.Dispatcher;
                dispatcher.UnhandledException += handler;
                var timer = new DispatcherTimer(
                    TimeSpan.FromMilliseconds(400),
                    DispatcherPriority.Background,
                    (_, _) => frame.Continue = false,
                    dispatcher);
                try
                {
                    window.Show();
                    timer.Start();
                    Dispatcher.PushFrame(frame);
                    TestAssert.True(dispatcherFailure is null,
                        "A corrupt local thumbnail must remain a bounded empty-image state.");
                    TestAssert.True(image.Source is null,
                        "A corrupt local thumbnail must not publish a partial bitmap.");
                }
                finally
                {
                    timer.Stop();
                    dispatcher.UnhandledException -= handler;
                    window.Close();
                }
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
