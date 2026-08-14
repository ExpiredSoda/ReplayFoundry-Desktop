using System.Collections.ObjectModel;
using System.IO;

namespace ReplayFoundry.Desktop.Media.AudioExtraction;

public enum AudioSegmentExtractionWarningCode
{
    DurationWithinTolerance,
    ToolVersionUnavailable,
    CleanupFailure,
}

public sealed record AudioSegmentExtractionWarning
{
    public AudioSegmentExtractionWarning(
        AudioSegmentExtractionWarningCode code,
        string message)
    {
        if (!Enum.IsDefined(code) ||
            string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "Audio extraction warnings require a defined code and message.");
        }

        Code = code;
        Message = message.Trim();
    }

    public AudioSegmentExtractionWarningCode Code { get; }

    public string Message { get; }
}

public sealed class AudioSegmentExtractionRequest
{
    public AudioSegmentExtractionRequest(
        string neighborhoodId,
        string sourcePath,
        TimeSpan sourceDuration,
        int absoluteAudioStreamIndex,
        TimeSpan start,
        TimeSpan end,
        TimeSpan processTimeout)
    {
        if (string.IsNullOrWhiteSpace(neighborhoodId))
        {
            throw new ArgumentException(
                "Audio extraction requires a neighborhood identity.",
                nameof(neighborhoodId));
        }

        if (string.IsNullOrWhiteSpace(sourcePath) ||
            !Path.IsPathFullyQualified(sourcePath))
        {
            throw new ArgumentException(
                "Audio extraction requires a fully qualified source path.",
                nameof(sourcePath));
        }

        if (sourceDuration <= TimeSpan.Zero ||
            absoluteAudioStreamIndex < 0 ||
            start < TimeSpan.Zero ||
            end <= start ||
            end > sourceDuration ||
            processTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                "The extraction interval, stream, duration, and timeout must be valid.");
        }

        NeighborhoodId = neighborhoodId.Trim();
        SourcePath = Path.GetFullPath(sourcePath);
        SourceDuration = sourceDuration;
        AbsoluteAudioStreamIndex = absoluteAudioStreamIndex;
        Start = start;
        End = end;
        ProcessTimeout = processTimeout;
    }

    public string NeighborhoodId { get; }

    public string SourcePath { get; }

    public TimeSpan SourceDuration { get; }

    public int AbsoluteAudioStreamIndex { get; }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public TimeSpan Duration => End - Start;

    public TimeSpan ProcessTimeout { get; }
}

public sealed class AudioSegmentExtractionManifest
{
    private readonly ReadOnlyCollection<string> _arguments;
    private readonly ReadOnlyCollection<AudioSegmentExtractionWarning>
        _warnings;

    public AudioSegmentExtractionManifest(
        string extractorName,
        string extractorVersion,
        string ffmpegPath,
        string ffmpegSha256,
        string ffmpegVersion,
        IEnumerable<string> arguments,
        string sourcePath,
        TimeSpan start,
        TimeSpan end,
        int absoluteAudioStreamIndex,
        int sampleRate,
        int channelCount,
        int bitsPerSample,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        TimeSpan elapsed,
        IEnumerable<AudioSegmentExtractionWarning>? warnings = null)
    {
        if (string.IsNullOrWhiteSpace(extractorName) ||
            string.IsNullOrWhiteSpace(extractorVersion) ||
            string.IsNullOrWhiteSpace(ffmpegVersion))
        {
            throw new ArgumentException(
                "Extraction manifest identities cannot be blank.");
        }

        if (string.IsNullOrWhiteSpace(ffmpegPath) ||
            !Path.IsPathFullyQualified(ffmpegPath) ||
            string.IsNullOrWhiteSpace(sourcePath) ||
            !Path.IsPathFullyQualified(sourcePath))
        {
            throw new ArgumentException(
                "Extraction manifest paths must be fully qualified.");
        }

        ArgumentNullException.ThrowIfNull(arguments);

        string[] argumentSnapshot =
            arguments.ToArray();
        AudioSegmentExtractionWarning[] warningSnapshot =
            warnings?.ToArray() ??
            [];

        if (argumentSnapshot.Any(static value => value is null) ||
            warningSnapshot.Any(static value => value is null) ||
            start < TimeSpan.Zero ||
            end <= start ||
            absoluteAudioStreamIndex < 0 ||
            sampleRate <= 0 ||
            channelCount <= 0 ||
            bitsPerSample <= 0 ||
            startedAtUtc.Offset != TimeSpan.Zero ||
            completedAtUtc.Offset != TimeSpan.Zero ||
            completedAtUtc < startedAtUtc ||
            elapsed < TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The extraction manifest contains invalid provenance.");
        }

        ExtractorName = extractorName.Trim();
        ExtractorVersion = extractorVersion.Trim();
        FfmpegPath = Path.GetFullPath(ffmpegPath);
        FfmpegSha256 =
            ReplayFoundry.Desktop.Media.Intelligence
                .ModelArtifactManifest.Sha256Value(
                    ffmpegSha256,
                    nameof(ffmpegSha256));
        FfmpegVersion = ffmpegVersion.Trim();
        SourcePath = Path.GetFullPath(sourcePath);
        Start = start;
        End = end;
        AbsoluteAudioStreamIndex = absoluteAudioStreamIndex;
        SampleRate = sampleRate;
        ChannelCount = channelCount;
        BitsPerSample = bitsPerSample;
        StartedAtUtc = startedAtUtc;
        CompletedAtUtc = completedAtUtc;
        Elapsed = elapsed;
        _arguments = Array.AsReadOnly(argumentSnapshot);
        _warnings = Array.AsReadOnly(warningSnapshot);
    }

    public string ExtractorName { get; }

    public string ExtractorVersion { get; }

    public string FfmpegPath { get; }

    public string FfmpegSha256 { get; }

    public string FfmpegVersion { get; }

    public IReadOnlyList<string> Arguments => _arguments;

    public string SourcePath { get; }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public int AbsoluteAudioStreamIndex { get; }

    public int SampleRate { get; }

    public int ChannelCount { get; }

    public int BitsPerSample { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset CompletedAtUtc { get; }

    public TimeSpan Elapsed { get; }

    public IReadOnlyList<AudioSegmentExtractionWarning> Warnings =>
        _warnings;
}

public sealed class ExtractedAudioSegment : IDisposable
{
    private readonly Action _cleanup;
    private bool _disposed;

    internal ExtractedAudioSegment(
        string neighborhoodId,
        string path,
        TimeSpan duration,
        long byteLength,
        AudioSegmentExtractionManifest manifest,
        Action cleanup)
    {
        if (string.IsNullOrWhiteSpace(neighborhoodId) ||
            string.IsNullOrWhiteSpace(path) ||
            !System.IO.Path.IsPathFullyQualified(path) ||
            duration <= TimeSpan.Zero ||
            byteLength <= 0)
        {
            throw new ArgumentException(
                "An extracted segment requires a valid identity, path, duration, and size.");
        }

        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(cleanup);

        NeighborhoodId = neighborhoodId.Trim();
        Path = System.IO.Path.GetFullPath(path);
        Duration = duration;
        ByteLength = byteLength;
        Manifest = manifest;
        _cleanup = cleanup;
    }

    public string NeighborhoodId { get; }

    public string Path { get; }

    public TimeSpan Duration { get; }

    public long ByteLength { get; }

    public AudioSegmentExtractionManifest Manifest { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cleanup();
    }
}

public interface IAudioSegmentExtractor
{
    Task<ExtractedAudioSegment> ExtractAsync(
        AudioSegmentExtractionRequest request,
        CancellationToken cancellationToken);
}

public class AudioSegmentExtractionException : Exception
{
    public AudioSegmentExtractionException(
        string message,
        string? diagnosticDetails = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "Audio extraction failures require a message.",
                nameof(message));
        }

        DiagnosticDetails =
            string.IsNullOrWhiteSpace(diagnosticDetails)
                ? null
                : diagnosticDetails.Trim();
    }

    public string? DiagnosticDetails { get; }
}
