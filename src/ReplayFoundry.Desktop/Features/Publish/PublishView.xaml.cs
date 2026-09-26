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
        ApplyContentLayout(compact: false);
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
        // Keep both panels together while there is room for readable clip titles and day cells.
        // On small windows the same controls stack, with bounded lists and page scrolling.
        ApplyContentLayout(width > 0 && width < 960);
    }

    private void ApplyContentLayout(bool compact)
    {
        LibraryPublishColumn.Width = new GridLength(compact ? 1 : .85, GridUnitType.Star);
        CalendarPublishColumn.Width = compact ? new GridLength(0) : new GridLength(1.15, GridUnitType.Star);
        PrimaryPublishRow.Height = compact ? new GridLength(470) : new GridLength(1, GridUnitType.Star);
        SecondaryPublishRow.Height = compact ? new GridLength(490) : new GridLength(0);
        Grid.SetColumn(PublishCalendar, compact ? 0 : 1); Grid.SetRow(PublishCalendar, compact ? 1 : 0);
        LibraryBrowser.Margin = compact ? new Thickness(0) : new Thickness(0, 0, 6, 0);
        PublishCalendar.Margin = compact ? new Thickness(0, 12, 0, 0) : new Thickness(6, 0, 0, 0);
        ActivityRow.Height = new GridLength(compact ? 260 : 112);
        WorkspaceLayout.Height = Math.Max(compact ? 1290 : 560, ActualHeight - 48);
    }
}
