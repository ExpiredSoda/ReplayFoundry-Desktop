using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ReplayFoundry.Desktop.Features.Library.Sections;

public sealed class LibraryVirtualizingWrapPanel : VirtualizingPanel
{
    public static readonly DependencyProperty IsViewportOwnerProperty =
        DependencyProperty.RegisterAttached(
            "IsViewportOwner",
            typeof(bool),
            typeof(LibraryVirtualizingWrapPanel),
            new PropertyMetadata(false));
    public static readonly DependencyProperty ItemWidthProperty =
        DimensionProperty(nameof(ItemWidth), 250d);
    public static readonly DependencyProperty ItemHeightProperty =
        DimensionProperty(nameof(ItemHeight), 350d);
    public static readonly DependencyProperty HeaderHeightProperty =
        DimensionProperty(nameof(HeaderHeight), 42d);

    private readonly List<Rect> _layouts = [];
    private ScrollViewer? _scrollViewer;
    private int _columns = 1;
    private int _layoutItemCount = -1;
    private double _layoutWidth = double.NaN;
    private double _extentHeight;
    private bool _layoutInvalid = true;

    public LibraryVirtualizingWrapPanel()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public double HeaderHeight
    {
        get => (double)GetValue(HeaderHeightProperty);
        set => SetValue(HeaderHeightProperty, value);
    }

    public static void SetIsViewportOwner(
        DependencyObject element,
        bool value) =>
        element.SetValue(IsViewportOwnerProperty, value);

    public static bool GetIsViewportOwner(DependencyObject element) =>
        (bool)element.GetValue(IsViewportOwnerProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = ResolveWidth(availableSize.Width);
        _columns = Math.Max(1, (int)Math.Floor(width / ItemWidth));
        ItemsControl? owner = ItemsControl.GetItemsOwner(this);
        if (owner is null)
        {
            return new Size(width, 0);
        }

        double height = EnsureLayout(owner, width);
        (int first, int last) = GetVisibleRange();
        RemoveChildrenOutside(first, last);
        RealizeChildren(first, last);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        IItemContainerGenerator generator = ItemContainerGenerator;
        for (int childIndex = 0;
             childIndex < InternalChildren.Count;
             childIndex++)
        {
            int itemIndex = generator.IndexFromGeneratorPosition(
                new GeneratorPosition(childIndex, 0));
            if (itemIndex >= 0 && itemIndex < _layouts.Count)
            {
                InternalChildren[childIndex].Arrange(_layouts[itemIndex]);
            }
        }
        return finalSize;
    }

    protected override void BringIndexIntoView(int index)
    {
        ItemsControl? owner = ItemsControl.GetItemsOwner(this);
        if (owner is null || index < 0 || index >= owner.Items.Count)
        {
            return;
        }

        AttachScrollViewer();
        double width = ResolveWidth(
            RenderSize.Width > 0 ? RenderSize.Width : DesiredSize.Width);
        EnsureLayout(owner, width);
        if (_scrollViewer is null || index >= _layouts.Count)
        {
            return;
        }

        Rect target = _layouts[index];
        double currentOffset = _scrollViewer.VerticalOffset;
        double contentPanelTop = currentOffset + ResolvePanelTop();
        double targetTop = contentPanelTop + target.Top;
        double targetBottom = targetTop + target.Height;
        double viewportHeight = ResolveViewportHeight();
        double targetOffset = currentOffset;
        if (targetTop < currentOffset)
        {
            targetOffset = targetTop;
        }
        else if (targetBottom > currentOffset + viewportHeight)
        {
            targetOffset = targetBottom - viewportHeight;
        }

        InvalidateMeasure();
        _scrollViewer.ScrollToVerticalOffset(Math.Max(0, targetOffset));
        _scrollViewer.UpdateLayout();
    }

    protected override void OnItemsChanged(
        object sender,
        ItemsChangedEventArgs args)
    {
        _layoutInvalid = true;
        base.OnItemsChanged(sender, args);
    }

    private double EnsureLayout(ItemsControl owner, double width)
    {
        if (!_layoutInvalid &&
            _layoutItemCount == owner.Items.Count &&
            Math.Abs(_layoutWidth - width) < 0.1)
        {
            return _extentHeight;
        }

        _layouts.Clear();
        double top = 0;
        int column = 0;
        foreach (object item in owner.Items)
        {
            bool isHeader = item is LibraryGridEntry { IsHeader: true };
            if (isHeader)
            {
                if (column > 0)
                {
                    top += ItemHeight;
                    column = 0;
                }
                _layouts.Add(new Rect(0, top, width, HeaderHeight));
                top += HeaderHeight;
                continue;
            }

            _layouts.Add(new Rect(
                column * ItemWidth,
                top,
                ItemWidth,
                ItemHeight));
            column++;
            if (column == _columns)
            {
                top += ItemHeight;
                column = 0;
            }
        }
        _layoutItemCount = owner.Items.Count;
        _layoutWidth = width;
        _extentHeight = top + (column > 0 ? ItemHeight : 0);
        _layoutInvalid = false;
        return _extentHeight;
    }

    private (int First, int Last) GetVisibleRange()
    {
        if (_layouts.Count == 0)
        {
            return (0, -1);
        }

        double viewportHeight = ResolveViewportHeight();
        double panelTop = ResolvePanelTop();
        double visibleTop = -panelTop - ItemHeight;
        double visibleBottom = -panelTop + viewportHeight + ItemHeight;
        int first = FindFirstVisibleByBottom(visibleTop);
        int last = FindLastVisibleByTop(visibleBottom);
        return first > last ? (0, -1) : (first, last);
    }

    private int FindFirstVisibleByBottom(double visibleTop)
    {
        int low = 0;
        int high = _layouts.Count;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (_layouts[middle].Bottom < visibleTop)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        return low;
    }

    private int FindLastVisibleByTop(double visibleBottom)
    {
        int low = 0;
        int high = _layouts.Count;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (_layouts[middle].Top <= visibleBottom)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        return low - 1;
    }

    private double ResolveViewportHeight()
    {
        double reportedViewport = _scrollViewer?.ViewportHeight ?? 0;
        double actualViewport = _scrollViewer?.ActualHeight ?? 0;
        return double.IsFinite(reportedViewport) && reportedViewport > 0
            ? reportedViewport
            : double.IsFinite(actualViewport) && actualViewport > 0
                ? actualViewport
                : ItemHeight * 2;
    }

    private void RealizeChildren(int first, int last)
    {
        if (last < first)
        {
            return;
        }

        IItemContainerGenerator generator = ItemContainerGenerator;
        GeneratorPosition position =
            generator.GeneratorPositionFromIndex(first);
        int childIndex = position.Offset == 0
            ? position.Index
            : position.Index + 1;
        using IDisposable generation = generator.StartAt(
            position,
            GeneratorDirection.Forward,
            allowStartAtRealizedItem: true);
        for (int itemIndex = first;
             itemIndex <= last;
             itemIndex++, childIndex++)
        {
            if (generator.GenerateNext(out bool created) is not UIElement child)
            {
                throw new InvalidOperationException(
                    "The flat Library item generator ended before its projected entries.");
            }
            if (created)
            {
                if (childIndex >= InternalChildren.Count)
                {
                    AddInternalChild(child);
                }
                else
                {
                    InsertInternalChild(childIndex, child);
                }
                generator.PrepareItemContainer(child);
            }
            Rect layout = _layouts[itemIndex];
            child.Measure(new Size(layout.Width, layout.Height));
        }
    }

    private void RemoveChildrenOutside(int first, int last)
    {
        IItemContainerGenerator generator = ItemContainerGenerator;
        for (int childIndex = InternalChildren.Count - 1;
             childIndex >= 0;
             childIndex--)
        {
            GeneratorPosition position = new(childIndex, 0);
            int itemIndex = generator.IndexFromGeneratorPosition(position);
            if (itemIndex >= first && itemIndex <= last)
            {
                continue;
            }
            generator.Remove(position, 1);
            RemoveInternalChildRange(childIndex, 1);
        }
    }

    private double ResolveWidth(double availableWidth)
    {
        AttachScrollViewer();
        double viewportWidth = _scrollViewer?.ViewportWidth > 0
            ? _scrollViewer.ViewportWidth
            : _scrollViewer?.ActualWidth ?? 0;
        double width = double.IsFinite(availableWidth)
            ? availableWidth
            : viewportWidth;
        return Math.Max(
            ItemWidth,
            Math.Min(width, viewportWidth > 0 ? viewportWidth : width));
    }

    private double ResolvePanelTop()
    {
        if (_scrollViewer is null)
        {
            return 0;
        }
        try
        {
            return TransformToAncestor(_scrollViewer)
                .Transform(new Point()).Y;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachScrollViewer();
        InvalidateMeasure();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        DetachScrollViewer();

    private void AttachScrollViewer()
    {
        ScrollViewer? viewer = FindViewportAncestor(this);
        if (ReferenceEquals(_scrollViewer, viewer))
        {
            return;
        }
        DetachScrollViewer();
        _scrollViewer = viewer;
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged += OnViewportChanged;
            _scrollViewer.SizeChanged += OnViewportSizeChanged;
        }
    }

    private void DetachScrollViewer()
    {
        if (_scrollViewer is null)
        {
            return;
        }
        _scrollViewer.ScrollChanged -= OnViewportChanged;
        _scrollViewer.SizeChanged -= OnViewportSizeChanged;
        _scrollViewer = null;
    }

    private void OnViewportChanged(object sender, ScrollChangedEventArgs e) =>
        InvalidateMeasure();

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e) =>
        InvalidateMeasure();

    private static ScrollViewer? FindViewportAncestor(DependencyObject child)
    {
        for (DependencyObject? current = VisualTreeHelper.GetParent(child);
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is ScrollViewer viewer &&
                GetIsViewportOwner(viewer))
            {
                return viewer;
            }
        }
        return null;
    }

    private static DependencyProperty DimensionProperty(
        string name,
        double defaultValue) =>
        DependencyProperty.Register(
            name,
            typeof(double),
            typeof(LibraryVirtualizingWrapPanel),
            new FrameworkPropertyMetadata(
                defaultValue,
                FrameworkPropertyMetadataOptions.AffectsMeasure,
                static (element, _) =>
                    ((LibraryVirtualizingWrapPanel)element)
                        ._layoutInvalid = true),
            static value =>
                value is double dimension &&
                double.IsFinite(dimension) &&
                dimension > 0);
}
