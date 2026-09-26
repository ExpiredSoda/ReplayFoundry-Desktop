using System.Windows;

namespace ReplayFoundry.Desktop.Platform.Dialogs;

public partial class RecentProjectsClearConfirmationWindow : Window
{
    public RecentProjectsClearConfirmationWindow(int projectCount)
    {
        if (projectCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectCount));
        }
        InitializeComponent();
        Summary = projectCount == 1
            ? "This removes 1 saved project from Recent Projects."
            : $"This removes all {projectCount} saved projects from Recent Projects.";
        DataContext = this;
    }

    public string Summary { get; }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private void Clear_Click(object sender, RoutedEventArgs e) =>
        DialogResult = true;
}
