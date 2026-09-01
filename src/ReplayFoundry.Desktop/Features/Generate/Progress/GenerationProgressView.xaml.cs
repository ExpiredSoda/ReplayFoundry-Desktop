using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace ReplayFoundry.Desktop.Features.Generate.Progress;

public partial class GenerationProgressView : UserControl
{
    private GenerationProgressViewModel? _viewModel;
    private bool _logoMotionRunning;

    public GenerationProgressView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as GenerationProgressViewModel);
        RefreshRunningVisuals();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(null);
        StopLogoMotion();
    }

    private void OnDataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        AttachViewModel(e.NewValue as GenerationProgressViewModel);
        RefreshRunningVisuals();
    }

    private void AttachViewModel(GenerationProgressViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GenerationProgressViewModel.IsRunning) or
            nameof(GenerationProgressViewModel.State))
        {
            RefreshRunningVisuals();
        }
    }

    private void RefreshRunningVisuals()
    {
        bool isRunning = IsLoaded && _viewModel?.IsRunning == true;
        if (isRunning)
        {
            StartLogoMotion();
            return;
        }

        StopLogoMotion();
    }

    private void StartLogoMotion()
    {
        if (_logoMotionRunning || !SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        Storyboard storyboard = (Storyboard)FindResource(
            "GenerationProgress.LogoAssemblyMotion");
        storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
        _logoMotionRunning = true;
    }

    private void StopLogoMotion()
    {
        if (!_logoMotionRunning)
        {
            return;
        }

        Storyboard storyboard = (Storyboard)FindResource(
            "GenerationProgress.LogoAssemblyMotion");
        storyboard.Remove(this);
        _logoMotionRunning = false;
    }

}
