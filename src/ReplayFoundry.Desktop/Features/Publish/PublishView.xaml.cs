using System.Windows;
using System.Windows.Controls;
using ReplayFoundry.Desktop.Presentation.Responsive;

namespace ReplayFoundry.Desktop.Features.Publish;

public partial class PublishView : UserControl
{
    private static readonly DependencyPropertyKey IsCompactLayoutPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsCompactLayout), typeof(bool), typeof(PublishView), new FrameworkPropertyMetadata(false));
    private static readonly DependencyPropertyKey IsStandardLayoutPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsStandardLayout), typeof(bool), typeof(PublishView), new FrameworkPropertyMetadata(true));
    private static readonly DependencyPropertyKey IsWideLayoutPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsWideLayout), typeof(bool), typeof(PublishView), new FrameworkPropertyMetadata(false));
    public static readonly DependencyProperty IsCompactLayoutProperty = IsCompactLayoutPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsStandardLayoutProperty = IsStandardLayoutPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsWideLayoutProperty = IsWideLayoutPropertyKey.DependencyProperty;

    public PublishView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        Loaded += OnLoaded;
    }

    public bool IsCompactLayout => (bool)GetValue(IsCompactLayoutProperty);
    public bool IsStandardLayout => (bool)GetValue(IsStandardLayoutProperty);
    public bool IsWideLayout => (bool)GetValue(IsWideLayoutProperty);

    internal void SetResponsiveWidthForTest(double width) => UpdateResponsiveState(width);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateResponsiveState(ActualWidth);
        if (DataContext is PublishViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveState(e.NewSize.Width);

    private void UpdateResponsiveState(double width)
    {
        ResponsiveLayoutBands layout = ResponsiveLayout.ForWidth(width);
        SetValue(IsCompactLayoutPropertyKey, layout.IsCompact);
        SetValue(IsStandardLayoutPropertyKey, layout.IsStandard);
        SetValue(IsWideLayoutPropertyKey, layout.IsWide);
        ApplyContentLayout(layout.IsCompact);
    }

    private void ApplyContentLayout(bool compact)
    {
        LibraryPublishColumn.Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(0.9, GridUnitType.Star);
        CalendarPublishColumn.Width = compact ? new GridLength(0) : new GridLength(1.1, GridUnitType.Star);
        SecondaryPublishRow.Height = compact ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(PublishCalendar, compact ? 0 : 1);
        Grid.SetRow(PublishCalendar, compact ? 1 : 0);
        LibraryBrowser.Margin = compact ? new Thickness(0) : new Thickness(0, 0, 6, 0);
        PublishCalendar.Margin = compact ? new Thickness(0, 12, 0, 0) : new Thickness(6, 0, 0, 0);
    }
}
