using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ReplayFoundry.Desktop.Features.Generate;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Presentation.Controls;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (T descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }
    private static GenerationOutputProject ManualWorkspaceProject(string path, ReplayFoundry.Desktop.Media.Inspection.MediaRational? frameRate = null)
    {
        var media = TestMediaFactory.Create(path, TimeSpan.FromMinutes(20), frameRate: frameRate);
        var asset = new GenerationOutputAsset("suggested", 1, media, null, TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(40), 80, 70, GenerationCandidateSelectionReason.QualityQualified, "Fixture candidate.");
        return new GenerationOutputProject("manual-workspace", GenerationMode.IndividualClips,
            Path.GetTempPath(), 1, ClipFulfillmentPreference.QualityFirst,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget, [asset], DateTimeOffset.UtcNow, sourceMedia: [media]);
    }
    private static Task ManualTimelineKeepsSelection()
    {
        RunOnSta(() =>
        {
            string path = Path.GetTempFileName();
            try
            {
                var session = new GenerationOutputSession();
                session.Publish(ManualWorkspaceProject(path));
                var media = new ManualWorkspacePreview(path);
                using var model = new StudioManualClipViewModel(session, media, () => true);
                model.Bind(session.Current);
                model.SourcePositionSeconds = 300;
                TestAssert.Equal(0d, model.SelectionStartSeconds, "Browsing must leave the selected cut intact.");
                TestAssert.Equal(30d, model.SelectionEndSeconds, "Moving the playhead cannot silently choose a different end.");
                model.IsOpen = true;
                TestAssert.Equal(1, media.Calls, "Opening prepares one bounded preview.");
                TestAssert.Equal(300d, model.Preview.PreviewPositionSeconds, "Source time must survive preview initialization.");
                model.SourcePositionSeconds = 312;
                TestAssert.Equal(1, media.Calls, "Seeking within the prepared window must not encode another proxy.");
                TestAssert.Equal("5:12", model.Preview.PreviewTimecode, "The viewer clock must show source time, not the proxy offset.");
                model.MarkStartCommand.Execute(null);
                model.SourcePositionSeconds = 327;
                model.MarkEndCommand.Execute(null);
                TestAssert.Equal(312d, model.SelectionStartSeconds, "I marks a distant moment at the actual playhead.");
                TestAssert.Equal(327d, model.SelectionEndSeconds, "O retains the marked start.");
                model.SelectionEndSeconds = 1000;
                TestAssert.Equal(492d, model.SelectionEndSeconds, "Dragging cannot exceed the three-minute cut limit.");
                model.SelectionEndSeconds = 327;
                model.ViewportStartSeconds = 99999;
                TestAssert.True(model.ViewportStartSeconds + model.ViewportDurationSeconds <= 1200, "The timeline cannot pan past the recording.");
                for (int i = 0; i < 15; i++) model.ZoomInCommand.Execute(null);
                TestAssert.Equal(5d, model.ViewportDurationSeconds, "Zoom has a useful lower bound.");
                model.ShowAllCommand.Execute(null);
                TestAssert.Equal(0d, model.ViewportStartSeconds, "Full recording restores the beginning.");
                TestAssert.Equal(1200d, model.ViewportDurationSeconds, "Full recording reveals the complete source.");
                model.PreviewSelectionCommand.Execute(null);
                TestAssert.Equal(TimeSpan.FromSeconds(15), media.Last!.Duration, "Selection preview prepares exactly the chosen cut.");
                model.AddCommand.Execute(null);
                TestAssert.False(model.IsOpen, "Adding a clip returns to normal Studio.");
                TestAssert.Equal(TimeSpan.FromSeconds(312), session.Current!.Assets.Last().SourceStart, "Add persists the chosen start.");
                TestAssert.Equal(TimeSpan.FromSeconds(327), session.Current.Assets.Last().SourceEnd, "Add persists the chosen end.");
            }
            finally { File.Delete(path); }
        });
        return Task.CompletedTask;
    }
    private static Task ManualWorkspaceReplacesStudio()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            string path = Path.GetTempFileName();
            try
            {
                using var model = new StudioViewModel();
                model.ManualClips.Bind(ManualWorkspaceProject(path));
                var view = new StudioView { DataContext = model };
                view.Measure(new Size(1120, 590)); view.Arrange(new Rect(0, 0, 1120, 590)); view.UpdateLayout();
                model.ManualClips.IsOpen = true;
                view.UpdateLayout();
                var viewport = (WorkspaceScrollViewport)view.FindName("StudioViewport");
                TestAssert.Equal(Visibility.Collapsed, viewport.Visibility, "The regular Studio workspace must be fully replaced.");
                var manual = FindVisualChildren<StudioManualClipView>(view).Single();
                var timeline = FindVisualChildren<StudioManualTimeline>(manual).Single();
                var preview = FindVisualChildren<StudioPreviewView>(manual).Single();
                TestAssert.True(timeline.TranslatePoint(new Point(0, 0), manual).Y > preview.TranslatePoint(new Point(0, preview.ActualHeight), manual).Y,
                    "The real preview stays above the timeline.");
                TestAssert.True(timeline.TranslatePoint(new Point(0, timeline.ActualHeight), manual).Y < 590,
                    "The timeline remains reachable in a compact app window without scrolling.");
                TestAssert.False(preview.ShowPositionSlider, "The timeline replaces the duplicate scrubber.");
                model.ManualClips.ToggleCommand.Execute(null);
                view.UpdateLayout();
                TestAssert.Equal(Visibility.Visible, viewport.Visibility, "Back restores the existing Studio session.");
            }
            finally { File.Delete(path); }
        });
        return Task.CompletedTask;
    }
    private static Task RecentProjectsScrollInPixels()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var view = new GenerateView
            {
                DataContext = new
                {
                    RecentProjects = Enumerable.Range(1, 10).Select(i => new { Title = $"Project {i}", Detail = "4 clips · Today" }).ToArray(),
                    HasRecentProjects = true, RecentProjectStatus = string.Empty,
                },
            };
            view.Measure(new Size(1120, 720)); view.Arrange(new Rect(0, 0, 1120, 720)); view.UpdateLayout();
            FindVisualChildren<Expander>(view).Single().IsExpanded = true;
            var scroll = (ScrollViewer)view.FindName("RecentProjectsScrollViewer");
            foreach (double width in new[] { 1120d, 850d })
            {
                view.Measure(new Size(width, 720)); view.Arrange(new Rect(0, 0, width, 720)); view.UpdateLayout();
                TestAssert.False(scroll.CanContentScroll, "The card collection must scroll in pixels, not as one logical item.");
                TestAssert.True(scroll.ExtentHeight <= 250, "Only four real rows may contribute to the scroll extent.");
                if (width == 1120) TestAssert.True(scroll.ScrollableHeight < 1, "Three rows of ten projects must fit without a scrollbar.");
                else
                {
                    scroll.ScrollToBottom(); view.UpdateLayout();
                    TestAssert.True(scroll.VerticalOffset <= 64, "Narrow layouts should scroll only the one overflowing row.");
                    scroll.ScrollToHome(); scroll.LineDown(); view.UpdateLayout();
                    TestAssert.True(scroll.VerticalOffset > 0 && scroll.VerticalOffset <= 48, "A scroll step cannot jump the entire card group.");
                }
            }
        });
        return Task.CompletedTask;
    }
    private static Task FoundryLoadingMotionLifecycle()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var mark = new FoundryLoadingMark { IsActive = true };
            var window = new Window { Width = 180, Height = 180, Content = mark, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show(); window.UpdateLayout();
                mark.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                TestAssert.Equal(SystemParameters.ClientAreaAnimation, mark.IsMotionRunning, "Running animation respects the Windows motion preference.");
                mark.Visibility = Visibility.Collapsed;
                TestAssert.False(mark.IsMotionRunning, "A hidden generation surface stops all logo clocks.");
                mark.Visibility = Visibility.Visible;
                mark.IsActive = false;
                TestAssert.False(mark.IsMotionRunning, "Completed and cancelled work must stop the animation.");
                mark.IsActive = true;
                window.Content = null;
                mark.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                TestAssert.False(mark.IsMotionRunning, "Unloading must release animation clocks and preference subscriptions.");
            }
            finally { window.Close(); }
        });
        return Task.CompletedTask;
    }
    private sealed class ManualWorkspacePreview(string path) : IStudioPreviewMediaService
    {
        public int Calls { get; private set; }
        public StudioPreviewMediaRequest? Last { get; private set; }
        public Task<StudioPreviewMediaLease> MaterializeAsync(StudioPreviewMediaRequest request, CancellationToken token)
        {
            Calls++; Last = request;
            return Task.FromResult(new StudioPreviewMediaLease(path, request.SourceStart, request.Duration, () => { }));
        }
    }
}
