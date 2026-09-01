using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ReplayFoundry.Desktop.Media.Intelligence;

namespace ReplayFoundry.Desktop.Media.Transcription;

public sealed class AudioTranscriptionRequest
{
    public AudioTranscriptionRequest(
        string neighborhoodId,
        string inputAudioPath,
        TimeSpan inputDuration,
        TimeSpan absoluteSourceOffset,
        TimeSpan sourceDuration,
        int absoluteAudioStreamIndex,
        AudioTranscriptionOptions options,
        AudioTranscriptionModelSettings modelSettings)
    {
        if (string.IsNullOrWhiteSpace(neighborhoodId))
        {
            throw new ArgumentException(
                "A transcription request requires a neighborhood identity.",
                nameof(neighborhoodId));
        }

        if (string.IsNullOrWhiteSpace(inputAudioPath) ||
            !Path.IsPathFullyQualified(inputAudioPath))
        {
            throw new ArgumentException(
                "Transcription input audio must be a fully qualified caller-owned path.",
                nameof(inputAudioPath));
        }

        if (inputDuration <= TimeSpan.Zero ||
            sourceDuration <= TimeSpan.Zero ||
            absoluteSourceOffset < TimeSpan.Zero ||
            absoluteSourceOffset + inputDuration > sourceDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inputDuration),
                "The bounded input interval must remain inside the source.");
        }

        if (absoluteAudioStreamIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(absoluteAudioStreamIndex));
        }

        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(modelSettings);

        NeighborhoodId = neighborhoodId.Trim();
        InputAudioPath = Path.GetFullPath(inputAudioPath);
        InputDuration = inputDuration;
        AbsoluteSourceOffset = absoluteSourceOffset;
        SourceDuration = sourceDuration;
        AbsoluteAudioStreamIndex = absoluteAudioStreamIndex;
        Options = options;
        ModelSettings = modelSettings;
    }

    public string NeighborhoodId { get; }

    public string InputAudioPath { get; }

    public TimeSpan InputDuration { get; }

    public TimeSpan AbsoluteSourceOffset { get; }

    public TimeSpan SourceDuration { get; }

    public int AbsoluteAudioStreamIndex { get; }

    public AudioTranscriptionOptions Options { get; }

    public AudioTranscriptionModelSettings ModelSettings { get; }
}
