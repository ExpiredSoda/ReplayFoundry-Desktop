using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataLanguageRules
{
    internal static string ActionStem(string value)
    {
        string word = value.ToLowerInvariant();
        if (word.Length > 5 && word.EndsWith("ing", StringComparison.Ordinal))
        {
            word = word[..^3];
        }
        else if (word.Length > 4 && word.EndsWith("ed", StringComparison.Ordinal))
        {
            word = word[..^2];
        }
        else if (word.Length > 4 && word.EndsWith("es", StringComparison.Ordinal))
        {
            word = word[..^2];
        }
        else if (word.Length > 3 && word.EndsWith('s'))
        {
            word = word[..^1];
        }
        if (word.Length > 3 && word[^1] == word[^2])
        {
            word = word[..^1];
        }
        return word;
    }

    internal static string QuoteForDiagnostic(string value)
    {
        const int maximumLength = 240;
        string bounded = value.Length <= maximumLength
            ? value
            : value[..maximumLength] + "…";
        return JsonSerializer.Serialize(bounded);
    }

    internal static bool ContainsInternalTiming(string value) =>
        Regex.IsMatch(
            value,
            @"(?ix)(?:\b(?:\d{1,2}:)?\d{1,2}:\d{2}\b|\b\d+(?:\.\d+)?\s*(?:seconds?|minutes?)\b|\b(?:source\s+(?:position|time)|timecode|timestamp|clip\s+duration)\b|\b(?:start(?:ing)?\s+at|end(?:ing)?\s+at|captured\s+between)\b.{0,24}\d)",
            RegexOptions.CultureInvariant);

    internal static bool ContainsAnalysisBookkeeping(string value) =>
        Regex.IsMatch(
            value,
            @"(?ix)\b(?:evidence|observation|observations|observed|analysis|analyzed|candidate|deterministic|sampling|timecode|timestamp|review\s+video|visual\s+(?:point|points))\b",
            RegexOptions.CultureInvariant);

    internal static bool UsesNonRetrospectiveTitleOpening(
        string title,
        string hashtag,
        bool allowCopularCommentary = false)
    {
        string[] words = Regex.Matches(
                title.Replace(hashtag, string.Empty, StringComparison.Ordinal),
                @"[\p{L}\p{Nd}'’_-]+",
                RegexOptions.CultureInvariant)
            .Select(static match => match.Value)
            .ToArray();
        int actionIndex = words.Length > 0 &&
            words[0] is "I" or "i" or "We" or "we" ? 1 : 0;
        for (int index = actionIndex; index < words.Length; index++)
        {
            string word = words[index];
            if (word.EndsWith("ed", StringComparison.OrdinalIgnoreCase) ||
                Qwen3VlGroundedMetadataRules.CommonIrregularPastForms.Contains(word) ||
                (word is "was" or "Was" or "were" or "Were" &&
                 index + 1 < words.Length &&
                 (words[index + 1].EndsWith("ing", StringComparison.OrdinalIgnoreCase) ||
                  words[index + 1].EndsWith("ed", StringComparison.OrdinalIgnoreCase) ||
                  Qwen3VlGroundedMetadataRules.CommonIrregularPastForms.Contains(words[index + 1]))))
            {
                return false;
            }
            if (allowCopularCommentary &&
                word is "am" or "Am" or "are" or "Are" or "is" or "Is")
            {
                if (index + 1 < words.Length &&
                    words[index + 1].EndsWith(
                        "ing",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                continue;
            }
            if (Qwen3VlGroundedMetadataRules.NonRetrospectiveActionForms.Contains(word) ||
                index == actionIndex &&
                word.EndsWith("ing", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    internal static bool UsesNonRetrospectiveDescription(
        string value,
        bool allowCopularCommentary = false)
    {
        if (Regex.IsMatch(
                value,
                @"\b(?:am|are|is)\s+[\p{L}\p{Nd}'’_-]+ing\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return true;
        }
        string forms = string.Join(
            "|",
            Qwen3VlGroundedMetadataRules.NonRetrospectiveActionForms
                .Where(form => !allowCopularCommentary ||
                    form is not ("am" or "are" or "is"))
                .Select(Regex.Escape));
        if (Regex.IsMatch(
                value,
                $@"\b(?:i|we)\s+(?:{forms})\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return true;
        }
        string thirdPersonForms = string.Join(
            "|",
            Qwen3VlGroundedMetadataRules.NonRetrospectiveActionForms
                .Where(form => form.EndsWith('s') &&
                    (!allowCopularCommentary || form != "is"))
                .Select(Regex.Escape));
        return Regex.IsMatch(
            value,
            $@"(?:^|[.!?]\s+)(?!(?:i|we)\b)(?:(?:a|an|the|this|that)\s+)?(?:[\p{{L}}\p{{Nd}}'’_-]+(?:,\s*|\s+)){{0,8}}(?:{thirdPersonForms})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static bool HasDanglingTitleEnding(
        string title,
        string hashtag)
    {
        MatchCollection words = Regex.Matches(
            title.Replace(hashtag, string.Empty, StringComparison.Ordinal),
            @"[\p{L}\p{Nd}'’_-]+",
            RegexOptions.CultureInvariant);
        if (words.Count == 0)
        {
            return true;
        }

        string lastWord = words[^1].Value;
        return Qwen3VlGroundedMetadataRules.DanglingTitleEndings
                .Contains(lastWord) &&
            !IsCompleteVerbParticleEnding(words);
    }

    private static bool IsCompleteVerbParticleEnding(MatchCollection words)
    {
        if (words.Count < 2 ||
            !words[^1].Value.Equals("in", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Keep this exemption deliberately lexical and bounded. These verbs
        // can form a complete title predicate with the particle "in"; a bare
        // preposition after any other verb remains structurally incomplete.
        string previousWord = words[^2].Value.ToLowerInvariant();
        return previousWord is
            "fade" or "fades" or "faded" or
            "check" or "checks" or "checked" or
            "log" or "logs" or "logged";
    }

    internal static bool IsGenericOnlyTitle(
        string title,
        string gameName,
        string hashtag)
    {
        string content = title
            .Replace(hashtag, string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(gameName, string.Empty, StringComparison.OrdinalIgnoreCase);
        HashSet<string> generic = new(
            [
                "a", "an", "and", "at", "best", "burst", "change",
                "changes", "clip", "clips", "crazy", "epic", "event",
                "events", "from", "gameplay", "highlight", "highlights",
                "in", "incredible", "insane", "moment", "moments", "of",
                "on", "peak", "scene", "segment", "short", "shorts",
                "the", "top", "video", "videos", "wild", "with",
                "activity", "awesome",
            ],
            StringComparer.OrdinalIgnoreCase);
        return Regex.Matches(
                content,
                @"[\p{L}\p{N}][\p{L}\p{N}'’\-]*",
                RegexOptions.CultureInvariant)
            .Select(static match => match.Value)
            .All(token => generic.Contains(token));
    }

    internal static string NormalizeWords(string value) =>
        string.Join(
            ' ',
            Regex.Matches(
                    value,
                    @"[\p{L}\p{Nd}'’_-]+",
                    RegexOptions.CultureInvariant)
                .Select(static match => match.Value.ToLowerInvariant()));
}
