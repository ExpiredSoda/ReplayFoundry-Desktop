using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public partial class StudioCaptionWordTimingHandles : UserControl
{
    public static readonly DependencyProperty WordProperty = DependencyProperty.Register(nameof(Word), typeof(StudioCaptionWordDraft),
        typeof(StudioCaptionWordTimingHandles), new PropertyMetadata(null, OnWordChanged));
    private StudioCaptionWordDraft? _observed;
    private StudioCaptionWordEdit? _dragBefore;
    public StudioCaptionWordTimingHandles()
    {
        InitializeComponent();
        Loaded += (_, _) => Observe();
        Unloaded += (_, _) => { if (_observed is not null) _observed.PropertyChanged -= WordChanged; _observed = null; };
    }
    public StudioCaptionWordDraft? Word { get => (StudioCaptionWordDraft?)GetValue(WordProperty); set => SetValue(WordProperty, value); }
    private static void OnWordChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) => ((StudioCaptionWordTimingHandles)owner).Observe();
    private void Observe()
    {
        if (!ReferenceEquals(_observed, Word))
        {
            if (_observed is not null) _observed.PropertyChanged -= WordChanged;
            _observed = Word;
            if (_observed is not null) _observed.PropertyChanged += WordChanged;
        }
        UpdateHandles();
    }
    private void WordChanged(object? sender, PropertyChangedEventArgs args) => UpdateHandles();
    private void OnTrackSizeChanged(object sender, SizeChangedEventArgs args) => UpdateHandles();
    private void UpdateHandles()
    {
        if (TrackCanvas is null) return;
        double width = Math.Max(1, TrackCanvas.ActualWidth - 12);
        Track.Width = width;
        bool valid = Word?.CanAdjustTiming == true;
        StartHandle.Visibility = EndHandle.Visibility = SelectedRange.Visibility = valid ? Visibility.Visible : Visibility.Collapsed;
        UnsetHint.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        if (!valid) return;
        var word = Word!;
        double Position(double value) => width * Math.Clamp((value - word.MinimumSeconds) / (word.MaximumSeconds - word.MinimumSeconds), 0, 1);
        double start = Position(word.StartSeconds), end = Position(word.EndSeconds);
        Canvas.SetLeft(StartHandle, start); Canvas.SetLeft(EndHandle, end);
        Canvas.SetLeft(SelectedRange, start + 6); SelectedRange.Width = Math.Max(0, end - start);
        StartHandle.ToolTip = $"Start {word.StartSeconds:0.###} s. Drag or use arrow keys (Shift: 0.1 s).";
        EndHandle.ToolTip = $"End {word.EndSeconds:0.###} s. Drag or use arrow keys (Shift: 0.1 s).";
    }
    private void OnDragStarted(object sender, DragStartedEventArgs args)
    {
        if (Word?.CanAdjustTiming != true) return;
        _dragBefore = Word.Snapshot(); Word.BeginTimingEdit();
    }
    private void OnDragDelta(object sender, DragDeltaEventArgs args)
    {
        if (_dragBefore is null || Word?.CanAdjustTiming != true) return;
        double seconds = args.HorizontalChange / Math.Max(1, TrackCanvas.ActualWidth - 12) * (Word.MaximumSeconds - Word.MinimumSeconds);
        if (ReferenceEquals(sender, StartHandle)) Word.MoveStartBy(seconds); else Word.MoveEndBy(seconds);
    }
    private void OnDragCompleted(object sender, DragCompletedEventArgs args)
    {
        if (_dragBefore is null || Word is null) return;
        if (args.Canceled) Word.RestoreTiming(_dragBefore);
        _dragBefore = null; Word.CompleteTimingEdit();
    }
    private void OnHandleKeyDown(object sender, KeyEventArgs args)
    {
        if (Word?.CanAdjustTiming != true || args.Key is not (Key.Left or Key.Right)) return;
        double delta = (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? .1 : .01) * (args.Key == Key.Left ? -1 : 1);
        Word.BeginTimingEdit();
        if (ReferenceEquals(sender, StartHandle)) Word.MoveStartBy(delta); else Word.MoveEndBy(delta);
        Word.CompleteTimingEdit(); args.Handled = true;
    }
}
