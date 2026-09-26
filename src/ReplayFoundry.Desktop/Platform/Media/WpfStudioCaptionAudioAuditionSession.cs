using System.Globalization;
using System.Windows.Media;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.AudioExtraction;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed class WpfStudioCaptionAudioAuditionSession : IStudioCaptionAudioAuditionSession
{
    private readonly IAudioSegmentExtractor _extractor;
    private GenerationOutputAsset? _asset;
    private int _streamIndex;
    private bool _busy, _disposed, _playing;
    private MediaPlayer? _player;
    private ExtractedAudioSegment? _sample;
    private CancellationTokenSource? _cancellation;
    private Task? _pending;
    private string? _measuredLevel;
    public WpfStudioCaptionAudioAuditionSession(IAudioSegmentExtractor? extractor = null)
    {
        _extractor = extractor ?? AudioSegmentExtractionFactory.CreateDefault();
    }
    public event EventHandler? Changed;
    public bool IsActive => _playing || _cancellation is not null;
    public string Status { get; private set; } = "Listen to the selected track before regenerating. Recording track names can be misleading.";
    public void Bind(GenerationOutputAsset? asset, int streamIndex)
    {
        Stop(); _asset = asset; _streamIndex = streamIndex; _measuredLevel = null;
        Status = "Listen to the selected track before regenerating. Recording track names can be misleading."; Notify();
    }
    public void SetHostBusy(bool busy) { _busy = busy; if (busy) Stop(); Notify(); }
    public bool CanListen => !_disposed && !_busy && _cancellation is null &&
        _asset?.SourceMedia.AudioStreams.Any(stream => stream.Index == _streamIndex) == true;
    public Task ListenAsync() => CanListen ? _pending = ListenCoreAsync() : _pending ?? Task.CompletedTask;
    private async Task ListenCoreAsync()
    {
        if (!CanListen || _asset is null) return;
        Stop();
        _measuredLevel = null;
        GenerationOutputAsset asset = _asset;
        int stream = _streamIndex;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        Status = "Preparing the selected track from the beginning of this cut…"; Notify();
        ExtractedAudioSegment? extracted = null;
        try
        {
            extracted = await _extractor.ExtractAsync(CreateRequest(asset, stream), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_disposed || !ReferenceEquals(asset, _asset) || stream != _streamIndex) return;
            string level = DescribePeak(WaveFileValidator.ReadPeakEnvelope(extracted.Path, 1).Max());
            _measuredLevel = level;
            _sample = extracted; extracted = null;
            _player ??= CreatePlayer();
            _player.Open(new Uri(_sample.Path, UriKind.Absolute)); _player.Volume = 1; _player.Play();
            _playing = true;
            Status = $"Playing track {stream} · {_sample.Duration.TotalSeconds:0.#} seconds · {level}. Confirm that you hear the voice you want captioned.";
        }
        catch (OperationCanceledException) { if (!_disposed) Status = "Track preview stopped."; }
        catch (Exception error) { StopPlayback(); Status = "Track preview failed: " + error.Message; }
        finally { extracted?.Dispose(); if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null; Notify(); }
    }
    internal static AudioSegmentExtractionRequest CreateRequest(GenerationOutputAsset asset, int streamIndex) => new(
        "studio-caption-audition-" + asset.Id, asset.SourceFullPath, asset.SourceDuration, streamIndex,
        asset.SourceStart, asset.SourceStart + (asset.Duration < TimeSpan.FromSeconds(15) ? asset.Duration : TimeSpan.FromSeconds(15)),
        TimeSpan.FromMinutes(1));
    internal static string DescribePeak(double peak) => peak <= 0 ? "digital silence" :
        "peak " + (20 * Math.Log10(peak)).ToString("0.0", CultureInfo.InvariantCulture) + " dBFS";
    private MediaPlayer CreatePlayer()
    {
        var player = new MediaPlayer();
        player.MediaEnded += (_, _) => { StopPlayback(); Status = "Track preview finished" + LevelSuffix() + ". Choose the track containing the desired voice."; Notify(); };
        player.MediaFailed += (_, args) => { StopPlayback(); Status = "Track preview could not play: " + args.ErrorException.Message; Notify(); };
        return player;
    }
    public void Stop()
    {
        bool active = _playing || _cancellation is not null;
        _cancellation?.Cancel(); StopPlayback();
        if (active) Status = "Track preview stopped" + LevelSuffix() + ".";
        Notify();
    }
    private string LevelSuffix() => _measuredLevel is null ? "" : " · " + _measuredLevel;
    private void StopPlayback()
    {
        _player?.Stop(); _player?.Close(); _playing = false;
        _sample?.Dispose(); _sample = null;
    }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Stop(); if (_pending is not null) await _pending.WaitAsync(cancellationToken);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; Stop(); _player = null;
    }
    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);
}
