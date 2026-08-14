using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataRules
{
    internal static readonly HashSet<string> NonRetrospectiveActionForms = new(
        [
            "appear", "appears", "attack", "attacks", "beat", "beats",
            "break", "breaks", "chase", "chases", "climb", "climbs",
            "confront", "confronts", "defeat", "defeats", "descend",
            "descends", "destroy", "destroys", "discover", "discovers",
            "enter", "enters", "erupt", "erupts", "escape", "escapes", "explore", "explores",
            "fail", "fails", "fall", "falls", "fight", "fights", "find",
            "finds", "float", "floats", "follow", "follows", "grab", "grabs", "hang", "hangs", "hold", "holds", "investigate",
            "investigates", "jump", "jumps", "kill", "kills", "leave",
            "leaves", "lose", "loses", "meet", "meets", "move", "moves",
            "occur", "occurs", "open", "opens", "reach", "reaches", "rescue", "rescues",
            "pulse", "pulses", "return", "returns", "run", "runs", "save", "saves", "shoot",
            "shoots", "say", "says", "shift", "shifts", "sneak", "sneaks", "survive", "survives", "unlock",
            "unlocks", "upgrade", "upgrades", "win", "wins", "glow", "glows",
            "raise", "raises", "stand", "stands", "carry", "carries",
            "walk", "walks",
        ],
        StringComparer.OrdinalIgnoreCase);
    internal static readonly HashSet<string> CommonIrregularPastForms = new(
        [
            "became", "began", "broke", "brought", "built", "bought", "came",
            "caught", "chose", "cut", "did", "drew", "drove", "fell", "felt",
            "fled", "flew", "fought", "found", "gave", "got", "heard", "held",
            "hid", "hit", "kept", "knew", "lay", "led", "left", "lost", "made",
            "met", "paid", "put", "ran", "read", "rode", "said", "saw", "sent",
            "set", "shot", "spoke", "stood", "stole", "struck", "survived",
            "swam", "took", "told", "thought", "threw", "understood", "went",
            "won", "wore", "wrote",
        ],
        StringComparer.OrdinalIgnoreCase);
    internal static readonly HashSet<string> DanglingTitleEndings = new(
        [
            "a", "an", "and", "as", "at", "before", "but", "by", "for",
            "from", "in", "into", "of", "on", "or", "the", "then", "through",
            "to", "with",
        ],
        StringComparer.OrdinalIgnoreCase);
    private static readonly Regex FirstPersonReference = new(
        @"\b(?:i|me|my|mine|we|us|our|ours)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex FirstPersonPossessive = new(
        @"\b(?:my|mine|our|ours)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex FirstPersonSubjectAction = new(
        @"\b(?:i|we)\s+(?:had\s+)?([a-z]+)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> CreatorEncounterActions = new(
        [
            "approached", "arrived", "confronted", "discovered",
            "encountered", "entered", "escaped", "faced", "followed",
            "found", "met", "noticed", "observed", "reached", "saw",
            "spotted", "watched", "witnessed",
        ],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> CreatorAffectedActions = new(
        [
            "became", "died", "dropped", "escaped", "fell", "got",
            "lost", "received", "stumbled", "suffered", "survived",
            "took", "was", "were",
        ],
        StringComparer.OrdinalIgnoreCase);

    internal static void Validate(
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
        bool creatorAuthorityUsesAudienceFieldsOnly = false)
    {
        string hashtag = request.Context.GameContext.GameHashtag;
        var failures = new List<string>();
        if (title.Length > ClipEditorialMetadataDraft.MaximumTitleLength)
        {
            failures.Add(
                $"title length {title.Length} exceeds " +
                $"{ClipEditorialMetadataDraft.MaximumTitleLength}");
        }
        if (!title.EndsWith($" {hashtag}", StringComparison.Ordinal))
        {
            failures.Add(
                $"title does not end with one space followed by exact hashtag '{hashtag}'");
        }
        if (description.Length > 900)
        {
            failures.Add($"description length {description.Length} exceeds 900");
        }
        if (tags.Count is < 1 or > 8)
        {
            failures.Add($"tag count {tags.Count} is outside 1 through 8");
        }
        if (tags.Any(static tag => tag.Length > 60))
        {
            failures.Add("one or more tags exceed 60 characters");
        }
        if (tags.Any(static tag => tag.Contains('#')))
        {
            failures.Add("one or more tags contain a leading # character");
        }
        if (tags.Distinct(StringComparer.OrdinalIgnoreCase).Count() != tags.Count)
        {
            failures.Add("tags contain a case-insensitive duplicate");
        }
        string[] forbiddenTags =
            ["player", "character", "streamer", "creator", "reaction"];
        if (tags.Any(tag => forbiddenTags.Contains(
                tag,
                StringComparer.OrdinalIgnoreCase)))
        {
            failures.Add("tags contain a generic role or unsupported reaction label");
        }
        if (tags.Any(tag =>
                ClipEditorialGeneratedTags.ContainsUnsupportedGeneratedClaim(
                    tag,
                    request.Context.GameContext.GameName,
                    request.Context.GameContext.GameHashtag,
                    request.Profile.DefaultTags)))
        {
            failures.Add(
                "tags contain an unsupported release, year, or platform claim");
        }
        if (Qwen3VlGroundedMetadataLanguageRules.ContainsInternalTiming(title))
        {
            failures.Add("title exposes internal source timing");
        }
        if (Qwen3VlGroundedMetadataLanguageRules.ContainsInternalTiming(description))
        {
            failures.Add("description exposes internal source timing");
        }
        if (Qwen3VlGroundedMetadataLanguageRules.ContainsAnalysisBookkeeping(title))
        {
            failures.Add("title exposes analysis bookkeeping");
        }
        if (Qwen3VlGroundedMetadataLanguageRules.ContainsAnalysisBookkeeping(description))
        {
            failures.Add("description exposes analysis bookkeeping");
        }
        if (requireLiteralActionEntailment && primaryVisualDraft is not null)
        {
            Qwen3VlGroundedMetadataActionStrengthPolicy.Validate(
                title, description, primaryVisualDraft, failures);
        }
        if (requireInterfaceAttributionAuthority)
        {
            Qwen3VlGroundedMetadataInterfaceAttributionPolicy.Validate(
                title,
                description,
                request,
                failures);
        }
        if (primaryVisualDraft is not null &&
            primaryActorAuthority is not null &&
            primaryCreatorExperienceRelation is not null)
        {
            ValidateCreatorActorAuthority(
                title,
                description,
                tags,
                request,
                primaryVisualDraft,
                primaryActorAuthority.Value,
                primaryCreatorExperienceRelation.Value,
                failures,
                creatorAuthorityUsesAudienceFieldsOnly);
        }
        if (Qwen3VlGroundedMetadataLanguageRules
                .UsesNonRetrospectiveTitleOpening(title, hashtag))
        {
            failures.Add(
                "title uses a command, present-tense, or gerund action opening");
        }
        if (Qwen3VlGroundedMetadataLanguageRules
                .HasDanglingTitleEnding(title, hashtag))
        {
            failures.Add("title ends with an incomplete connective or article");
        }
        if (Qwen3VlGroundedMetadataLanguageRules
                .UsesNonRetrospectiveDescription(description))
        {
            failures.Add("description uses non-retrospective narration");
        }
        if (Qwen3VlGroundedMetadataLanguageRules.IsGenericOnlyTitle(
                title,
                request.Context.GameContext.GameName,
                hashtag))
        {
            failures.Add("title contains no concrete supported content words");
        }
        if (Qwen3VlGroundedMetadataLanguagePolicy.ContainsUnapprovedNonLatinAudienceCopy(
                title,
                request) ||
            Qwen3VlGroundedMetadataLanguagePolicy.ContainsUnapprovedNonLatinAudienceCopy(
                description,
                request) ||
            tags.Any(tag => Qwen3VlGroundedMetadataLanguagePolicy.ContainsUnapprovedNonLatinAudienceCopy(
                tag,
                request)))
        {
            failures.Add(
                "audience copy does not preserve the English output-language policy");
        }
        IEnumerable<ClipEditorialMetadataQualityIssue> qualityIssues =
            ClipEditorialMetadataQuality.Evaluate(
                    title,
                    description,
                    request.Context);
        bool neutralPersonSubjectPermitted = allowNeutralPersonSubject &&
            primaryActorAuthority is
                Qwen3VlGroundedMetadataActorAuthority.Unknown or
                Qwen3VlGroundedMetadataActorAuthority.OtherPerson &&
            primaryCreatorExperienceRelation !=
                Qwen3VlGroundedMetadataCreatorExperienceRelation.CreatorActed;
        bool containsForbiddenCreatorRole = Regex.IsMatch(
            title + "\n" + description,
            @"\b(?:player|character|streamer|creator|camera\s+wearer)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (neutralPersonSubjectPermitted && !containsForbiddenCreatorRole)
        {
            qualityIssues = qualityIssues.Where(static issue =>
                issue.Code !=
                    ClipEditorialMetadataQualityIssueCode
                        .ThirdPersonCreatorFraming);
        }
        failures.AddRange(
            qualityIssues
                .Select(static issue =>
                    $"quality {issue.Code}: {issue.Message}"));
        if (failures.Count > 0)
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen metadata violated its bounded game-grounded " +
                $"contract for candidate '{request.Context.CandidateId}'. " +
                $"Failed rules: {string.Join("; ", failures)}. " +
                $"Title={Qwen3VlGroundedMetadataLanguageRules.QuoteForDiagnostic(title)}; " +
                $"Description={Qwen3VlGroundedMetadataLanguageRules.QuoteForDiagnostic(description)}; " +
                $"Tags=[{string.Join(", ", tags.Select(Qwen3VlGroundedMetadataLanguageRules.QuoteForDiagnostic))}].");
        }
    }


    private static void ValidateCreatorActorAuthority(
        string title,
        string description,
        IReadOnlyList<string> tags,
        ClipEditorialMetadataRequest request,
        Qwen3VlGroundedMetadataVisualDraft primaryVisualDraft,
        Qwen3VlGroundedMetadataActorAuthority actorAuthority,
        Qwen3VlGroundedMetadataCreatorExperienceRelation creatorRelation,
        ICollection<string> failures,
        bool audienceFieldsOnly)
    {
        string audienceCopy = audienceFieldsOnly
            ? string.Join('\n', [title, description])
            : string.Join('\n', [title, description, .. tags]);
        if (!FirstPersonReference.IsMatch(audienceCopy) ||
            request.VariantIntent == ClipEditorialVariantIntent.CommentaryLed &&
            HasReviewedCommentarySupport(request, audienceCopy))
        {
            return;
        }
        if (creatorRelation ==
            Qwen3VlGroundedMetadataCreatorExperienceRelation.Unestablished)
        {
            failures.Add(
                "unsupported creator embodiment without an established " +
                "creator-experience relation");
            return;
        }
        if (actorAuthority ==
            Qwen3VlGroundedMetadataActorAuthority.CreatorControlled)
        {
            return;
        }

        string[] directActions = FirstPersonSubjectAction.Matches(audienceCopy)
            .Select(static match => match.Groups[1].Value)
            .ToArray();
        if (creatorRelation ==
            Qwen3VlGroundedMetadataCreatorExperienceRelation.CreatorEncountered)
        {
            if (FirstPersonPossessive.IsMatch(audienceCopy) ||
                directActions.Any(static action =>
                    !CreatorEncounterActions.Contains(action)))
            {
                failures.Add(
                    "unsupported creator embodiment for another person's " +
                    "primary action; neutral past-action or grounded " +
                    "creator-encounter wording is required");
            }
            return;
        }

        if (creatorRelation ==
            Qwen3VlGroundedMetadataCreatorExperienceRelation.CreatorAffected)
        {
            var primaryActionStems = primaryVisualDraft.Actions
                .SelectMany(static action => Regex.Matches(
                    action,
                    @"[\p{L}\p{Nd}'’_-]+",
                    RegexOptions.CultureInvariant))
                .Select(static match =>
                    Qwen3VlGroundedMetadataLanguageRules.ActionStem(
                        match.Value))
                .Where(static value => value.Length >= 3)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (actorAuthority ==
                    Qwen3VlGroundedMetadataActorAuthority.OtherPerson &&
                    FirstPersonPossessive.IsMatch(audienceCopy) ||
                directActions.Any(action =>
                    !CreatorAffectedActions.Contains(action) &&
                        !CreatorEncounterActions.Contains(action) ||
                    action is not ("got" or "was" or "were") &&
                    primaryActionStems.Contains(
                        Qwen3VlGroundedMetadataLanguageRules.ActionStem(
                            action))))
            {
                failures.Add(
                    "unsupported creator embodiment for another person's body " +
                    "or primary action; only the grounded effect on the creator " +
                    "experience is permitted");
            }
            return;
        }

        failures.Add(
            "unsupported creator embodiment without creator-controlled " +
            "primary-action authority");
    }

    private static bool HasReviewedCommentarySupport(
        ClipEditorialMetadataRequest request,
        string audienceCopy)
    {
        string normalizedAudience = " " +
            Qwen3VlGroundedMetadataLanguageRules.NormalizeWords(audienceCopy) +
            " ";
        return request.Context.Transcripts
            .Where(static transcript =>
                transcript.MaySupportVerbatimAudienceCopy)
            .Any(transcript =>
            {
                string[] words = Qwen3VlGroundedMetadataLanguageRules
                    .NormalizeWords(transcript.Text)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return Enumerable.Range(0, Math.Max(0, words.Length - 2))
                    .Select(index => string.Join(' ', words[index..(index + 3)]))
                    .Any(term => normalizedAudience.Contains(
                        " " + term + " ",
                        StringComparison.Ordinal));
            });
    }

}
