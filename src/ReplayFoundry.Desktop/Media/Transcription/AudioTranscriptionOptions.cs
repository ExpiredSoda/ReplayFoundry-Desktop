using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ReplayFoundry.Desktop.Media.Intelligence;

namespace ReplayFoundry.Desktop.Media.Transcription;

public sealed class AudioTranscriptionOptions
{
    public const string CurrentPolicyVersion = "0.1";

    public AudioTranscriptionOptions(
        AudioTranscriptionLanguageMode languageMode,
        AudioTranscriptionLanguage? requestedLanguage,
        bool translateToEnglish,
        bool requireSegmentTimestamps,
        bool requestWordTimestamps,
        double? temperature,
        int? threadCount,
        AudioTranscriptionProcessorHint processorHint,
        TimeSpan maximumProcessDuration,
        AudioTranscriptionOutputFormatPolicy outputFormatPolicy,
        string policyVersion = CurrentPolicyVersion)
    {
        if (!Enum.IsDefined(languageMode) ||
            !Enum.IsDefined(processorHint) ||
            !Enum.IsDefined(outputFormatPolicy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(languageMode),
                "Transcription option enums must be defined.");
        }

        if ((languageMode == AudioTranscriptionLanguageMode.Auto) !=
            (requestedLanguage is null))
        {
            throw new ArgumentException(
                "Auto language must not provide a language, and explicit language must provide one.",
                nameof(requestedLanguage));
        }

        if (!requireSegmentTimestamps)
        {
            throw new ArgumentException(
                "Replay Foundry transcription requires segment timestamps.",
                nameof(requireSegmentTimestamps));
        }

        if (temperature is double actualTemperature &&
            (!double.IsFinite(actualTemperature) ||
             actualTemperature is < 0 or > 1))
        {
            throw new ArgumentOutOfRangeException(nameof(temperature));
        }

        if (threadCount is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(threadCount));
        }

        if (maximumProcessDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumProcessDuration));
        }

        if (string.IsNullOrWhiteSpace(policyVersion))
        {
            throw new ArgumentException(
                "Transcription options require a policy version.",
                nameof(policyVersion));
        }

        LanguageMode = languageMode;
        RequestedLanguage = requestedLanguage;
        TranslateToEnglish = translateToEnglish;
        RequireSegmentTimestamps = requireSegmentTimestamps;
        RequestWordTimestamps = requestWordTimestamps;
        Temperature = temperature;
        ThreadCount = threadCount;
        ProcessorHint = processorHint;
        MaximumProcessDuration = maximumProcessDuration;
        OutputFormatPolicy = outputFormatPolicy;
        PolicyVersion = policyVersion.Trim();
    }

    public AudioTranscriptionLanguageMode LanguageMode { get; }

    public AudioTranscriptionLanguage? RequestedLanguage { get; }

    public bool TranslateToEnglish { get; }

    public bool RequireSegmentTimestamps { get; }

    public bool RequestWordTimestamps { get; }

    public double? Temperature { get; }

    public int? ThreadCount { get; }

    public AudioTranscriptionProcessorHint ProcessorHint { get; }

    public TimeSpan MaximumProcessDuration { get; }

    public AudioTranscriptionOutputFormatPolicy OutputFormatPolicy { get; }

    public string PolicyVersion { get; }

    public AudioTranscriptionOptions WithLanguage(
        AudioTranscriptionLanguageMode languageMode,
        AudioTranscriptionLanguage? requestedLanguage) =>
        new(
            languageMode,
            requestedLanguage,
            TranslateToEnglish,
            RequireSegmentTimestamps,
            RequestWordTimestamps,
            Temperature,
            ThreadCount,
            ProcessorHint,
            MaximumProcessDuration,
            OutputFormatPolicy,
            PolicyVersion);

    public static AudioTranscriptionOptions CreateDefaults() =>
        new(
            AudioTranscriptionLanguageMode.Auto,
            requestedLanguage: null,
            translateToEnglish: false,
            requireSegmentTimestamps: true,
            requestWordTimestamps: false,
            temperature: 0,
            threadCount: null,
            AudioTranscriptionProcessorHint.Auto,
            TimeSpan.FromMinutes(10),
            AudioTranscriptionOutputFormatPolicy.StructuredJson);
}

public sealed record AudioTranscriptionModelSettings
{
    public AudioTranscriptionModelSettings(
        string modelPath,
        string displayName,
        string modelFormat,
        string? licenseIdentifier = null,
        string? sourceUrlOrNote = null,
        string? languageCapabilityDescription = null)
    {
        if (string.IsNullOrWhiteSpace(modelPath) ||
            !Path.IsPathFullyQualified(modelPath))
        {
            throw new ArgumentException(
                "A fully qualified model path is required.",
                nameof(modelPath));
        }

        if (string.IsNullOrWhiteSpace(displayName) ||
            string.IsNullOrWhiteSpace(modelFormat))
        {
            throw new ArgumentException(
                "Model display name and format are required.");
        }

        ModelPath = Path.GetFullPath(modelPath);
        DisplayName = displayName.Trim();
        ModelFormat = modelFormat.Trim();
        LicenseIdentifier = Optional(licenseIdentifier);
        SourceUrlOrNote = Optional(sourceUrlOrNote);
        LanguageCapabilityDescription =
            Optional(languageCapabilityDescription);
    }

    public string ModelPath { get; }

    public string DisplayName { get; }

    public string ModelFormat { get; }

    public string? LicenseIdentifier { get; }

    public string? SourceUrlOrNote { get; }

    public string? LanguageCapabilityDescription { get; }

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}
