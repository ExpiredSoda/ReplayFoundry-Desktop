using System.Windows;
using System.Windows.Controls;

namespace ReplayFoundry.Desktop.Features.Publish.Sections;

public partial class PublishAssetView : UserControl
{
    public PublishAssetView() => InitializeComponent();
}

public partial class PublishChecklistView : UserControl
{
    public PublishChecklistView() => InitializeComponent();
}

public partial class PublishDestinationsView : UserControl
{
    public PublishDestinationsView() => InitializeComponent();
    private void ChannelOptions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }
}

public partial class PublishMetadataView : UserControl
{
    public PublishMetadataView() => InitializeComponent();
}

public partial class PublishOutputSettingsView : UserControl
{
    public PublishOutputSettingsView() => InitializeComponent();
}

public partial class PublishQueueHistoryView : UserControl
{
    public PublishQueueHistoryView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width > 0 && e.NewSize.Width < 944;
        HistoryColumn.Width = compact ? new GridLength(0) : new GridLength(1.2, GridUnitType.Star);
        PrimaryQueueRow.Height = compact ? new GridLength(118) : new GridLength(1, GridUnitType.Star);
        SecondaryQueueRow.Height = compact ? new GridLength(130) : new GridLength(0);
        Grid.SetColumn(HistoryCard, compact ? 0 : 1);
        Grid.SetRow(HistoryCard, compact ? 1 : 0);
        QueueCard.Margin = compact ? new Thickness(0) : new Thickness(0, 0, 6, 0);
        HistoryCard.Margin = compact ? new Thickness(0, 12, 0, 0) : new Thickness(6, 0, 0, 0);
    }

    private void HistoryOptions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void Analytics_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PublishViewModel model) return;
        new PublishAnalyticsWindow(model) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
}

public partial class PublishCalendarView : UserControl
{
    public PublishCalendarView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => CalendarDaysList.Height = Math.Clamp(ActualHeight * .48, 192, 350);
    }
}
