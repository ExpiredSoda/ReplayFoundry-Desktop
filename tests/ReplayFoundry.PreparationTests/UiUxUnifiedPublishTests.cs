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
                model.History.Refresh(Enumerable.Range(0, 80).Select(index => new YouTubePublishHistoryEntry(
                    $"history-{index}", $"clip-{index}",
                    $"An unexpectedly long gameplay title about finding a way through the abandoned town, moment {index}",
                    $"video-{index}", $"https://www.youtube.com/watch?v=video-{index}",
                    YouTubePublishOutcome.Published, YouTubeVideoVisibility.Public,
                    DateTimeOffset.UtcNow.AddMinutes(-index), null)).ToArray());
                var view = new PublishView { DataContext = model };
                foreach (Size size in new[] { new Size(1200, 600), new Size(1266, 720), new Size(1000, 650), new Size(760, 600), new Size(1700, 950) })
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
                    if (size.Width >= 1180 && size.Height >= 650)
                    {
                        var workspaceScroll = EnumerateVisualDescendants<ScrollViewer>(view).First();
                        TestAssert.True(workspaceScroll.ScrollableHeight < 1,
                            $"A desktop workspace should keep activity visible without page scrolling (overflow {workspaceScroll.ScrollableHeight}).");
                    }

                    var activity = EnumerateVisualDescendants<PublishQueueHistoryView>(view).Single();
                    var history = (ListBox)activity.FindName("ActivityItemsList");
                    foreach (bool uploading in new[] { false, true })
                    {
                        activity.DataContext = new
                        {
                            model.History,
                            model.Analytics,
                            model.CancelPublishCommand,
                            IsPublishing = uploading,
                            QueueItems = new[] { new PublishJobItem("A long clip title that needs room beside upload progress and the cancel button", "Uploading", "Uploading the video to YouTube") },
                            OperationTitle = "Uploading to YouTube",
                            OperationDetail = "42 MB of 128 MB uploaded · About one minute remaining",
                            OperationPercentage = 33d,
                            IsOperationIndeterminate = false,
                        };
                        view.UpdateLayout();
                        PumpDispatcher(TimeSpan.FromMilliseconds(30));
                        view.UpdateLayout();
                        TestAssert.Equal(20, history.Items.Count, "The dashboard must retain a useful recent history without loading the entire archive.");
                        TestAssert.True(activity.ActualWidth >= 300 && history.ActualHeight >= (uploading ? 180 : 280),
                            $"History needs a readable viewport even during upload (size {size}, uploading {uploading}, activity width {activity.ActualWidth}, list height {history.ActualHeight}).");
                        var queue = (FrameworkElement)activity.FindName("QueueCard");
                        TestAssert.True(queue.TransformToAncestor(activity).Transform(new Point(0, queue.ActualHeight)).Y <=
                            history.TransformToAncestor(activity).Transform(new Point()).Y,
                            "Expanded upload details must not overlap recent activity.");
                        var historyScroll = EnumerateVisualDescendants<ScrollViewer>(history).First();
                        TestAssert.True(historyScroll.ScrollableHeight > 0 && historyScroll.ScrollableWidth < 1,
                            $"Long activity must scroll vertically without cutting off content horizontally (size {size}, uploading {uploading}, scrollable height {historyScroll.ScrollableHeight}, width {historyScroll.ScrollableWidth}, viewport {history.ActualWidth}x{history.ActualHeight}).");
                        history.SelectedIndex = history.Items.Count - 1;
                        history.ScrollIntoView(history.SelectedItem);
                        view.UpdateLayout();
                        TestAssert.True(history.ItemContainerGenerator.ContainerFromIndex(history.SelectedIndex) is ListBoxItem last &&
                            last.TransformToAncestor(history).Transform(new Point()).Y < history.ActualHeight,
                            "The last recent record must be reachable inside the activity viewport.");
                        ListBoxItem[] activityRows = EnumerateVisualDescendants<ListBoxItem>(history).Take(2).ToArray();
                        TestAssert.True(activityRows.Length == 2 &&
                            activityRows.Sum(row => row.ActualHeight + row.Margin.Top + row.Margin.Bottom) <= history.ActualHeight,
                            $"At least two complete long-title activity records must fit even during upload (size {size}, uploading {uploading}).");
                        TestAssert.True(((FrameworkElement)activity.FindName("ActiveUpload")).Visibility ==
                            (uploading ? Visibility.Visible : Visibility.Collapsed),
                            "Upload progress must appear only while an upload is active.");
                    }
                    activity.ClearValue(FrameworkElement.DataContextProperty);
                }
            });
        }
        finally { Directory.Delete(folder, recursive: true); }
        return Task.CompletedTask;
    }
}
