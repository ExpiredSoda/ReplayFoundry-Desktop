using System.Windows;
using ReplayFoundry.Desktop.Features.Library;

namespace ReplayFoundry.Desktop.Platform.Dialogs;

public partial class LibraryRemovalConfirmationWindow : Window
{
    public LibraryRemovalConfirmationWindow(
        IReadOnlyList<LibraryMediaAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        InitializeComponent();
        Summary = assets.Count == 1
            ? $"Remove ‘{assets[0].Title}’ from your Library?"
            : $"Remove {assets.Count} selected videos from your Library?";
        DataContext = this;
    }

    public string Summary { get; }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private void Remove_Click(object sender, RoutedEventArgs e) =>
        DialogResult = true;
}
