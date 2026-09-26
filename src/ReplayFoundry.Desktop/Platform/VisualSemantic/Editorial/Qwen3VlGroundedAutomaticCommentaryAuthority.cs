using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedAutomaticCommentaryAuthority
{
    private static readonly Regex FirstPersonReference = new(
        @"\b(?:i|me|my|mine|we|us|our|ours)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    internal static bool HasSafeAutomaticCommentarySupport(
        ClipEditorialMetadataRequest request,
        string audienceCopy)
    {
        GroundedEditorialBrief brief = request.Context.EditorialBrief;
        string? angle = brief.SafeCommentaryAngle;
        if (string.IsNullOrWhiteSpace(angle) || !brief.QualityFlags.Contains(
                "AutomaticCreatorReactionAngleAvailable", StringComparer.Ordinal))
        {
            return false;
        }
        string normalizedAngle = NormalizeAutomaticCommentary(angle);
        if (!request.Context.Transcripts.Any(transcript =>
                transcript.Role.Role == AudioContentRole.CreatorSpeech &&
                transcript.Authority == ClipEditorialTranscriptAuthority.AutomaticUnreviewed &&
                (NormalizeAutomaticCommentary(transcript.Text)
                    .Contains(normalizedAngle, StringComparison.Ordinal) ||
                 NormalizeAutomaticCommentary(string.Join(' ', transcript.Spans.Select(static span => span.Text)))
                    .Contains(normalizedAngle, StringComparison.Ordinal))))
        {
            return false;
        }
        bool found = false;
        HashSet<string> topic = AutomaticCommentaryTopicTerms(angle);
        const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        bool negated = Regex.IsMatch(angle,
            @"\b(?:no|not|never|neither|nor|without)\b|n['’]t\b", options);
        foreach (string sentence in Regex.Split(audienceCopy, @"(?<=[.!?])\s+|\n+"))
        {
            string references = Regex.Replace(sentence,
                @"\b(a|the|this|that)\s+mine\b", "$1 object", options);
            if (!FirstPersonReference.IsMatch(references))
            {
                continue;
            }
            Match opening = Regex.Match(sentence,
                @"^\s*I\s+(?:wondered\s+(?:whether|if|about)\s+|compared\s+)", options);
            if (!opening.Success || FirstPersonReference.IsMatch(references[opening.Length..]))
            {
                return false;
            }
            if (negated && !Regex.IsMatch(sentence, @"^\s*I\s+wondered\s+about\b", options))
            {
                return false;
            }
            string remainder = sentence[opening.Length..];
            if (string.IsNullOrWhiteSpace(remainder.Trim(' ', '.', '?', '!')) ||
                Regex.IsMatch(remainder,
                    "[\\\"'“”‘’«»‹›「」『』`;:,]|\\b(?:and|or|but|then|so|because|before|after|while)\\b", options))
            {
                return false;
            }
            HashSet<string> remainderTopic = AutomaticCommentaryTopicTerms(remainder);
            if (!topic.Overlaps(remainderTopic) || !remainderTopic.IsSubsetOf(topic))
            {
                return false;
            }
            string[] originalWords = normalizedAngle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string normalizedCopy = " " + NormalizeAutomaticCommentary(sentence) + " ";
            for (int index = 0; index + 4 <= originalWords.Length; index++)
            {
                if (normalizedCopy.Contains(" " + string.Join(' ', originalWords.Skip(index).Take(4)) + " ",
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }
            found = true;
        }
        return found;
    }

    private static HashSet<string> AutomaticCommentaryTopicTerms(string value)
    {
        const string stopWords =
            "about after again also and any are back because been being but can could " +
            "did does doing for from guess had has have how into its just kind know like " +
            "look looks lot make more most much not now only our out really right said " +
            "same say see should some take than that the their them then there these " +
            "they thing think this those too very was way wearing well were what " +
            "when where which who why will with would you your";
        HashSet<string> stop = stopWords.Split(' ').ToHashSet(StringComparer.Ordinal);
        return NormalizeAutomaticCommentary(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= 3 && !stop.Contains(word))
            .Select(AutomaticCommentaryStem)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string NormalizeAutomaticCommentary(string value) =>
        string.Join(' ', Regex.Matches(value, "[A-Za-z0-9]+", RegexOptions.CultureInvariant)
            .Select(static match => match.Value.ToLowerInvariant()));

    private static string AutomaticCommentaryStem(string word)
    {
        if (word.Length > 5 && word.EndsWith("ing", StringComparison.Ordinal))
        {
            word = word[..^3];
            if (word.Length > 3 && word[^1] == word[^2])
            {
                word = word[..^1];
            }
        }
        else if (word.Length > 3 && word.EndsWith('s'))
        {
            word = word[..^1];
        }
        return word;
    }

}
