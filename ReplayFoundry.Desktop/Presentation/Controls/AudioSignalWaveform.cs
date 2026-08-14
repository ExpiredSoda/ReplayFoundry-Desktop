using System.Windows;
using System.Windows.Media;

namespace ReplayFoundry.Desktop.Presentation.Controls;

public sealed class AudioSignalWaveform : FrameworkElement
{
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
                static (_, value) => Math.Clamp((double)value, 0, 1)));

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
        IReadOnlyList<double> peaks = Peaks ?? Array.Empty<double>();
        if (peaks.Count == 0 || ActualWidth <= 1 || ActualHeight <= 1)
        {
            return;
        }

        double slot = ActualWidth / peaks.Count;
        double barWidth = Math.Max(1, slot * 0.62);
        double center = ActualHeight / 2;
        double maximumHalfHeight = Math.Max(1, center - 3);
        double progressX = Progress * ActualWidth;

        drawingContext.DrawLine(
            new Pen(InactiveBrush, 1),
            new Point(0, center),
            new Point(ActualWidth, center));

        for (int index = 0; index < peaks.Count; index++)
        {
            double x = index * slot + (slot - barWidth) / 2;
            double height = Math.Max(2, peaks[index] * maximumHalfHeight * 2);
            var bar = new Rect(x, center - height / 2, barWidth, height);
            Brush brush = x + barWidth / 2 <= progressX
                ? ActiveBrush
                : InactiveBrush;
            drawingContext.DrawRoundedRectangle(brush, null, bar, 1.5, 1.5);
        }

        if (Progress <= 0 && !IsPlaying)
        {
            return;
        }

        double boundedX = Math.Clamp(progressX, 1, Math.Max(1, ActualWidth - 1));
        var halo = new RadialGradientBrush(
            Color.FromArgb(82, 88, 214, 255),
            Color.FromArgb(0, 88, 214, 255));
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
