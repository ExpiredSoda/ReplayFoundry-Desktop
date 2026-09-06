using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

/// <summary>Changes presentation casing while retaining every observed speech interval.</summary>
public static class StudioCaptionDisplayText
{
    public static string SliceWordText(StudioCaptionCue cue, StudioCaptionWordSpan span)
    {
        int index = -1;
        for (int candidate = 0; candidate < cue.WordSpans.Count; candidate++)
            if (cue.WordSpans[candidate] == span) { index = candidate; break; }
        if (index < 0) throw new ArgumentException("The displayed word span must belong to the cue.", nameof(span));
        return SliceWordRangeText(cue, index, 1);
    }

    internal static string SliceWordRangeText(StudioCaptionCue cue, int firstWord, int wordCount)
    {
        if (firstWord < 0 || wordCount < 1 || firstWord + wordCount > cue.WordSpans.Count)
            throw new ArgumentOutOfRangeException(nameof(firstWord));
        int start = firstWord == 0 ? 0 : WordBoundary(cue, firstWord - 1);
        int end = firstWord + wordCount == cue.WordSpans.Count ? cue.Text.Length : WordBoundary(cue, firstWord + wordCount - 1);
        return cue.Text[start..end].Trim();
    }

    private static int WordBoundary(StudioCaptionCue cue, int leftIndex)
    {
        int start = cue.WordSpans[leftIndex].StartIndex + cue.WordSpans[leftIndex].Length;
        int end = cue.WordSpans[leftIndex + 1].StartIndex;
        // Closing punctuation remains with the preceding word; whitespace and
        // an opening quote/bracket before the next word remain on its side.
        for (int index = start; index < end; index++)
            if (char.IsWhiteSpace(cue.Text[index])) return index;
        while (end > start && cue.Text[end - 1] is '"' or '“' or '‘' or '(' or '[' or '{') end--;
        return end;
    }

    public static int MapIndex(string text, int index, StudioCaptionTypography typography) =>
        typography.DisplayText(text[..Math.Clamp(index, 0, text.Length)]).Length;

    public static StudioCaptionCue Transform(StudioCaptionCue cue, StudioCaptionTypography typography)
    {
        if (typography.Casing == StudioCaptionCasing.Original) return cue;
        string text = typography.DisplayText(cue.Text);
        var spans = cue.WordSpans.Select(span =>
        {
            int start = MapIndex(cue.Text, span.StartIndex, typography);
            int end = MapIndex(cue.Text, span.StartIndex + span.Length, typography);
            AudioTranscriptionWord word = span.Word;
            return new StudioCaptionWordSpan(new AudioTranscriptionWord(text[start..end],
                word.RelativeStart, word.RelativeEnd, word.AbsoluteSourceStart, word.AbsoluteSourceEnd,
                word.ProviderReportedProbability, word.IsEmphasized), start, end - start);
        }).ToArray();
        return new StudioCaptionCue(text, cue.RelativeStart, cue.RelativeEnd,
            cue.AbsoluteSourceStart, cue.AbsoluteSourceEnd, spans);
    }

    public static StudioCaptionCue Slice(StudioCaptionCue cue, int start, int length)
    {
        int end = start + length;
        var spans = cue.WordSpans.Where(span => span.StartIndex < end && span.StartIndex + span.Length > start)
            .Select(span =>
            {
                int left = Math.Max(start, span.StartIndex), right = Math.Min(end, span.StartIndex + span.Length);
                AudioTranscriptionWord word = span.Word;
                return new StudioCaptionWordSpan(new AudioTranscriptionWord(cue.Text[left..right],
                    word.RelativeStart, word.RelativeEnd, word.AbsoluteSourceStart, word.AbsoluteSourceEnd,
                    word.ProviderReportedProbability, word.IsEmphasized), left - start, right - left);
            }).ToArray();
        return new StudioCaptionCue(cue.Text.Substring(start, length), cue.RelativeStart, cue.RelativeEnd,
            cue.AbsoluteSourceStart, cue.AbsoluteSourceEnd, spans);
    }
}
