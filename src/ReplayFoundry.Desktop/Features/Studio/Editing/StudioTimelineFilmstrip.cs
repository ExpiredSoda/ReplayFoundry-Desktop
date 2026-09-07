using System.IO;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Preview;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed record StudioTimelineThumbnail(double StartSeconds, double EndSeconds, VideoPreviewFrame Frame);

public sealed class StudioTimelineFilmstrip : ObservableObject, IDisposable
{
    private const int FrameCount = 20;
    private const int MaximumCachedFrames = 160;
    private readonly IVideoPreviewFrameProvider? _provider;
    private readonly DebouncedUiAction _debounce;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, VideoPreviewFrame> _cache = new(StringComparer.Ordinal);
    private CancellationTokenSource? _cancellation;
    private MediaProbeResult? _media;
    private double _start;
    private double _duration;
    private bool _disposed;
    private Task _quiescence = Task.CompletedTask;
    public IReadOnlyList<StudioTimelineThumbnail> Frames { get; private set; } = [];
    public string Status { get; private set; } = string.Empty;

    public StudioTimelineFilmstrip(IVideoPreviewFrameProvider? provider)
    {
        _provider = provider;
        _debounce = new(TimeSpan.FromMilliseconds(300), () => _ = LoadAsync());
    }
    public void Request(MediaProbeResult? media, double start, double duration)
    {
        if (_disposed) return;
        _debounce.Cancel();
        _cancellation?.Cancel();
        bool sourceChanged = !string.Equals(_media?.FullPath, media?.FullPath, StringComparison.OrdinalIgnoreCase);
        _media = media; _start = start; _duration = duration;
        if (sourceChanged || media is null) { Frames = []; OnPropertyChanged(nameof(Frames)); }
        Status = media is null || _provider is null ? string.Empty : "Loading scene thumbnails…";
        OnPropertyChanged(nameof(Status));
        if (media is not null && _provider is not null) _debounce.Restart();
    }
    internal Task LoadAsync()
    {
        Task current = LoadCoreAsync();
        _quiescence = _quiescence.IsCompleted ? current : Task.WhenAll(_quiescence, current);
        return current;
    }
    internal Task StopAsync(CancellationToken cancellationToken)
    {
        Request(null, 0, 0);
        return _quiescence.WaitAsync(cancellationToken);
    }
    private async Task LoadCoreAsync()
    {
        _debounce.Cancel();
        if (_disposed || _media is null || _provider is null || _duration <= 0) return;
        _cancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var media = _media;
        double start = _start, duration = Math.Min(_duration, media.Duration.TotalSeconds - start);
        bool entered = false;
        try
        {
            await _gate.WaitAsync(cancellation.Token);
            entered = true;
            var info = new FileInfo(media.FullPath);
            string identity = $"{media.FullPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            var frames = new List<StudioTimelineThumbnail>(FrameCount);
            for (int i = 0; i < FrameCount; i++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                double from = start + duration * i / FrameCount;
                double to = start + duration * (i + 1) / FrameCount;
                double timestamp = Math.Clamp(Math.Round((from + to) * 2) / 4, from, Math.Max(from, to - 0.001));
                string key = $"{identity}|{BitConverter.DoubleToInt64Bits(timestamp)}";
                if (!_cache.TryGetValue(key, out VideoPreviewFrame? frame))
                {
                    frame = await _provider.GetFrameAsync(new VideoPreviewFrameRequest(media, TimeSpan.FromSeconds(timestamp),
                        maximumWidth: 256, maximumHeight: 144, workIntent: VideoPreviewWorkIntent.BackgroundFilmstrip), cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (_cache.Count >= MaximumCachedFrames) _cache.Remove(_cache.Keys.First());
                    _cache[key] = frame;
                }
                frames.Add(new(from, to, frame));
                if (ReferenceEquals(_cancellation, cancellation))
                {
                    Frames = frames.ToArray(); OnPropertyChanged(nameof(Frames));
                }
            }
            if (ReferenceEquals(_cancellation, cancellation)) { Status = string.Empty; OnPropertyChanged(nameof(Status)); }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!cancellation.IsCancellationRequested && ReferenceEquals(_cancellation, cancellation))
            {
                Status = "Scene thumbnails couldn't load. You can still use the timeline.";
                OnPropertyChanged(nameof(Status));
            }
        }
        finally
        {
            if (entered) _gate.Release();
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
            cancellation.Dispose();
        }
    }
    public void Dispose()
    {
        _disposed = true;
        _debounce.Dispose();
        _cancellation?.Cancel();
        // The active extraction owns cancellation disposal and releases the gate.
        _cache.Clear(); Frames = [];
    }
}
