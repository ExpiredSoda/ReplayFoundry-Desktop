using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Shell.Windowing;

namespace ReplayFoundry.Desktop.Features.Publish;

public partial class PublishPreparationWindow : Window
{
    private readonly PublishViewModel _viewModel;
    private readonly DispatcherTimer _positionTimer;
    private bool _isPlaying;
    private bool _isMediaOpened;
    private bool _isScrubbing;
    private bool _isUpdatingSlider;
    private bool _resumeAfterScrub;

    public PublishPreparationWindow(PublishViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        InitializeComponent();
        _positionTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(100),
            DispatcherPriority.Background,
            (_, _) => UpdatePositionFromPlayer(),
            Dispatcher);
        PreviewPosition.AddHandler(
            Thumb.DragStartedEvent,
            new DragStartedEventHandler(PreviewPosition_DragStarted));
        PreviewPosition.AddHandler(
            Thumb.DragCompletedEvent,
            new DragCompletedEventHandler(PreviewPosition_DragCompleted));
        PreviewPosition.PreviewMouseLeftButtonDown +=
            PreviewPosition_PreviewMouseLeftButtonDown;
        PreviewPosition.PreviewMouseLeftButtonUp +=
            PreviewPosition_PreviewMouseLeftButtonUp;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Loaded += Window_Loaded;
        Closed += Window_Closed;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        DialogWindowSizing.FitToOwnerWorkArea(this);
        OpenSelectedAsset();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _positionTimer.Stop();
        PreviewPlayer.Stop();
        PreviewPlayer.Close();
    }

    private void ViewModel_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PublishViewModel.SelectedAsset))
        {
            OpenSelectedAsset();
        }
    }

    private void OpenSelectedAsset()
    {
        _positionTimer.Stop();
        _isMediaOpened = false;
        _isPlaying = false;
        _isScrubbing = false;
        _resumeAfterScrub = false;
        PlayPauseIcon.IconKey = "Icon.Play";
        SetSliderValue(0);
        PreviewPosition.Maximum = 1;
        UpdateTimeText(TimeSpan.Zero, TimeSpan.Zero);
        PreviewPlayer.Close();

        string? path = _viewModel.SelectedAsset?.OutputFullPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ShowPreviewStatus("The selected Library video is not available on this PC.");
            PreviewPlayer.Source = null;
            return;
        }

        ShowPreviewStatus("Loading preview…");
        PreviewPlayer.Source = new Uri(path, UriKind.Absolute);
        // Manual MediaElement behavior does not initialize the graph until a
        // transport method is called. Pausing primes the exact finalized file
        // without starting audible playback.
        PreviewPlayer.Pause();
    }

    private void PreviewPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        _isMediaOpened = true;
        TimeSpan duration = PreviewPlayer.NaturalDuration.HasTimeSpan
            ? PreviewPlayer.NaturalDuration.TimeSpan
            : _viewModel.SelectedAsset?.Duration ?? TimeSpan.Zero;
        PreviewPosition.Maximum = Math.Max(0.001, duration.TotalSeconds);
        PreviewPlayer.Position = TimeSpan.Zero;
        PreviewPlayer.Pause();
        _isPlaying = false;
        PlayPauseIcon.IconKey = "Icon.Play";
        PreviewStatusSurface.Visibility = Visibility.Collapsed;
        SetSliderValue(0);
        UpdateTimeText(TimeSpan.Zero, duration);
        _positionTimer.Start();
    }

    private void PreviewPlayer_MediaFailed(
        object sender,
        ExceptionRoutedEventArgs e)
    {
        _isMediaOpened = false;
        _isPlaying = false;
        _positionTimer.Stop();
        PlayPauseIcon.IconKey = "Icon.Play";
        ShowPreviewStatus(
            "This rendered video could not be opened for preview. " +
            (e.ErrorException?.Message ?? "The media decoder did not provide details."));
    }

    private void PreviewPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        PreviewPlayer.Position = TimeSpan.Zero;
        PreviewPlayer.Pause();
        _isPlaying = false;
        PlayPauseIcon.IconKey = "Icon.Play";
        SetSliderValue(0);
        UpdateTimeText(TimeSpan.Zero, GetDuration());
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) =>
        TogglePlayback();

    private void Back_Click(object sender, RoutedEventArgs e) => Seek(-5);
    private void Forward_Click(object sender, RoutedEventArgs e) => Seek(5);

    private void PreviewPosition_DragStarted(
        object sender,
        DragStartedEventArgs e) => BeginScrub();

    private void PreviewPosition_DragCompleted(
        object sender,
        DragCompletedEventArgs e) => EndScrub();

    private void PreviewPosition_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e) => BeginScrub();

    private void PreviewPosition_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e) => EndScrub();

    private void PreviewPosition_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingSlider || !_isMediaOpened)
        {
            return;
        }

        if (_isScrubbing || PreviewPosition.IsKeyboardFocusWithin)
        {
            SeekTo(PreviewPosition.Value);
        }
    }

    private void BeginScrub()
    {
        if (!_isMediaOpened || _isScrubbing) return;
        _isScrubbing = true;
        _resumeAfterScrub = _isPlaying;
        if (_isPlaying)
        {
            PreviewPlayer.Pause();
            _isPlaying = false;
            PlayPauseIcon.IconKey = "Icon.Play";
        }
    }

    private void EndScrub()
    {
        if (!_isScrubbing) return;
        SeekTo(PreviewPosition.Value);
        _isScrubbing = false;
        if (_resumeAfterScrub)
        {
            PreviewPlayer.Play();
            _isPlaying = true;
            PlayPauseIcon.IconKey = "Icon.Pause";
        }
        _resumeAfterScrub = false;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void SaveDraft_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase or ComboBox or DatePicker)
        {
            return;
        }

        if (e.Key == Key.Space)
        {
            TogglePlayback();
            e.Handled = true;
        }
        else if (Keyboard.FocusedElement is not Slider && e.Key == Key.Left)
        {
            Seek(-5);
            e.Handled = true;
        }
        else if (Keyboard.FocusedElement is not Slider && e.Key == Key.Right)
        {
            Seek(5);
            e.Handled = true;
        }
    }

    private void TogglePlayback()
    {
        if (!_isMediaOpened)
        {
            return;
        }
        if (_isPlaying)
        {
            PreviewPlayer.Pause();
        }
        else
        {
            PreviewPlayer.Play();
        }
        _isPlaying = !_isPlaying;
        PlayPauseIcon.IconKey = _isPlaying ? "Icon.Pause" : "Icon.Play";
    }

    private void Seek(double seconds)
    {
        if (!_isMediaOpened) return;
        SeekTo(PreviewPlayer.Position.TotalSeconds + seconds);
        SetSliderValue(PreviewPlayer.Position.TotalSeconds);
    }

    private void SeekTo(double seconds)
    {
        double maximum = Math.Max(0, PreviewPosition.Maximum);
        double target = Math.Clamp(seconds, 0, maximum);
        PreviewPlayer.Position = TimeSpan.FromSeconds(target);
        UpdateTimeText(PreviewPlayer.Position, GetDuration());
    }

    private void UpdatePositionFromPlayer()
    {
        if (!_isMediaOpened || _isScrubbing)
        {
            return;
        }
        SetSliderValue(PreviewPlayer.Position.TotalSeconds);
        UpdateTimeText(PreviewPlayer.Position, GetDuration());
    }

    private void SetSliderValue(double value)
    {
        _isUpdatingSlider = true;
        try
        {
            PreviewPosition.Value = Math.Clamp(
                value,
                PreviewPosition.Minimum,
                PreviewPosition.Maximum);
        }
        finally
        {
            _isUpdatingSlider = false;
        }
    }

    private TimeSpan GetDuration() => PreviewPlayer.NaturalDuration.HasTimeSpan
        ? PreviewPlayer.NaturalDuration.TimeSpan
        : _viewModel.SelectedAsset?.Duration ?? TimeSpan.Zero;

    private void UpdateTimeText(TimeSpan current, TimeSpan duration) =>
        PreviewTimeText.Text =
            $"{MediaTimeFormatter.Format(current)} / {MediaTimeFormatter.Format(duration)}";

    private void ShowPreviewStatus(string message)
    {
        PreviewStatusText.Text = message;
        PreviewStatusSurface.Visibility = Visibility.Visible;
    }
}
