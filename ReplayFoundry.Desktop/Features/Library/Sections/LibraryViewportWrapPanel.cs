using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ReplayFoundry.Desktop.Features.Library.Sections;

/// <summary>
/// Keeps grouped Library cards inside the owning collection viewport.
/// WPF otherwise measures a grouped WrapPanel with infinite width, which
/// places later cards beyond a disabled horizontal scroll range.
/// </summary>
public sealed class LibraryViewportWrapPanel : WrapPanel
{
    private ScrollViewer? _scrollViewer;

    public LibraryViewportWrapPanel()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double viewportWidth = ResolveViewportWidth();
        double constrainedWidth = viewportWidth > 0
            ? Math.Min(
                double.IsFinite(availableSize.Width)
                    ? availableSize.Width
                    : viewportWidth,
                viewportWidth)
            : availableSize.Width;

        // ListBox can offer its items panel the visible height even when its
        // physical ScrollViewer is enabled. Measuring the complete wrapped
        // extent lets that ScrollViewer expose every lower row instead of
        // clipping it as if the collection ended at the viewport boundary.
        return base.MeasureOverride(
            new Size(constrainedWidth, double.PositiveInfinity));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachScrollViewer();
        InvalidateMeasure();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_scrollViewer is not null)
        {
            _scrollViewer.SizeChanged -= OnViewportSizeChanged;
            _scrollViewer = null;
        }
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e) =>
        InvalidateMeasure();

    private double ResolveViewportWidth()
    {
        AttachScrollViewer();
        if (_scrollViewer is null)
        {
            return 0;
        }

        double width = _scrollViewer.ViewportWidth > 0
            ? _scrollViewer.ViewportWidth
            : _scrollViewer.ActualWidth;

        return double.IsFinite(width)
            ? Math.Max(0, width)
            : 0;
    }

    private void AttachScrollViewer()
    {
        ScrollViewer? scrollViewer = FindVisualAncestor<ScrollViewer>(this);
        if (ReferenceEquals(_scrollViewer, scrollViewer))
        {
            return;
        }

        if (_scrollViewer is not null)
        {
            _scrollViewer.SizeChanged -= OnViewportSizeChanged;
        }

        _scrollViewer = scrollViewer;
        if (_scrollViewer is not null)
        {
            _scrollViewer.SizeChanged += OnViewportSizeChanged;
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject child)
        where T : DependencyObject
    {
        DependencyObject? current = VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
