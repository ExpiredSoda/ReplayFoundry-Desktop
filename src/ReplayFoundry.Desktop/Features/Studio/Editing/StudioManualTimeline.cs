using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ReplayFoundry.Desktop.Media.Preview;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

/// <summary>A source ruler, real scene thumbnails, trim range, and full-recording overview.</summary>
public sealed class StudioManualTimeline : FrameworkElement
{
    public static readonly DependencyProperty SourceDurationProperty = Number(nameof(SourceDuration), 0);
    public static readonly DependencyProperty PositionProperty = Number(nameof(Position), 0);
    public static readonly DependencyProperty SelectionStartProperty = Number(nameof(SelectionStart), 0);
    public static readonly DependencyProperty SelectionEndProperty = Number(nameof(SelectionEnd), 30);
    public static readonly DependencyProperty ViewportStartProperty = Number(nameof(ViewportStart), 0);
    public static readonly DependencyProperty ViewportDurationProperty = Number(nameof(ViewportDuration), 120);
    public static readonly DependencyProperty ThumbnailsProperty = DependencyProperty.Register(nameof(Thumbnails),
        typeof(IReadOnlyList<StudioTimelineThumbnail>), typeof(StudioManualTimeline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnThumbnailsChanged));
    private readonly Dictionary<VideoPreviewFrame, BitmapSource> _images = new();
    private DragTarget _drag;
    private double _pointerStart;
    private StudioManualRange _initialRange;
    private bool _finishingGesture;
    public event EventHandler? RangeEditStarted;
    public event EventHandler? RangeEditCompleted;
    public event EventHandler? RangeEditCanceled;
    public static readonly DependencyProperty MoveRangeCommandProperty = DependencyProperty.Register(nameof(MoveRangeCommand),
        typeof(ICommand), typeof(StudioManualTimeline));
    public ICommand? MoveRangeCommand { get => (ICommand?)GetValue(MoveRangeCommandProperty); set => SetValue(MoveRangeCommandProperty, value); }
    private const double Inset = 12;
    private double TrackWidth => Math.Max(1, ActualWidth - Inset * 2);
    private double VisibleDuration => Math.Max(0.05, ViewportDuration);
    public double SourceDuration { get => (double)GetValue(SourceDurationProperty); set => SetValue(SourceDurationProperty, value); }
    public double Position { get => (double)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public double SelectionStart { get => (double)GetValue(SelectionStartProperty); set => SetValue(SelectionStartProperty, value); }
    public double SelectionEnd { get => (double)GetValue(SelectionEndProperty); set => SetValue(SelectionEndProperty, value); }
    public double ViewportStart { get => (double)GetValue(ViewportStartProperty); set => SetValue(ViewportStartProperty, value); }
    public double ViewportDuration { get => (double)GetValue(ViewportDurationProperty); set => SetValue(ViewportDurationProperty, value); }
    public IReadOnlyList<StudioTimelineThumbnail>? Thumbnails
    {
        get => (IReadOnlyList<StudioTimelineThumbnail>?)GetValue(ThumbnailsProperty);
        set => SetValue(ThumbnailsProperty, value);
    }
    public StudioManualTimeline() { Focusable = true; ClipToBounds = true; Cursor = Cursors.Arrow; }
    private static DependencyProperty Number(string name, double value) => DependencyProperty.Register(name,
        typeof(double), typeof(StudioManualTimeline), new FrameworkPropertyMetadata(value,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault),
        candidate => candidate is double number && double.IsFinite(number));
    private Brush Brush(string name) => (Brush)FindResource(name);
    private double X(double seconds) => Inset + (seconds - ViewportStart) / VisibleDuration * TrackWidth;
    private double Time(double x) => Math.Clamp(ViewportStart + (x - Inset) / TrackWidth * VisibleDuration, 0, SourceDuration);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRoundedRectangle(Brush("Brush.SurfaceInset"), null, new Rect(0, 0, ActualWidth, ActualHeight), 6, 6);
        if (SourceDuration <= 0 || ActualWidth < 100) return;
        dc.PushClip(new RectangleGeometry(new Rect(Inset, 0, TrackWidth, ActualHeight)));
        DrawRuler(dc);
        dc.DrawRoundedRectangle(Brush("Brush.SurfacePanel"), null, new Rect(Inset, 33, TrackWidth, 46), 4, 4);
        dc.PushOpacity(0.55); DrawThumbnails(dc); dc.Pop();
        double left = Math.Clamp(X(SelectionStart), Inset, ActualWidth - Inset);
        double right = Math.Clamp(X(SelectionEnd), Inset, ActualWidth - Inset);
        if (right > left)
        {
            var selection = new Rect(left, 33, right - left, 46);
            dc.PushClip(new RectangleGeometry(selection)); DrawThumbnails(dc); dc.Pop();
            dc.PushOpacity(0.2); dc.DrawRoundedRectangle(Brush("Brush.BrandCyan"), null, selection, 3, 3); dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(Brush("Brush.BrandCyan"), 1), selection, 3, 3);
            if (right - left > 100)
            {
                dc.DrawRoundedRectangle(Brush("Brush.SurfaceInset"), null, new Rect(left + 8, 49, 78, 21), 3, 3);
                Label(dc, "YOUR CLIP", left + 14, 52, "Brush.BrandCyan", 11);
            }
        }
        if (X(SelectionStart) >= Inset && X(SelectionStart) <= ActualWidth - Inset) DrawHandle(dc, X(SelectionStart), "Start", false);
        if (X(SelectionEnd) >= Inset && X(SelectionEnd) <= ActualWidth - Inset) DrawHandle(dc, X(SelectionEnd), "End", true);
        if (X(Position) >= Inset && X(Position) <= ActualWidth - Inset)
        {
            dc.DrawLine(new Pen(Brush("Brush.TextPrimary"), 1.5), new Point(X(Position), 21), new Point(X(Position), 83));
            dc.DrawRoundedRectangle(Brush("Brush.TextPrimary"), null, new Rect(X(Position) - 4, 21, 8, 7), 1, 1);
        }
        dc.Pop();
        DrawOverview(dc);
        if (IsKeyboardFocusWithin)
            dc.DrawRoundedRectangle(null, new Pen(Brush("Brush.BorderStrong"), 1), new Rect(0.5, 0.5, ActualWidth - 1, ActualHeight - 1), 6, 6);
    }
    private static void OnThumbnailsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var control = (StudioManualTimeline)sender;
        if (control.Thumbnails is not { Count: > 0 }) { control._images.Clear(); return; }
        foreach (StudioTimelineThumbnail thumbnail in control.Thumbnails)
        {
            if (control._images.ContainsKey(thumbnail.Frame)) continue;
            try
            {
                // Tiny in-memory PNGs; no disk access or video decoding on the UI thread.
                using var stream = new MemoryStream(thumbnail.Frame.PngData.ToArray(), writable: false);
                BitmapFrame bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                bitmap.Freeze();
                if (control._images.Count >= 160) control._images.Remove(control._images.Keys.First());
                control._images[thumbnail.Frame] = bitmap;
            }
            catch (Exception exception) when (exception is NotSupportedException or IOException) { }
        }
    }
    private void DrawThumbnails(DrawingContext dc)
    {
        foreach (StudioTimelineThumbnail thumbnail in Thumbnails ?? [])
        {
            if (!_images.TryGetValue(thumbnail.Frame, out BitmapSource? bitmap) ||
                thumbnail.EndSeconds < ViewportStart || thumbnail.StartSeconds > ViewportStart + VisibleDuration) continue;
            double x = X(thumbnail.StartSeconds), width = Math.Max(1, X(thumbnail.EndSeconds) - x);
            var cell = new Rect(x, 33, width, 46);
            double scale = Math.Max(width / bitmap.PixelWidth, 46d / bitmap.PixelHeight);
            var image = new Rect(x + (width - bitmap.PixelWidth * scale) / 2,
                33 + (46 - bitmap.PixelHeight * scale) / 2, bitmap.PixelWidth * scale, bitmap.PixelHeight * scale);
            dc.PushClip(new RectangleGeometry(cell)); dc.DrawImage(bitmap, image); dc.Pop();
            dc.DrawLine(new Pen(Brush("Brush.SurfaceInset"), 1), new Point(x, 33), new Point(x, 79));
        }
    }
    private void DrawRuler(DrawingContext dc)
    {
        double target = VisibleDuration / Math.Max(2, TrackWidth / 90);
        double step = new double[] { 0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600 }
            .FirstOrDefault(value => value >= target, Math.Ceiling(target / 3600) * 3600);
        for (double time = Math.Ceiling(ViewportStart / step) * step; time <= Math.Min(SourceDuration, ViewportStart + VisibleDuration); time += step)
        {
            double x = X(time);
            Label(dc, TimeSpan.FromSeconds(time).ToString(time >= 3600 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture), x + 3, 3);
            dc.DrawLine(new Pen(Brush("Brush.BorderSubtle"), 1), new Point(x, 23), new Point(x, 30));
        }
    }
    private void DrawHandle(DrawingContext dc, double x, string text, bool end)
    {
        dc.DrawRoundedRectangle(Brush("Brush.BrandYellow"), null, new Rect(x - 4, 32, 8, 48), 2, 2);
        dc.DrawLine(new Pen(Brush("Brush.SurfaceInset"), 1), new Point(x, 47), new Point(x, 63));
        if (Math.Abs(X(SelectionEnd) - X(SelectionStart)) >= 70)
            Label(dc, text, end ? x - 24 : x + 6, 81, "Brush.BrandYellow", 10);
        else if (!end) Label(dc, "Your cut", Math.Min(x + 4, ActualWidth - 52), 81, "Brush.BrandYellow", 10);
    }
    private void DrawOverview(DrawingContext dc)
    {
        double scale = TrackWidth / SourceDuration;
        dc.DrawRoundedRectangle(Brush("Brush.SurfacePanel"), null, new Rect(Inset, 105, TrackWidth, 13), 3, 3);
        dc.DrawRectangle(Brush("Brush.BrandCyan"), null,
            new Rect(Inset + Math.Clamp(SelectionStart, 0, SourceDuration) * scale, 109,
                Math.Max(1, (Math.Clamp(SelectionEnd, 0, SourceDuration) - Math.Clamp(SelectionStart, 0, SourceDuration)) * scale), 5));
        double width = Math.Min(TrackWidth, VisibleDuration * scale);
        dc.DrawRoundedRectangle(null, new Pen(Brush("Brush.TextSecondary"), 1),
            new Rect(Inset + ViewportStart * scale, 103, width, 17), 3, 3);
        dc.DrawLine(new Pen(Brush("Brush.TextPrimary"), 2),
            new Point(Inset + Position * scale, 104), new Point(Inset + Position * scale, 119));
    }
    private void Label(DrawingContext dc, string value, double x, double y, string brush = "Brush.TextSecondary", double size = 10)
    {
        var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface((FontFamily)FindResource("Font.Interface"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            size, Brush(brush), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(x, y));
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (SourceDuration <= 0) return;
        Focus();
        Point point = e.GetPosition(this);
        _drag = HitTarget(point);
        _pointerStart = point.X;
        _initialRange = new(SelectionStart, SelectionEnd);
        if (IsRangeDrag) RangeEditStarted?.Invoke(this, EventArgs.Empty);
        CaptureMouse();
        UpdateDrag(point); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured) UpdateDrag(e.GetPosition(this));
        else
        {
            Point p = e.GetPosition(this);
            Cursor = HitTarget(p) switch
            {
                DragTarget.Start or DragTarget.End => Cursors.SizeWE,
                DragTarget.Range => Cursors.SizeAll,
                DragTarget.Overview => Cursors.Hand,
                _ => Cursors.Arrow,
            };
        }
    }
    private bool IsRangeDrag => _drag is DragTarget.Start or DragTarget.End or DragTarget.Range;
    private DragTarget HitTarget(Point p) => p.Y >= 99 ? DragTarget.Overview :
        p.Y >= 30 && p.Y <= 96 && Math.Abs(p.X - X(SelectionStart)) < 10 ? DragTarget.Start :
        p.Y >= 30 && p.Y <= 96 && Math.Abs(p.X - X(SelectionEnd)) < 10 ? DragTarget.End :
        p.Y >= 33 && p.Y <= 79 && p.X > X(SelectionStart) && p.X < X(SelectionEnd) ? DragTarget.Range : DragTarget.Playhead;
    private void UpdateDrag(Point point)
    {
        double time = Time(point.X);
        switch (_drag)
        {
            case DragTarget.Range:
                var range = _initialRange.MoveTo(_initialRange.Start + (point.X - _pointerStart) / TrackWidth * VisibleDuration, SourceDuration);
                if (MoveRangeCommand?.CanExecute(range.Start) == true) MoveRangeCommand.Execute(range.Start);
                else { SetCurrentValue(SelectionStartProperty, range.Start); SetCurrentValue(SelectionEndProperty, range.End); }
                break;
            case DragTarget.Start:
                SetCurrentValue(SelectionStartProperty, Math.Clamp(time, Math.Max(0, SelectionEnd - 180), Math.Max(0, SelectionEnd - 0.05)));
                SetCurrentValue(PositionProperty, SelectionStart);
                break;
            case DragTarget.End:
                SetCurrentValue(SelectionEndProperty, Math.Clamp(time, Math.Min(SourceDuration, SelectionStart + 0.05), Math.Min(SourceDuration, SelectionStart + 180)));
                SetCurrentValue(PositionProperty, SelectionEnd);
                break;
            case DragTarget.Overview:
                double position = Math.Clamp((point.X - Inset) / TrackWidth * SourceDuration, 0, SourceDuration);
                SetCurrentValue(ViewportStartProperty, Math.Clamp(position - VisibleDuration / 2, 0, Math.Max(0, SourceDuration - VisibleDuration)));
                SetCurrentValue(PositionProperty, position);
                break;
            case DragTarget.Playhead: SetCurrentValue(PositionProperty, time); break;
        }
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        FinishGesture(cancel: false); e.Handled = true;
    }
    public void CancelGesture() => FinishGesture(cancel: true);
    private void FinishGesture(bool cancel)
    {
        if (_drag == DragTarget.None || _finishingGesture) return;
        _finishingGesture = true;
        try
        {
            if (IsRangeDrag)
            {
                if (cancel) RangeEditCanceled?.Invoke(this, EventArgs.Empty);
                else RangeEditCompleted?.Invoke(this, EventArgs.Empty);
            }
            _drag = DragTarget.None; ReleaseMouseCapture();
        }
        finally { _finishingGesture = false; }
    }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!_finishingGesture) FinishGesture(cancel: true);
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (SourceDuration <= 0) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            SetCurrentValue(ViewportStartProperty, Math.Clamp(ViewportStart - Math.Sign(e.Delta) * VisibleDuration / 5, 0, Math.Max(0, SourceDuration - VisibleDuration)));
        else
        {
            double ratio = Math.Clamp((e.GetPosition(this).X - Inset) / TrackWidth, 0, 1);
            double anchor = ViewportStart + ratio * VisibleDuration;
            double duration = Math.Clamp(VisibleDuration * (e.Delta > 0 ? 0.8 : 1.25), Math.Min(5, SourceDuration), SourceDuration);
            SetCurrentValue(ViewportDurationProperty, duration);
            SetCurrentValue(ViewportStartProperty, Math.Clamp(anchor - ratio * duration, 0, Math.Max(0, SourceDuration - duration)));
        }
        e.Handled = true;
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
    private enum DragTarget { None, Playhead, Start, End, Range, Overview }
}
