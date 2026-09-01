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
        bool compact = e.NewSize.Width > 0 && e.NewSize.Width < 900;
        HistoryColumn.Width = compact ? new GridLength(0) : new GridLength(1.2, GridUnitType.Star);
        SecondaryQueueRow.Height = compact ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(HistoryCard, compact ? 0 : 1);
        Grid.SetRow(HistoryCard, compact ? 1 : 0);
        QueueCard.Margin = compact ? new Thickness(0) : new Thickness(0, 0, 6, 0);
        HistoryCard.Margin = compact ? new Thickness(0, 12, 0, 0) : new Thickness(6, 0, 0, 0);
    }
}

public partial class PublishCalendarView : UserControl
{
    public PublishCalendarView() => InitializeComponent();
}
