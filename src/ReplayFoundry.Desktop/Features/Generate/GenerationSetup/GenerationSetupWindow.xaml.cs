using System;
using System.Windows;
using ReplayFoundry.Desktop.Shell.Windowing;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

public partial class GenerationSetupWindow : Window
{
    private readonly GenerationSetupViewModel _viewModel;

    public GenerationSetupWindow(
        GenerationSetupViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.CancelRequested +=
            ViewModel_CancelRequested;

        _viewModel.FinishRequested +=
            ViewModel_FinishRequested;

        Loaded += GenerationSetupWindow_Loaded;

        Closed +=
            GenerationSetupWindow_Closed;
    }

    public GenerationSetupOptions? Result { get; private set; }

    private void GenerationSetupWindow_Loaded(object sender, RoutedEventArgs e)
    {
        DialogWindowSizing.FitToOwnerWorkArea(this);
    }

    private void ViewModel_CancelRequested(
        object? sender,
        EventArgs e)
    {
        DialogResult = false;
    }

    private void ViewModel_FinishRequested(
        object? sender,
        GenerationSetupCompletedEventArgs e)
    {
        Result = e.Options;
        DialogResult = true;
    }

    private void GenerationSetupWindow_Closed(
        object? sender,
        EventArgs e)
    {
        _viewModel.CancelRequested -=
            ViewModel_CancelRequested;

        _viewModel.FinishRequested -=
            ViewModel_FinishRequested;

        _viewModel.Dispose();

        Loaded -= GenerationSetupWindow_Loaded;

        Closed -=
            GenerationSetupWindow_Closed;
    }
}
