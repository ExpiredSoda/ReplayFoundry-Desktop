using System;
using System.Windows;
using System.Windows.Data;
using ReplayFoundry.Desktop.Shell.Windowing;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

public partial class GenerationSetupWindow : Window
{
    private static readonly DependencyPropertyKey IsCompactLayoutPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsCompactLayout),
            typeof(bool),
            typeof(GenerationSetupWindow),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsCompactLayoutProperty =
        IsCompactLayoutPropertyKey.DependencyProperty;

    private readonly GenerationSetupViewModel _viewModel;
    private object? _displayedStepContent;

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

    public bool IsCompactLayout => (bool)GetValue(IsCompactLayoutProperty);

    private void GenerationSetupWindow_Loaded(object sender, RoutedEventArgs e)
    {
        DialogWindowSizing.FitToOwnerWorkArea(this);
        UpdateCompactLayout();
    }

    private void GenerationSetupWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCompactLayout();
    }

    private void UpdateCompactLayout() =>
        SetValue(IsCompactLayoutPropertyKey, ActualWidth > 0 && ActualWidth < 1040);

    private void StepContent_TargetUpdated(object sender, DataTransferEventArgs e)
    {
        object? currentContent = StepContent.Content;
        if (ReferenceEquals(currentContent, _displayedStepContent)) return;

        _displayedStepContent = currentContent;
        StepScrollViewer.ScrollToTop();
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
