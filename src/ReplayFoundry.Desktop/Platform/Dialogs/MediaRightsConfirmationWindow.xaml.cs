using System.Windows;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;

namespace ReplayFoundry.Desktop.Platform.Dialogs;

public partial class MediaRightsConfirmationWindow : Window
{
    public MediaRightsConfirmationWindow(
        IReadOnlyList<SelectedVideoSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        InitializeComponent();
        SelectionSummary = sources.Count == 1
            ? "1 local video selected"
            : $"{sources.Count} local videos selected";
        SourceNames = sources
            .Select(static source => source.FileName)
            .ToArray();
        DataContext = this;
    }

    public string SelectionSummary { get; }
    public IReadOnlyList<string> SourceNames { get; }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private void Confirm_Click(object sender, RoutedEventArgs e) =>
        DialogResult = true;
}
