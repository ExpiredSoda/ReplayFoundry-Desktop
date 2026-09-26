using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace ReplayFoundry.Desktop.Presentation.Controls;

public sealed class AudioSignalWaveform : FrameworkElement
{
    public static readonly DependencyProperty SeekCommandProperty = DependencyProperty.Register(
        nameof(SeekCommand), typeof(ICommand), typeof(AudioSignalWaveform), new PropertyMetadata(null));
    public ICommand? SeekCommand { get => (ICommand?)GetValue(SeekCommandProperty); set => SetValue(SeekCommandProperty, value); }
    public AudioSignalWaveform() { Focusable = true; Cursor = Cursors.Hand; ClipToBounds = true; }
    internal bool CanSeek => Peaks?.Count > 0 && SeekCommand?.CanExecute(Progress) == true;
    internal void SeekTo(double value)
    {
        if (double.IsFinite(value) && CanSeek) SeekCommand!.Execute(Math.Clamp(value, 0, 1));
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!CanSeek) return;
        Focus(); CaptureMouse(); SeekTo(e.GetPosition(this).X / Math.Max(1, ActualWidth)); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured) SeekTo(e.GetPosition(this).X / Math.Max(1, ActualWidth));
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) { ReleaseMouseCapture(); e.Handled = true; }
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!CanSeek) return;
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? .1 : .01;
        double? value = e.Key switch { Key.Left => Progress - step, Key.Right => Progress + step, Key.Home => 0, Key.End => 1, _ => null };
        if (value is double position) { SeekTo(position); e.Handled = true; }
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new WaveformPeer(this);
    private sealed class WaveformPeer(AudioSignalWaveform owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        protected override string GetClassNameCore() => nameof(AudioSignalWaveform);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        public override object? GetPattern(PatternInterface pattern) => pattern == PatternInterface.RangeValue ? this : base.GetPattern(pattern);
        public bool IsReadOnly => !owner.CanSeek;
        public double LargeChange => 10;
        public double SmallChange => 1;
        public double Maximum => 100;
        public double Minimum => 0;
        public double Value => owner.Progress * 100;
        public void SetValue(double value) => owner.SeekTo(value / 100);
    }
    public static readonly DependencyProperty PeaksProperty =
        DependencyProperty.Register(
            nameof(Peaks),
            typeof(IReadOnlyList<double>),
            typeof(AudioSignalWaveform),
            new FrameworkPropertyMetadata(
                Array.Empty<double>(),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(
            nameof(Progress),
            typeof(double),
            typeof(AudioSignalWaveform),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.AffectsRender,
                null,
                static (_, value) => double.IsFinite((double)value) ? Math.Clamp((double)value, 0, 1) : 0d));

    public static readonly DependencyProperty IsPlayingProperty =
        DependencyProperty.Register(
            nameof(IsPlaying),
            typeof(bool),
            typeof(AudioSignalWaveform),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty InactiveBrushProperty =
        RegisterBrush(nameof(InactiveBrush));

    public static readonly DependencyProperty ActiveBrushProperty =
        RegisterBrush(nameof(ActiveBrush));

    public static readonly DependencyProperty PlayheadBrushProperty =
        RegisterBrush(nameof(PlayheadBrush));

    public IReadOnlyList<double> Peaks
    {
        get => (IReadOnlyList<double>)GetValue(PeaksProperty);
        set => SetValue(PeaksProperty, value);
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public Brush InactiveBrush
    {
        get => (Brush)GetValue(InactiveBrushProperty);
        set => SetValue(InactiveBrushProperty, value);
    }

    public Brush ActiveBrush
    {
        get => (Brush)GetValue(ActiveBrushProperty);
        set => SetValue(ActiveBrushProperty, value);
    }

    public Brush PlayheadBrush
    {
        get => (Brush)GetValue(PlayheadBrushProperty);
        set => SetValue(PlayheadBrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        IReadOnlyList<double> peaks = Peaks ?? Array.Empty<double>();
        if (peaks.Count == 0 || ActualWidth <= 1 || ActualHeight <= 1)
        {
            return;
        }

        int count = Math.Min(peaks.Count, Math.Max(1, (int)(ActualWidth / 3)));
        double slot = ActualWidth / count;
        double barWidth = Math.Max(1, slot * 0.62);
        double center = ActualHeight / 2;
        double maximumHalfHeight = Math.Max(1, center - 3);
        double progressX = Progress * ActualWidth;
        // Display relative detail even for a quietly recorded microphone. This
        // changes only drawing, never gain; the floor keeps near-silence subtle.
        double displayScale = Math.Max(.005, peaks.Where(double.IsFinite).DefaultIfEmpty(0).Max());

        drawingContext.DrawLine(
            new Pen(InactiveBrush, 1),
            new Point(0, center),
            new Point(ActualWidth, center));

        for (int index = 0; index < count; index++)
        {
            double peak = 0;
            for (int sample = index * peaks.Count / count; sample < (index + 1) * peaks.Count / count; sample++)
                if (double.IsFinite(peaks[sample])) peak = Math.Max(peak, Math.Clamp(peaks[sample], 0, 1));
            double x = index * slot + (slot - barWidth) / 2;
            double height = Math.Max(2, Math.Pow(Math.Clamp(peak / displayScale, 0, 1), .7) * maximumHalfHeight * 2);
            var bar = new Rect(x, center - height / 2, barWidth, height);
            Brush brush = x + barWidth / 2 <= progressX
                ? ActiveBrush
                : InactiveBrush;
            drawingContext.DrawRoundedRectangle(brush, null, bar, 1.5, 1.5);
        }

        double boundedX = Math.Clamp(progressX, 1, Math.Max(1, ActualWidth - 1));
        Color accent = (ActiveBrush as SolidColorBrush)?.Color ?? Colors.Transparent;
        var halo = new RadialGradientBrush(
            Color.FromArgb(82, accent.R, accent.G, accent.B),
            Color.FromArgb(0, accent.R, accent.G, accent.B));
        halo.Freeze();
        drawingContext.DrawEllipse(
            halo,
            null,
            new Point(boundedX, center),
            IsPlaying ? 13 : 8,
            center);
        drawingContext.DrawLine(
            new Pen(PlayheadBrush, IsPlaying ? 2 : 1),
            new Point(boundedX, 2),
            new Point(boundedX, ActualHeight - 2));
        drawingContext.DrawRoundedRectangle(PlayheadBrush, null, new Rect(boundedX - 3, 1, 6, 5), 1, 1);
        if (IsKeyboardFocused)
            drawingContext.DrawRoundedRectangle(null, new Pen(ActiveBrush, 1), new Rect(.5, .5, ActualWidth - 1, ActualHeight - 1), 4, 4);
    }

    private static DependencyProperty RegisterBrush(string name) =>
        DependencyProperty.Register(
            name,
            typeof(Brush),
            typeof(AudioSignalWaveform),
            new FrameworkPropertyMetadata(
                Brushes.Transparent,
                FrameworkPropertyMetadataOptions.AffectsRender));
}
