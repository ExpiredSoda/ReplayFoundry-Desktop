using System.Diagnostics;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Library.Sections;
using ReplayFoundry.Desktop.Features.Publish.Sections;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Preview;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    private static Task PreviewTimersStayIdleUntilPlayback()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            foreach (object view in new object[] { new LibraryDetailsView(), new StudioPreviewView() })
            {
                var timer = (DispatcherTimer)view.GetType().GetField("_positionTimer",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                TestAssert.False(timer.IsEnabled, "Unloaded preview variants must not run timers or retain themselves through the dispatcher.");
            }
        });
        return Task.CompletedTask;
    }

    private static Task NestedScrollViewersHandOffAtEdges()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var content = new Border { Height = 60, Background = Brushes.Navy };
            var inner = new ScrollViewer { Height = 80, Content = content };
            var stack = new StackPanel(); stack.Children.Add(inner); stack.Children.Add(new Border { Height = 800 });
            var outer = new ScrollViewer { Width = 400, Height = 200, Content = stack };
            outer.Measure(new Size(400, 200)); outer.Arrange(new Rect(0, 0, 400, 200)); outer.UpdateLayout();
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
                { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            content.RaiseEvent(wheel);
            outer.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            TestAssert.True(wheel.Handled && outer.VerticalOffset > 0, "A short embedded gallery must scroll the surrounding inspector.");
            outer.ScrollToTop(); content.Height = 600; outer.UpdateLayout();
            var inside = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
                { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            content.RaiseEvent(inside);
            TestAssert.False(inside.Handled, "An embedded list with remaining content must retain its normal scrolling.");
            TestAssert.Equal(0d, outer.VerticalOffset, "Do not steal the wheel from a scrollable child.");
            inner.ScrollToEnd(); outer.UpdateLayout();
            var atEnd = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
                { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            content.RaiseEvent(atEnd); outer.UpdateLayout();
            TestAssert.True(atEnd.Handled && outer.VerticalOffset > 0, "After the inner list ends, continued scrolling must reach the outer panel.");
        });
        return Task.CompletedTask;
    }

    private static Task CaptionSlidersNotifyOnlyChangedControls()
    {
        var editor = new StudioClipEditorViewModel(null);
        var properties = new List<string?>();
        int rangeChanges = 0, effectChanges = 0, previewChanges = 0;
        editor.PropertyChanged += (_, e) => properties.Add(e.PropertyName);
        editor.Effects.PropertyChanged += (_, _) => effectChanges++;
        editor.DraftRangeChanged += (_, _) => rangeChanges++;
        editor.DraftAppearanceChanged += (_, _) => previewChanges++;
        var watch = Stopwatch.StartNew();
        for (int index = 0; index < 120; index++) editor.CaptionShadowDepth = index % 12 + 0.5;
        Console.WriteLine($"      120 shadow updates: {properties.Count} properties, {effectChanges} effect updates, {rangeChanges} range updates, {watch.Elapsed.TotalMilliseconds:0.0} ms model work.");
        TestAssert.Equal(120, previewChanges, "Every slider value must still reach the live preview and autosave listener.");
        TestAssert.Equal(0, rangeChanges, "Appearance changes cannot reset or rebuild timeline state.");
        TestAssert.Equal(0, effectChanges, "Caption editing must not invalidate color-effect controls.");
        TestAssert.True(properties.Count <= 480, "A shadow drag must not broadcast all caption, font, range, and color properties.");
        TestAssert.Equal(11.5, editor.DraftAppearance.CaptionTypography.ShadowDepth, "The last drag value must survive coalescing.");
        properties.Clear();
        editor.CaptionShadowDepth = 11.5;
        TestAssert.Equal(0, properties.Count, "Repeating the same value must be a no-op.");
        editor.SelectedCaptionStyle = editor.CaptionStyleOptions.Single(o => o.Value == GenerationCaptionStylePreset.Pop);
        TestAssert.True(properties.Contains(nameof(editor.IsPopCaptionPhraseSizeLocked)), "Changing the caption effect must still update dependent controls.");
        TestAssert.Equal(0, rangeChanges, "A style change does not change the cut.");
        editor.SelectedVideoEffect = editor.VideoEffectOptions.Single(o => o.Value == StudioVideoEffectPreset.Noir);
        TestAssert.True(editor.Effects.HasEffect && effectChanges > 0, "Actual color-effect changes must still refresh effect state.");
        return Task.CompletedTask;
    }

    private static Task BoundCaptionControlsPreserveDependentState()
    {
        RunOnSta(() =>
        {
            var app = EnsureApplication();
            var editor = new StudioClipEditorViewModel(null);
            var checkbox = new CheckBox { DataContext = editor, Style = (Style)app.FindResource("Control.ThemedCheckBox") };
            checkbox.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(editor.CaptionBold)) { Mode = BindingMode.TwoWay });
            checkbox.ApplyTemplate();
            var toggle = (IToggleProvider)new CheckBoxAutomationPeer(checkbox).GetPattern(PatternInterface.Toggle)!;
            for (int index = 0; index < 50; index++)
            {
                toggle.Toggle();
                TestAssert.Equal(index % 2 != 0, editor.CaptionBold, "Each accessible checkbox click must update the draft immediately.");
            }
            var slider = new Slider { Minimum = 0, Maximum = 12, DataContext = editor };
            slider.SetBinding(RangeBase.ValueProperty, new Binding(nameof(editor.CaptionShadowDepth)) { Mode = BindingMode.TwoWay });
            for (int index = 0; index < 120; index++) slider.Value = index / 10d;
            TestAssert.Equal(11.9, editor.CaptionShadowDepth, "The real two-way slider binding must retain the last value.");
            var changed = new List<string?>();
            editor.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            editor.CaptionFontFamily = "Arial";
            TestAssert.True(changed.Contains(nameof(editor.CaptionFontFamilies)) && changed.Contains(nameof(editor.CaptionFontWarning)), "A font change must refresh fallback choices and warnings.");
            changed.Clear();
            editor.UseCustomCaptionSafeArea = true;
            editor.CaptionSafeLeftPercent = 9;
            TestAssert.True(changed.Contains(nameof(editor.UseCustomCaptionSafeArea)) && changed.Contains(nameof(editor.CaptionSafeLeftPercent)), "Custom safe-area toggles and margins must remain bound.");
            editor.UseCustomCaptionSafeArea = false;
            TestAssert.Equal(0d, editor.CaptionSafeLeftPercent, "Disabling custom margins restores the platform preset.");
        });
        return Task.CompletedTask;
    }

    private static Task PublishDragMovesBoundedCachedCard()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var surface = new Border { Width = 1200, Height = 800 };
            surface.Measure(new Size(1200, 800)); surface.Arrange(new Rect(0, 0, 1200, 800));
            var source = new Border { Width = 300, Height = 90, Background = Brushes.Navy, Child = new TextBlock { Text = "A readable drag preview" } };
            source.Measure(new Size(300, 90)); source.Arrange(new Rect(0, 0, 300, 90));
            var ghost = new PublishLibraryBrowserView.DragPreviewAdorner(surface, source);
            ghost.Measure(surface.RenderSize); ghost.Arrange(new Rect(surface.RenderSize));
            var card = (Border)VisualTreeHelper.GetChild(ghost, 0);
            var snapshot = ((ImageBrush)card.Background).ImageSource;
            TestAssert.True(snapshot.IsFrozen && card.CacheMode is BitmapCache, "Dragging must reuse an immutable snapshot.");
            TestAssert.True(ghost.Effect is null && card.Effect is not null && card.ActualWidth <= 340 && card.ActualHeight <= 112,
                "The shadow belongs to the small card, never the entire planner surface.");
            for (int index = 0; index < 1000; index++) ghost.UpdatePosition(new Point(index, index / 2d));
            TestAssert.True(ReferenceEquals(snapshot, ((ImageBrush)card.Background).ImageSource), "Pointer movement must not recapture or replace the card.");
            ghost.UpdatePosition(new Point(4000, -100));
            var translation = (TranslateTransform)card.RenderTransform;
            TestAssert.Equal(900d, translation.X, "The card must stay within the planner's right edge.");
            TestAssert.Equal(0d, translation.Y, "The card must stay within the planner's top edge.");
            TestAssert.False(ghost.IsHitTestVisible, "The preview must never intercept a drop target.");
        });
        return Task.CompletedTask;
    }

    private static Task LibraryScrubCompletesWhenSliderConsumesRelease()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var path = Path.GetTempFileName(); File.WriteAllBytes(path, [0, 1, 2, 3]);
            var store = new InMemoryLibraryCatalogStore();
            store.Replace([new LibraryMediaAsset("gesture", "project", GenerationMode.IndividualClips, 1,
                path, null, TimeSpan.FromSeconds(30), 1080, 1920, "Gesture test", "", [], DateTimeOffset.UtcNow)]);
            using var catalog = new GenerationLibraryCatalog(new GenerationOutputSession(), store);
            using var model = new LibraryViewModel(catalog);
            var view = new LibraryDetailsView { DataContext = model };
            var window = new Window { Content = view, Width = 600, Height = 800, ShowInTaskbar = false };
            try
            {
                window.Show();
                var slider = (Slider)view.FindName("PreviewPositionSlider");
                model.Playback.PlayPauseCommand.Execute(null);
                slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseDownEvent });
                TestAssert.False(model.Playback.IsPlaying, "Dragging pauses active playback.");
                slider.Value = 12.5;
                slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseUpEvent, Handled = true });
                TestAssert.True(model.Playback.IsPlaying, "A handled release must still resume playback after the seek.");
                TestAssert.Equal(12.5, model.Playback.PositionSeconds, "The scrubbed position must be retained.");
                model.Playback.PlayPauseCommand.Execute(null);
                TestAssert.False(model.Playback.IsPlaying, "The pause button must still stop transport after dragging.");
            }
            finally { window.Close(); File.Delete(path); }
        });
        return Task.CompletedTask;
    }
}
