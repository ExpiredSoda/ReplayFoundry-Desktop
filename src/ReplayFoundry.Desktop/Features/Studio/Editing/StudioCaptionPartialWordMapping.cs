using System.Text;
using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

/// <summary>Recovers uniquely matched provider words without guessing missing word clocks.</summary>
internal static class StudioCaptionPartialWordMapping
{
    internal static IReadOnlyList<AudioTranscriptionWord> RestoreAnchors(AudioTranscriptionSegment segment)
    {
        var tokens = Regex.Matches(segment.Text, @"\S+").Select(m => m.Value).ToArray();
        var source = tokens.Select(Lexical).ToArray();
        var words = segment.Words;
        if (words.Count == 0 || tokens.Length > 512 || words.Count >= tokens.Length || source.Any(s => s.Length == 0))
            return [];
        var expected = words.Select(w => Lexical(w.Text)).ToArray();
        // Only recover the known provider defect: omitted opening words with
        // an otherwise complete suffix. Text added later or unmatched internal
        // edits still need explicit alignment against the audio.
        if (!source.Skip(tokens.Length - words.Count).SequenceEqual(expected)) return [];
        // Count ordered mappings, capped at two. Ambiguous repeated text must
        // not acquire a different word's clock merely because it matches first.
        var paths = new byte[tokens.Length + 1, words.Count + 1];
        for (int i = 0; i <= tokens.Length; i++) paths[i, words.Count] = 1;
        for (int i = tokens.Length - 1; i >= 0; i--)
            for (int j = words.Count - 1; j >= 0; j--)
                paths[i, j] = (byte)Math.Min(2, paths[i + 1, j] +
                    (source[i] == expected[j] ? paths[i + 1, j + 1] : 0));
        if (paths[0, 0] != 1) return [];
        var result = new List<AudioTranscriptionWord>();
        int wordIndex = 0;
        TimeSpan anchor = segment.RelativeStart;
        TimeSpan absoluteOffset = segment.AbsoluteSourceStart - segment.RelativeStart;
        for (int i = 0; i < tokens.Length; i++)
        {
            if (wordIndex < words.Count && source[i] == expected[wordIndex] && paths[i + 1, wordIndex + 1] > 0)
            {
                var word = words[wordIndex++];
                result.Add(word);
                anchor = word.RelativeEnd;
            }
            else
                result.Add(new AudioTranscriptionWord(tokens[i], anchor, anchor,
                    absoluteOffset + anchor, absoluteOffset + anchor));
        }
        return result;
    }

    private static string Lexical(string text) => new(text.Normalize(NormalizationForm.FormKC)
        .Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
