using System.Text.Json;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Platform.Transcription;

internal static class WhisperCppWordParser
{
    public static AudioTranscriptionWord[] ReadWords(
        JsonElement segment,
        TimeSpan absoluteOffset,
        TimeSpan segmentStart,
        TimeSpan segmentEnd,
        out bool timingCanonicalized)
    {
        timingCanonicalized = false;
        bool containsProviderWords = segment.TryGetProperty(
            "words",
            out JsonElement words);
        if (!containsProviderWords && !segment.TryGetProperty("tokens", out words))
        {
            return [];
        }

        if (words.ValueKind != JsonValueKind.Array)
        {
            throw new WhisperCppTranscriptionException(
                "Transcript word data must be an array.");
        }

        return containsProviderWords
            ? ReadProviderWords(
                words,
                absoluteOffset,
                segmentStart,
                segmentEnd,
                ref timingCanonicalized)
            : ReadWhisperTokens(
                words,
                absoluteOffset,
                segmentStart,
                segmentEnd,
                ref timingCanonicalized);
    }

    private static AudioTranscriptionWord[] ReadProviderWords(
        JsonElement words,
        TimeSpan absoluteOffset,
        TimeSpan segmentStart,
        TimeSpan segmentEnd,
        ref bool timingCanonicalized)
    {
        var result = new List<AudioTranscriptionWord>();
        TimeSpan previousEnd = segmentStart;
        foreach (JsonElement word in words.EnumerateArray())
        {
            string text = WhisperCppJsonReader.RequiredString(word, "text");
            (TimeSpan start, TimeSpan end) =
                WhisperCppJsonReader.ReadTimes(word);
            CanonicalizeToSegment(
                ref start,
                ref end,
                segmentStart,
                segmentEnd,
                previousEnd,
                ref timingCanonicalized);
            result.Add(
                CreateWord(
                    text,
                    start,
                    end,
                    absoluteOffset,
                    WhisperCppJsonReader.OptionalRatio(word, "probability") ??
                    WhisperCppJsonReader.OptionalRatio(word, "p")));
            previousEnd = end;
        }

        return result.ToArray();
    }

    private static AudioTranscriptionWord[] ReadWhisperTokens(
        JsonElement tokens,
        TimeSpan absoluteOffset,
        TimeSpan segmentStart,
        TimeSpan segmentEnd,
        ref bool timingCanonicalized)
    {
        var groups = new List<WhisperWordGroup>();
        bool startNextWord = true;
        foreach (JsonElement token in tokens.EnumerateArray())
        {
            string rawText =
                WhisperCppJsonReader.RequiredRawTokenString(token, "text");
            if (string.IsNullOrWhiteSpace(rawText))
            {
                startNextWord = true;
                continue;
            }

            string text = rawText.Trim();
            if (IsSpecialToken(text))
            {
                continue;
            }

            bool startsWord =
                startNextWord ||
                char.IsWhiteSpace(rawText[0]) ||
                groups.Count == 0;
            startNextWord = false;
            (TimeSpan start, TimeSpan end) =
                WhisperCppJsonReader.ReadTimes(token);
            TimeSpan minimumStart = startsWord && groups.Count > 0
                ? groups[^1].End
                : segmentStart;
            CanonicalizeToSegment(
                ref start,
                ref end,
                segmentStart,
                segmentEnd,
                minimumStart,
                ref timingCanonicalized);
            if (startsWord)
            {
                groups.Add(
                    new WhisperWordGroup(
                        text,
                        start,
                        end,
                        WhisperCppJsonReader.OptionalRatio(token, "p"),
                        1));
                continue;
            }

            WhisperWordGroup current = groups[^1];
            groups[^1] = current with
            {
                Text = current.Text + text,
                End = end > current.End ? end : current.End,
                Probability = null,
                TokenCount = current.TokenCount + 1,
            };
        }

        return groups
            .Where(static group => !string.IsNullOrWhiteSpace(group.Text))
            .Select(group => CreateWord(
                group.Text,
                group.Start,
                group.End,
                absoluteOffset,
                group.TokenCount == 1 ? group.Probability : null))
            .ToArray();
    }

    private static void CanonicalizeToSegment(
        ref TimeSpan start,
        ref TimeSpan end,
        TimeSpan segmentStart,
        TimeSpan segmentEnd,
        TimeSpan minimumStart,
        ref bool timingCanonicalized)
    {
        TimeSpan providerStart = start;
        TimeSpan providerEnd = end;
        start = start < segmentStart
            ? segmentStart
            : start > segmentEnd
                ? segmentEnd
                : start;
        end = end < segmentStart
            ? segmentStart
            : end > segmentEnd
                ? segmentEnd
                : end;
        if (start < minimumStart)
        {
            start = minimumStart;
        }

        if (end < start)
        {
            end = start;
        }

        timingCanonicalized |=
            providerStart != start ||
            providerEnd != end;
    }

    private static AudioTranscriptionWord CreateWord(
        string text,
        TimeSpan start,
        TimeSpan end,
        TimeSpan absoluteOffset,
        double? probability) =>
        new(
            text,
            start,
            end,
            absoluteOffset + start,
            absoluteOffset + end,
            probability);

    private static bool IsSpecialToken(string value) =>
        (value.StartsWith("[_", StringComparison.Ordinal) &&
         value.EndsWith("]", StringComparison.Ordinal)) ||
        (value.StartsWith("<|", StringComparison.Ordinal) &&
         value.EndsWith("|>", StringComparison.Ordinal));

    private sealed record WhisperWordGroup(
        string Text,
        TimeSpan Start,
        TimeSpan End,
        double? Probability,
        int TokenCount);
}
