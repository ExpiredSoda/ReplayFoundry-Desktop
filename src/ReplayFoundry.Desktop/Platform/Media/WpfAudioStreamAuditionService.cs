using System.Windows.Media;
using System.Windows.Threading;
using System.Collections.Concurrent;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.Audio;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Media.AudioExtraction;

namespace ReplayFoundry.Desktop.Platform.Media;

public sealed class WpfAudioStreamAuditionService :
    IAudioStreamAuditionService,
    IDisposable
{
    private static readonly TimeSpan SampleDuration = TimeSpan.FromSeconds(30);
    private readonly IAudioSegmentExtractor _extractor;
    private readonly MediaPlayer _player = new();
    private readonly DispatcherTimer _progressTimer;
    private readonly ConcurrentDictionary<string, PreparedAudition>
        _prepared = new(StringComparer.OrdinalIgnoreCase);
    private string? _playingKey;
    private bool _isDisposed;
    private bool _mediaOpened;
    private bool _playWhenOpened;
    private TimeSpan _pendingPosition;
    public bool CanSeek => !_isDisposed;

    public WpfAudioStreamAuditionService(IAudioSegmentExtractor extractor)
    {
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _progressTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(40),
            DispatcherPriority.Render,
            (_, _) => PublishProgress(),
            _player.Dispatcher);
        _progressTimer.Stop();
        _player.MediaEnded += (_, _) => CompletePlayback();
        _player.MediaFailed += (_, _) => Stop();
        _player.MediaOpened += (_, _) =>
        {
            if (TryGetPlaying() is null) return;
            _mediaOpened = true;
            _player.Position = _pendingPosition;
            if (_playWhenOpened) { _player.Play(); _progressTimer.Start(); }
            else _player.Pause();
            PublishProgress();
        };
    }

    public event EventHandler<AudioStreamAuditionPlaybackChangedEventArgs>?
        PlaybackChanged;

    public async Task<AudioStreamAuditionPreview> PrepareAsync(
        PreparedGenerationSource source,
        int absoluteAudioStreamIndex,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(source);
        if (!source.Media.AudioStreams.Any(value =>
                value.Index == absoluteAudioStreamIndex))
        {
            throw new ArgumentException(
                "The audition stream does not belong to the prepared source.",
                nameof(absoluteAudioStreamIndex));
        }
        string key = CreateKey(source, absoluteAudioStreamIndex);
        if (_prepared.TryGetValue(key, out PreparedAudition? retained))
        {
            return retained.Preview;
        }

        TimeSpan boundedStart = SelectRepresentativeStart(
            source.Media.Duration);
        TimeSpan end = boundedStart + SampleDuration;
        if (end > source.Media.Duration)
        {
            end = source.Media.Duration;
            boundedStart = end > SampleDuration
                ? end - SampleDuration
                : TimeSpan.Zero;
        }
        ExtractedAudioSegment extracted = await _extractor.ExtractAsync(
            new AudioSegmentExtractionRequest(
                $"setup-audition-{absoluteAudioStreamIndex}-{boundedStart.Ticks}",
                source.Media.FullPath,
                source.Media.Duration,
                absoluteAudioStreamIndex,
                boundedStart,
                end,
                TimeSpan.FromMinutes(1)),
            cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            double[] peaks = WaveFileValidator.ReadPeakEnvelope(
                extracted.Path,
                360);
            var preview = new AudioStreamAuditionPreview(
                source.Media.FullPath,
                absoluteAudioStreamIndex,
                boundedStart,
                extracted.Duration,
                peaks);
            var prepared = new PreparedAudition(extracted, preview);
            if (_prepared.TryAdd(key, prepared))
            {
                return preview;
            }

            prepared.Dispose();
            return _prepared[key].Preview;
        }
        catch
        {
            extracted.Dispose();
            throw;
        }
    }

    public async Task PlayAsync(
        PreparedGenerationSource source,
        int absoluteAudioStreamIndex,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        await PrepareAsync(
            source,
            absoluteAudioStreamIndex,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        string key = CreateKey(source, absoluteAudioStreamIndex);
        PreparedAudition prepared = _prepared[key];
        if (!string.Equals(key, _playingKey, StringComparison.OrdinalIgnoreCase)) OpenPrepared(key, prepared, TimeSpan.Zero);
        _playWhenOpened = true;
        if (_mediaOpened) { _player.Play(); _progressTimer.Start(); }
        PublishProgress();
    }

    public bool Seek(PreparedGenerationSource source, int absoluteAudioStreamIndex, double progress)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!double.IsFinite(progress)) return false;
        string key = CreateKey(source, absoluteAudioStreamIndex);
        if (!_prepared.TryGetValue(key, out PreparedAudition? prepared)) return false;
        TimeSpan position = TimeSpan.FromSeconds(Math.Clamp(progress, 0, 1) * prepared.Preview.Duration.TotalSeconds);
        if (!string.Equals(key, _playingKey, StringComparison.OrdinalIgnoreCase)) OpenPrepared(key, prepared, position);
        _pendingPosition = position;
        if (_mediaOpened) _player.Position = position;
        PublishPlayback(prepared.Preview, position, _playWhenOpened);
        return true;
    }

    private void OpenPrepared(string key, PreparedAudition prepared, TimeSpan position)
    {
        Stop();
        _playingKey = key;
        _pendingPosition = position;
        _player.Open(new Uri(prepared.Segment.Path, UriKind.Absolute));
        _player.Volume = 1;
    }

    public void Stop()
    {
        PreparedAudition? playing = TryGetPlaying();
        _progressTimer.Stop();
        _player.Stop();
        _player.Close();
        _playingKey = null;
        _mediaOpened = false;
        _playWhenOpened = false;
        _pendingPosition = TimeSpan.Zero;
        if (playing is not null)
        {
            PublishPlayback(playing.Preview, TimeSpan.Zero, isPlaying: false);
        }
    }

    public void Release(PreparedGenerationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        string prefix = source.Media.FullPath + "|";
        foreach (KeyValuePair<string, PreparedAudition> item in _prepared
                     .Where(item => item.Key.StartsWith(
                         prefix,
                         StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            if (!_prepared.TryRemove(item.Key, out PreparedAudition? removed))
            {
                continue;
            }
            if (string.Equals(_playingKey, item.Key, StringComparison.OrdinalIgnoreCase))
            {
                Stop();
            }
            removed.Dispose();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }
        _isDisposed = true;
        Stop();
        foreach (PreparedAudition prepared in _prepared.Values)
        {
            prepared.Dispose();
        }
        _prepared.Clear();
    }

    private static TimeSpan SelectRepresentativeStart(TimeSpan sourceDuration)
    {
        if (sourceDuration <= SampleDuration)
        {
            return TimeSpan.Zero;
        }

        // Avoid a capture's first few seconds without scanning or assigning
        // semantic meaning. The same deterministic fifth-of-source position
        // is used for every stream so comparisons remain honest.
        double latest = (sourceDuration - SampleDuration).TotalSeconds;
        double seconds = Math.Clamp(
            sourceDuration.TotalSeconds * 0.20,
            Math.Min(15, latest),
            latest);
        return TimeSpan.FromMilliseconds(
            Math.Round(seconds * 1000, MidpointRounding.AwayFromZero));
    }

    private static string CreateKey(
        PreparedGenerationSource source,
        int absoluteAudioStreamIndex) =>
        source.Media.FullPath + "|" + absoluteAudioStreamIndex;

    private PreparedAudition? TryGetPlaying() =>
        _playingKey is not null &&
        _prepared.TryGetValue(_playingKey, out PreparedAudition? prepared)
            ? prepared
            : null;

    private void PublishProgress()
    {
        PreparedAudition? playing = TryGetPlaying();
        if (playing is null)
        {
            _progressTimer.Stop();
            return;
        }

        TimeSpan position = _mediaOpened ? _player.Position : _pendingPosition;
        if (position > playing.Preview.Duration)
        {
            position = playing.Preview.Duration;
        }
        PublishPlayback(playing.Preview, position, isPlaying: _playWhenOpened);
    }

    private void CompletePlayback()
    {
        PreparedAudition? playing = TryGetPlaying();
        _progressTimer.Stop();
        _player.Stop();
        _player.Close();
        _playingKey = null;
        _mediaOpened = false;
        _playWhenOpened = false;
        _pendingPosition = TimeSpan.Zero;
        if (playing is not null)
        {
            PublishPlayback(
                playing.Preview,
                playing.Preview.Duration,
                isPlaying: false);
        }
    }

    private void PublishPlayback(
        AudioStreamAuditionPreview preview,
        TimeSpan position,
        bool isPlaying) =>
        PlaybackChanged?.Invoke(
            this,
            new AudioStreamAuditionPlaybackChangedEventArgs(
                preview.SourceFullPath,
                preview.AbsoluteAudioStreamIndex,
                position,
                preview.Duration,
                isPlaying));

    private sealed record PreparedAudition(
        ExtractedAudioSegment Segment,
        AudioStreamAuditionPreview Preview) : IDisposable
    {
        public void Dispose() => Segment.Dispose();
    }
}
