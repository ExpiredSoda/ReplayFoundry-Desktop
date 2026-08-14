using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.MomentGuidance;

public partial class MomentGuidanceStepView : UserControl
{
    private readonly DispatcherTimer _positionTimer;
    private MomentGuidanceStepViewModel? _viewModel;
    private bool _isScrubbing;
    private bool _resumeAfterScrub;

    public MomentGuidanceStepView()
    {
        InitializeComponent();
        _positionTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(100),
            DispatcherPriority.Background,
            OnPositionTick,
            Dispatcher);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void RefreshPreview()
    {
        if (DataContext is MomentGuidanceStepViewModel viewModel &&
            viewModel.SelectedSource.RefreshPreviewCommand.CanExecute(null))
        {
            viewModel.SelectedSource.RefreshPreviewCommand.Execute(null);
        }
    }

    private void TimelineSlider_MouseLeftButtonUp(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        EndScrub();
        RefreshPreview();
    }

    private void TimelineSlider_MouseLeftButtonDown(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        _isScrubbing = true;
        _resumeAfterScrub = _viewModel?.SelectedSource.IsPlaybackPlaying == true;
        PriorityPlayer.Pause();
        _viewModel?.SelectedSource.ReportPlaybackState(false);
    }

    private void TimelineSlider_LostMouseCapture(
        object sender,
        System.Windows.Input.MouseEventArgs e) => EndScrub();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Bind(DataContext as MomentGuidanceStepViewModel);
        OpenSelectedSource();
        _positionTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _positionTimer.Stop();
        PriorityPlayer.Stop();
        Bind(null);
    }

    private void OnDataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        Bind(e.NewValue as MomentGuidanceStepViewModel);
        if (IsLoaded)
        {
            OpenSelectedSource();
        }
    }

    private void Bind(MomentGuidanceStepViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel)) return;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }
        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MomentGuidanceStepViewModel.SelectedSource))
        {
            OpenSelectedSource();
        }
    }

    private void OpenSelectedSource()
    {
        PriorityPlayer.Stop();
        if (_viewModel?.SelectedSource is not { } source)
        {
            PriorityPlayer.Source = null;
            return;
        }
        source.ReportPlaybackState(false);
        PriorityPlayer.Source = new Uri(source.SourceFullPath, UriKind.Absolute);
        PriorityPlayer.Position = TimeSpan.FromSeconds(source.CurrentPositionSeconds);
    }

    private void PriorityPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedSource is not { } source) return;
        source.ReportPlaybackOpened();
        PriorityPlayer.Position = TimeSpan.FromSeconds(source.CurrentPositionSeconds);
    }

    private void PriorityPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        PriorityPlayer.Pause();
        _viewModel?.SelectedSource.ReportPlaybackState(false);
    }

    private void PriorityPlayer_MediaFailed(
        object sender,
        ExceptionRoutedEventArgs e) =>
        _viewModel?.SelectedSource.ReportPlaybackFailure(
            e.ErrorException?.Message ?? "the local decoder rejected the file");

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedSource is not { IsPlaybackOpen: true } source)
        {
            return;
        }
        if (source.IsPlaybackPlaying)
        {
            PriorityPlayer.Pause();
            source.ReportPlaybackState(false);
        }
        else
        {
            PriorityPlayer.Play();
            source.ReportPlaybackState(true);
        }
    }

    private void Rewind_Click(object sender, RoutedEventArgs e) => SeekBy(-5);
    private void Forward_Click(object sender, RoutedEventArgs e) => SeekBy(5);

    private void SeekBy(double seconds)
    {
        if (_viewModel?.SelectedSource is not { IsPlaybackOpen: true } source)
        {
            return;
        }
        source.CurrentPositionSeconds = Math.Clamp(
            source.CurrentPositionSeconds + seconds,
            0,
            source.MaximumSeconds);
        PriorityPlayer.Position = TimeSpan.FromSeconds(source.CurrentPositionSeconds);
    }

    private void OnPositionTick(object? sender, EventArgs e)
    {
        if (!_isScrubbing &&
            _viewModel?.SelectedSource is { IsPlaybackPlaying: true } source)
        {
            source.CurrentPositionSeconds = Math.Clamp(
                PriorityPlayer.Position.TotalSeconds,
                0,
                source.MaximumSeconds);
        }
    }

    private void EndScrub()
    {
        if (!_isScrubbing || _viewModel?.SelectedSource is not { } source)
        {
            return;
        }
        _isScrubbing = false;
        PriorityPlayer.Position = TimeSpan.FromSeconds(source.CurrentPositionSeconds);
        if (_resumeAfterScrub && source.IsPlaybackOpen)
        {
            PriorityPlayer.Play();
            source.ReportPlaybackState(true);
        }
        _resumeAfterScrub = false;
    }
}
