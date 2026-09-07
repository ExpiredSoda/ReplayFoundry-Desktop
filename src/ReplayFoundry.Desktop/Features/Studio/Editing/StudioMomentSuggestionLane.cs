using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation.Peers;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed record StudioMomentMarkerGroup(double Left, double Right, IReadOnlyList<StudioMomentSuggestion> Moments);

public static class StudioMomentMarkerProjection
{
    public static StudioMomentSuggestion AtTime(StudioMomentMarkerGroup group, double seconds, string? selectedId)
    {
        StudioMomentSuggestion[] nearby = group.Moments.Where(item => item.Start <= seconds && item.End >= seconds)
            .OrderByDescending(item => item.Score).ToArray();
        if (nearby.Length == 0) return group.Moments.MinBy(item => Math.Min(Math.Abs(item.Start - seconds), Math.Abs(item.End - seconds)))!;
        int current = Array.FindIndex(nearby, item => item.Id == selectedId);
        return nearby[(current + 1) % nearby.Length];
    }
    public static IReadOnlyList<StudioMomentMarkerGroup> Project(IReadOnlyList<StudioMomentSuggestion> items,
        double start, double duration, double width)
    {
        if (!double.IsFinite(start) || !double.IsFinite(duration) || !double.IsFinite(width) || duration <= 0 || width <= 0) return [];
        var groups = new List<StudioMomentMarkerGroup>();
        foreach (StudioMomentSuggestion item in items.Where(item => item.End > start && item.Start < start + duration).OrderBy(item => item.Start))
        {
            double left = Math.Clamp((item.Start - start) / duration * width, 0, width);
            double right = Math.Clamp((item.End - start) / duration * width, 0, width);
            if (groups.Count > 0 && left <= groups[^1].Right + 5)
            {
                StudioMomentMarkerGroup previous = groups[^1];
                groups[^1] = previous with { Right = Math.Max(previous.Right, right), Moments = [.. previous.Moments, item] };
            }
            else groups.Add(new(left, right, [item]));
        }
        return groups;
    }
}

public sealed class StudioMomentSuggestionLane : FrameworkElement
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(nameof(Items),
        typeof(IReadOnlyList<StudioMomentSuggestion>), typeof(StudioMomentSuggestionLane), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SelectedProperty = DependencyProperty.Register(nameof(Selected),
        typeof(StudioMomentSuggestion), typeof(StudioMomentSuggestionLane), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ViewportStartProperty = Number(nameof(ViewportStart), 0);
    public static readonly DependencyProperty ViewportDurationProperty = Number(nameof(ViewportDuration), 120);
    public static readonly DependencyProperty SelectCommandProperty = DependencyProperty.Register(nameof(SelectCommand),
        typeof(ICommand), typeof(StudioMomentSuggestionLane));
    public IReadOnlyList<StudioMomentSuggestion>? Items { get => (IReadOnlyList<StudioMomentSuggestion>?)GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public StudioMomentSuggestion? Selected { get => (StudioMomentSuggestion?)GetValue(SelectedProperty); set => SetValue(SelectedProperty, value); }
    public double ViewportStart { get => (double)GetValue(ViewportStartProperty); set => SetValue(ViewportStartProperty, value); }
    public double ViewportDuration { get => (double)GetValue(ViewportDurationProperty); set => SetValue(ViewportDurationProperty, value); }
    public ICommand? SelectCommand { get => (ICommand?)GetValue(SelectCommandProperty); set => SetValue(SelectCommandProperty, value); }
    private IReadOnlyList<StudioMomentMarkerGroup> Groups => StudioMomentMarkerProjection.Project(Items ?? [], ViewportStart, ViewportDuration, ActualWidth - 24);
    private static DependencyProperty Number(string name, double value) => DependencyProperty.Register(name, typeof(double),
        typeof(StudioMomentSuggestionLane), new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsRender));
    private Brush Color(string name) => (Brush)FindResource(name);
    public StudioMomentSuggestionLane() { Height = 28; ClipToBounds = true; Cursor = Cursors.Hand; }
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        foreach (StudioMomentMarkerGroup group in Groups)
        {
            bool selected = group.Moments.Any(item => item.Id == Selected?.Id);
            var bounds = new Rect(group.Left + 12, 5, Math.Max(3, group.Right - group.Left), 18);
            dc.PushOpacity(selected ? 0.55 : 0.22);
            dc.DrawRoundedRectangle(Color("Brush.BrandCyan"), null, bounds, 3, 3);
            dc.Pop();
            dc.DrawLine(new Pen(Color("Brush.BrandCyan"), selected ? 3 : 1.5), new(bounds.Left, 6), new(bounds.Left, 23));
            double lastTick = double.NegativeInfinity;
            dc.PushOpacity(.65);
            foreach (StudioMomentSuggestion moment in group.Moments)
            {
                double tick = 12 + (moment.Start - ViewportStart) / ViewportDuration * (ActualWidth - 24);
                if (tick < bounds.Left || tick > bounds.Right || tick - lastTick < 4) continue;
                dc.DrawLine(new Pen(Color("Brush.BrandCyan"), 1), new(tick, 18), new(tick, 23));
                lastTick = tick;
            }
            dc.Pop();
            if (Selected is { } active && selected)
            {
                double left = Math.Clamp((active.Start - ViewportStart) / ViewportDuration * (ActualWidth - 24) + 12, bounds.Left, bounds.Right);
                double right = Math.Clamp((active.End - ViewportStart) / ViewportDuration * (ActualWidth - 24) + 12, left, bounds.Right);
                dc.DrawRoundedRectangle(null, new Pen(Color("Brush.BrandCyan"), 1.5), new Rect(left, 4, Math.Max(1, right - left), 20), 3, 3);
            }
            if (bounds.Width > 30 && group.Moments.Count > 1)
            {
                var text = new FormattedText($"{group.Moments.Count}", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 10, Color("Brush.TextPrimary"), VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(text, new Point(bounds.Left + 6, 7));
            }
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        StudioMomentMarkerGroup? group = Hit(e.GetPosition(this).X);
        ToolTip = group is null ? "Cyan marks show suggested moments. Use Previous and Next to explore with the keyboard."
            : $"{group.Moments.Count} suggested moment(s) · {group.Moments[0].TimeText}\nClick to preview. Click again to explore overlapping moments.";
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Hit(e.GetPosition(this).X) is not { } group) return;
        double seconds = ViewportStart + (e.GetPosition(this).X - 12) / Math.Max(1, ActualWidth - 24) * ViewportDuration;
        StudioMomentSuggestion next = StudioMomentMarkerProjection.AtTime(group, seconds, Selected?.Id);
        if (SelectCommand?.CanExecute(next) == true) SelectCommand.Execute(next);
        e.Handled = true;
    }
    private StudioMomentMarkerGroup? Hit(double x) => Groups.Where(group => x >= group.Left + 8 && x <= group.Right + 16)
        .OrderBy(group => Math.Abs(x - (group.Left + group.Right) / 2 - 12)).FirstOrDefault();
}
