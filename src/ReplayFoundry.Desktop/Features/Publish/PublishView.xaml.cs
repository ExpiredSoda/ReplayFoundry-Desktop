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
        ApplyContentLayout(1280);
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
        ApplyContentLayout(width);
    }

    private void ApplyContentLayout(double width)
    {
        bool stacked = width > 0 && width < 900;
        bool activityBesidePlanner = width <= 0 || width >= 1180;
        LibraryPublishColumn.Width = new GridLength(.95, GridUnitType.Star);
        CalendarPublishColumn.Width = stacked ? new GridLength(0) : new GridLength(1.05, GridUnitType.Star);
        ActivityPublishColumn.Width = new GridLength(activityBesidePlanner ? Math.Clamp(width * .27, 320, 420) : 0);
        PrimaryPublishRow.Height = activityBesidePlanner ? new GridLength(1, GridUnitType.Star) : new GridLength(stacked ? 440 : 510);
        SecondaryPublishRow.Height = new GridLength(stacked ? 480 : activityBesidePlanner ? 0 : 500);
        TertiaryPublishRow.Height = new GridLength(stacked ? 500 : 0);
        Grid.SetColumn(PublishCalendar, stacked ? 0 : 1);
        Grid.SetRow(PublishCalendar, stacked ? 1 : 0);
        Grid.SetColumn(PublishActivity, activityBesidePlanner ? 2 : 0);
        Grid.SetRow(PublishActivity, stacked ? 2 : activityBesidePlanner ? 0 : 1);
        Grid.SetColumnSpan(PublishActivity, !stacked && !activityBesidePlanner ? 2 : 1);
        LibraryBrowser.Margin = stacked ? new Thickness(0) : new Thickness(0, 0, 6, 0);
        PublishCalendar.Margin = stacked ? new Thickness(0, 12, 0, 0) : new Thickness(6, 0, activityBesidePlanner ? 6 : 0, 0);
        PublishActivity.Margin = activityBesidePlanner ? new Thickness(6, 0, 0, 0) : new Thickness(0, 12, 0, 0);
        // Short windows scroll the workspace before compressing readable content into a strip.
        WorkspaceLayout.Height = Math.Max(stacked ? 1500 : activityBesidePlanner ? 560 : 1090, ActualHeight - 48);
    }
}
