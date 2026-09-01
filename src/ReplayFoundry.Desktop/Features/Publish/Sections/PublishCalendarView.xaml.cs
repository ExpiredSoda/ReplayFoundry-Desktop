using System.Windows;
using System.Windows.Controls;

namespace ReplayFoundry.Desktop.Features.Publish.Sections;

internal enum PublishCalendarDropTargetState
{
    None,
    Accepted,
    RejectedPastDate,
}

public partial class PublishCalendarView
{
    public static readonly DependencyProperty IsDropTargetActiveProperty =
        DependencyProperty.RegisterAttached(
            "IsDropTargetActive",
            typeof(bool),
            typeof(PublishCalendarView),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsDropTargetRejectedProperty =
        DependencyProperty.RegisterAttached(
            "IsDropTargetRejected",
            typeof(bool),
            typeof(PublishCalendarView),
            new FrameworkPropertyMetadata(false));

    public static bool GetIsDropTargetActive(DependencyObject element) =>
        (bool)element.GetValue(IsDropTargetActiveProperty);

    public static void SetIsDropTargetActive(
        DependencyObject element,
        bool value) =>
        element.SetValue(IsDropTargetActiveProperty, value);

    public static bool GetIsDropTargetRejected(DependencyObject element) =>
        (bool)element.GetValue(IsDropTargetRejectedProperty);

    public static void SetIsDropTargetRejected(
        DependencyObject element,
        bool value) =>
        element.SetValue(IsDropTargetRejectedProperty, value);

    private void Day_DragEnter(object sender, DragEventArgs e) =>
        UpdateDropTarget(sender, e);

    private void Day_DragOver(object sender, DragEventArgs e)
    {
        UpdateDropTarget(sender, e);
    }

    private void UpdateDropTarget(object sender, DragEventArgs e)
    {
        PublishCalendarDropTargetState state = ResolveDropTargetState(
            DataContext as PublishViewModel,
            (sender as FrameworkElement)?.DataContext as PublishCalendarDay,
            e.Data.GetDataPresent(PublishLibraryBrowserView.DragFormat));
        e.Effects = state == PublishCalendarDropTargetState.Accepted
            ? DragDropEffects.Link
            : DragDropEffects.None;
        if (sender is DependencyObject target)
        {
            SetIsDropTargetActive(
                target,
                state == PublishCalendarDropTargetState.Accepted);
            SetIsDropTargetRejected(
                target,
                state == PublishCalendarDropTargetState.RejectedPastDate);
        }
        e.Handled = true;
    }

    private void Day_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is DependencyObject target)
        {
            ClearDropTarget(target);
        }
        e.Handled = true;
    }

    private void Day_Drop(object sender, DragEventArgs e)
    {
        if (sender is DependencyObject target)
        {
            ClearDropTarget(target);
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

    internal static PublishCalendarDropTargetState ResolveDropTargetState(
        PublishViewModel? viewModel,
        PublishCalendarDay? day,
        bool hasLibraryAsset)
    {
        if (!hasLibraryAsset || viewModel is null || day is null)
        {
            return PublishCalendarDropTargetState.None;
        }

        return viewModel.CanPrepareAssetForDate(day.Date)
            ? PublishCalendarDropTargetState.Accepted
            : PublishCalendarDropTargetState.RejectedPastDate;
    }

    private static void ClearDropTarget(DependencyObject target)
    {
        SetIsDropTargetActive(target, false);
        SetIsDropTargetRejected(target, false);
    }
}
