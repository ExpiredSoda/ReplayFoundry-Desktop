using System.IO;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Platform.Transcription;

public sealed class WhisperCppProviderSettings
{
    public WhisperCppProviderSettings(
        string executablePath,
        AudioTranscriptionModelSettings model,
        string? executableVersionArgument = "--version",
        string? vadModelPath = null)
    {
        if (string.IsNullOrWhiteSpace(executablePath) ||
            !Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException(
                "whisper.cpp requires an explicit fully qualified executable path.",
                nameof(executablePath));
        }

        ArgumentNullException.ThrowIfNull(model);

        if (string.IsNullOrWhiteSpace(executableVersionArgument))
        {
            throw new ArgumentException(
                "A version probe argument is required.",
                nameof(executableVersionArgument));
        }

        ExecutablePath = Path.GetFullPath(executablePath);
        Model = model;
        ExecutableVersionArgument =
            executableVersionArgument.Trim();
        if (vadModelPath is not null &&
            (string.IsNullOrWhiteSpace(vadModelPath) ||
             !Path.IsPathFullyQualified(vadModelPath)))
        {
            throw new ArgumentException(
                "whisper.cpp VAD requires an explicit fully qualified model path.",
                nameof(vadModelPath));
        }
        VadModelPath = vadModelPath is null
            ? null
            : Path.GetFullPath(vadModelPath);
    }

    public string ExecutablePath { get; }

    public AudioTranscriptionModelSettings Model { get; }

    public string ExecutableVersionArgument { get; }

    public string? VadModelPath { get; }
}

public sealed class WhisperCppInitializationException :
    Exception
{
    public WhisperCppInitializationException(
        string message,
        string? diagnosticDetails = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        DiagnosticDetails =
            string.IsNullOrWhiteSpace(diagnosticDetails)
                ? null
                : diagnosticDetails.Trim();
    }

    public string? DiagnosticDetails { get; }
}

public sealed class WhisperCppTranscriptionException :
    Exception
{
    public WhisperCppTranscriptionException(
        string message,
        string? diagnosticDetails = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        DiagnosticDetails =
            string.IsNullOrWhiteSpace(diagnosticDetails)
                ? null
                : diagnosticDetails.Trim();
    }

    public string? DiagnosticDetails { get; }
}
