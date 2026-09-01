using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;

namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial;

public static partial class ClipEditorialMetadataQuality
{
    public const int PreferredMaximumTitleLength = 80;
    public const int PreferredMaximumDescriptionLength = 420;
    private static readonly HashSet<string> AdditionalDetailStopWords = new(
        [
            "a", "an", "and", "as", "at", "before", "by", "for", "from",
            "i", "in", "into", "it", "my", "of", "on", "or", "the",
            "then", "through", "to", "we", "with",
        ],
        StringComparer.Ordinal);
    private static readonly HashSet<string> CommentaryAngleStopWords = new(
        [
            "about", "after", "again", "also", "because", "before", "could",
            "from", "have", "here", "into", "just", "really", "that", "their",
            "there", "these", "they", "this", "those", "through", "very", "were",
            "what", "when", "where", "which", "while", "with", "would",
        ],
        StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, string>
        EditorialEquivalents = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["materials"] = "material",
            ["materialstab"] = "material",
            ["remained"] = "remain",
            ["remains"] = "remain",
            ["stayed"] = "remain",
            ["stays"] = "remain",
            ["stay"] = "remain",
        };

    public static IReadOnlyList<ClipEditorialMetadataQualityIssue> Evaluate(
        string title,
        string description,
        ClipEditorialContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(context);

        var issues = new List<ClipEditorialMetadataQualityIssue>();
        string audienceTitle = title.Replace(
            context.GameContext.AudienceGameHashtag,
            string.Empty,
            StringComparison.OrdinalIgnoreCase).Trim();
        string combined = audienceTitle + "\n" + description;

        AddIf(
            issues,
            ThirdPersonCreatorRegex().IsMatch(combined) ||
            GenericPersonSubjectOpeningRegex().IsMatch(audienceTitle) ||
            GenericPersonSubjectOpeningRegex().IsMatch(description) ||
            FirstPersonGenericObserverOpeningRegex().IsMatch(description),
            ClipEditorialMetadataQualityIssueCode.ThirdPersonCreatorFraming,
            "Audience copy uses a generic observer or person label instead of creator voice or a grounded named entity.");
        AddIf(
            issues,
            GenericOpeningRegex().IsMatch(description),
            ClipEditorialMetadataQualityIssueCode.GenericOpening,
            "The description opens with generic video-summary boilerplate instead of the supported action.");
        HashSet<string> audienceAngleTerms = NormalizeLexical(combined)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(static token =>
                token.Length >= 4 &&
                !CommentaryAngleStopWords.Contains(token))
            .ToHashSet(StringComparer.Ordinal);
        bool hasGroundedReviewedCreatorCommentaryAngle = context.Transcripts.Any(
            transcript =>
                transcript.Role.Role == AudioContentRole.CreatorSpeech &&
                transcript.MaySupportVerbatimAudienceCopy &&
                NormalizeLexical(transcript.Text)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Any(token => token.Length >= 4 &&
                        !CommentaryAngleStopWords.Contains(token) &&
                        audienceAngleTerms.Contains(token)));
        bool hasCreatorEditorialAngle =
            hasGroundedReviewedCreatorCommentaryAngle ||
            context.EditorialBrief.SafeCommentaryAngle is not null;
        AddIf(
            issues,
            HighRiskLiteralSceneReportRegex().IsMatch(combined) ||
            (hasCreatorEditorialAngle &&
                (LiteralSceneReportRegex().IsMatch(audienceTitle) ||
                    LiteralSceneReportRegex().IsMatch(description))),
            ClipEditorialMetadataQualityIssueCode.LiteralSceneReport,
            "Audience copy falls back to a literal scene report instead of a supported clip thesis or creator angle.");
        AddIf(
            issues,
            UnsupportedMentalStateRegex().IsMatch(combined),
            ClipEditorialMetadataQualityIssueCode.UnsupportedMentalState,
            "Audience copy assigns an emotion, intent, or internal state that visual evidence alone cannot establish.");
        int preferredTitleMaximum = Math.Min(
            ClipEditorialMetadataDraft.MaximumTitleLength,
            Math.Max(
                PreferredMaximumTitleLength,
                context.GameContext.AudienceGameHashtag.Length + 12));
        AddIf(
            issues,
            title.Length > preferredTitleMaximum ||
            description.Length > PreferredMaximumDescriptionLength,
            ClipEditorialMetadataQualityIssueCode.OverlongAudienceCopy,
            $"Audience copy exceeds the preferred {preferredTitleMaximum}-character title or {PreferredMaximumDescriptionLength}-character description limit.");

        string normalizedTitle = NormalizeLexical(audienceTitle);
        string normalizedDescription = NormalizeLexical(description);
        string normalizedGameName = NormalizeLexical(
            context.GameContext.AudienceGameName);
        AddIf(
            issues,
            normalizedGameName.Length >= 3 &&
            context.GameContext.AudienceGameName.All(static value => value <= '\u007f') &&
            (" " + normalizedTitle + " ").Contains(
                " " + normalizedGameName + " ",
                StringComparison.Ordinal),
            ClipEditorialMetadataQualityIssueCode.RedundantGameIdentity,
            "The title repeats the confirmed game name even though separate generated tags already carry that identity.");
        AddIf(
            issues,
            IsUnexpandedTitleRepetition(
                normalizedTitle,
                normalizedDescription),
            ClipEditorialMetadataQualityIssueCode.TitleDescriptionRepetition,
            "The description repeats the title rather than adding grounded context.");

        foreach (ClipEditorialTranscriptContext transcript in
                 context.Transcripts.Where(static value =>
                     !value.MaySupportVerbatimAudienceCopy))
        {
            if (ContainsTranscriptWindow(
                    combined,
                    transcript.Text,
                    minimumWindow: 4))
            {
                issues.Add(new ClipEditorialMetadataQualityIssue(
                    ClipEditorialMetadataQualityIssueCode.UnreviewedTranscriptReuse,
                    "Audience copy reuses a phrase from an automatic transcript that has not been corrected or human-reviewed."));
                break;
            }
        }

        return new ReadOnlyCollection<ClipEditorialMetadataQualityIssue>(
            issues.ToArray());
    }

    private static bool ContainsTranscriptWindow(
        string audienceCopy,
        string transcript,
        int minimumWindow)
    {
        string normalizedCopy = $" {NormalizeLexical(audienceCopy)} ";
        string[] tokens = NormalizeLexical(transcript).Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < minimumWindow)
        {
            return false;
        }

        for (int index = 0;
             index <= tokens.Length - minimumWindow;
             index++)
        {
            string phrase = " " + string.Join(
                ' ',
                tokens.Skip(index).Take(minimumWindow)) + " ";
            if (normalizedCopy.Contains(phrase, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnexpandedTitleRepetition(
        string title,
        string description)
    {
        HashSet<string> titleVocabulary = ContentTerms(title);
        if (titleVocabulary.Count < 3)
        {
            return false;
        }

        HashSet<string> descriptionVocabulary = ContentTerms(description);
        int coveredTitleTerms = titleVocabulary.Count(
            descriptionVocabulary.Contains);
        if (coveredTitleTerms * 4 < titleVocabulary.Count * 3)
        {
            return false;
        }

        int addedDetailCount = descriptionVocabulary
            .Count(token => !titleVocabulary.Contains(token));
        return addedDetailCount < 2;
    }

    private static HashSet<string> ContentTerms(string value) =>
        NormalizeLexical(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(static token => !AdditionalDetailStopWords.Contains(token))
            .Select(CanonicalizeTerm)
            .Where(static token => token.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

    private static string CanonicalizeTerm(string token) =>
        EditorialEquivalents.TryGetValue(token, out string? canonical)
            ? canonical
            : token;

    private static string NormalizeLexical(string value) =>
        WhitespaceRegex().Replace(
            NonLexicalRegex().Replace(value.ToLowerInvariant(), " "),
            " ").Trim();

    private static void AddIf(
        ICollection<ClipEditorialMetadataQualityIssue> issues,
        bool condition,
        ClipEditorialMetadataQualityIssueCode code,
        string message)
    {
        if (condition)
        {
            issues.Add(new ClipEditorialMetadataQualityIssue(code, message));
        }
    }

    [GeneratedRegex(
        @"(?ix)\b(?:player|character|streamer|creator|camera\s+wearer)\b")]
    private static partial Regex ThirdPersonCreatorRegex();

    [GeneratedRegex(
        @"(?ix)^\s*(?:(?:a|an|the|this|that)\s+(?:[\p{L}\p{N}'’_-]+\s+){0,4})?(?:man|woman|person|guy|player|character)\b")]
    private static partial Regex GenericPersonSubjectOpeningRegex();

    [GeneratedRegex(
        @"(?ix)^\s*(?:i|we)\s+(?:heard|noticed|observed|saw|spotted|watched)\s+(?:(?:a|an|the|this|that)\s+)?(?:[\p{L}\p{N}'’_-]+\s+){0,4}(?:man|woman|person|guy|player|character)\b")]
    private static partial Regex FirstPersonGenericObserverOpeningRegex();

    [GeneratedRegex(
        @"(?ix)^\s*(?:this\s+(?:clip|video)\s+(?:shows|features|captures|is\s+about)|in\s+this\s+(?:clip|video)|i\s+(?:watch|see)\b|watch\s+as\b)")]
    private static partial Regex GenericOpeningRegex();

    [GeneratedRegex(
        @"(?ix)^\s*(?:(?:a|an|the|this|that)\s+(?:person|man|woman|guy|document|note|screen|menu|interface|scene|sequence|cutscene)\b|(?:document|note|screen|menu|interface|scene|sequence|cutscene)\s+(?:showed|displayed|revealed|listed|contained|presented)\b|(?:i|we)\s+(?:entered|walked|looked|opened|watched|saw|found)\b)")]
    private static partial Regex LiteralSceneReportRegex();

    [GeneratedRegex(
        @"(?ix)\b(?:appears?|appeared|shows?|showed|displays?|displayed)\s+on\s+(?:the\s+)?screen\b|\b(?:stay|stays|stayed|remain|remains|remained)\s+(?:in|on)\s+(?:the\s+)?(?:room|scene|screen|frame)\b")]
    private static partial Regex HighRiskLiteralSceneReportRegex();

    [GeneratedRegex(
        @"(?ix)\b(?:appears?|seems?)\s+(?:afraid|angry|anxious|confused|distressed|excited|frustrated|happy|nervous|sad|scared|shocked|surprised|tense)|\b(?:reacts?|reacted|reacting|reaction)\s+(?:with|to)\b|\bas\s+if\b|\bsomething\s+unseen\b|\b(?:visible|visibly)\s+(?:afraid|angry|anxious|confused|distressed|excited|frustrated|happy|nervous|sad|scared|shocked|surprised|tense)\b|\b(?:afraid|angry|anxious|confused|distressed|excited|frustrated|happy|nervous|sad|scared|shocked|surprised|tense)\s+(?:demeanou?r|expression|look)\b|\b(?:await(?:s|ed|ing)?|wait(?:s|ed|ing)?)\s+(?:for|to)\b")]
    private static partial Regex UnsupportedMentalStateRegex();

    [GeneratedRegex(@"[^\p{L}\p{N}'’]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonLexicalRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
