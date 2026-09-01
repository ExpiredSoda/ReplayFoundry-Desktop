using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.VisualText;

namespace ReplayFoundry.Desktop.Features.Generate.Editorial;

internal sealed record HeuristicAudienceCopyCandidate(
    string TitleBody,
    string Description,
    IReadOnlyList<ClipEditorialEvidenceReference> Evidence,
    bool IsGrounded);

internal static partial class HeuristicAudienceCopyPolicy
{
    internal static IReadOnlyList<HeuristicAudienceCopyCandidate> Build(
        ClipEditorialContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        HeuristicAudienceCopyCandidate[] grounded =
            BuildGroundedCandidates(context).ToArray();
        HeuristicAudienceCopyCandidate[] broadCatalog =
            BuildBroadCandidates(context);
        HeuristicAudienceCopyCandidate[] broad = Rotate(
            broadCatalog,
            StableVariantOffset(context, broadCatalog.Length));
        HeuristicAudienceCopyCandidate[] ordered = grounded.Length == 0
            ? broad
            : [.. grounded, .. broad];
        return ordered
            .Take(ClipEditorialPriorTitleExclusion.MaximumRetainedTitles)
            .ToArray();
    }

    internal static bool ContainsInternalProcessText(
        string title,
        string description) =>
        InternalProcessTextRegex().IsMatch(Normalize(title)) ||
        InternalProcessTextRegex().IsMatch(Normalize(description));

    internal static bool RequiresAudienceCopyFallback(
        string title,
        string description,
        ClipEditorialContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (ContainsInternalProcessText(title, description) ||
            EvidenceReportingTextRegex().IsMatch(Normalize(description)) ||
            AbstractNarrativeFillerRegex().IsMatch(Normalize(title)) ||
            AbstractNarrativeFillerRegex().IsMatch(Normalize(description)))
        {
            return true;
        }

        string titleBody = Normalize(title).Replace(
            context.GameContext.AudienceGameHashtag,
            string.Empty,
            StringComparison.OrdinalIgnoreCase).TrimEnd(' ', '.', '!', '?');
        string normalizedDescription = Normalize(description)
            .TrimEnd(' ', '.', '!', '?');
        return AudienceEvidenceText(context).Any(value =>
            value.Equals(titleBody, StringComparison.OrdinalIgnoreCase) ||
            value.Equals(
                normalizedDescription,
                StringComparison.OrdinalIgnoreCase));
    }

    private static HeuristicAudienceCopyCandidate[] BuildBroadCandidates(
        ClipEditorialContext context)
    {
        string game = GameReference(context);
        string gameplay = GameplayReference(context);
        string playthrough = PlaythroughReference(context);
        string run = RunReference(context);
        return
        [
            new(
                "An Uncut Gameplay Sequence",
                $"An uninterrupted section from {playthrough}.",
                [],
                IsGrounded: false),
            new(
                "A Short Playthrough Highlight",
                $"A short gameplay highlight from {run}.",
                [],
                IsGrounded: false),
            new(
                "One Continuous Gameplay Section",
                $"One continuous section of {gameplay}.",
                [],
                IsGrounded: false),
            new(
                "A Complete Part of the Run",
                $"A complete part of {playthrough}, kept intact from start to finish.",
                [],
                IsGrounded: false),
            new(
                "An Uninterrupted Gameplay Highlight",
                $"An uninterrupted highlight from {playthrough}.",
                [],
                IsGrounded: false),
            new(
                "A Full Playthrough Sequence",
                $"One full sequence from {run}.",
                [],
                IsGrounded: false),
            new(
                "A Continuous Part of the Run",
                $"A continuous part of {playthrough}.",
                [],
                IsGrounded: false),
            new(
                "One Full Gameplay Highlight",
                $"One full gameplay highlight from {game}.",
                [],
                IsGrounded: false),
            new(
                "A Complete Gameplay Sequence",
                $"A complete, uninterrupted {gameplay} sequence.",
                [],
                IsGrounded: false),
            new(
                "An Uncut Part of the Playthrough",
                $"An uncut section from {playthrough}.",
                [],
                IsGrounded: false),
            new(
                "A Short Section of Gameplay",
                $"A short, continuous section of {gameplay}.",
                [],
                IsGrounded: false),
            new(
                "One More Gameplay Sequence",
                $"Another complete sequence from {playthrough}.",
                [],
                IsGrounded: false),
        ];
    }

    private static IEnumerable<HeuristicAudienceCopyCandidate>
        BuildGroundedCandidates(ClipEditorialContext context)
    {
        ClipEditorialEvidenceReference[] evidence = LocalEvidence(context);
        string[] signals = LocalSignals(context);
        GroundedEditorialPresentationKind presentation =
            context.EditorialBrief.PresentationKind ==
                GroundedEditorialPresentationKind.Unclear
                ? ClassifyPresentation(signals)
                : context.EditorialBrief.PresentationKind;
        GroundedEditorialMomentKind moment =
            context.EditorialBrief.MomentKind ==
                GroundedEditorialMomentKind.Unclear
                ? ClassifyMoment(signals)
                : context.EditorialBrief.MomentKind;
        HeuristicAudienceCopyCandidate? momentCandidate = MomentCandidate(
            moment,
            evidence,
            context);
        HeuristicAudienceCopyCandidate[] presentationCandidates =
            PresentationCandidates(presentation, evidence, context).ToArray();
        IEnumerable<HeuristicAudienceCopyCandidate> candidates =
            presentationCandidates.Length == 0
                ? []
                : Rotate(
                    presentationCandidates,
                    StableVariantOffset(context, presentationCandidates.Length));
        if (momentCandidate is not null)
        {
            candidates = candidates.Append(momentCandidate);
        }
        return candidates
            .DistinctBy(
                static candidate => candidate.TitleBody,
                StringComparer.OrdinalIgnoreCase);
    }

    private static ClipEditorialEvidenceReference[] LocalEvidence(
        ClipEditorialContext context) =>
        context.Evidence.Where(static evidence =>
                evidence.Kind == ClipEditorialEvidenceKind.VisualObservation)
            .Concat(StableOcrEvidence(context))
            .GroupBy(static evidence => evidence.Id, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();

    internal static IEnumerable<ClipEditorialEvidenceReference>
        StableOcrEvidence(ClipEditorialContext context) =>
        (context.VisualText?.GroundingAnchors ?? []).Select(
            static anchor => new ClipEditorialEvidenceReference(
                anchor.EvidenceId,
                ClipEditorialEvidenceKind.VisualObservation,
                "Stable local text was retained as private evidence."));

    private static string[] LocalSignals(ClipEditorialContext context) =>
        new[]
            {
                context.EditorialBrief.PrimaryGameplayBeat,
                context.EditorialBrief.LeadIn,
                context.EditorialBrief.VisibleFollowThrough,
            }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .Concat(context.Evidence.Where(static evidence =>
                    evidence.Kind == ClipEditorialEvidenceKind.VisualObservation)
                .Select(static evidence => evidence.Description))
            .Concat((context.VisualText?.GroundingAnchors ?? [])
                .Select(static anchor => anchor.DisplayText))
            .Select(Normalize)
            .Where(static value => value.Length > 0 &&
                !ContainsInternalProcessText(value, value) &&
                !EvidenceReportingTextRegex().IsMatch(value))
            .Take(16)
            .ToArray();

    private static GroundedEditorialPresentationKind ClassifyPresentation(
        IReadOnlyList<string> signals)
    {
        string combined = string.Join(' ', signals);
        if (DocumentSignalRegex().IsMatch(combined))
            return GroundedEditorialPresentationKind.DocumentOrLore;
        if (RecordingSignalRegex().IsMatch(combined))
            return GroundedEditorialPresentationKind.InWorldRecording;
        if (CinematicSignalRegex().IsMatch(combined))
            return GroundedEditorialPresentationKind.CinematicSequence;
        if (MenuSignalRegex().IsMatch(combined))
            return GroundedEditorialPresentationKind.MenuOrLoadout;
        if (ObjectiveSignalRegex().IsMatch(combined))
            return GroundedEditorialPresentationKind.ObjectiveOrInterface;
        return GroundedEditorialPresentationKind.Unclear;
    }

    private static GroundedEditorialMomentKind ClassifyMoment(
        IReadOnlyList<string> signals)
    {
        string combined = string.Join(' ', signals);
        if (ComplicationSignalRegex().IsMatch(combined))
            return GroundedEditorialMomentKind.Complication;
        if (ActionSignalRegex().IsMatch(combined))
            return GroundedEditorialMomentKind.Action;
        if (DiscoverySignalRegex().IsMatch(combined))
            return GroundedEditorialMomentKind.Discovery;
        if (DecisionSignalRegex().IsMatch(combined))
            return GroundedEditorialMomentKind.Decision;
        if (OutcomeSignalRegex().IsMatch(combined))
            return GroundedEditorialMomentKind.Outcome;
        if (ExpositionSignalRegex().IsMatch(combined))
            return GroundedEditorialMomentKind.Exposition;
        if (ProgressSignalRegex().IsMatch(combined))
            return GroundedEditorialMomentKind.Progress;
        return GroundedEditorialMomentKind.Unclear;
    }

    private static IReadOnlyList<HeuristicAudienceCopyCandidate>
        PresentationCandidates(
        GroundedEditorialPresentationKind kind,
        IReadOnlyList<ClipEditorialEvidenceReference> evidence,
        ClipEditorialContext context)
    {
        string game = GameReference(context);
        return kind switch
        {
            GroundedEditorialPresentationKind.CinematicSequence =>
            [
                new(
                    "A Cutscene Played During the Run",
                    $"A cutscene played during this part of {game}.",
                    evidence,
                    IsGrounded: true),
                new(
                    "An In-Game Cutscene Played",
                    $"The playthrough entered a cutscene during this section of {game}.",
                    evidence,
                    IsGrounded: true),
                new(
                    "The Playthrough Reached a Cutscene",
                    "This section of the playthrough contained an in-game cutscene.",
                    evidence,
                    IsGrounded: true),
            ],
            GroundedEditorialPresentationKind.InWorldRecording =>
            [
                new(
                    "An In-World Recording Played",
                    $"An in-world recording played during this part of {game}.",
                    evidence,
                    IsGrounded: true),
                new(
                    "An In-Game Recording Played",
                    $"An in-world recording played during this section of {game}.",
                    evidence,
                    IsGrounded: true),
                new(
                    "A Recording Played During Gameplay",
                    "A recording played during this gameplay section.",
                    evidence,
                    IsGrounded: true),
                new(
                    "The Playthrough Reached a Recording",
                    $"This part of {game} contained an in-game recording.",
                    evidence,
                    IsGrounded: true),
            ],
            GroundedEditorialPresentationKind.DocumentOrLore =>
            [
                new(
                    "An In-Game Document Was Reviewed",
                    $"An in-game document was opened and read during this part of {game}.",
                    evidence,
                    IsGrounded: true),
                new(
                    "An In-Game Document Was Opened",
                    $"The playthrough paused to read an in-game document in {game}.",
                    evidence,
                    IsGrounded: true),
                new(
                    "An In-Game Document Was Read",
                    $"An in-game document was reviewed during this section of {game}.",
                    evidence,
                    IsGrounded: true),
                new(
                    "The Playthrough Paused on a Document",
                    $"An in-game document was opened during this section of {game}.",
                    evidence,
                    IsGrounded: true),
            ],
            GroundedEditorialPresentationKind.MenuOrLoadout =>
            [
                new(
                    "A Menu Choice Was Made",
                    $"A menu choice was made during this part of {game}.",
                    evidence,
                    IsGrounded: true),
                new(
                    "The Gameplay Paused for a Menu Choice",
                    "The playthrough paused briefly while the menu was reviewed.",
                    evidence,
                    IsGrounded: true),
                new(
                    "A Menu Decision Adjusted the Setup",
                    $"A menu decision adjusted the setup for this part of {game}.",
                    evidence,
                    IsGrounded: true),
                new(
                    "The Setup Changed in the Menu",
                    $"The setup was adjusted through the menu during this part of {game}.",
                    evidence,
                    IsGrounded: true),
            ],
            GroundedEditorialPresentationKind.ObjectiveOrInterface =>
            [
                new(
                    "The Current Objective Pointed the Way",
                    $"The current objective made the route through {game} clear.",
                    evidence,
                    IsGrounded: true),
                new(
                    "The Objective Set a Clear Direction",
                    $"The objective gave a clear direction to follow in {game}.",
                    evidence,
                    IsGrounded: true),
                new(
                    "An Objective Marked the Route Forward",
                    $"The current objective pointed toward the next destination in {game}.",
                    evidence,
                    IsGrounded: true),
                new(
                    "The Interface Clarified the Objective",
                    $"The interface clarified the current objective during this part of {game}.",
                    evidence,
                    IsGrounded: true),
            ],
            GroundedEditorialPresentationKind.MixedOrTransition =>
            [
                new(
                    "The Gameplay Shifted Between Sequences",
                    $"This part of {game} moved from one presentation sequence into another.",
                    evidence,
                    IsGrounded: true),
                new(
                    "One Sequence Gave Way to Another",
                    $"The presentation changed partway through this section of {game}.",
                    evidence,
                    IsGrounded: true),
            ],
            _ => [],
        };
    }

    private static HeuristicAudienceCopyCandidate? MomentCandidate(
        GroundedEditorialMomentKind kind,
        IReadOnlyList<ClipEditorialEvidenceReference> evidence,
        ClipEditorialContext context)
    {
        string game = GameReference(context);
        return kind switch
        {
            GroundedEditorialMomentKind.Action => new(
                "A Gameplay Action Sequence Played Out",
                "This clip contained a sustained gameplay action sequence.",
                evidence,
                IsGrounded: true),
            GroundedEditorialMomentKind.Progress => new(
                "The Playthrough Continued Along the Route",
                "The clip followed continued progress through the current route.",
                evidence,
                IsGrounded: true),
            GroundedEditorialMomentKind.Complication => new(
                "A Problem Changed the Plan",
                $"A complication changed how this part of {game} played out.",
                evidence,
                IsGrounded: true),
            GroundedEditorialMomentKind.Discovery => new(
                "A Discovery Changed the Route",
                $"The discovery gave this part of {game} a different direction.",
                evidence,
                IsGrounded: true),
            GroundedEditorialMomentKind.Exposition => new(
                "An Exposition Sequence Played",
                "This section contained an in-game exposition sequence.",
                evidence,
                IsGrounded: true),
            GroundedEditorialMomentKind.Decision => new(
                "A Choice Changed the Plan",
                $"A decision changed what happened next in {game}.",
                evidence,
                IsGrounded: true),
            GroundedEditorialMomentKind.Outcome => new(
                "The Outcome Became Clear",
                $"The sequence reached a visible result during this part of {game}.",
                evidence,
                IsGrounded: true),
            GroundedEditorialMomentKind.Routine => new(
                "The Run Stayed Steady",
                $"{RunReference(context)} continued through a steady stretch of gameplay.",
                evidence,
                IsGrounded: true),
            _ => null,
        };
    }

    private static T[] Rotate<T>(IReadOnlyList<T> values, int start)
    {
        if (values.Count == 0)
        {
            return [];
        }

        var rotated = new T[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            rotated[index] = values[(start + index) % values.Count];
        }
        return rotated;
    }

    private static int StableVariantOffset(
        ClipEditorialContext context,
        int candidateCount)
    {
        string identity = string.Join(
            '\u001f',
            context.CandidateId,
            context.SourceStart.Ticks,
            context.SourceEnd.Ticks,
            context.EditorialBrief.Fingerprint);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        uint value = BinaryPrimitives.ReadUInt32BigEndian(digest);
        return (int)(value % (uint)candidateCount);
    }

    private static string GameReference(ClipEditorialContext context) =>
        context.GameContext.IsUserGrounded
            ? context.GameContext.AudienceGameName
            : "the game";

    private static string GameplayReference(ClipEditorialContext context) =>
        context.GameContext.IsUserGrounded
            ? $"{context.GameContext.AudienceGameName} gameplay"
            : "gameplay";

    private static string PlaythroughReference(
        ClipEditorialContext context) =>
        context.GameContext.IsUserGrounded
            ? $"a playthrough of {context.GameContext.AudienceGameName}"
            : "the playthrough";

    private static string RunReference(ClipEditorialContext context) =>
        context.GameContext.IsUserGrounded
            ? $"the current {context.GameContext.AudienceGameName} run"
            : "the current run";

    private static IEnumerable<string> AudienceEvidenceText(
        ClipEditorialContext context) =>
        (context.VisualText?.GroundingAnchors ?? [])
            .Select(static anchor => anchor.DisplayText)
            .Concat(context.Evidence
                .Where(static evidence => evidence.Kind ==
                    ClipEditorialEvidenceKind.VisualObservation)
                .Select(static evidence => evidence.Description))
            .Concat(context.Transcripts
                .Where(static transcript =>
                    transcript.MaySupportVerbatimAudienceCopy)
                .SelectMany(static transcript => transcript.Spans.Count == 0
                    ? [transcript.Text]
                    : transcript.Spans.Select(static span => span.Text)))
            .Select(Normalize)
            .Where(static value => value.Length > 0)
            .Select(static value => value.TrimEnd(' ', '.', '!', '?'));

    private static string Normalize(string value) =>
        WhitespaceRegex().Replace(value ?? string.Empty, " ").Trim();

    [GeneratedRegex(
        @"(?ix)(?:\bevidence\s+point\b|" +
        @"\bsupports?\s+the\s+observation\b|" +
        @"\bqualified\s+visual(?:\s+evidence)?\b|" +
        @"\bdeterministic\s+(?:candidate|score|evidence)\b|" +
        @"\bunverified\s+visible\s+details\b|" +
        @"\b(?:grounding|groundedness)\s+" +
        @"(?:audit|check|validation|failure|process)\b|" +
        @"\binternal\s+(?:process|workflow|bookkeeping)\b|" +
        @"\b(?:provider|model)\s+" +
        @"(?:candidate|output|response|rewrite)\b|" +
        @"\b(?:review|inspect)\s+the\s+bounded\s+clip\s+in\s+" +
        @"(?:replay\s+foundry|studio)\b|" +
        @"\breplace\s+this\s+working\s+(?:copy|label|text)\b)",
        RegexOptions.CultureInvariant)]
    private static partial Regex InternalProcessTextRegex();

    [GeneratedRegex(
        @"(?ix)\b(?:on[- ]screen\s+text|screen\s+text|visible\s+text)\s+" +
        @"(?:reads?|includes?|says?|shows?)\b|" +
        @"\bthe\s+screen\s+(?:shows?|displays?|reads?)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex EvidenceReportingTextRegex();

    [GeneratedRegex(
        @"(?ix)\b(?:a\s+new\s+piece\s+of\s+the\s+story\s+emerged|" +
        @"the\s+next\s+beat\s+landed|revealed\s+more|added\s+more\s+context|" +
        @"filled\s+in\s+(?:more\s+)?details?|more\s+details\s+came\s+into\s+focus|" +
        @"a\s+closer\s+look\s+at\s+this\s+moment|" +
        @"focused\s+gameplay\s+moment|one\s+moment\s+from\s+the\s+recording|" +
        @"selected\s+gameplay\s+sequence|" +
        @"kept\s+together\s+as\s+one\s+complete\s+sequence)\b",
        RegexOptions.CultureInvariant)]
    internal static partial Regex AbstractNarrativeFillerRegex();

    [GeneratedRegex(
        @"(?ix)\b(?:document|lore|report|notice|file|page|letter|memo|" +
        @"reminder|read|reading)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex DocumentSignalRegex();

    [GeneratedRegex(
        @"(?ix)\b(?:recording|audio\s+log|intercom|broadcast|transmission)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex RecordingSignalRegex();

    [GeneratedRegex(
        @"(?ix)\b(?:cutscene|cinematic|flashback|films?|filming)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex CinematicSignalRegex();

    [GeneratedRegex(
        @"(?ix)\b(?:menu|inventory|loadout|upgrade|skill\s+tree)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex MenuSignalRegex();

    [GeneratedRegex(
        @"(?ix)\b(?:objective|mission|task|interface|waypoint)\b|" +
        @"\bproceed(?:ed|ing)?\s+(?:further|to|toward|through|into)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex ObjectiveSignalRegex();

    [GeneratedRegex(
        @"(?ix)\b(?:glitch|fail(?:ed|ure)?|die|died|blocked|trap(?:ped)?|" +
        @"problem|complication|stuck)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex ComplicationSignalRegex();

    [GeneratedRegex(
        @"(?ix)\b(?:fight|attack|shoot|shot|battle|combat|strike|hit|chase)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex ActionSignalRegex();

    [GeneratedRegex(
        @"(?ix)\b(?:discover(?:ed|y)?|find|found|collect(?:ed)?|pickup|" +
        @"crate|secret|reveal(?:ed)?)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex DiscoverySignalRegex();

    [GeneratedRegex(
        @"(?ix)\b(?:decide|decided|choose|chose|choice|select(?:ed)?)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex DecisionSignalRegex();

    [GeneratedRegex(
        @"(?ix)\b(?:complete(?:d)?|finish(?:ed)?|escape(?:d)?|defeat(?:ed)?|" +
        @"survive(?:d)?|win|won)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex OutcomeSignalRegex();

    [GeneratedRegex(
        @"(?ix)\b(?:speak|spoke|talk|conversation|explain(?:ed)?|learn(?:ed)?)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex ExpositionSignalRegex();

    [GeneratedRegex(
        @"(?ix)\b(?:open(?:ed)?|door|gate|ladder|climb(?:ed)?|cross(?:ed)?|" +
        @"walk(?:ed)?|route|bridge|path|enter(?:ed)?|continue(?:d)?)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex ProgressSignalRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
