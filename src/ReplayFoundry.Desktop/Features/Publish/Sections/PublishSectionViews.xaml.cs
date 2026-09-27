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
    public PublishQueueHistoryView() => InitializeComponent();

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
