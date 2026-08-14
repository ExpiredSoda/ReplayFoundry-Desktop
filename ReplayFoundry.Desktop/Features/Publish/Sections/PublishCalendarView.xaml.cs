using System.Windows;
using System.Windows.Controls;

namespace ReplayFoundry.Desktop.Features.Publish.Sections;

public partial class PublishCalendarView
{
    public static readonly DependencyProperty IsDropTargetActiveProperty =
        DependencyProperty.RegisterAttached(
            "IsDropTargetActive",
            typeof(bool),
            typeof(PublishCalendarView),
            new FrameworkPropertyMetadata(false));

    public static bool GetIsDropTargetActive(DependencyObject element) =>
        (bool)element.GetValue(IsDropTargetActiveProperty);

    public static void SetIsDropTargetActive(
        DependencyObject element,
        bool value) =>
        element.SetValue(IsDropTargetActiveProperty, value);

    private void Day_DragEnter(object sender, DragEventArgs e) =>
        UpdateDropTarget(sender, e);

    private void Day_DragOver(object sender, DragEventArgs e)
    {
        UpdateDropTarget(sender, e);
    }

    private static void UpdateDropTarget(object sender, DragEventArgs e)
    {
        bool canDrop = e.Data.GetDataPresent(
            PublishLibraryBrowserView.DragFormat);
        e.Effects = canDrop ? DragDropEffects.Link : DragDropEffects.None;
        if (sender is DependencyObject target)
        {
            SetIsDropTargetActive(target, canDrop);
        }
        e.Handled = true;
    }

    private void Day_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is DependencyObject target)
        {
            SetIsDropTargetActive(target, false);
        }
        e.Handled = true;
    }

    private void Day_Drop(object sender, DragEventArgs e)
    {
        if (sender is DependencyObject target)
        {
            SetIsDropTargetActive(target, false);
        }
        if (sender is not FrameworkElement { DataContext: PublishCalendarDay day } ||
            DataContext is not PublishViewModel viewModel ||
            e.Data.GetData(PublishLibraryBrowserView.DragFormat) is not string assetId)
        {
            return;
        }
        var asset = viewModel.AvailableAssets.FirstOrDefault(value =>
            value.Id.Equals(assetId, StringComparison.Ordinal));
        if (asset is not null)
        {
            viewModel.PrepareAssetForDate(asset, day.Date);
            e.Handled = true;
        }
    }
}
