using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    private static Task KineticCanvasControlsPreserveSemanticInteraction()
    {
        RunOnSta(() =>
        {
            Application app = EnsureApplication();
            foreach (string key in new[]
                     {
                         "Control.InlineSelectorComboBox",
                         "Control.InlineSearchTextBox",
                         "Control.ThemedButton",
                         "Control.CanvasRailListBoxItem",
                         "Control.PreferenceChoice",
                         "Control.GhostButton",
                         "Control.DestructiveButton",
                         "Control.DestructiveIconButton",
                         "Control.CanvasPane",
                         "Control.CanvasFilterPane",
                         "Control.CanvasInsetCard",
                         "Control.CanvasGhostZone",
                         "Control.KineticMediaCard",
                         "Brush.KineticGlow",
                         "Brush.KineticGlowSoft",
                         "Radius.OrganicPane",
                         "Radius.OrganicPopup",
                         "Motion.Release",
                     })
            {
                TestAssert.True(
                    app.TryFindResource(key) is not null,
                    $"The kinetic-canvas resource {key} must resolve from the shared theme.");
            }

            var comboBox = new ComboBox
            {
                Width = 260,
                Height = 40,
                ItemsSource = new[] { "Balanced", "Thorough" },
                SelectedIndex = 0,
                Style = (Style)app.FindResource("Control.InlineSelectorComboBox"),
            };
            comboBox.ApplyTemplate();
            comboBox.Measure(new Size(260, 40));
            comboBox.Arrange(new Rect(0, 0, 260, 40));
            comboBox.UpdateLayout();

            ToggleButton completeToggle = comboBox.Template.FindName(
                    "DropDownToggle",
                    comboBox) as ToggleButton ??
                throw new InvalidOperationException(
                    "The shared kinetic selector lost its complete-field toggle.");
            TestAssert.True(
                completeToggle.ActualWidth >= 250,
                "The editorial selector must remain clickable across the whole field.");
            TestAssert.True(
                comboBox.Template.FindName("DropDownCaret", comboBox) is System.Windows.Shapes.Path &&
                comboBox.Template.FindName("OpenRail", comboBox) is null,
                "The shared selector should expose its stateful caret without a disconnected partial underline rail.");

            var checkBox = new CheckBox
            {
                Style = (Style)app.FindResource("Control.ThemedCheckBox"),
                Content = "Include captions",
            };
            checkBox.ApplyTemplate();
            TestAssert.True(
                checkBox.Template.FindName("Box", checkBox) is Border &&
                checkBox.Template.FindName("SelectionWash", checkBox) is null,
                "The shared checkbox must keep its bounded native indicator without washing or shadowing the entire label area.");

            var preference = new RadioButton
            {
                Style = (Style)app.FindResource("Control.PreferenceChoice"),
                Content = "Like",
                IsChecked = true,
            };
            preference.ApplyTemplate();
            TestAssert.True(
                preference.Template.FindName("ChoiceSurface", preference) is Border &&
                preference.Template.FindName("StateDot", preference) is System.Windows.Shapes.Ellipse,
                "Studio preference feedback must use a complete pressed/selected choice surface instead of a generic radio circle.");

            var thumb = new Thumb
            {
                Style = (Style)app.FindResource("Control.RangeThumb"),
            };
            thumb.ApplyTemplate();
            TestAssert.True(
                thumb.Width >= 20 && thumb.Height >= 28,
                "The Slider node must retain a practical pointer hit target.");
            TestAssert.True(
                thumb.Template.FindName("ThumbSurface", thumb) is System.Windows.Shapes.Ellipse &&
                thumb.Template.FindName("LaserGuide", thumb) is null &&
                thumb.Template.FindName("ThumbGlow", thumb) is null,
                "The Slider thumb should respond without painting stray guide lines or detached glow shapes.");

            var slider = new Slider
            {
                Width = 300,
                Minimum = 0,
                Maximum = 100,
                Value = 50,
                Style = (Style)app.FindResource(typeof(Slider)),
            };
            slider.ApplyTemplate();
            slider.Measure(new Size(300, 40));
            slider.Arrange(new Rect(0, 0, 300, 40));
            slider.UpdateLayout();
            TestAssert.True(
                slider.FocusVisualStyle is Style,
                "The Slider must expose the shared keyboard-only focus adorner instead of mutating its thumb for mouse focus.");
            Track rangeTrack = slider.Template.FindName(
                    "PART_Track",
                    slider) as Track ??
                throw new InvalidOperationException(
                    "The shared Slider lost its native Track.");
            TestAssert.Equal(
                new Thickness(-13, 0, -13, 0),
                rangeTrack.Thumb.Margin,
                "The Slider must measure the 40-pixel hit target as its painted 14-pixel node without shrinking pointer access.");
            TestAssert.Equal(
                new Thickness(),
                rangeTrack.DecreaseRepeatButton.Margin,
                "The played rail must not use a negative overlap that paints progress at zero.");
            TestAssert.Equal(
                new Thickness(),
                rangeTrack.IncreaseRepeatButton.Margin,
                "The remaining rail must meet the painted node without extending beyond either endpoint.");

            slider.Value = slider.Minimum;
            slider.UpdateLayout();
            FrameworkElement thumbSurface =
                rangeTrack.Thumb.Template.FindName(
                    "ThumbSurface",
                    rangeTrack.Thumb) as FrameworkElement ??
                throw new InvalidOperationException(
                    "The shared Slider lost its painted thumb node.");
            double minimumNodeLeft = thumbSurface
                .TranslatePoint(new Point(), slider)
                .X;
            TestAssert.True(
                rangeTrack.DecreaseRepeatButton.ActualWidth <= 0.01 &&
                Math.Abs(minimumNodeLeft) <= 0.01,
                "A zero-value Slider must have no played rail and its painted node must sit flush with the left edge.");

            slider.Value = slider.Maximum;
            slider.UpdateLayout();
            double maximumNodeRight = thumbSurface
                .TranslatePoint(
                    new Point(thumbSurface.ActualWidth, 0),
                    slider)
                .X;
            TestAssert.True(
                rangeTrack.IncreaseRepeatButton.ActualWidth <= 0.01 &&
                Math.Abs(maximumNodeRight - slider.ActualWidth) <= 0.01,
                "A maximum-value Slider must finish flush with the right edge without a trailing rail segment.");

            foreach (double width in new[] { 96d, 900d })
            {
                var progress = new ProgressBar
                {
                    Width = width,
                    Minimum = 0,
                    Maximum = 100,
                    Value = 37,
                    Style = (Style)app.FindResource("Control.ProgressBar.Featured"),
                };
                var progressContainer = new Grid();
                progressContainer.Children.Add(progress);
                var progressHost = new Window
                {
                    Width = width + 24,
                    Height = 64,
                    Left = -10000,
                    Top = -10000,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    Content = progressContainer,
                };
                bool progressHostClosed = false;
                progressHost.Show();
                try
                {
                    progress.ApplyTemplate();
                    progressHost.UpdateLayout();

                    FrameworkElement progressTrack =
                        progress.Template.FindName(
                            "PART_Track",
                            progress) as FrameworkElement ??
                        throw new InvalidOperationException(
                            "The shared ProgressBar lost its native track.");
                    FrameworkElement progressIndicator =
                        progress.Template.FindName(
                            "PART_Indicator",
                            progress) as FrameworkElement ??
                        throw new InvalidOperationException(
                            "The shared ProgressBar lost its native indicator.");
                    Border indeterminateSignal =
                        progress.Template.FindName(
                            "IndeterminateSignal",
                            progress) as Border ??
                        throw new InvalidOperationException(
                            "The shared ProgressBar lost its indeterminate signal.");

                    AssertDeterminateProgress(37);
                    AssertDeterminateProgress(0);
                    AssertDeterminateProgress(100);

                    progress.IsIndeterminate = true;
                    progress.UpdateLayout();
                    TestAssert.True(
                        progressIndicator.Opacity == 0 &&
                        indeterminateSignal.Opacity == 1 &&
                        ReferenceEquals(
                            progress.Foreground,
                            indeterminateSignal.Background) &&
                        indeterminateSignal.RenderTransform is ScaleTransform
                        {
                            ScaleX: 0.28,
                        },
                        "Indeterminate progress must replace the real fill with one bounded signal painted by the high-contrast-aware semantic brush.");

                    AssertMotionState(
                        indeterminateSignal,
                        "Indeterminate progress must visibly move instead of remaining as a centered pseudo-progress block.");

                    progress.IsIndeterminate = false;
                    progress.Value = 37;
                    progress.UpdateLayout();
                    TestAssert.True(
                        progressIndicator.Opacity == 1 &&
                        indeterminateSignal.Opacity == 0 &&
                        indeterminateSignal.RenderTransformOrigin ==
                            new Point(0.5, 0.5),
                        "Returning to determinate progress must stop the moving signal and restore the real fill.");
                    AssertDeterminateProgress(37);
                    progress.IsIndeterminate = true;
                    progress.UpdateLayout();

                    progress.Visibility = Visibility.Collapsed;
                    PumpDispatcher(TimeSpan.FromMilliseconds(20));
                    TestAssert.Equal(
                        new Point(0.5, 0.5),
                        indeterminateSignal.RenderTransformOrigin,
                        "A collapsed progress bar must stop its offscreen animation.");
                    progress.Visibility = Visibility.Visible;
                    progressHost.UpdateLayout();
                    AssertMotionState(
                        indeterminateSignal,
                        "A revealed progress bar must restart its shared animation.");

                    progressContainer.Visibility = Visibility.Collapsed;
                    PumpDispatcher(TimeSpan.FromMilliseconds(20));
                    TestAssert.Equal(
                        new Point(0.5, 0.5),
                        indeterminateSignal.RenderTransformOrigin,
                        "An indeterminate bar hidden by an ancestor must stop its offscreen animation.");
                    progressContainer.Visibility = Visibility.Visible;
                    progressHost.UpdateLayout();
                    AssertMotionState(
                        indeterminateSignal,
                        "An indeterminate bar revealed by its ancestor must restart its shared animation.");

                    const string replacementTemplateXaml =
                        "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                        "TargetType=\"{x:Type ProgressBar}\">" +
                        "<Grid><Border x:Name=\"IndeterminateSignal\" " +
                        "Background=\"{TemplateBinding Foreground}\" " +
                        "RenderTransformOrigin=\"0.5,0.5\">" +
                        "<Border.RenderTransform><ScaleTransform ScaleX=\"0.28\" />" +
                        "</Border.RenderTransform></Border></Grid></ControlTemplate>";
                    progress.Template = (ControlTemplate)XamlReader.Parse(
                        replacementTemplateXaml);
                    progress.ApplyTemplate();
                    progressHost.UpdateLayout();
                    Border replacementSignal = progress.Template.FindName(
                            "IndeterminateSignal",
                            progress) as Border ??
                        throw new InvalidOperationException(
                            "A replacement ProgressBar template lost its shared signal.");
                    TestAssert.True(
                        !ReferenceEquals(
                            indeterminateSignal,
                            replacementSignal) &&
                        indeterminateSignal.RenderTransformOrigin ==
                            new Point(0.5, 0.5),
                        "Retemplating must stop and release the previous signal.");
                    AssertMotionState(
                        replacementSignal,
                        "A loaded replacement template must receive the shared animation immediately.");

                    progressHost.Close();
                    progressHostClosed = true;
                    PumpDispatcher(TimeSpan.FromMilliseconds(20));
                    TestAssert.Equal(
                        new Point(0.5, 0.5),
                        replacementSignal.RenderTransformOrigin,
                        "Unloading a progress bar must stop its animation clock.");

                    void AssertMotionState(
                        FrameworkElement signal,
                        string movingFailure)
                    {
                        Point firstOrigin = signal.RenderTransformOrigin;
                        PumpDispatcher(TimeSpan.FromMilliseconds(90));
                        Point secondOrigin = signal.RenderTransformOrigin;
                        if (SystemParameters.ClientAreaAnimation)
                        {
                            TestAssert.True(
                                Math.Abs(secondOrigin.X - firstOrigin.X) > 0.001,
                                movingFailure);
                        }
                        else
                        {
                            TestAssert.Equal(
                                new Point(0.5, 0.5),
                                secondOrigin,
                                "Reduced motion must keep one centered static progress signal.");
                        }
                    }

                    void AssertDeterminateProgress(double value)
                    {
                        progress.Value = value;
                        progress.UpdateLayout();
                        double expected =
                            progressTrack.ActualWidth * value / 100;
                        TestAssert.True(
                            Math.Abs(
                                progressIndicator.ActualWidth - expected) <= 1,
                            $"Determinate progress at {value:0}% must size its indicator from the real value at every shared width.");
                    }
                }
                finally
                {
                    if (!progressHostClosed)
                    {
                        progressHost.Close();
                    }
                }
            }

            var disabledPrimary = new Button
            {
                Content = "Unavailable action",
                IsEnabled = false,
                Style = (Style)app.FindResource("Control.PrimaryButton"),
            };
            disabledPrimary.ApplyTemplate();
            TestAssert.True(
                ReferenceEquals(
                    app.FindResource("Brush.SurfaceInset"),
                    disabledPrimary.Background) &&
                ReferenceEquals(
                    app.FindResource("Brush.BorderSubtle"),
                    disabledPrimary.BorderBrush),
                "A disabled primary action must look inactive instead of retaining the live cyan call-to-action surface.");
            TestAssert.True(
                disabledPrimary.Template.FindName(
                    "AccentLeak",
                    disabledPrimary) is null,
                "Buttons must use a complete hover surface without a detached animated underline.");

            var kineticPrimary = new Button
            {
                Content = "Schedule",
                Style = (Style)app.FindResource("Control.PrimaryButton"),
            };
            kineticPrimary.ApplyTemplate();
            TestAssert.True(
                kineticPrimary.Template.FindName("MotionRoot", kineticPrimary) is Grid &&
                kineticPrimary.Template.FindName("HoverScale", kineticPrimary) is ScaleTransform &&
                kineticPrimary.Template.FindName("PressScale", kineticPrimary) is ScaleTransform &&
                kineticPrimary.Template.FindName("HoverTranslate", kineticPrimary) is TranslateTransform &&
                kineticPrimary.Template.FindName("PressTranslate", kineticPrimary) is TranslateTransform,
                "Every shared button must animate its complete silhouette with composable hover and press transforms.");
            TestAssert.True(
                kineticPrimary.Template.FindName("Surface", kineticPrimary) is Border &&
                kineticPrimary.Template.FindName("KineticAura", kineticPrimary) is null &&
                kineticPrimary.Template.FindName("HoverAuraRoot", kineticPrimary) is null &&
                kineticPrimary.Template.FindName("PressAuraGate", kineticPrimary) is null &&
                kineticPrimary.FocusVisualStyle is Style,
                "Every shared button must use one interactive surface and one keyboard-only focus adorner without a second aura rim.");

            var railItem = new ListBoxItem
            {
                Content = "Library",
                IsSelected = true,
                Style = (Style)app.FindResource("Control.CanvasRailListBoxItem"),
            };
            railItem.ApplyTemplate();
            TestAssert.True(
                railItem.Template.FindName("SelectionSurface", railItem) is Border
                {
                    BorderThickness.Left: 0,
                } &&
                railItem.Template.FindName("SelectionRail", railItem) is Border selectionRail &&
                selectionRail.Opacity == 1 &&
                railItem.FocusVisualStyle is Style,
                "Rail selection must use one tonal surface and one state rail while keyboard focus remains a separate keyboard-only cue.");

            var ghost = new Button
            {
                Content = "Turn off",
                Style = (Style)app.FindResource("Control.GhostButton"),
            };
            ghost.ApplyTemplate();
            TestAssert.True(
                ghost.BorderThickness.Left >= 1 &&
                ghost.Background is not null &&
                ghost.BorderBrush is not null,
                "A secondary settings action must render as a bounded button rather than unframed link text.");

            var destructive = new Button
            {
                Content = "Delete local data",
                Style = (Style)app.FindResource("Control.DestructiveButton"),
            };
            destructive.ApplyTemplate();
            TestAssert.True(
                destructive.BorderThickness.Left >= 1 &&
                ReferenceEquals(app.FindResource("Brush.StatusError"), destructive.Foreground) &&
                ReferenceEquals(app.FindResource("Brush.StatusError"), destructive.BorderBrush),
                "Permanent removal actions must use the shared error-colored destructive button treatment.");
        });

        return Task.CompletedTask;
    }

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(
            DispatcherPriority.Background,
            Dispatcher.CurrentDispatcher)
        {
            Interval = duration,
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static Task KineticCanvasSurfacesReuseSharedStyles()
    {
        string root = RepositoryLayout.Root;
        string filter = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ReplayFoundry.Desktop",
            "Features",
            "Library",
            "Sections",
            "LibraryFilterBarView.xaml"));
        string categoryRail = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ReplayFoundry.Desktop",
            "Features",
            "Library",
            "Sections",
            "LibraryCategoryRailView.xaml"));
        string content = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ReplayFoundry.Desktop",
            "Features",
            "Library",
            "Sections",
            "LibraryContentView.xaml"));
        string dock = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ReplayFoundry.Desktop",
            "Resources",
            "Controls",
            "FloatingDockStyles.xaml"));
        string buttons = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ReplayFoundry.Desktop",
            "Resources",
            "Controls",
            "ButtonStyles.xaml"));
        string selection = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ReplayFoundry.Desktop",
            "Resources",
            "Controls",
            "SelectionStyles.xaml"));
        string regions = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ReplayFoundry.Desktop",
            "Features",
            "Generate",
            "CompositionReview",
            "CompositionRegionEditor.xaml"));
        string publishCalendar = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ReplayFoundry.Desktop",
            "Features",
            "Publish",
            "Sections",
            "PublishCalendarView.xaml"));
        string publishLibrary = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ReplayFoundry.Desktop",
            "Features",
            "Publish",
            "Sections",
            "PublishLibraryBrowserView.xaml"));
        string publishQueue = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ReplayFoundry.Desktop",
            "Features",
            "Publish",
            "Sections",
            "PublishQueueHistoryView.xaml"));
        string studioBrowser = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ReplayFoundry.Desktop",
            "Features",
            "Studio",
            "Browser",
            "StudioBrowserView.xaml"));
        string studioPreview = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ReplayFoundry.Desktop",
            "Features",
            "Studio",
            "Preview",
            "StudioPreviewView.xaml"));
        string studioInspector = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ReplayFoundry.Desktop",
            "Features",
            "Studio",
            "Inspector",
            "StudioInspectorView.xaml"));
        string settings = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ReplayFoundry.Desktop",
            "Features",
            "Settings",
            "SettingsView.xaml"));
        string privacySettings = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ReplayFoundry.Desktop",
            "Features",
            "Settings",
            "Sections",
            "PrivacyDiagnosticsSettingsView.xaml"));
        string storageSettings = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ReplayFoundry.Desktop",
            "Features",
            "Settings",
            "Sections",
            "StorageSettingsView.xaml"));
        string aiSettings = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ReplayFoundry.Desktop",
            "Features",
            "Settings",
            "Sections",
            "AiModelsSettingsView.xaml"));

        TestAssert.True(
            filter.Contains("Control.InlineSearchTextBox", StringComparison.Ordinal) &&
            filter.Contains("Control.InlineSelectorComboBox", StringComparison.Ordinal) &&
            filter.Contains("Text=\"·\"", StringComparison.Ordinal),
            "Library filters should read as one borderless editorial row separated by whitespace and dots.");
        TestAssert.True(
            categoryRail.Contains("Control.CanvasRailListBoxItem", StringComparison.Ordinal) &&
            categoryRail.Contains("Control.CanvasPane", StringComparison.Ordinal),
            "Library categories should use the shared cyan rail and tonal canvas pane.");
        TestAssert.True(
            content.Contains("Control.KineticMediaCard", StringComparison.Ordinal) &&
            content.Contains("Control.GhostButton", StringComparison.Ordinal) &&
            content.Contains("x:Key=\"LibraryGridItemContainer\"", StringComparison.Ordinal) &&
            content.Contains("BorderThickness\" Value=\"0\"", StringComparison.Ordinal) &&
            content.Contains("Background=\"{DynamicResource Brush.BorderFocus}\"", StringComparison.Ordinal),
            "Library card mode should keep the item container transparent and show selection through one tonal card and one rail.");
        TestAssert.True(
            !buttons.Contains("KineticAura", StringComparison.Ordinal) &&
            !buttons.Contains("HoverAuraRoot", StringComparison.Ordinal) &&
            buttons.Contains("x:Name=\"Surface\"", StringComparison.Ordinal) &&
            selection.Contains("x:Name=\"SelectionRail\"", StringComparison.Ordinal),
            "The shared visual system must not restore stacked hover auras or full selected-card outlines.");
        TestAssert.True(
            dock.Contains("Brush.KineticGlowSoft", StringComparison.Ordinal) &&
            !dock.Contains("Brush.BrandYellow", StringComparison.Ordinal),
            "The dock active state should use an upward cyan aura instead of a hard yellow underline.");
        TestAssert.True(
            regions.Contains("CompositionReview.CropMark", StringComparison.Ordinal) &&
            !regions.Contains("Value=\"4\"", StringComparison.Ordinal),
            "Layout Review should use precise crop marks and thin outlines instead of thick selected-region boxes.");
        TestAssert.True(
            publishCalendar.Contains("Control.CanvasPane", StringComparison.Ordinal) &&
            publishCalendar.Contains("AncestorType={x:Type ListBoxItem}", StringComparison.Ordinal) &&
            publishCalendar.Contains("Brush.KineticGlowSoft", StringComparison.Ordinal) &&
            !publishCalendar.Contains("Height=\"1.5\"", StringComparison.Ordinal),
            "Publish scheduling should show selected days as a tonal cell without stacking another selection border under keyboard focus.");
        TestAssert.True(
            publishLibrary.Contains("Control.InlineSearchTextBox", StringComparison.Ordinal) &&
            publishLibrary.Contains("Control.InlineSelectorComboBox", StringComparison.Ordinal) &&
            publishLibrary.Contains("Control.GhostButton", StringComparison.Ordinal),
            "Publish Library filtering should reuse the same editorial search, selector, and ghost-action language.");
        TestAssert.True(
            studioBrowser.Contains("Control.CanvasPane", StringComparison.Ordinal) &&
            studioBrowser.Contains("Control.CanvasRailListBoxItem", StringComparison.Ordinal) &&
            studioBrowser.Contains("Control.StudioClipCard", StringComparison.Ordinal) &&
            studioBrowser.Contains("StudioBrowser.ClipItem", StringComparison.Ordinal) &&
            studioBrowser.Contains("SelectedValue=\"{Binding SelectedAsset.Id", StringComparison.Ordinal) &&
            studioBrowser.Contains("SelectionChanged=\"BrowserItems_SelectionChanged\"", StringComparison.Ordinal) &&
            studioBrowser.Contains("Control.StudioSecondaryButton", StringComparison.Ordinal) &&
            studioBrowser.Contains("Control.StudioDestructiveButton", StringComparison.Ordinal) &&
            studioBrowser.Contains("Text=\"EXCLUDED\"", StringComparison.Ordinal) &&
            !studioBrowser.Contains("Control.KineticMediaCard", StringComparison.Ordinal) &&
            !studioBrowser.Contains("Control.IconButton", StringComparison.Ordinal) &&
            !studioBrowser.Contains("Control.StudioCardHitTarget", StringComparison.Ordinal) &&
            studioBrowser.Contains("Margin=\"4,4,11,20\"", StringComparison.Ordinal) &&
            studioBrowser.Contains("ClipToBounds=\"False\"", StringComparison.Ordinal),
            "Studio Browser should make the complete tonal card the keyboard and pointer target while keeping nested secondary and red exclusion actions distinct.");
        TestAssert.True(
            buttons.Contains("x:Key=\"Control.StudioPrimaryButton\"", StringComparison.Ordinal) &&
            buttons.Contains("x:Key=\"Control.StudioSecondaryButton\"", StringComparison.Ordinal) &&
            buttons.Contains("x:Key=\"Control.StudioDestructiveButton\"", StringComparison.Ordinal) &&
            buttons.Contains("x:Key=\"Control.StudioDestructiveIconButton\"", StringComparison.Ordinal),
            "Studio must retain explicit primary, secondary, and destructive button silhouettes without restoring focus rims.");
        TestAssert.True(
            studioPreview.Contains("Control.CanvasPane", StringComparison.Ordinal) &&
            studioPreview.Contains("Control.CanvasGhostZone", StringComparison.Ordinal) &&
            studioPreview.Contains("Grid.Column=\"1\"", StringComparison.Ordinal) &&
            studioPreview.Contains("TextAlignment=\"Center\"", StringComparison.Ordinal) &&
            !studioPreview.Contains("BorderThickness=\"2\"", StringComparison.Ordinal),
            "Studio preview should center its status with the transport while keeping a quiet ghost zone without a harsh video border.");
        TestAssert.True(
            publishQueue.Contains("<Grid.ColumnDefinitions>", StringComparison.Ordinal) &&
            publishQueue.Contains("Grid.Column=\"1\"", StringComparison.Ordinal) &&
            publishQueue.Contains("VerticalAlignment=\"Center\"", StringComparison.Ordinal),
            "Publish queue status must occupy a dedicated centered header column instead of overlapping the heading.");
        TestAssert.True(
            studioInspector.Contains("Control.CanvasPane", StringComparison.Ordinal) &&
            studioInspector.Contains("Control.CanvasInsetCard", StringComparison.Ordinal),
            "Studio Inspector should group editing tools through shared tonal surfaces rather than nested boxes.");
        TestAssert.True(
            settings.Contains("Control.CanvasPane", StringComparison.Ordinal) &&
            settings.Contains("Control.CanvasRailListBoxItem", StringComparison.Ordinal),
            "Settings navigation should reuse the shared negative-space pane and cyan rail selection.");
        TestAssert.True(
            privacySettings.Contains("Control.DestructiveButton", StringComparison.Ordinal) &&
            privacySettings.Contains("Turn off research sharing", StringComparison.Ordinal) &&
            storageSettings.Contains("Control.DestructiveButton", StringComparison.Ordinal) &&
            storageSettings.Contains("Include these additional records", StringComparison.Ordinal) &&
            !storageSettings.Contains("Also delete", StringComparison.Ordinal) &&
            !storageSettings.Contains("Also forget", StringComparison.Ordinal),
            "Settings must distinguish visible turn-off actions from clearly destructive data-removal actions using plain language.");
        TestAssert.True(
            aiSettings.Contains("Title rewriting", StringComparison.Ordinal) &&
            aiSettings.Contains("Use local AI for Studio and Publish rewrites", StringComparison.Ordinal) &&
            aiSettings.Contains("Control.DestructiveButton", StringComparison.Ordinal),
            "Local AI settings should explain title rewriting and present model removal as a destructive action.");

        return Task.CompletedTask;
    }
}
