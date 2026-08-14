using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;

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
                kineticPrimary.Template.FindName("KineticAura", kineticPrimary) is Border
                {
                    Effect: BlurEffect,
                    IsHitTestVisible: false,
                },
                "Every shared button must expose a non-interactive full-contour aura instead of an underline or detached highlight.");

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

    private static Task KineticCanvasSurfacesReuseSharedStyles()
    {
        string root = FindRepositoryRoot();
        string filter = File.ReadAllText(Path.Combine(
            root,
            "ReplayFoundry.Desktop",
            "Features",
            "Library",
            "Sections",
            "LibraryFilterBarView.xaml"));
        string categoryRail = File.ReadAllText(Path.Combine(
            root,
            "ReplayFoundry.Desktop",
            "Features",
            "Library",
            "Sections",
            "LibraryCategoryRailView.xaml"));
        string content = File.ReadAllText(Path.Combine(
            root,
            "ReplayFoundry.Desktop",
            "Features",
            "Library",
            "Sections",
            "LibraryContentView.xaml"));
        string dock = File.ReadAllText(Path.Combine(
            root,
            "ReplayFoundry.Desktop",
            "Resources",
            "Controls",
            "FloatingDockStyles.xaml"));
        string regions = File.ReadAllText(Path.Combine(
            root,
            "ReplayFoundry.Desktop",
            "Features",
            "Generate",
            "CompositionReview",
            "CompositionRegionEditor.xaml"));
        string publishCalendar = File.ReadAllText(Path.Combine(
            root,
            "ReplayFoundry.Desktop",
            "Features",
            "Publish",
            "Sections",
            "PublishCalendarView.xaml"));
        string publishLibrary = File.ReadAllText(Path.Combine(
            root,
            "ReplayFoundry.Desktop",
            "Features",
            "Publish",
            "Sections",
            "PublishLibraryBrowserView.xaml"));
        string publishQueue = File.ReadAllText(Path.Combine(
            root,
            "ReplayFoundry.Desktop",
            "Features",
            "Publish",
            "Sections",
            "PublishQueueHistoryView.xaml"));
        string studioBrowser = File.ReadAllText(Path.Combine(
            root,
            "ReplayFoundry.Desktop",
            "Features",
            "Studio",
            "Browser",
            "StudioBrowserView.xaml"));
        string studioPreview = File.ReadAllText(Path.Combine(
            root,
            "ReplayFoundry.Desktop",
            "Features",
            "Studio",
            "Preview",
            "StudioPreviewView.xaml"));
        string studioInspector = File.ReadAllText(Path.Combine(
            root,
            "ReplayFoundry.Desktop",
            "Features",
            "Studio",
            "Inspector",
            "StudioInspectorView.xaml"));
        string settings = File.ReadAllText(Path.Combine(
            root,
            "ReplayFoundry.Desktop",
            "Features",
            "Settings",
            "SettingsView.xaml"));
        string privacySettings = File.ReadAllText(Path.Combine(
            root,
            "ReplayFoundry.Desktop",
            "Features",
            "Settings",
            "Sections",
            "PrivacyDiagnosticsSettingsView.xaml"));
        string storageSettings = File.ReadAllText(Path.Combine(
            root,
            "ReplayFoundry.Desktop",
            "Features",
            "Settings",
            "Sections",
            "StorageSettingsView.xaml"));
        string aiSettings = File.ReadAllText(Path.Combine(
            root,
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
            content.Contains("Control.GhostButton", StringComparison.Ordinal),
            "Library media and secondary actions should reuse the shared kinetic-card and ghost-button language.");
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
            publishCalendar.Contains("Property=\"BorderBrush\"", StringComparison.Ordinal) &&
            publishCalendar.Contains("Brush.KineticGlowSoft", StringComparison.Ordinal) &&
            !publishCalendar.Contains("Height=\"1.5\"", StringComparison.Ordinal),
            "Publish scheduling should show selected days as complete bounded cells without partial cyan corner marks.");
        TestAssert.True(
            publishLibrary.Contains("Control.InlineSearchTextBox", StringComparison.Ordinal) &&
            publishLibrary.Contains("Control.InlineSelectorComboBox", StringComparison.Ordinal) &&
            publishLibrary.Contains("Control.GhostButton", StringComparison.Ordinal),
            "Publish Library filtering should reuse the same editorial search, selector, and ghost-action language.");
        TestAssert.True(
            studioBrowser.Contains("Control.CanvasPane", StringComparison.Ordinal) &&
            studioBrowser.Contains("Control.CanvasRailListBoxItem", StringComparison.Ordinal) &&
            studioBrowser.Contains("Control.KineticMediaCard", StringComparison.Ordinal),
            "Studio Browser should use the shared tonal pane, rail selection, and kinetic media-card language.");
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
            aiSettings.Contains("Reroll engine", StringComparison.Ordinal) &&
            aiSettings.Contains("Use Qwen for Studio and Publish rerolls", StringComparison.Ordinal) &&
            aiSettings.Contains("Control.DestructiveButton", StringComparison.Ordinal),
            "Local AI settings should explain the reroll scope and render model removal as a destructive action.");

        return Task.CompletedTask;
    }
}
