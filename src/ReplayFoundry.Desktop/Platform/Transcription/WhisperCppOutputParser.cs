using System.Text.Json;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Platform.Transcription;

internal sealed record WhisperCppParsedOutput(
    IReadOnlyList<AudioTranscriptionSegment> Segments,
    AudioTranscriptionLanguage? DetectedLanguage,
    IReadOnlyList<AudioTranscriptionWarning> Warnings);

internal static class WhisperCppOutputParser
{
    public static WhisperCppParsedOutput Parse(
        string json,
        AudioTranscriptionRequest request,
        WhisperCppVadTimeMap? vadTimeMap = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new WhisperCppTranscriptionException(
                "whisper.cpp produced an empty structured output.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });
            JsonElement root = document.RootElement;
            JsonElement items = WhisperCppJsonReader.FindSegments(root);
            var warnings = new List<AudioTranscriptionWarning>();
            var retained = new List<AudioTranscriptionSegment>();
            int index = 0;
            foreach (JsonElement item in items.EnumerateArray())
            {
                if (IsTrailingVadPadding(item, index, items.GetArrayLength(), request, vadTimeMap, retained))
                {
                    warnings.Add(new AudioTranscriptionWarning(
                        AudioTranscriptionWarningCode.ProviderReportedWarning,
                        $"The provider returned an extra segment immediately after the audio ended at {request.InputDuration:c}. " +
                        "That out-of-range segment was excluded; retained speech and word timestamps were not shifted."));
                }
                else
                {
                    retained.Add(WhisperCppSegmentParser.Parse(item, index, request, vadTimeMap, warnings));
                }
                index++;
            }
            AudioTranscriptionSegment[] segments = retained.ToArray();
            segments = WhisperCppSegmentSequenceNormalizer.Normalize(segments, warnings);
            AudioTranscriptionLanguage? detectedLanguage =
                WhisperCppJsonReader.ReadDetectedLanguage(root);
            AddRootWarnings(segments, detectedLanguage, warnings);

            return new WhisperCppParsedOutput(
                Array.AsReadOnly(segments),
                detectedLanguage,
                Array.AsReadOnly(warnings.ToArray()));
        }
        catch (WhisperCppTranscriptionException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is JsonException or
                  InvalidOperationException or
                  FormatException or
                  ArgumentException or
                  OverflowException)
        {
            throw new WhisperCppTranscriptionException(
                "whisper.cpp structured output is malformed or incomplete.",
                innerException: exception);
        }
    }

    private static bool IsTrailingVadPadding(JsonElement item, int index, int count,
        AudioTranscriptionRequest request, WhisperCppVadTimeMap? vadTimeMap,
        IReadOnlyList<AudioTranscriptionSegment> retained)
    {
        // whisper.cpp can append a 100 ms VAD tail after a phrase that already
        // reaches the physical input boundary. It has no audio to caption.
        // Keep rejecting other out-of-bounds output and never pull this text
        // back into the final frame or invent a word interval for it.
        if (vadTimeMap is null || !request.Options.RequestWordTimestamps || index != count - 1 ||
            retained.Count == 0 || retained[^1].RelativeEnd != request.InputDuration)
            return false;
        (TimeSpan start, TimeSpan end) = WhisperCppJsonReader.ReadTimes(item);
        return start == request.InputDuration && end > start &&
            end - start <= TimeSpan.FromMilliseconds(100) &&
            !string.IsNullOrWhiteSpace(WhisperCppJsonReader.RequiredString(item, "text"));
    }

    private static void AddRootWarnings(
        IReadOnlyCollection<AudioTranscriptionSegment> segments,
        AudioTranscriptionLanguage? detectedLanguage,
        ICollection<AudioTranscriptionWarning> warnings)
    {
        if (detectedLanguage is null)
        {
            warnings.Add(
                new AudioTranscriptionWarning(
                    AudioTranscriptionWarningCode.LanguageNotReported,
                    "The provider did not report a detected language."));
        }

        if (segments.Count == 0)
        {
            warnings.Add(
                new AudioTranscriptionWarning(
                    AudioTranscriptionWarningCode.NoSpeechDetected,
                    "The provider returned no nonempty transcript segments."));
        }
    }
}
