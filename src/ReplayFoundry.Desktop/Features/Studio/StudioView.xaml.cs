using System;
using System.Windows;
using System.Windows.Controls;
using ReplayFoundry.Desktop.Presentation.Responsive;

namespace ReplayFoundry.Desktop.Features.Studio;

public partial class StudioView : UserControl
{
    private double _workspaceViewportHeight;
    private static readonly DependencyPropertyKey IsCompactLayoutPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsCompactLayout), typeof(bool), typeof(StudioView), new FrameworkPropertyMetadata(false));
    private static readonly DependencyPropertyKey IsStandardLayoutPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsStandardLayout), typeof(bool), typeof(StudioView), new FrameworkPropertyMetadata(true));
    private static readonly DependencyPropertyKey IsWideLayoutPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsWideLayout), typeof(bool), typeof(StudioView), new FrameworkPropertyMetadata(false));
    private static readonly DependencyPropertyKey PaneHeightPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(PaneHeight), typeof(double), typeof(StudioView), new FrameworkPropertyMetadata(700d));
    public static readonly DependencyProperty IsCompactLayoutProperty = IsCompactLayoutPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsStandardLayoutProperty = IsStandardLayoutPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsWideLayoutProperty = IsWideLayoutPropertyKey.DependencyProperty;
    public static readonly DependencyProperty PaneHeightProperty = PaneHeightPropertyKey.DependencyProperty;

    public StudioView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        Loaded += OnLoaded;
    }

    public bool IsCompactLayout => (bool)GetValue(IsCompactLayoutProperty);
    public bool IsStandardLayout => (bool)GetValue(IsStandardLayoutProperty);
    public bool IsWideLayout => (bool)GetValue(IsWideLayoutProperty);
    public double PaneHeight => (double)GetValue(PaneHeightProperty);

    internal void SetResponsiveWidthForTest(double width) => UpdateResponsiveState(width);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateResponsiveState(ActualWidth);
        UpdatePaneHeight();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveState(e.NewSize.Width);
        UpdatePaneHeight();
    }

    private void OnProjectControlsSizeChanged(object sender, SizeChangedEventArgs e) => UpdatePaneHeight();

    private void OnWorkspaceScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Ignore the nested browser and inspector scrollers. The workspace's
        // viewport already excludes its continuation cue and any scroll chrome.
        if (e.OriginalSource is not ScrollViewer scrollViewer ||
            !ReferenceEquals(scrollViewer.TemplatedParent, StudioViewport) ||
            scrollViewer.ViewportHeight <= 0d)
        {
            return;
        }

        _workspaceViewportHeight = scrollViewer.ViewportHeight;
        UpdatePaneHeight();
    }

    private void UpdatePaneHeight()
    {
        if (ActualHeight <= 0) return;

        double headerHeight = ProjectControls.Visibility == Visibility.Collapsed
            ? 0d
            : ProjectControls.ActualHeight + ProjectControls.Margin.Top + ProjectControls.Margin.Bottom;
        // Use the measured inner viewport: the workspace can reserve space for
        // a continuation cue after its first layout. The queue remains below.
        double viewportHeight = _workspaceViewportHeight > 0d ? _workspaceViewportHeight : ActualHeight;
        double availableHeight = viewportHeight - StudioContent.Margin.Top - headerHeight - 12d;
        SetValue(PaneHeightPropertyKey, Math.Clamp(availableHeight, 320d, 700d));
    }

    private void UpdateResponsiveState(double width)
    {
        ResponsiveLayoutBands layout = ResponsiveLayout.ForWidth(width);
        SetValue(IsCompactLayoutPropertyKey, layout.IsCompact);
        SetValue(IsStandardLayoutPropertyKey, layout.IsStandard);
        SetValue(IsWideLayoutPropertyKey, layout.IsWide);
    }
}
