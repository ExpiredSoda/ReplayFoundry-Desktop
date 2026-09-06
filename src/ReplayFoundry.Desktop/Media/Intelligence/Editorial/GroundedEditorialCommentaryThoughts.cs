using System.Text;

namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial;

/// <summary>Complete lexical nominations only; punctuation is not factual authority.</summary>
internal static class GroundedEditorialCommentaryThoughts
{
    private const int MaximumLength = 320;
    private static readonly TimeSpan MaximumJoinGap = TimeSpan.FromSeconds(3);
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mr.", "Mrs.", "Ms.", "Dr.", "Prof.", "St.", "Jr.", "Sr.", "vs.", "etc.",
    };
    private static readonly HashSet<string> OpenEndings = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "as", "at", "because", "but", "by", "for", "from", "if",
        "into", "my", "of", "or", "our", "than", "that", "the", "their", "to", "with", "your",
    };

    internal static IEnumerable<string> Candidates(ClipEditorialTranscriptContext transcript)
    {
        foreach (string run in ContiguousRuns(transcript))
        {
            ThoughtPart[] sentences = SentenceParts(run).ToArray();
            for (int start = 0; start < sentences.Length; start++)
            {
                // A correction belongs with the preceding thought. Never select
                // the tempting premise while dropping its immediately retained negation.
                if (!sentences[start].Complete || (start > 0 && IsCorrection(sentences[start].Text))) continue;
                var candidate = new StringBuilder(sentences[start].Text);
                bool complete = true;
                int end = start;
                while (end + 1 < sentences.Length && IsCorrection(sentences[end + 1].Text))
                {
                    ThoughtPart next = sentences[++end];
                    candidate.Append(' ').Append(next.Text);
                    complete &= next.Complete;
                }
                if (complete && candidate.Length <= MaximumLength && HasContent(candidate.ToString()))
                    yield return candidate.ToString();
            }
        }
    }

    private static IEnumerable<string> ContiguousRuns(ClipEditorialTranscriptContext transcript)
    {
        if (transcript.Spans.Count == 0)
        {
            yield return transcript.Text;
            yield break;
        }
        var run = new StringBuilder();
        ClipEditorialTranscriptSpan? previous = null;
        foreach (ClipEditorialTranscriptSpan span in transcript.Spans)
        {
            if (previous is not null && span.SourceStart - previous.SourceEnd > MaximumJoinGap)
            {
                yield return run.ToString();
                run.Clear();
            }
            if (run.Length > 0) run.Append(' ');
            run.Append(span.Text);
            previous = span;
        }
        if (run.Length > 0) yield return run.ToString();
    }

    private static IEnumerable<ThoughtPart> SentenceParts(string run)
    {
        int start = 0;
        for (int index = 0; index < run.Length; index++)
        {
            char value = run[index];
            if (value is not ('.' or '!' or '?' or '。' or '！' or '？')) continue;
            if ((index > 0 && run[index - 1] == '.') || (index + 1 < run.Length && run[index + 1] == '.')) continue;
            int end = index + 1;
            while (end < run.Length && "\"'”’)]}»".Contains(run[end])) end++;
            if (end < run.Length && !char.IsWhiteSpace(run[end])) continue;
            string candidate = run[start..end].Trim();
            string[] words = candidate.TrimEnd('"', '\'', '”', '’', ')', ']', '}', '»')
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) continue;
            string last = words[^1];
            if (value == '.' && (Abbreviations.Contains(last) || last.Length <= 2 || last.AsSpan(0, last.Length - 1).Contains('.'))) continue;
            string first = words[0].TrimStart('"', '\'', '“', '‘', '(', '[', '{', '«');
            bool incompleteStart = first.Length == 0 || char.IsLower(first[0]) ||
                ((first.ToLowerInvariant() is "because" or "if" or "when" or "while" or "unless" or "although") && !candidate.Contains(','));
            yield return new(candidate, !incompleteStart && !OpenEndings.Contains(last.TrimEnd('.', '!', '?', '。', '！', '？')));
            start = end;
        }
        // Preserve incomplete corrections as boundaries: dropping them could
        // nominate a preceding premise while hiding its retained negation.
        if (!string.IsNullOrWhiteSpace(run[start..])) yield return new(run[start..].Trim(), false);
    }

    private static bool IsCorrection(string text)
    {
        string value = text.ToLowerInvariant();
        return new[] { "actually", "but ", "yet ", "however", "except", "instead", "no, it", "no, that", "no, i ", "not ",
            "it isn't", "it isn’t", "it wasn't", "it wasn’t", "it is not", "it was not",
            "that isn't", "that isn’t", "that wasn't", "that wasn’t", "that's not", "that’s not" }
            .Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static bool HasContent(string text)
    {
        string lexical = ForScoring(text);
        return lexical.Split([' ', '\t', '\r', '\n', ',', '.', '!', '?', ';', ':'], StringSplitOptions.RemoveEmptyEntries)
            .Count(static word => word.Any(char.IsLetter) && word is not
                ("a" or "an" or "the" or "so" or "i" or "we" or "it" or "is" or "was" or "am" or
                 "are" or "be" or "this" or "that" or "think" or "guess" or "know" or "said" or "like")) >= 2;
    }

    internal static string ForScoring(string text)
    {
        string value = text.ToLowerInvariant();
        foreach (string filler in new[] { "like i said", "like we said", "like i mentioned", "like i was saying", "you know" })
            value = value.Replace(filler, " ", StringComparison.Ordinal);
        return value;
    }

    private sealed record ThoughtPart(string Text, bool Complete);
}
