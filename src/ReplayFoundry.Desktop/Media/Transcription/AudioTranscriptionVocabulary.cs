using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ReplayFoundry.Desktop.Media.Intelligence;

namespace ReplayFoundry.Desktop.Media.Transcription;

public enum AudioTranscriptionLanguageMode
{
    Auto,
    Explicit,
}

public enum AudioTranscriptionOutputFormatPolicy
{
    StructuredJson,
}

public enum AudioTranscriptionProcessorHint
{
    Auto,
    Cpu,
    Gpu,
}

public enum AudioTranscriptionWarningCode
{
    LanguageNotReported,
    WordTimestampsUnavailable,
    ConfidenceNotReported,
    NoSpeechDetected,
    ProviderReportedWarning,
    BoundaryClamped,
    WordTimingCanonicalized,
}

public sealed record AudioTranscriptionWarning
{
    public AudioTranscriptionWarning(
        AudioTranscriptionWarningCode code,
        string message,
        string? segmentId = null)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "A transcription warning requires a message.",
                nameof(message));
        }

        Code = code;
        Message = message.Trim();
        SegmentId =
            string.IsNullOrWhiteSpace(segmentId)
                ? null
                : segmentId.Trim();
    }

    public AudioTranscriptionWarningCode Code { get; }

    public string Message { get; }

    public string? SegmentId { get; }
}

public sealed record AudioTranscriptionLanguage
{
    public AudioTranscriptionLanguage(
        string code,
        string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException(
                "A transcription language requires a code.",
                nameof(code));
        }

        string normalized = code.Trim();

        if (normalized.Any(
                static character =>
                    !(char.IsLetterOrDigit(character) ||
                      character is '-' or '_')))
        {
            throw new ArgumentException(
                "Language codes may contain only letters, digits, hyphens, or underscores.",
                nameof(code));
        }

        Code = normalized.ToLowerInvariant();
        DisplayName =
            string.IsNullOrWhiteSpace(displayName)
                ? null
                : displayName.Trim();
    }

    public string Code { get; }

    public string? DisplayName { get; }
}
