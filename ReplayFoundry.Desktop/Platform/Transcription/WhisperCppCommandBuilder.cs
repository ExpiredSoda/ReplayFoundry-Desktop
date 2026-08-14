using System.Globalization;
using System.IO;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Platform.Transcription;

internal sealed record WhisperCppCliCapabilities(
    string ModelOption,
    string InputFileOption,
    string OutputJsonOption,
    string? OutputJsonFullOption,
    string OutputFileOption,
    string? LanguageOption,
    string? TranslateOption,
    string? SplitOnWordOption,
    string? TemperatureOption,
    string? ThreadsOption,
    string? NoGpuOption,
    string? NoPrintsOption,
    string? VadOption,
    string? VadModelOption,
    string? VadThresholdOption,
    string? VadMinimumSpeechOption,
    string? VadMinimumSilenceOption,
    string? VadSpeechPadOption,
    string? VadSamplesOverlapOption)
{
    public AudioTranscriptionProviderCapabilities ToPublic() =>
        new(
            supportsAutomaticLanguage:
                LanguageOption is not null,
            supportsExplicitLanguage:
                LanguageOption is not null,
            supportsTranslationToEnglish:
                TranslateOption is not null,
            supportsSegmentTimestamps: true,
            supportsWordTimestamps:
                OutputJsonFullOption is not null,
            supportsTemperature:
                TemperatureOption is not null,
            supportsThreadCount:
                ThreadsOption is not null,
            reportsExecutionBackend: false);

    public static WhisperCppCliCapabilities Discover(
        string helpOutput)
    {
        if (string.IsNullOrWhiteSpace(helpOutput))
        {
            throw new WhisperCppInitializationException(
                "whisper.cpp returned no help output.");
        }

        string model =
            RequireOption(
                helpOutput,
                "--model",
                "model path");
        string input =
            RequireOption(
                helpOutput,
                "--file",
                "input audio file");
        string json =
            RequireOption(
                helpOutput,
                "--output-json",
                "structured JSON output");
        string output =
            RequireOption(
                helpOutput,
                "--output-file",
                "output file prefix");

        return new WhisperCppCliCapabilities(
            model,
            input,
            json,
            FindOption(helpOutput, "--output-json-full"),
            output,
            FindOption(helpOutput, "--language"),
            FindOption(helpOutput, "--translate"),
            FindOption(helpOutput, "--split-on-word"),
            FindOption(helpOutput, "--temperature"),
            FindOption(helpOutput, "--threads"),
            FindOption(helpOutput, "--no-gpu"),
            FindOption(helpOutput, "--no-prints"),
            FindOption(helpOutput, "--vad"),
            FindOption(helpOutput, "--vad-model"),
            FindOption(helpOutput, "--vad-threshold"),
            FindOption(helpOutput, "--vad-min-speech-duration-ms"),
            FindOption(helpOutput, "--vad-min-silence-duration-ms"),
            FindOption(helpOutput, "--vad-speech-pad-ms"),
            FindOption(helpOutput, "--vad-samples-overlap"));
    }

    private static string RequireOption(
        string help,
        string option,
        string capability) =>
        FindOption(help, option) ??
        throw new WhisperCppInitializationException(
            $"The installed whisper.cpp CLI does not advertise required {capability} capability ({option}).");

    private static string? FindOption(
        string help,
        string option) =>
        help.Contains(
            option,
            StringComparison.Ordinal)
            ? option
            : null;
}

internal sealed record WhisperCppCommand(
    IReadOnlyList<string> Arguments,
    string OutputPrefix,
    string OutputJsonPath,
    IReadOnlyDictionary<string, string> NormalizedOptions);

internal static class WhisperCppCommandBuilder
{
    public static WhisperCppCommand Build(
        AudioTranscriptionRequest request,
        WhisperCppCliCapabilities capabilities,
        string outputPrefix,
        string? vadModelPath = null,
        string? vadModelSha256 = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(capabilities);

        if (string.IsNullOrWhiteSpace(outputPrefix) ||
            !Path.IsPathFullyQualified(outputPrefix))
        {
            throw new ArgumentException(
                "whisper.cpp output prefix must be fully qualified.",
                nameof(outputPrefix));
        }

        AudioTranscriptionOptions options =
            request.Options;
        string structuredOutputOption =
            options.RequestWordTimestamps
                ? capabilities.OutputJsonFullOption ??
                  throw new WhisperCppInitializationException(
                      "The installed whisper.cpp CLI does not advertise full structured JSON token timing (--output-json-full).")
                : capabilities.OutputJsonOption;
        var arguments =
            new List<string>
            {
                capabilities.ModelOption,
                request.ModelSettings.ModelPath,
                capabilities.InputFileOption,
                request.InputAudioPath,
                structuredOutputOption,
                capabilities.OutputFileOption,
                outputPrefix,
            };
        var normalized =
            new SortedDictionary<string, string>(
                StringComparer.Ordinal)
            {
                ["language"] =
                    options.LanguageMode ==
                    AudioTranscriptionLanguageMode.Auto
                        ? "auto"
                        : options.RequestedLanguage!.Code,
                ["outputFormat"] =
                    options.OutputFormatPolicy.ToString(),
                ["policyVersion"] =
                    options.PolicyVersion,
                ["processor"] =
                    options.ProcessorHint.ToString(),
                ["segmentTimestamps"] = "true",
                ["translateToEnglish"] =
                    options.TranslateToEnglish
                        ? "true"
                        : "false",
                ["wordTimestamps"] =
                    options.RequestWordTimestamps
                        ? "true"
                        : "false",
            };

        if (vadModelPath is not null)
        {
            if (!Path.IsPathFullyQualified(vadModelPath) ||
                string.IsNullOrWhiteSpace(vadModelSha256) ||
                vadModelSha256.Length != 64 ||
                vadModelSha256.Any(static value => !Uri.IsHexDigit(value)))
            {
                throw new ArgumentException(
                    "whisper.cpp VAD requires a fully qualified model and its SHA-256.",
                    nameof(vadModelPath));
            }

            string vad = capabilities.VadOption ??
                throw MissingVadCapability("--vad");
            string vadModel = capabilities.VadModelOption ??
                throw MissingVadCapability("--vad-model");
            string threshold = capabilities.VadThresholdOption ??
                throw MissingVadCapability("--vad-threshold");
            string minimumSpeech = capabilities.VadMinimumSpeechOption ??
                throw MissingVadCapability("--vad-min-speech-duration-ms");
            string minimumSilence = capabilities.VadMinimumSilenceOption ??
                throw MissingVadCapability("--vad-min-silence-duration-ms");
            string speechPad = capabilities.VadSpeechPadOption ??
                throw MissingVadCapability("--vad-speech-pad-ms");
            string samplesOverlap = capabilities.VadSamplesOverlapOption ??
                throw MissingVadCapability("--vad-samples-overlap");

            arguments.Add(vad);
            arguments.Add(vadModel);
            arguments.Add(vadModelPath);
            arguments.Add(threshold);
            arguments.Add("0.50");
            arguments.Add(minimumSpeech);
            arguments.Add("250");
            arguments.Add(minimumSilence);
            arguments.Add("300");
            arguments.Add(speechPad);
            arguments.Add("80");
            arguments.Add(samplesOverlap);
            arguments.Add("0.10");

            normalized["vad"] = "silero-v6.2.0";
            normalized["vadModelSha256"] =
                vadModelSha256.ToUpperInvariant();
            normalized["vadThreshold"] = "0.50";
            normalized["vadMinimumSpeechMilliseconds"] = "250";
            normalized["vadMinimumSilenceMilliseconds"] = "300";
            normalized["vadSpeechPaddingMilliseconds"] = "80";
            normalized["vadSamplesOverlapSeconds"] = "0.10";
        }

        if (capabilities.LanguageOption is null)
        {
            throw new WhisperCppInitializationException(
                "The installed whisper.cpp CLI does not advertise language selection.");
        }

        arguments.Add(capabilities.LanguageOption);
        arguments.Add(
            options.LanguageMode ==
            AudioTranscriptionLanguageMode.Auto
                ? "auto"
                : options.RequestedLanguage!.Code);

        if (options.TranslateToEnglish)
        {
            if (capabilities.TranslateOption is null)
            {
                throw new WhisperCppInitializationException(
                    "The installed whisper.cpp CLI does not support translation to English.");
            }

            arguments.Add(capabilities.TranslateOption);
        }

        if (options.RequestWordTimestamps)
        {
            if (capabilities.SplitOnWordOption is not null)
            {
                arguments.Add(capabilities.SplitOnWordOption);
            }
            normalized["wordTimestampSource"] =
                "full-json-token-timing";
        }

        if (options.Temperature is double temperature)
        {
            if (capabilities.TemperatureOption is null)
            {
                throw new WhisperCppInitializationException(
                    "The installed whisper.cpp CLI does not advertise a temperature option.");
            }

            arguments.Add(
                capabilities.TemperatureOption);
            arguments.Add(
                temperature.ToString(
                    "0.#########",
                    CultureInfo.InvariantCulture));
            normalized["temperature"] =
                temperature.ToString(
                    "0.#########",
                    CultureInfo.InvariantCulture);
        }

        if (options.ThreadCount is int threads)
        {
            if (capabilities.ThreadsOption is null)
            {
                throw new WhisperCppInitializationException(
                    "The installed whisper.cpp CLI does not advertise thread-count control.");
            }

            arguments.Add(capabilities.ThreadsOption);
            arguments.Add(
                threads.ToString(
                    CultureInfo.InvariantCulture));
            normalized["threads"] =
                threads.ToString(
                    CultureInfo.InvariantCulture);
        }

        if (options.ProcessorHint ==
            AudioTranscriptionProcessorHint.Cpu)
        {
            if (capabilities.NoGpuOption is null)
            {
                throw new WhisperCppInitializationException(
                    "The installed whisper.cpp CLI cannot explicitly select CPU execution.");
            }

            arguments.Add(capabilities.NoGpuOption);
        }

        if (capabilities.NoPrintsOption is not null)
        {
            arguments.Add(capabilities.NoPrintsOption);
        }

        return new WhisperCppCommand(
            Array.AsReadOnly(arguments.ToArray()),
            outputPrefix,
            outputPrefix + ".json",
            new System.Collections.ObjectModel
                .ReadOnlyDictionary<string, string>(
                    normalized));
    }

    private static WhisperCppInitializationException MissingVadCapability(
        string option) =>
        new(
            "The installed whisper.cpp CLI does not advertise the required " +
            $"speech-timing option ({option}).");
}
