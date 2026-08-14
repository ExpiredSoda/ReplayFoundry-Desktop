using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ReplayFoundry.Desktop.Features.Generate.CompositionReview;

public partial class CompositionRegionEditor :
    UserControl
{
    private const double KeyboardStep = 0.005;

    private Point? _lastDragPosition;

    public CompositionRegionEditor()
    {
        InitializeComponent();
    }

    private void Region_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not
                CompositionRegionDraftViewModel region)
        {
            return;
        }

        region.RequestSelection();
        element.Focus();
        element.CaptureMouse();

        _lastDragPosition =
            e.GetPosition(
                CoordinatePlane);

        e.Handled = true;
    }

    private void Region_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (_lastDragPosition is not Point previous ||
            e.LeftButton !=
            MouseButtonState.Pressed ||
            sender is not FrameworkElement element ||
            !element.IsMouseCaptured ||
            element.DataContext is not
                CompositionRegionDraftViewModel region)
        {
            return;
        }

        Point current =
            e.GetPosition(
                CoordinatePlane);

        if (CoordinatePlane.ActualWidth <= 0 ||
            CoordinatePlane.ActualHeight <= 0)
        {
            return;
        }

        region.MoveBy(
            (current.X - previous.X) /
            CoordinatePlane.ActualWidth,
            (current.Y - previous.Y) /
            CoordinatePlane.ActualHeight);

        _lastDragPosition = current;
        e.Handled = true;
    }

    private void Region_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element &&
            element.IsMouseCaptured)
        {
            element.ReleaseMouseCapture();
        }

        _lastDragPosition = null;
        e.Handled = true;
    }

    private void ResizeHandle_DragDelta(
        object sender,
        DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb ||
            thumb.DataContext is not
                CompositionRegionDraftViewModel region ||
            thumb.Tag is not string handleText ||
            !Enum.TryParse(
                handleText,
                ignoreCase: false,
                out CompositionRegionResizeHandle handle))
        {
            return;
        }

        if (CoordinatePlane.ActualWidth <= 0 ||
            CoordinatePlane.ActualHeight <= 0)
        {
            return;
        }

        region.RequestSelection();

        region.ResizeFromHandle(
            handle,
            CompositionRegionGeometryEditor
                .NormalizeDragDelta(
                    e.HorizontalChange,
                    CoordinatePlane.ActualWidth),
            CompositionRegionGeometryEditor
                .NormalizeDragDelta(
                    e.VerticalChange,
                    CoordinatePlane.ActualHeight));

        e.Handled = true;
    }

    private void Region_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not
                CompositionRegionDraftViewModel region)
        {
            return;
        }

        region.RequestSelection();

        if (e.Key == Key.Delete)
        {
            region.RequestRemoval();
            e.Handled = true;
            return;
        }

        double horizontalDelta =
            e.Key switch
            {
                Key.Left => -KeyboardStep,
                Key.Right => KeyboardStep,
                _ => 0,
            };

        double verticalDelta =
            e.Key switch
            {
                Key.Up => -KeyboardStep,
                Key.Down => KeyboardStep,
                _ => 0,
            };

        if (horizontalDelta == 0 &&
            verticalDelta == 0)
        {
            return;
        }

        if ((Keyboard.Modifiers &
             ModifierKeys.Shift) != 0)
        {
            region.ResizeBy(
                horizontalDelta,
                verticalDelta);
        }
        else
        {
            region.MoveBy(
                horizontalDelta,
                verticalDelta);
        }

        e.Handled = true;
    }
}
