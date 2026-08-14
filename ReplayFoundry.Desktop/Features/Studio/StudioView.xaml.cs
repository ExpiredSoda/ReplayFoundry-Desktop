using System.Windows;
using System.Windows.Controls;

namespace ReplayFoundry.Desktop.Features.Studio;

public partial class StudioView : UserControl
{
    private static readonly DependencyPropertyKey IsCompactLayoutPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsCompactLayout), typeof(bool), typeof(StudioView), new FrameworkPropertyMetadata(false));
    private static readonly DependencyPropertyKey IsStandardLayoutPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsStandardLayout), typeof(bool), typeof(StudioView), new FrameworkPropertyMetadata(true));
    private static readonly DependencyPropertyKey IsWideLayoutPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsWideLayout), typeof(bool), typeof(StudioView), new FrameworkPropertyMetadata(false));
    public static readonly DependencyProperty IsCompactLayoutProperty = IsCompactLayoutPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsStandardLayoutProperty = IsStandardLayoutPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsWideLayoutProperty = IsWideLayoutPropertyKey.DependencyProperty;

    public StudioView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        Loaded += OnLoaded;
    }

    public bool IsCompactLayout => (bool)GetValue(IsCompactLayoutProperty);
    public bool IsStandardLayout => (bool)GetValue(IsStandardLayoutProperty);
    public bool IsWideLayout => (bool)GetValue(IsWideLayoutProperty);

    internal void SetResponsiveWidthForTest(double width) => UpdateResponsiveState(width);

    private void OnLoaded(object sender, RoutedEventArgs e) => UpdateResponsiveState(ActualWidth);

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveState(e.NewSize.Width);

    private void UpdateResponsiveState(double width)
    {
        bool compact = width > 0 && width < 1120;
        bool standard = width >= 1120 && width < 1600;
        bool wide = width >= 1600;
        SetValue(IsCompactLayoutPropertyKey, compact);
        SetValue(IsStandardLayoutPropertyKey, standard || (!compact && !wide));
        SetValue(IsWideLayoutPropertyKey, wide);
    }
}
