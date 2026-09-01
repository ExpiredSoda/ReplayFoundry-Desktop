using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Runtime.InteropServices;

namespace ReplayFoundry.Desktop.Features.Publish.Sections;

public partial class PublishLibraryBrowserView : UserControl
{
    internal const string DragFormat = "ReplayFoundry.LibraryAssetId";
    private Point _dragStart;
    private bool _canStartDrag;
    private DragPreviewAdorner? _dragPreview;

    public PublishLibraryBrowserView() => InitializeComponent();

    private void Items_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _canStartDrag = IsAssetDragOrigin(
            e.OriginalSource as DependencyObject);
    }

    internal static bool IsAssetDragOrigin(DependencyObject? origin) =>
        origin is not null &&
        FindAncestor<ListBoxItem>(origin) is not null &&
        FindAncestor<ButtonBase>(origin) is null &&
        FindAncestor<ScrollBar>(origin) is null;

    private void Items_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_canStartDrag ||
            e.LeftButton != MouseButtonState.Pressed ||
            sender is not ListBox listBox ||
            listBox.SelectedItem is not PublishLibraryItem item)
        {
            return;
        }
        Point current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }
        var data = new DataObject();
        data.SetData(DragFormat, item.Asset.Id);
        ListBoxItem? container = listBox.ItemContainerGenerator
            .ContainerFromItem(item) as ListBoxItem;
        AdornerDecorator? decorator = FindAncestor<AdornerDecorator>(listBox);
        UIElement adornerSurface = decorator?.Child ?? listBox;
        AdornerLayer? layer = AdornerLayer.GetAdornerLayer(adornerSurface);
        if (container is not null && layer is not null)
        {
            _dragPreview = new DragPreviewAdorner(adornerSurface, container);
            _dragPreview.UpdatePosition(Mouse.GetPosition(adornerSurface));
            layer.Add(_dragPreview);
            listBox.GiveFeedback += Items_GiveFeedback;
        }
        try
        {
            DragDrop.DoDragDrop(listBox, data, DragDropEffects.Link);
        }
        finally
        {
            listBox.GiveFeedback -= Items_GiveFeedback;
            if (_dragPreview is not null && layer is not null)
            {
                layer.Remove(_dragPreview);
            }
            _dragPreview = null;
            _canStartDrag = false;
        }
    }

    private void Items_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        if (_dragPreview is not null)
        {
            _dragPreview.UpdateFromCurrentCursor();
        }
        e.UseDefaultCursors = true;
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = current switch
            {
                Visual => VisualTreeHelper.GetParent(current),
                FrameworkContentElement content => content.Parent,
                _ => LogicalTreeHelper.GetParent(current),
            };
        }
        return null;
    }

    private sealed class DragPreviewAdorner : Adorner
    {
        private readonly VisualBrush _preview;
        private readonly Size _previewSize;
        private Point _position;

        public DragPreviewAdorner(UIElement adornedElement, FrameworkElement source)
            : base(adornedElement)
        {
            IsHitTestVisible = false;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            double sourceWidth = Math.Max(1, source.ActualWidth);
            double sourceHeight = Math.Max(1, source.ActualHeight);
            double previewWidth = Math.Clamp(sourceWidth, 220, 340);
            double previewScale = previewWidth / sourceWidth;
            _previewSize = new Size(
                previewWidth,
                Math.Clamp(sourceHeight * previewScale, 72, 112));
            _preview = new VisualBrush(source)
            {
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                Stretch = Stretch.UniformToFill,
                Viewbox = new Rect(0, 0, sourceWidth, sourceHeight),
                ViewboxUnits = BrushMappingMode.Absolute,
            };
            Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                Direction = 270,
                Opacity = 0.42,
                ShadowDepth = 8,
                Color = Colors.Black,
            };
        }

        public void UpdatePosition(Point position)
        {
            _position = position;
            InvalidateVisual();
        }

        public void UpdateFromCurrentCursor()
        {
            if (!GetCursorPos(out NativePoint cursor))
            {
                return;
            }

            // OLE drag/drop can leave WPF's Mouse position at the drag origin.
            // PointFromScreen also performs the monitor's current DPI transform.
            UpdatePosition(AdornedElement.PointFromScreen(
                new Point(cursor.X, cursor.Y)));
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            Point origin = new(_position.X + 18, _position.Y + 18);
            double maximumX = Math.Max(0, AdornedElement.RenderSize.Width -
                _previewSize.Width);
            double maximumY = Math.Max(0, AdornedElement.RenderSize.Height -
                _previewSize.Height);
            var bounds = new Rect(
                Math.Clamp(origin.X, 0, maximumX),
                Math.Clamp(origin.Y, 0, maximumY),
                _previewSize.Width,
                _previewSize.Height);
            var clip = new RectangleGeometry(bounds, 10, 10);
            drawingContext.PushClip(clip);
            drawingContext.PushOpacity(0.96);
            drawingContext.DrawRoundedRectangle(
                _preview,
                new Pen(
                    (Brush)FindResource("Brush.StatusInfo"),
                    2),
                bounds,
                10,
                10);
            drawingContext.DrawRectangle(
                (Brush)FindResource("Brush.StatusInfo"),
                null,
                new Rect(bounds.X, bounds.Y, 4, bounds.Height));
            drawingContext.Pop();
            drawingContext.Pop();
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out NativePoint point);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }
    }
}
