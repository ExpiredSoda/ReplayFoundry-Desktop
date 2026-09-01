using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ReplayFoundry.Desktop.Media.Intelligence;

namespace ReplayFoundry.Desktop.Media.Transcription;

public sealed class AudioTranscriptionProviderCapabilities
{
    public AudioTranscriptionProviderCapabilities(
        bool supportsAutomaticLanguage,
        bool supportsExplicitLanguage,
        bool supportsTranslationToEnglish,
        bool supportsSegmentTimestamps,
        bool supportsWordTimestamps,
        bool supportsTemperature,
        bool supportsThreadCount,
        bool reportsExecutionBackend)
    {
        if (!supportsSegmentTimestamps)
        {
            throw new ArgumentException(
                "Replay Foundry providers must support segment timestamps.",
                nameof(supportsSegmentTimestamps));
        }

        SupportsAutomaticLanguage = supportsAutomaticLanguage;
        SupportsExplicitLanguage = supportsExplicitLanguage;
        SupportsTranslationToEnglish =
            supportsTranslationToEnglish;
        SupportsSegmentTimestamps = supportsSegmentTimestamps;
        SupportsWordTimestamps = supportsWordTimestamps;
        SupportsTemperature = supportsTemperature;
        SupportsThreadCount = supportsThreadCount;
        ReportsExecutionBackend = reportsExecutionBackend;
    }

    public bool SupportsAutomaticLanguage { get; }

    public bool SupportsExplicitLanguage { get; }

    public bool SupportsTranslationToEnglish { get; }

    public bool SupportsSegmentTimestamps { get; }

    public bool SupportsWordTimestamps { get; }

    public bool SupportsTemperature { get; }

    public bool SupportsThreadCount { get; }

    public bool ReportsExecutionBackend { get; }
}

public sealed class AudioTranscriptionManifest
{
    public AudioTranscriptionManifest(
        string neighborhoodId,
        TimeSpan inputDuration,
        TimeSpan absoluteSourceOffset,
        TimeSpan sourceDuration,
        int absoluteAudioStreamIndex,
        AudioTranscriptionOptions options,
        InferenceExecutionManifest execution)
    {
        if (string.IsNullOrWhiteSpace(neighborhoodId))
        {
            throw new ArgumentException(
                "A transcription manifest requires a neighborhood identity.",
                nameof(neighborhoodId));
        }

        if (inputDuration <= TimeSpan.Zero ||
            absoluteSourceOffset < TimeSpan.Zero ||
            sourceDuration <= TimeSpan.Zero ||
            absoluteSourceOffset + inputDuration > sourceDuration ||
            absoluteAudioStreamIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inputDuration));
        }

        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(execution);

        NeighborhoodId = neighborhoodId.Trim();
        InputDuration = inputDuration;
        AbsoluteSourceOffset = absoluteSourceOffset;
        SourceDuration = sourceDuration;
        AbsoluteAudioStreamIndex = absoluteAudioStreamIndex;
        Options = options;
        Execution = execution;
    }

    public string NeighborhoodId { get; }

    public TimeSpan InputDuration { get; }

    public TimeSpan AbsoluteSourceOffset { get; }

    public TimeSpan SourceDuration { get; }

    public int AbsoluteAudioStreamIndex { get; }

    public AudioTranscriptionOptions Options { get; }

    public InferenceExecutionManifest Execution { get; }
}
