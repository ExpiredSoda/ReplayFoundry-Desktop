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
        AudioTranscriptionRequest request)
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
            AudioTranscriptionSegment[] segments = items
                .EnumerateArray()
                .Select((item, index) => WhisperCppSegmentParser.Parse(
                    item,
                    index,
                    request,
                    warnings))
                .ToArray();
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
