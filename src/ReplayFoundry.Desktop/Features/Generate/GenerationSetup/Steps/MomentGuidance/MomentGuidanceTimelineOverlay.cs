using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.MomentGuidance;

public sealed class MomentGuidanceTimelineOverlay : FrameworkElement
{
    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(
            nameof(Items),
            typeof(IEnumerable<UserMomentGuidanceItemViewModel>),
            typeof(MomentGuidanceTimelineOverlay),
            new FrameworkPropertyMetadata(null, OnItemsChanged));

    public static readonly DependencyProperty DurationSecondsProperty =
        DependencyProperty.Register(
            nameof(DurationSeconds),
            typeof(double),
            typeof(MomentGuidanceTimelineOverlay),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PointBrushProperty =
        DependencyProperty.Register(
            nameof(PointBrush),
            typeof(Brush),
            typeof(MomentGuidanceTimelineOverlay),
            new FrameworkPropertyMetadata(
                Brushes.DeepSkyBlue,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RangeBrushProperty =
        DependencyProperty.Register(
            nameof(RangeBrush),
            typeof(Brush),
            typeof(MomentGuidanceTimelineOverlay),
            new FrameworkPropertyMetadata(
                Brushes.Gold,
                FrameworkPropertyMetadataOptions.AffectsRender));

    private INotifyCollectionChanged? _observedItems;

    public IEnumerable<UserMomentGuidanceItemViewModel>? Items
    {
        get => (IEnumerable<UserMomentGuidanceItemViewModel>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public double DurationSeconds
    {
        get => (double)GetValue(DurationSecondsProperty);
        set => SetValue(DurationSecondsProperty, value);
    }

    public Brush PointBrush
    {
        get => (Brush)GetValue(PointBrushProperty);
        set => SetValue(PointBrushProperty, value);
    }

    public Brush RangeBrush
    {
        get => (Brush)GetValue(RangeBrushProperty);
        set => SetValue(RangeBrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (Items is null ||
            !double.IsFinite(DurationSeconds) ||
            DurationSeconds <= 0 ||
            ActualWidth <= 0 ||
            ActualHeight <= 0)
        {
            return;
        }

        foreach (UserMomentGuidanceItemViewModel item in Items)
        {
            Rect bounds = MarkerBounds(
                item,
                DurationSeconds,
                ActualWidth,
                ActualHeight);
            if (bounds.IsEmpty)
            {
                continue;
            }

            Brush brush = item.IsPoint ? PointBrush : RangeBrush;
            drawingContext.DrawRoundedRectangle(
                brush,
                null,
                bounds,
                radiusX: 2,
                radiusY: 2);
        }
    }

    internal static Rect MarkerBounds(
        UserMomentGuidanceItemViewModel item,
        double durationSeconds,
        double width,
        double height)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0 ||
            !double.IsFinite(width) || width <= 0 ||
            !double.IsFinite(height) || height <= 0)
        {
            return Rect.Empty;
        }

        double start = Math.Clamp(
            item.StartSeconds / durationSeconds * width,
            0,
            width);
        if (item.IsPoint)
        {
            const double pointWidth = 3;
            return new Rect(
                Math.Min(start, Math.Max(0, width - pointWidth)),
                0,
                Math.Min(pointWidth, width),
                height);
        }

        double end = Math.Clamp(
            (item.StartSeconds + item.DurationSeconds) /
            durationSeconds * width,
            start,
            width);
        double markerWidth = Math.Min(
            Math.Max(3, end - start),
            width - start);
        if (markerWidth <= 0)
        {
            return Rect.Empty;
        }

        double markerHeight = Math.Min(8, height);
        return new Rect(
            start,
            (height - markerHeight) / 2,
            markerWidth,
            markerHeight);
    }

    private static void OnItemsChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        var overlay = (MomentGuidanceTimelineOverlay)dependencyObject;
        overlay.Observe(eventArgs.NewValue as INotifyCollectionChanged);
        overlay.InvalidateVisual();
    }

    private void Observe(INotifyCollectionChanged? items)
    {
        if (_observedItems is not null)
        {
            CollectionChangedEventManager.RemoveHandler(
                _observedItems,
                OnCollectionChanged);
        }

        _observedItems = items;
        if (_observedItems is not null)
        {
            CollectionChangedEventManager.AddHandler(
                _observedItems,
                OnCollectionChanged);
        }
    }

    private void OnCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs eventArgs) =>
        InvalidateVisual();
}
