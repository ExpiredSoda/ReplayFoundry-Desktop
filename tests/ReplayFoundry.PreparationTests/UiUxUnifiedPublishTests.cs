using System.IO;
using System.Windows;
using System.Windows.Controls;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Publish;
using ReplayFoundry.Desktop.Features.Publish.Sections;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Presentation.Workspaces;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    private static Task UnifiedPublishKeepsLongLibrariesBounded()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ReplayFoundry-publish-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            LibraryMediaAsset[] assets = Enumerable.Range(0, 200).Select(index =>
            {
                string path = Path.Combine(folder, $"clip-{index}.mp4");
                File.WriteAllBytes(path, [0]);
                return new LibraryMediaAsset($"clip-{index}", "layout-project", GenerationMode.IndividualClips,
                    index + 1, path, null, TimeSpan.FromSeconds(32), 1080, 1920,
                    $"Finished gameplay moment {index + 1}", string.Empty, [], DateTimeOffset.UtcNow);
            }).ToArray();
            RunOnSta(() =>
            {
                EnsureApplication();
                using var model = new PublishViewModel(new PublishLibraryCatalog(assets), null,
                    new InMemoryYouTubePublishPreferencesStore(), null, WorkspaceSurfaceState.ContentReady,
                    static () => DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
                var view = new PublishView { DataContext = model };
                foreach (Size size in new[] { new Size(1266, 720), new Size(760, 600), new Size(1700, 950) })
                {
                    view.Width = size.Width;
                    view.Height = size.Height;
                    view.Measure(size);
                    view.Arrange(new Rect(size));
                    view.UpdateLayout();
                    PumpDispatcher(TimeSpan.FromMilliseconds(30));
                    view.UpdateLayout();
                    var browser = EnumerateVisualDescendants<PublishLibraryBrowserView>(view).Single();
                    var calendar = EnumerateVisualDescendants<PublishCalendarView>(view).Single();
                    var clips = (ListBox)browser.FindName("LibraryItemsList");
                    TestAssert.Equal(200, clips.Items.Count, "The bounded list must retain all matching clips.");
                    TestAssert.True(clips.ActualHeight is > 100 and < 800, "The clip viewport must remain finite as the library grows.");
                    TestAssert.True(calendar.Visibility == Visibility.Visible && calendar.ActualWidth > 300,
                        "The calendar must remain present at every supported window width.");
                    TestAssert.True(EnumerateVisualDescendants<ListBoxItem>(clips).Count() < 30,
                        "Long libraries must virtualize rows instead of building an entire page of clips.");
                    clips.SelectedIndex = 199;
                    clips.ScrollIntoView(clips.SelectedItem);
                    view.UpdateLayout();
                    TestAssert.Equal(((PublishLibraryItem)clips.Items[199]).Asset, model.SelectedAsset,
                        "Clips beyond the viewport must remain selectable in the library's displayed sort order.");
                    TestAssert.True(clips.ItemContainerGenerator.ContainerFromIndex(199) is ListBoxItem,
                        "Scrolling must reach the last clip without expanding the page.");
                    TestAssert.Equal(42, model.CalendarDays.Count, "Clip scrolling must leave the complete month available.");
                    var days = EnumerateVisualDescendants<ListBox>(calendar).Single();
                    model.TodayCalendarCommand.Execute(null);
                    view.UpdateLayout();
                    PumpDispatcher(TimeSpan.FromMilliseconds(30));
                    TestAssert.True(model.SelectedCalendarDay?.IsToday == true && days.SelectedItem is PublishCalendarDay selected &&
                        selected.Date == model.SelectedCalendarDay.Date,
                        $"Refreshing calendar items must keep today's agenda selected (model {model.SelectedCalendarDay?.Date}, list {days.SelectedValue}, size {size}).");
                    var dayScroll = EnumerateVisualDescendants<ScrollViewer>(days).First();
                    TestAssert.True(dayScroll.ScrollableHeight < 1,
                        "Every week of the month must fit without a hidden inner calendar scrollbar.");
                    if (size.Width >= 960)
                    {
                        var workspaceScroll = EnumerateVisualDescendants<ScrollViewer>(view).First();
                        TestAssert.True(workspaceScroll.ScrollableHeight < 1,
                            $"A desktop workspace should keep activity visible without page scrolling (overflow {workspaceScroll.ScrollableHeight}).");
                    }
                }
            });
        }
        finally { Directory.Delete(folder, recursive: true); }
        return Task.CompletedTask;
    }
}
