using System.ComponentModel;
using System.Runtime.CompilerServices;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Preview;
using ReplayFoundry.Desktop.Presentation;

namespace ReplayFoundry.Desktop.Features.Generate.CompositionReview;

public sealed class CompositionPreviewViewModel :
    INotifyPropertyChanged,
    IDisposable
{
    private readonly PreparedGenerationSource _preparedSource;
    private readonly IVideoPreviewFrameProvider _frameProvider;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _loadSync = new();
    private Task? _activeLoad;
    private VideoPreviewFrame? _frame;
    private string? _error;
    private TimeSpan _requestedTimestamp;
    private int _width = 1280;
    private int _height = 720;
    private bool _isLoading;
    private bool _isDisposed;

    public CompositionPreviewViewModel(
        PreparedGenerationSource preparedSource,
        IVideoPreviewFrameProvider frameProvider)
    {
        _preparedSource = preparedSource ??
            throw new ArgumentNullException(nameof(preparedSource));
        _frameProvider = frameProvider ??
            throw new ArgumentNullException(nameof(frameProvider));
        _requestedTimestamp = CompositionPreviewTimestampPolicy.GetInitialTimestamp(
            preparedSource.Media.Duration);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TimeSpan RequestedTimestamp => _requestedTimestamp;

    public double RequestedTimestampSeconds
    {
        get => RequestedTimestamp.TotalSeconds;
        set
        {
            if (!double.IsFinite(value) ||
                value < 0 ||
                value >= _preparedSource.Media.Duration.TotalSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "The preview timestamp must be within the source timeline.");
            }

            TimeSpan timestamp = TimeSpan.FromSeconds(value);
            if (_requestedTimestamp == timestamp)
            {
                return;
            }

            _requestedTimestamp = timestamp;
            Error = null;
            Notify(
                nameof(RequestedTimestampSeconds),
                nameof(RequestedTimestamp),
                nameof(RequestedTimestampText),
                nameof(IsCurrent));
        }
    }

    public double MaximumTimestampSeconds =>
        TimeSpan.FromTicks(_preparedSource.Media.Duration.Ticks - 1).TotalSeconds;

    public string RequestedTimestampText =>
        MediaTimeFormatter.Format(RequestedTimestamp);

    public VideoPreviewFrame? Frame
    {
        get => _frame;
        private set
        {
            if (ReferenceEquals(_frame, value))
            {
                return;
            }

            _frame = value;
            Notify(
                nameof(Frame),
                nameof(HasFrame),
                nameof(IsCurrent),
                nameof(ActualDecodedTimestampText));
        }
    }

    public bool HasFrame => Frame is not null;

    public bool IsCurrent => Frame?.RequestedTimestamp == RequestedTimestamp;

    public int Width
    {
        get => _width;
        private set => SetField(ref _width, value);
    }

    public int Height
    {
        get => _height;
        private set => SetField(ref _height, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetField(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(CanRetry));
            }
        }
    }

    public string? Error
    {
        get => _error;
        private set
        {
            if (string.Equals(_error, value, StringComparison.Ordinal))
            {
                return;
            }

            _error = value;
            Notify(nameof(Error), nameof(HasError), nameof(CanRetry));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    public bool CanRetry => HasError && !IsLoading;

    public string? ActualDecodedTimestampText =>
        Frame?.DecodedTimestamp is TimeSpan decodedTimestamp
            ? MediaTimeFormatter.Format(decodedTimestamp)
            : null;

    internal bool WasSuccessfullyLoaded { get; private set; }

    public Task LoadAsync()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        lock (_loadSync)
        {
            if (Frame?.RequestedTimestamp == RequestedTimestamp)
            {
                return Task.CompletedTask;
            }

            if (_activeLoad is not null)
            {
                return _activeLoad;
            }

            TimeSpan requestedTimestamp = RequestedTimestamp;
            var completionSource = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _activeLoad = completionSource.Task;
            _ = CompleteLoadAsync(requestedTimestamp, completionSource);
            return completionSource.Task;
        }
    }

    internal void ReportUnexpectedFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!_isDisposed)
        {
            Error =
                "Replay Foundry could not observe the preview result. " +
                exception.Message;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
    }

    private async Task CompleteLoadAsync(
        TimeSpan requestedTimestamp,
        TaskCompletionSource completionSource)
    {
        try
        {
            await LoadCoreAsync(requestedTimestamp);
            completionSource.TrySetResult();
        }
        catch (OperationCanceledException)
        {
            completionSource.TrySetCanceled();
        }
        catch (Exception exception)
        {
            completionSource.TrySetException(exception);
        }
        finally
        {
            lock (_loadSync)
            {
                if (ReferenceEquals(_activeLoad, completionSource.Task))
                {
                    _activeLoad = null;
                }
            }
        }
    }

    private async Task LoadCoreAsync(TimeSpan requestedTimestamp)
    {
        IsLoading = true;
        Error = null;

        try
        {
            var request = new VideoPreviewFrameRequest(
                _preparedSource.Media,
                requestedTimestamp);
            VideoPreviewFrame frame = await _frameProvider.GetFrameAsync(
                request,
                _lifetimeCancellation.Token);

            ValidateFrame(frame, requestedTimestamp);
            Width = frame.Width;
            Height = frame.Height;
            WasSuccessfullyLoaded = true;
            Frame = frame;
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            Error = exception.Message;
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ValidateFrame(
        VideoPreviewFrame frame,
        TimeSpan requestedTimestamp)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (!string.Equals(
                frame.SourcePath,
                _preparedSource.Source.FullPath,
                StringComparison.OrdinalIgnoreCase) ||
            frame.SourceDuration != _preparedSource.Media.Duration ||
            frame.RequestedTimestamp != requestedTimestamp ||
            frame.CoordinateSpace !=
            CompositionCoordinateSpace.EffectiveDisplayNormalizedBeforeCrop)
        {
            throw new VideoPreviewFrameException(
                "The preview frame did not match the requested prepared source.");
        }
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void Notify(params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            OnPropertyChanged(propertyName);
        }
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}
