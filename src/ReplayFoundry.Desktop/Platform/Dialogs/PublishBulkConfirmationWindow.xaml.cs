using System.Windows;

namespace ReplayFoundry.Desktop.Platform.Dialogs;

public partial class PublishBulkConfirmationWindow : Window
{
    public PublishBulkConfirmationWindow(int videoCount)
    {
        InitializeComponent();
        Summary = $"{videoCount} saved video{(videoCount == 1 ? string.Empty : "s")} will be uploaded to YouTube and made public as soon as processing finishes.";
        DataContext = this;
    }
    public string Summary { get; }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
