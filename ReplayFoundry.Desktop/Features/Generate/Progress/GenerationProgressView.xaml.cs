using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Animation;

namespace ReplayFoundry.Desktop.Features.Generate.Progress;

public partial class GenerationProgressView : UserControl
{
    private GenerationProgressViewModel? _viewModel;
    private HwndTarget? _softwareRenderTarget;
    private RenderMode _previousRenderMode;
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
        RestoreHardwareRendering();
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
            UseSoftwareRenderingDuringGeneration();
            StartLogoMotion();
            return;
        }

        StopLogoMotion();
        RestoreHardwareRendering();
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

    private void UseSoftwareRenderingDuringGeneration()
    {
        if (_softwareRenderTarget is not null)
        {
            return;
        }

        Window? window = Window.GetWindow(this);
        if (window is null ||
            PresentationSource.FromVisual(window)?.CompositionTarget is not
                HwndTarget target)
        {
            return;
        }

        _previousRenderMode = target.RenderMode;
        target.RenderMode = RenderMode.SoftwareOnly;
        _softwareRenderTarget = target;
    }

    private void RestoreHardwareRendering()
    {
        if (_softwareRenderTarget is null)
        {
            return;
        }

        _softwareRenderTarget.RenderMode = _previousRenderMode;
        _softwareRenderTarget = null;
    }
}
