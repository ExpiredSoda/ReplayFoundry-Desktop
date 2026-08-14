using System.Text.Json;
using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataAudienceValidator
{
    private static readonly HashSet<string> KnowledgeStopWords = new(
        StringComparer.Ordinal)
    {
        "about", "after", "again", "also", "another", "before", "being",
        "during", "following", "from", "into", "only", "other", "their",
        "there", "these", "they", "this", "through", "under", "when",
        "where", "which", "while", "with", "would", "the",
    };

    internal static void ValidateMetadata(
        string title,
        string description,
        IReadOnlyList<string> tags,
        ClipEditorialMetadataRequest request,
        Qwen3VlGroundedMetadataVisualDraft? primaryVisualDraft = null,
        Qwen3VlGroundedMetadataActorAuthority? primaryActorAuthority = null,
        Qwen3VlGroundedMetadataCreatorExperienceRelation?
            primaryCreatorExperienceRelation = null,
        bool requireLiteralActionEntailment = false,
        bool requireInterfaceAttributionAuthority = false,
        bool allowNeutralPersonSubject = false,
        bool creatorAuthorityUsesAudienceFieldsOnly = false) =>
        Qwen3VlGroundedMetadataRules.Validate(
            title,
            description,
            tags,
            request,
            primaryVisualDraft,
            primaryActorAuthority,
            primaryCreatorExperienceRelation,
            requireLiteralActionEntailment,
            requireInterfaceAttributionAuthority,
            allowNeutralPersonSubject,
            creatorAuthorityUsesAudienceFieldsOnly);

    internal static Qwen3VlGroundedMetadataGroundingReference[] ParseGrounding(
        JsonElement metadata,
        ClipEditorialMetadataRequest request,
        string title,
        string description)
    {
        JsonElement[] grounding = Qwen3VlEditorialJson.Array(metadata, "grounding");
        if (grounding.Length > 2)
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen metadata returned too many knowledge claims.");
        }
        ClipGameKnowledgeContext? knowledge = request.Context.GameKnowledge;
        var matches = (knowledge?.Matches ?? [])
            .Where(static value =>
                value.Strength is
                    GameKnowledgeMatchStrength.ClipLinked or
                    GameKnowledgeMatchStrength.CandidateForVisualGrounding)
            .ToDictionary(static value => value.Passage.Id, StringComparer.Ordinal);
        var results = new List<Qwen3VlGroundedMetadataGroundingReference>();
        var audienceFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement item in grounding)
        {
            Qwen3VlEditorialJson.Exact(
                item,
                "audienceField",
                "knowledgeReferenceIds",
                "clipEvidenceReferenceIds");
            string audienceField = Qwen3VlEditorialJson.Text(item, "audienceField");
            string[] knowledgeIds = TextArray(item, "knowledgeReferenceIds", 4);
            string[] clipIds = TextArray(item, "clipEvidenceReferenceIds", 8);
            if (audienceField is not ("Title" or "Description") ||
                !audienceFields.Add(audienceField) ||
                knowledgeIds.Length == 0 ||
                clipIds.Length == 0 ||
                knowledgeIds.Any(id => !matches.ContainsKey(id)))
            {
                throw new Qwen3VlOutputParseException(
                    "Grounded Qwen metadata returned an invalid audience-field knowledge binding.");
            }
            var permittedClipIds = knowledgeIds
                .SelectMany(id => matches[id].Strength ==
                        GameKnowledgeMatchStrength.CandidateForVisualGrounding
                    ? [Qwen3VlGroundedMetadataExecutor.ReviewEvidenceId(request.ReviewVideo!)]
                    : matches[id].ClipEvidenceIds)
                .ToHashSet(StringComparer.Ordinal);
            if (clipIds.Any(id => !permittedClipIds.Contains(id)))
            {
                throw new Qwen3VlOutputParseException(
                    "Grounded Qwen metadata cited knowledge that was not clip-linked.");
            }
            string audienceCopy = audienceField == "Title" ? title : description;
            if (!KnowledgeClaimIsSpecific(
                    audienceCopy,
                    knowledgeIds.Select(id => matches[id].Passage.Text)))
            {
                throw new Qwen3VlOutputParseException(
                    "Grounded Qwen metadata cited knowledge without a canonical name or two distinctive passage terms.");
            }
            results.Add(new Qwen3VlGroundedMetadataGroundingReference(
                audienceField,
                knowledgeIds,
                clipIds));
        }
        return results.ToArray();
    }

    internal static void ValidateGrounding(
        JsonElement metadata,
        ClipEditorialMetadataRequest request,
        string title,
        string description) =>
        _ = ParseGrounding(metadata, request, title, description);

    private static bool KnowledgeClaimIsSpecific(
        string audienceCopy,
        IEnumerable<string> passages)
    {
        var audienceTokens = KnowledgeTokens(audienceCopy);
        string[] passageValues = passages.ToArray();
        var passageTokens = passageValues
            .SelectMany(KnowledgeTokens)
            .ToHashSet(StringComparer.Ordinal);
        if (audienceTokens.Intersect(
                passageTokens,
                StringComparer.Ordinal).Take(2).Count() >= 2)
        {
            return true;
        }
        var properNames = passageValues
            .SelectMany(static passage => Regex.Matches(
                passage,
                @"\b[A-Z][A-Za-z0-9'’_-]{2,}\b",
                RegexOptions.CultureInvariant))
            .Select(static match => match.Value.ToLowerInvariant())
            .Where(static token => !KnowledgeStopWords.Contains(token))
            .ToHashSet(StringComparer.Ordinal);
        return audienceTokens.Overlaps(properNames);
    }

    private static HashSet<string> KnowledgeTokens(string value) =>
        Regex.Matches(
                value.Normalize(),
                @"\p{L}[\p{L}\p{Nd}'’_-]*",
                RegexOptions.CultureInvariant)
            .Select(static match => match.Value.ToLowerInvariant())
            .Where(static token => token.Length >= 5 &&
                !KnowledgeStopWords.Contains(token))
            .ToHashSet(StringComparer.Ordinal);

    private static string[] TextArray(
        JsonElement value,
        string name,
        int maximum)
    {
        JsonElement[] array = Qwen3VlEditorialJson.Array(value, name);
        string[] results = array
            .Select(item => item.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(item.GetString())
                ? item.GetString()!
                : throw new Qwen3VlOutputParseException(
                    $"Grounded Qwen metadata '{name}' must contain text."))
            .ToArray();
        if (results.Length > maximum ||
            results.Distinct(StringComparer.Ordinal).Count() != results.Length)
        {
            throw new Qwen3VlOutputParseException(
                $"Grounded Qwen metadata '{name}' is invalid.");
        }
        return results;
    }
}
