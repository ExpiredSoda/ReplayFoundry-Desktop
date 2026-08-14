using System.Globalization;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Platform.Transcription;

internal static class WhisperCppSegmentParser
{
    public static AudioTranscriptionSegment Parse(
        JsonElement item,
        int index,
        AudioTranscriptionRequest request,
        ICollection<AudioTranscriptionWarning> warnings)
    {
        string text = WhisperCppJsonReader.RequiredString(item, "text");
        (TimeSpan start, TimeSpan end) = WhisperCppJsonReader.ReadTimes(item);
        TimeSpan providerStart = start;
        TimeSpan providerEnd = end;
        bool boundaryClamped = ClampToInput(
            request.InputDuration,
            ref start,
            ref end);
        string id = TranscriptionStableId.Create(
            "ts",
            request.NeighborhoodId,
            index.ToString(CultureInfo.InvariantCulture),
            start.Ticks.ToString(CultureInfo.InvariantCulture),
            end.Ticks.ToString(CultureInfo.InvariantCulture),
            text);
        bool wordTimingCanonicalized = false;
        AudioTranscriptionWord[] words = request.Options.RequestWordTimestamps
            ? WhisperCppWordParser.ReadWords(
                item,
                request.AbsoluteSourceOffset,
                start,
                end,
                out wordTimingCanonicalized)
            : [];
        AddWarnings(
            request,
            id,
            providerStart,
            providerEnd,
            start,
            end,
            boundaryClamped,
            wordTimingCanonicalized,
            words,
            warnings);

        return new AudioTranscriptionSegment(
            id,
            request.NeighborhoodId,
            text,
            start,
            end,
            request.AbsoluteSourceOffset + start,
            request.AbsoluteSourceOffset + end,
            words,
            WhisperCppJsonReader.OptionalRatio(item, "confidence") ??
            WhisperCppJsonReader.OptionalRatio(item, "probability"),
            WhisperCppJsonReader.OptionalLanguage(item, "language"));
    }

    private static bool ClampToInput(
        TimeSpan inputDuration,
        ref TimeSpan start,
        ref TimeSpan end)
    {
        TimeSpan providerStart = start;
        TimeSpan providerEnd = end;
        if (end <= TimeSpan.Zero || start >= inputDuration)
        {
            throw new WhisperCppTranscriptionException(
                "whisper.cpp returned a segment wholly outside the bounded neighborhood.",
                $"Segment: {providerStart:c}–{providerEnd:c}; " +
                $"neighborhood duration: {inputDuration:c}.");
        }

        start = start < TimeSpan.Zero ? TimeSpan.Zero : start;
        end = end > inputDuration ? inputDuration : end;
        if (start < TimeSpan.Zero || end < start || end > inputDuration)
        {
            throw new WhisperCppTranscriptionException(
                "whisper.cpp returned a segment outside the bounded neighborhood.",
                $"Segment: {start:c}–{end:c}; " +
                $"neighborhood duration: {inputDuration:c}.");
        }

        return providerStart != start || providerEnd != end;
    }

    private static void AddWarnings(
        AudioTranscriptionRequest request,
        string id,
        TimeSpan providerStart,
        TimeSpan providerEnd,
        TimeSpan start,
        TimeSpan end,
        bool boundaryClamped,
        bool wordTimingCanonicalized,
        IReadOnlyCollection<AudioTranscriptionWord> words,
        ICollection<AudioTranscriptionWarning> warnings)
    {
        if (boundaryClamped)
        {
            warnings.Add(
                new AudioTranscriptionWarning(
                    AudioTranscriptionWarningCode.BoundaryClamped,
                    $"The provider segment {providerStart:c}–{providerEnd:c} " +
                    $"extended beyond the bounded neighborhood and was " +
                    $"clipped to {start:c}–{end:c}.",
                    id));
        }

        if (wordTimingCanonicalized)
        {
            warnings.Add(
                new AudioTranscriptionWarning(
                    AudioTranscriptionWarningCode.WordTimingCanonicalized,
                    "The provider returned word timing outside its segment, " +
                    "overlapping, or inverted. Replay Foundry preserved the " +
                    "text and canonicalized the affected span without " +
                    "extending its coverage.",
                    id));
        }

        if (request.Options.RequestWordTimestamps && words.Count == 0)
        {
            warnings.Add(
                new AudioTranscriptionWarning(
                    AudioTranscriptionWarningCode.WordTimestampsUnavailable,
                    "The provider returned no usable word timing for this segment.",
                    id));
        }
    }
}
