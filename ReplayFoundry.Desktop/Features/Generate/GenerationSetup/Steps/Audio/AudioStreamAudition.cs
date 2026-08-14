using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Preparation;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.Audio;

public sealed class AudioStreamAuditionPreview
{
    private readonly IReadOnlyList<double> _waveformPeaks;

    public AudioStreamAuditionPreview(
        string sourceFullPath,
        int absoluteAudioStreamIndex,
        TimeSpan start,
        TimeSpan duration,
        IEnumerable<double> waveformPeaks)
    {
        if (string.IsNullOrWhiteSpace(sourceFullPath) ||
            !Path.IsPathFullyQualified(sourceFullPath) ||
            absoluteAudioStreamIndex < 0 ||
            start < TimeSpan.Zero ||
            duration <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "An audio audition preview requires a source, stream, and positive interval.");
        }
        ArgumentNullException.ThrowIfNull(waveformPeaks);
        double[] peaks = waveformPeaks.ToArray();
        if (peaks.Length == 0 ||
            peaks.Any(static value =>
                !double.IsFinite(value) || value is < 0 or > 1))
        {
            throw new ArgumentException(
                "Audio waveform peaks must be finite values from zero through one.",
                nameof(waveformPeaks));
        }

        SourceFullPath = Path.GetFullPath(sourceFullPath);
        AbsoluteAudioStreamIndex = absoluteAudioStreamIndex;
        Start = start;
        Duration = duration;
        _waveformPeaks = Array.AsReadOnly(peaks);
    }

    public string SourceFullPath { get; }
    public int AbsoluteAudioStreamIndex { get; }
    public TimeSpan Start { get; }
    public TimeSpan Duration { get; }
    public IReadOnlyList<double> WaveformPeaks => _waveformPeaks;
}

public sealed class AudioStreamAuditionPlaybackChangedEventArgs : EventArgs
{
    public AudioStreamAuditionPlaybackChangedEventArgs(
        string sourceFullPath,
        int absoluteAudioStreamIndex,
        TimeSpan position,
        TimeSpan duration,
        bool isPlaying)
    {
        if (string.IsNullOrWhiteSpace(sourceFullPath) ||
            !Path.IsPathFullyQualified(sourceFullPath) ||
            absoluteAudioStreamIndex < 0 ||
            position < TimeSpan.Zero ||
            duration <= TimeSpan.Zero ||
            position > duration)
        {
            throw new ArgumentException(
                "Playback progress requires a source, stream, and bounded interval.");
        }

        SourceFullPath = Path.GetFullPath(sourceFullPath);
        AbsoluteAudioStreamIndex = absoluteAudioStreamIndex;
        Position = position;
        Duration = duration;
        IsPlaying = isPlaying;
    }

    public string SourceFullPath { get; }
    public int AbsoluteAudioStreamIndex { get; }
    public TimeSpan Position { get; }
    public TimeSpan Duration { get; }
    public bool IsPlaying { get; }
    public double Progress => Math.Clamp(
        Position.TotalSeconds / Duration.TotalSeconds,
        0,
        1);
}

public interface IAudioStreamAuditionService
{
    event EventHandler<AudioStreamAuditionPlaybackChangedEventArgs>?
        PlaybackChanged;

    Task<AudioStreamAuditionPreview> PrepareAsync(
        PreparedGenerationSource source,
        int absoluteAudioStreamIndex,
        CancellationToken cancellationToken);

    Task PlayAsync(
        PreparedGenerationSource source,
        int absoluteAudioStreamIndex,
        CancellationToken cancellationToken);

    void Stop();

    void Release(PreparedGenerationSource source);
}
