using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ReplayFoundry.Desktop.Presentation.Controls;

/// <summary>Lets the containing panel scroll when an embedded viewer reaches its edge.</summary>
public static class ScrollChaining
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ScrollChaining), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs change)
    {
        if (element is not ScrollViewer viewer) return;
        if ((bool)change.NewValue) viewer.PreviewMouseWheel += OnPreviewMouseWheel;
        else viewer.PreviewMouseWheel -= OnPreviewMouseWheel;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Preserve horizontal scrolling and application zoom shortcuts.
        if (e.Handled || e.Delta == 0 || Keyboard.Modifiers != ModifierKeys.None || sender is not ScrollViewer viewer)
            return;
        var origin = e.OriginalSource as DependencyObject;
        // Preview events tunnel through outer viewers first. Only the innermost
        // viewer may decide whether the wheel belongs to it or its parent.
        if (!ReferenceEquals(FindViewer(origin), viewer) || CanScroll(viewer, e.Delta)) return;
        var parent = FindViewer(Parent(viewer));
        while (parent is not null && !CanScroll(parent, e.Delta)) parent = FindViewer(Parent(parent));
        if (parent is null) return;
        e.Handled = true;
        parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = Mouse.MouseWheelEvent,
            Source = parent,
        });
    }

    private static bool CanScroll(ScrollViewer viewer, int delta) =>
        viewer.IsEnabled && viewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled &&
        (delta > 0 ? viewer.VerticalOffset > 0.01 : viewer.ScrollableHeight - viewer.VerticalOffset > 0.01);

    private static ScrollViewer? FindViewer(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is ScrollViewer viewer) return viewer;
            element = Parent(element);
        }
        return null;
    }

    private static DependencyObject? Parent(DependencyObject element) => element switch
    {
        Visual => VisualTreeHelper.GetParent(element),
        FrameworkContentElement content => content.Parent,
        _ => LogicalTreeHelper.GetParent(element),
    };
}
