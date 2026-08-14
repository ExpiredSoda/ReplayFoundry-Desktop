using System;
using System.Windows;
using System.Windows.Media;
using ReplayFoundry.Desktop.Shell.Windowing;

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

        ReadabilityState = WindowWorkAreaCalculatorState();
        Closed += MainWindow_Closed;
    }

    public ResponsiveReadabilityState ReadabilityState { get; private set; }

    private void MainWindow_Closed(
        object? sender,
        EventArgs e)
    {
        Closed -= MainWindow_Closed;
        _viewModel.Dispose();
    }

    private void ShellSurface_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        ReadabilityState = ResponsiveReadabilityState.Calculate(
            e.NewSize.Width,
            e.NewSize.Height,
            Application.Current?.TryFindResource("Accessibility.TextScale") is double textScale
                ? textScale
                : 1,
            (uint)Math.Max(96, Math.Round(VisualTreeHelper.GetDpi(this).PixelsPerInchX)));
    }

    private ResponsiveReadabilityState WindowWorkAreaCalculatorState() =>
        ResponsiveReadabilityState.Calculate(Width, Height, 1, 96);
}
