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
        Unloaded += (_, _) => { if (DataContext is PublishViewModel model) model.PropertyChanged -= ViewModelChanged; };
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
            viewModel.PropertyChanged -= ViewModelChanged;
            viewModel.PropertyChanged += ViewModelChanged;
            await viewModel.InitializeAsync();
        }
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveState(e.NewSize.Width);
    private void ViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PublishViewModel.SelectedPublishView)) ApplyContentLayout(IsCompactLayout);
    }

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
        bool calendar = (DataContext as PublishViewModel)?.SelectedPublishView == "Calendar";
        LibraryPublishColumn.Width = new GridLength(calendar && !compact ? .9 : 1, GridUnitType.Star);
        CalendarPublishColumn.Width = calendar && !compact ? new GridLength(1.1, GridUnitType.Star) : new GridLength(0);
        SecondaryPublishRow.Height = calendar && compact ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(PublishCalendar, compact ? 0 : 1); Grid.SetRow(PublishCalendar, compact ? 1 : 0);
        LibraryBrowser.Visibility = Visibility.Visible;
        PublishCalendar.Visibility = calendar ? Visibility.Visible : Visibility.Collapsed;
        LibraryBrowser.Margin = new Thickness(0); PublishCalendar.Margin = new Thickness(0);
    }
}
