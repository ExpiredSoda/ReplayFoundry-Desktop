using System.Windows;
using ReplayFoundry.Desktop.Features.Settings;

namespace ReplayFoundry.Desktop.Platform.Dialogs;

public partial class LocalDataCleanupConfirmationWindow : Window
{
    public LocalDataCleanupConfirmationWindow(
        ReplayFoundryLocalDataResetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        InitializeComponent();
        Items = request.Kinds.Select(Label).ToArray();
        DataContext = this;
    }

    public IReadOnlyList<string> Items { get; }

    private static string Label(ReplayFoundryLocalDataKind kind) => kind switch
    {
        ReplayFoundryLocalDataKind.DerivedCaches => "• Temporary cache files",
        ReplayFoundryLocalDataKind.DiagnosticsAndReports =>
            "• Diagnostic logs and saved feedback or crash reports",
        ReplayFoundryLocalDataKind.PreferencesAndHistory =>
            "• App preferences, remembered source choices, YouTube connection permission, recent-project summaries, and local publishing history",
        ReplayFoundryLocalDataKind.LibraryCatalog =>
            "• Library catalog records (video files remain on disk)",
        ReplayFoundryLocalDataKind.StudioProjects =>
            "• Saved Studio project files",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private void Schedule_Click(object sender, RoutedEventArgs e) =>
        DialogResult = true;
}
