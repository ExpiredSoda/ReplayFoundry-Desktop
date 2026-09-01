using System.Windows;
using System.Windows.Input;
using ReplayFoundry.Desktop.Shell.Windowing;

namespace ReplayFoundry.Desktop.Features.Publish;

public partial class PublishHistoryWindow : Window
{
    public PublishHistoryWindow(PublishHistoryViewModel viewModel)
    {
        DataContext = viewModel ??
            throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        Loaded += Window_Loaded;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) =>
        DialogWindowSizing.FitToOwnerWorkArea(this);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Close();
        e.Handled = true;
    }
}
