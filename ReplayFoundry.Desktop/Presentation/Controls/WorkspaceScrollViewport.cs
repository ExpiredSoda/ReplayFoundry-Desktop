using System.Windows;
using System.Windows.Controls;

namespace ReplayFoundry.Desktop.Presentation.Controls;

[TemplatePart(Name = ScrollViewerPartName, Type = typeof(ScrollViewer))]
public sealed class WorkspaceScrollViewport : ContentControl
{
    private const string ScrollViewerPartName = "PART_ScrollViewer";
    private const double ContinuationTolerance = 1d;
    private static readonly DependencyPropertyKey HasMoreBelowPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(HasMoreBelow),
            typeof(bool),
            typeof(WorkspaceScrollViewport),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty HasMoreBelowProperty =
        HasMoreBelowPropertyKey.DependencyProperty;

    public static readonly DependencyProperty CueTextProperty =
        DependencyProperty.Register(
            nameof(CueText),
            typeof(string),
            typeof(WorkspaceScrollViewport),
            new FrameworkPropertyMetadata("More below"));

    private ScrollViewer? _scrollViewer;

    static WorkspaceScrollViewport() =>
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(WorkspaceScrollViewport),
            new FrameworkPropertyMetadata(typeof(WorkspaceScrollViewport)));

    public bool HasMoreBelow => (bool)GetValue(HasMoreBelowProperty);

    public string CueText
    {
        get => (string)GetValue(CueTextProperty);
        set => SetValue(CueTextProperty, value);
    }

    public void ScrollToTop()
    {
        ApplyTemplate();
        _scrollViewer?.ScrollToTop();
    }

    public override void OnApplyTemplate()
    {
        DetachScrollViewer();
        base.OnApplyTemplate();

        _scrollViewer =
            GetTemplateChild(ScrollViewerPartName) as ScrollViewer;
        if (_scrollViewer is null)
        {
            SetValue(HasMoreBelowPropertyKey, false);
            return;
        }

        _scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
        _scrollViewer.SizeChanged += ScrollViewer_SizeChanged;
        UpdateContinuationState();
    }

    private void ScrollViewer_ScrollChanged(
        object sender,
        ScrollChangedEventArgs e) =>
        UpdateContinuationState();

    private void ScrollViewer_SizeChanged(
        object sender,
        SizeChangedEventArgs e) =>
        UpdateContinuationState();

    private void UpdateContinuationState()
    {
        bool hasMoreBelow =
            _scrollViewer is not null &&
            _scrollViewer.ScrollableHeight -
            _scrollViewer.VerticalOffset > ContinuationTolerance;

        SetValue(HasMoreBelowPropertyKey, hasMoreBelow);
    }

    private void DetachScrollViewer()
    {
        if (_scrollViewer is null)
        {
            return;
        }

        _scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
        _scrollViewer.SizeChanged -= ScrollViewer_SizeChanged;
        _scrollViewer = null;
    }
}
