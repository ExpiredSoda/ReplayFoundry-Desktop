using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ReplayFoundry.Desktop.Features.Publish;
using ReplayFoundry.Desktop.Features.Publish.YouTube;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    private static Task SharedTextStylesResolveAndPublishHistoryLabelsStayReadable()
    {
        RunOnSta(() =>
        {
            Application app = EnsureApplication();
            string sourceRoot = Path.Combine(
                RepositoryLayout.Root,
                "src",
                "ReplayFoundry.Desktop");
            string[] referencedTextStyles = Directory
                .EnumerateFiles(sourceRoot, "*.xaml", SearchOption.AllDirectories)
                .SelectMany(path => Regex.Matches(
                    File.ReadAllText(path),
                    "Style=\"\\{(?:Dynamic|Static)Resource (Text\\.[A-Za-z0-9_.-]+)\\}\""))
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
            string[] missingTextStyles = referencedTextStyles
                .Where(key => app.TryFindResource(key) is not Style)
                .ToArray();
            TestAssert.Equal(
                0,
                missingTextStyles.Length,
                $"Every shared Text.* style referenced by XAML must resolve. Missing: {string.Join(", ", missingTextStyles)}");

            string[] unthemedWindows = Directory
                .EnumerateFiles(sourceRoot, "*.xaml", SearchOption.AllDirectories)
                .Where(path => File.ReadAllText(path)
                    .TrimStart()
                    .StartsWith("<Window", StringComparison.Ordinal))
                .Where(path => !File.ReadAllText(path).Contains(
                    "Style=\"{DynamicResource Control.ThemedWindow}\"",
                    StringComparison.Ordinal))
                .Select(path => Path.GetFileName(path) ?? path)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            TestAssert.Equal(
                0,
                unthemedWindows.Length,
                $"Every derived app window must opt into the shared foreground style. Missing: {string.Join(", ", unthemedWindows)}");

            string[] literalTextColors = Directory
                .EnumerateFiles(sourceRoot, "*.xaml", SearchOption.AllDirectories)
                .SelectMany(path => Regex.Matches(
                    File.ReadAllText(path),
                    "Foreground=\"(?:White|Black|#[A-Fa-f0-9]{3,8})\""))
                .Select(match => match.Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            TestAssert.Equal(
                0,
                literalTextColors.Length,
                $"Text foregrounds must use semantic theme brushes. Literal values: {string.Join(", ", literalTextColors)}");

            DateTimeOffset now = new(
                2026,
                9,
                1,
                16,
                0,
                0,
                TimeSpan.Zero);
            var historyViewModel = new PublishHistoryViewModel(
                utcNow: () => now,
                timeZone: TimeZoneInfo.Utc);
            historyViewModel.Refresh(
            [
                new YouTubePublishHistoryEntry(
                    "history-contrast",
                    "asset-contrast",
                    "A readable history title",
                    "video-contrast",
                    "https://youtu.be/video-contrast",
                    YouTubePublishOutcome.Published,
                    YouTubeVideoVisibility.Public,
                    now,
                    scheduledForUtc: null),
            ]);
            var history = new PublishHistoryWindow(historyViewModel)
            {
                Width = 1120,
                Height = 780,
            };
            FrameworkElement historyContent = history.Content as
                FrameworkElement ??
                throw new InvalidOperationException(
                    "The YouTube history window is missing its content root.");
            historyContent.Measure(new Size(history.Width, history.Height));
            historyContent.Arrange(new Rect(
                0,
                0,
                history.Width,
                history.Height));
            historyContent.UpdateLayout();

            var primaryTextBrush = (SolidColorBrush)app.FindResource(
                "Brush.TextPrimary");
            TestAssert.True(
                history.Foreground is SolidColorBrush windowForeground &&
                windowForeground.Color == primaryTextBrush.Color,
                "A derived Replay Foundry window must inherit the shared primary foreground default.");
            var unstyledText = new TextBlock
            {
                Text = "Window foreground probe",
            };
            var primaryAction = new Button
            {
                Content = "Control foreground probe",
                Style = (Style)app.FindResource("Control.PrimaryButton"),
            };
            if (historyContent is not Grid contentGrid)
            {
                throw new InvalidOperationException(
                    "The YouTube history window content root must remain a Grid.");
            }
            contentGrid.Children.Add(unstyledText);
            contentGrid.Children.Add(primaryAction);
            TestAssert.True(
                unstyledText.Foreground is SolidColorBrush inheritedForeground &&
                inheritedForeground.Color == primaryTextBrush.Color,
                "An unstyled standalone TextBlock must inherit the app window's primary theme foreground.");
            var accentTextBrush = (SolidColorBrush)app.FindResource(
                "Brush.TextOnAccent");
            TestAssert.True(
                primaryAction.Foreground is SolidColorBrush actionForeground &&
                actionForeground.Color == accentTextBrush.Color,
                "The shared window foreground must not override a control-specific foreground.");

            string[] filterLabelNames =
            [
                "SEARCH TITLES",
                "STATUS",
                "FROM",
                "TO",
                "ORDER",
            ];
            TextBlock[] filterLabels = EnumerateVisualDescendants<TextBlock>(
                    historyContent)
                .Where(label => filterLabelNames.Contains(
                    label.Text,
                    StringComparer.Ordinal))
                .ToArray();
            TestAssert.Equal(
                filterLabelNames.Length,
                filterLabels.Length,
                "The rendered YouTube history filter bar must expose every field label.");

            Style formLabelStyle = (Style)app.FindResource("Text.FormLabel");
            var textBrush = (SolidColorBrush)app.FindResource(
                "Brush.TextSecondary");
            var surfaceBrush = (SolidColorBrush)app.FindResource(
                "Brush.SurfacePanel");
            var elevatedSurfaceBrush = (SolidColorBrush)app.FindResource(
                "Brush.SurfaceElevated");
            var insetSurfaceBrush = (SolidColorBrush)app.FindResource(
                "Brush.SurfaceInset");
            var mutedTextBrush = (SolidColorBrush)app.FindResource(
                "Brush.TextMuted");
            var selectedSurfaceBrush = (SolidColorBrush)app.FindResource(
                "Brush.InteractiveSelected");
            foreach (TextBlock label in filterLabels)
            {
                TestAssert.True(
                    ReferenceEquals(formLabelStyle, label.Style),
                    $"{label.Text} must use the shared form-label style instead of an unresolved local style key.");
                TestAssert.True(
                    label.Foreground is SolidColorBrush foreground &&
                    foreground.Color == textBrush.Color,
                    $"{label.Text} must render with the shared secondary text color rather than inherited black.");
            }
            TestAssert.True(
                ContrastRatio(textBrush.Color, surfaceBrush.Color) >= 4.5,
                "Shared form labels must keep readable contrast on the shared section-card surface.");
            foreach (SolidColorBrush surface in new[]
                     {
                         surfaceBrush,
                         elevatedSurfaceBrush,
                         insetSurfaceBrush,
                     })
            {
                TestAssert.True(
                    ContrastRatio(mutedTextBrush.Color, surface.Color) >= 4.5,
                    "Muted text must remain readable on every shared dark content surface.");
            }

            ListBox historyList = EnumerateVisualDescendants<ListBox>(
                    historyContent)
                .Single(list => System.Windows.Automation.AutomationProperties
                    .GetName(list)
                    .Equals(
                        "Filtered YouTube history",
                        StringComparison.Ordinal));
            historyList.UnselectAll();
            historyContent.UpdateLayout();
            TextBlock rowSummary = EnumerateVisualDescendants<TextBlock>(
                    historyList)
                .Single(text => System.Windows.Automation.AutomationProperties
                    .GetName(text)
                    .Equals(
                        "YouTube history record summary",
                        StringComparison.Ordinal));
            TestAssert.True(
                rowSummary.Foreground is SolidColorBrush unselectedSummary &&
                unselectedSummary.Color == mutedTextBrush.Color,
                "An unselected history summary must retain the shared muted metadata color.");
            historyList.SelectedIndex = 0;
            historyContent.UpdateLayout();
            TestAssert.True(
                rowSummary.Foreground is SolidColorBrush selectedSummary &&
                selectedSummary.Color == textBrush.Color,
                "Selected-row metadata must promote to the shared secondary text color.");
            TestAssert.True(
                ContrastRatio(textBrush.Color, selectedSurfaceBrush.Color) >=
                4.5,
                "Selected-row metadata must remain readable on the shared selected surface.");
        });
        return Task.CompletedTask;
    }

    private static double ContrastRatio(Color first, Color second)
    {
        static double Luminance(Color color)
        {
            static double Linearize(byte channel)
            {
                double value = channel / 255d;
                return value <= 0.04045
                    ? value / 12.92
                    : Math.Pow((value + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Linearize(color.R) +
                   0.7152 * Linearize(color.G) +
                   0.0722 * Linearize(color.B);
        }

        double lighter = Math.Max(Luminance(first), Luminance(second));
        double darker = Math.Min(Luminance(first), Luminance(second));
        return (lighter + 0.05) / (darker + 0.05);
    }
}
