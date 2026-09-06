using System.Globalization;
using System.IO;
using System.Windows.Media;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Platform.Processes;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed class WpfStudioMixAudioAuditionSession : IStudioMixAudioAuditionSession
{
    private readonly IProcessRunner _runner = new WindowsProcessRunner();
    private readonly IFfmpegToolLocator _tools = new FfmpegToolLocator();
    private GenerationOutputAsset? _asset;
    private bool _busy, _disposed, _playing;
    private MediaPlayer? _player;
    private string? _ownedPath;
    private CancellationTokenSource? _cancellation;
    private Task _pending = Task.CompletedTask;
    public event EventHandler? Changed;
    public event EventHandler? PlaybackStarting;
    public bool IsActive => _playing || _cancellation is not null;
    public string Status { get; private set; } = "Compare the original all-track mix with this cut's saved mix and mastering.";
    public void Bind(GenerationOutputAsset? asset)
    {
        if (ReferenceEquals(asset, _asset)) return;
        Stop(); _asset = asset; Notify();
    }
    public void SetHostBusy(bool busy) { _busy = busy; if (busy) Stop(); Notify(); }
    public bool CanListen => !_disposed && !_busy && _cancellation is null && _asset?.SourceMedia.AudioStreams.Count > 0;
    public Task StartAsync(bool processed) => CanListen ? _pending = PrepareAsync(processed) : Task.CompletedTask;
    private async Task PrepareAsync(bool processed)
    {
        if (!CanListen) return;
        Stop();
        GenerationOutputAsset asset = _asset!;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        string path = ReplayFoundryLocalDataPaths.ResolveTemporary(
            Path.Combine("MixAuditions", Guid.NewGuid().ToString("N") + ".wav"));
        Status = processed ? "Preparing the saved mix for this exact cut…" : "Preparing the original all-track mix for this exact cut…";
        Notify();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using IDisposable lease = await MediaWorkBudget.AcquireAsync(cancellation.Token, MediaWorkPriority.Foreground);
            ProcessRunResult result = await _runner.RunAsync(CreateRequest(asset, processed, path, _tools.LocateFfmpeg()), cancellation.Token);
            if (!result.Succeeded || !File.Exists(path) || new FileInfo(path).Length < 44)
                throw new InvalidOperationException("The mix preview could not be prepared. " +
                    (result.StandardError.Length <= 800 ? result.StandardError : result.StandardError[^800..]));
            cancellation.Token.ThrowIfCancellationRequested();
            if (_disposed || !ReferenceEquals(asset, _asset)) return;
            _ownedPath = path;
            _player ??= CreatePlayer();
            PlaybackStarting?.Invoke(this, EventArgs.Empty);
            _player.Open(new Uri(path)); _player.Volume = 1; _player.Play(); _playing = true;
            Status = processed ? "Playing the saved mix, including gain, mute, ducking and enabled mastering." :
                "Playing all source tracks at their original gain, with the standard safety limiter.";
        }
        catch (OperationCanceledException) { if (!_disposed) Status = "Mix preview stopped."; }
        catch (Exception exception) { StopPlayback(); Status = "Mix preview failed: " + exception.Message; }
        finally
        {
            if (!string.Equals(_ownedPath, path, StringComparison.Ordinal)) Delete(path);
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
            Notify();
        }
    }
    internal static ProcessRunRequest CreateRequest(GenerationOutputAsset asset, bool processed, string output, string executable)
    {
        var graph = new List<string>();
        FfmpegStudioCompositionGraph.AppendAudio(graph, asset.SourceMedia, processed ? asset.RenderSettings : new StudioRenderSettings(), asset.Duration);
        return new(executable, ["-hide_banner", "-nostdin", "-v", "error", "-n",
            "-ss", asset.SourceStart.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture),
            "-i", asset.SourceFullPath, "-filter_complex", string.Join(';', graph), "-map", "[aout]",
            "-t", asset.Duration.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture),
            "-vn", "-ar", "48000", "-ac", "2", "-c:a", "pcm_s16le", output],
            TimeSpan.FromSeconds(Math.Clamp(asset.Duration.TotalSeconds * 2, 60, 600)));
    }
    private MediaPlayer CreatePlayer()
    {
        var player = new MediaPlayer();
        player.MediaEnded += (_, _) => { StopPlayback(); Status = "Mix preview finished."; Notify(); };
        player.MediaFailed += (_, args) => { StopPlayback(); Status = "Mix preview could not play: " + args.ErrorException.Message; Notify(); };
        return player;
    }
    public void Stop()
    {
        bool active = _playing || _cancellation is not null;
        _cancellation?.Cancel(); StopPlayback();
        if (active) Status = "Mix preview stopped.";
        Notify();
    }
    private void StopPlayback()
    {
        _player?.Stop(); _player?.Close(); _playing = false;
        if (_ownedPath is { } path) { _ownedPath = null; Delete(path); }
    }
    private static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public async Task StopAsync(CancellationToken cancellationToken) { Stop(); await _pending.WaitAsync(cancellationToken); }
    public void Dispose() { if (_disposed) return; _disposed = true; Stop(); _player = null; }
    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);
}
