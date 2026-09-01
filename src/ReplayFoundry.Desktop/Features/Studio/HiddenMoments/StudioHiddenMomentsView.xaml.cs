using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ReplayFoundry.Desktop.Features.Studio.HiddenMoments;

public partial class StudioHiddenMomentsView : UserControl
{
    private readonly DispatcherTimer _acceptanceLivenessTimer;

    public StudioHiddenMomentsView()
    {
        InitializeComponent();
        _acceptanceLivenessTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            OnAcceptanceLivenessTick,
            Dispatcher);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateLayoutMode(ActualWidth);
        RefreshAcceptanceLiveness();
        _acceptanceLivenessTimer.Start();
        Focus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        _acceptanceLivenessTimer.Stop();

    private void OnAcceptanceLivenessTick(object? sender, EventArgs e) =>
        RefreshAcceptanceLiveness();

    private void RefreshAcceptanceLiveness() =>
        (DataContext as StudioHiddenMomentsViewModel)?
            .RefreshAcceptanceLiveness();

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateLayoutMode(e.NewSize.Width);

    private void UpdateLayoutMode(double width)
    {
        bool compact = width > 0 && width < 900;
        DetailColumn.Width = compact ? new GridLength(0) : new GridLength(360);
        DetailRow.Height = compact ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(DetailPanel, compact ? 0 : 1);
        Grid.SetRow(DetailPanel, compact ? 1 : 0);
        DetailPanel.Margin = compact
            ? new Thickness(0, 14, 0, 0)
            : new Thickness(14, 0, 0, 0);
    }
}
