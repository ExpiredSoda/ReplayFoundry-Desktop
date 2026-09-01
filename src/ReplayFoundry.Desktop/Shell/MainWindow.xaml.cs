using System;
using System.Windows;

namespace ReplayFoundry.Desktop.Shell;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(
        MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(
            viewModel);

        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        Closed += MainWindow_Closed;
    }

    private void MainWindow_Closed(
        object? sender,
        EventArgs e)
    {
        Closed -= MainWindow_Closed;
        _viewModel.Dispose();
    }

}
