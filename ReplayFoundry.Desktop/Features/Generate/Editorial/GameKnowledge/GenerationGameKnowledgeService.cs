using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Net.Http;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Platform.GameKnowledge;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Features.Generate.Editorial.GameKnowledge;

public interface IGenerationGameKnowledgeService
{
    Task<ClipEditorialContext> EnrichAsync(
        ClipEditorialContext context,
        CancellationToken cancellationToken);
}

public sealed class GenerationGameKnowledgeService :
    IGenerationGameKnowledgeService
{
    private readonly IGameKnowledgeSnapshotProvider _provider;
    private readonly IGameKnowledgeSnapshotStore _store;
    private readonly DeterministicGameKnowledgeRetriever _retriever;

    public GenerationGameKnowledgeService(
        IGameKnowledgeSnapshotProvider provider,
        IGameKnowledgeSnapshotStore store,
        DeterministicGameKnowledgeRetriever? retriever = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _retriever = retriever ?? new DeterministicGameKnowledgeRetriever();
    }

    public async Task<ClipEditorialContext> EnrichAsync(
        ClipEditorialContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.GameContext.UseOpenGameKnowledge ||
            !context.GameContext.IsUserGrounded)
        {
            return context;
        }
        cancellationToken.ThrowIfCancellationRequested();

        GameKnowledgeSnapshot? snapshot = null;
        try
        {
            snapshot = _store.Find(context.GameContext.GameName);
        }
        catch (InvalidDataException)
        {
            // A bad local snapshot is never trusted. A successful official
            // refresh below atomically replaces it.
        }
        if (snapshot is not null &&
            (!snapshot.Provider.Name.Equals(
                _provider.Identity.Name,
                StringComparison.Ordinal) ||
             !snapshot.Provider.Version.Equals(
                _provider.Identity.Version,
                StringComparison.Ordinal)))
        {
            snapshot = null;
        }

        try
        {
            if (snapshot is null)
            {
                snapshot = await _provider.AcquireAsync(
                    context.GameContext.GameName,
                    cancellationToken);
                _store.Remember(snapshot);
            }
            return context.WithGameKnowledge(
                _retriever.Retrieve(snapshot, context));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or IOException or
                InvalidDataException or UnauthorizedAccessException)
        {
            return context.WithGameKnowledge(
                new ClipGameKnowledgeContext(
                    context.GameContext.GameName,
                    snapshot: null,
                    warnings:
                    [
                        new GameKnowledgeWarning(
                            GameKnowledgeWarningCode.Unavailable,
                            "Open game knowledge was unavailable, so metadata remained grounded only in the clip and local user context."),
                    ]));
        }
    }
}

public sealed partial class DeterministicGameKnowledgeRetriever
{
    public const string PolicyVersion = "1.4";
    public const int MaximumMatches = 4;
    public const int MaximumTotalCharacters = 3_000;
    public const int MaximumClipLinkedMatches = 2;
    public const int MaximumVisualGroundingCandidates = 2;
    public const int MaximumTranscriptNominatedPassages = 3;

    private static readonly HashSet<string> StopWords = new(
        StringComparer.Ordinal)
    {
        "about", "after", "again", "against", "also", "another", "around",
        "and", "are", "because", "before", "being", "between", "both",
        "could", "during", "each", "evidence", "for", "from", "game",
        "gameplay", "has", "have", "having", "into", "itself", "nearby",
        "bounded", "clip", "interesting", "just", "know", "later", "like",
        "manual", "more", "most", "okay", "other", "over", "review",
        "scene", "some", "someone", "something", "such",
        "than", "thing",
        "that", "the", "their", "them", "then", "there", "these", "they",
        "this", "through", "under", "video", "visible", "what", "when",
        "where", "which", "while", "with", "would", "your", "visual",
    };
    private static readonly HashSet<string> GenericVisualTerms = new(
        StringComparer.Ordinal)
    {
        "appear", "area", "change", "figure", "move", "observation",
        "person", "player", "point", "protagonist", "ready", "show",
        "support", "transition", "view",
    };

    public ClipGameKnowledgeContext Retrieve(
        GameKnowledgeSnapshot snapshot,
        ClipEditorialContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);
        if (!snapshot.GameName.Equals(
                context.GameContext.GameName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Game-knowledge retrieval requires the confirmed game snapshot.",
                nameof(snapshot));
        }

        string[] gameTokens = Tokenize(snapshot.GameName);
        RetrievalAnchor[] anchors = BuildAnchors(context)
            .Select(anchor => anchor with
            {
                Tokens = Tokenize(anchor.Text)
                    .Except(gameTokens, StringComparer.Ordinal)
                    .ToArray(),
            })
            .Where(static anchor => anchor.Tokens.Length > 0)
            .ToArray();
        IReadOnlyDictionary<string, int> documentFrequency =
            BuildDocumentFrequency(snapshot.Passages, gameTokens);
        var scored = new List<ScoredPassage>();
        foreach (GameKnowledgePassage passage in snapshot.Passages)
        {
            string[] passageTokens = Tokenize(passage.Text)
                .Except(gameTokens, StringComparer.Ordinal)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var passageTokenSet = passageTokens.ToHashSet(StringComparer.Ordinal);
            var matchedTerms = new HashSet<string>(StringComparer.Ordinal);
            var strongMatchedTerms = new HashSet<string>(StringComparer.Ordinal);
            var linkedEvidence = new HashSet<string>(StringComparer.Ordinal);
            double raw = 0;
            bool visualGroundingCandidateMatch = false;
            bool generalContextAnchorMatch = false;
            foreach (RetrievalAnchor anchor in anchors)
            {
                string[] tokenMatches = anchor.Tokens
                    .Where(passageTokenSet.Contains)
                    .Where(term => IsDiscriminative(
                        term,
                        documentFrequency,
                        snapshot.Passages.Count))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (tokenMatches.Length == 0)
                {
                    continue;
                }
                foreach (string term in tokenMatches)
                {
                    matchedTerms.Add(term);
                    int frequency = documentFrequency.GetValueOrDefault(term, 1);
                    double inverseFrequency = 1 + Math.Log(
                        (snapshot.Passages.Count + 1d) / (frequency + 1d));
                    raw += anchor.Weight * inverseFrequency;
                }
                if (anchor.MayClipLink)
                {
                    linkedEvidence.Add(anchor.EvidenceId);
                    foreach (string term in tokenMatches)
                    {
                        strongMatchedTerms.Add(term);
                    }
                }
                visualGroundingCandidateMatch |=
                    anchor.MayProposeVisualGrounding;
                generalContextAnchorMatch |= anchor.MaySupplyGeneralContext;
            }
            if (matchedTerms.Count == 0)
            {
                continue;
            }
            GameKnowledgeMatchStrength strength =
                strongMatchedTerms.Count >= 2
                    ? GameKnowledgeMatchStrength.ClipLinked
                    : visualGroundingCandidateMatch &&
                        IsNarrativeSection(passage.Section)
                        ? GameKnowledgeMatchStrength
                            .CandidateForVisualGrounding
                    : GameKnowledgeMatchStrength.GeneralContext;
            if (strength == GameKnowledgeMatchStrength.GeneralContext &&
                (!generalContextAnchorMatch || matchedTerms.Count < 2))
            {
                continue;
            }
            double relevance = Math.Clamp(1 - Math.Exp(-raw / 8), 0, 1);
            scored.Add(new ScoredPassage(
                passage,
                strength,
                relevance,
                matchedTerms.ToArray(),
                strength == GameKnowledgeMatchStrength.ClipLinked
                    ? linkedEvidence.ToArray()
                    : []));
        }

        var selectedMatches = new List<GameKnowledgeMatch>();
        int characters = 0;
        int transcriptNominatedPassages = 0;
        IReadOnlyDictionary<string, int> sourceOrder = snapshot.Passages
            .Select(static (passage, index) => (passage.Id, index))
            .ToDictionary(
                static value => value.Id,
                static value => value.index,
                StringComparer.Ordinal);
        foreach (ScoredPassage item in scored
                     .OrderByDescending(static value => value.Strength)
                     .ThenByDescending(static value => value.Relevance)
                     .ThenBy(value => sourceOrder[value.Passage.Id]))
        {
            if (item.Strength ==
                    GameKnowledgeMatchStrength.CandidateForVisualGrounding &&
                transcriptNominatedPassages >=
                    MaximumTranscriptNominatedPassages)
            {
                continue;
            }
            if (selectedMatches.Count >= MaximumMatches ||
                characters + item.Passage.Text.Length > MaximumTotalCharacters)
            {
                continue;
            }
            selectedMatches.Add(new GameKnowledgeMatch(
                item.Passage,
                item.Strength,
                item.Relevance,
                item.MatchedTerms,
                item.ClipEvidenceIds,
                item.Strength == GameKnowledgeMatchStrength.GeneralContext
                    ? GameKnowledgeTemporalRelation.Unspecified
                    : GameKnowledgeTemporalRelation.CurrentEventCandidate));
            characters += item.Passage.Text.Length;
            if (item.Strength ==
                GameKnowledgeMatchStrength.CandidateForVisualGrounding)
            {
                transcriptNominatedPassages++;
            }
        }
        if (selectedMatches.Count == 0)
        {
            selectedMatches.AddRange(
                SelectVisualGroundingCandidates(snapshot));
        }
        else
        {
            if (selectedMatches.Any(static match =>
                    match.Strength == GameKnowledgeMatchStrength.ClipLinked))
            {
                selectedMatches = selectedMatches
                    .Where(static match => match.Strength ==
                        GameKnowledgeMatchStrength.ClipLinked)
                    .Take(MaximumClipLinkedMatches)
                    .ToList();
            }
            selectedMatches = AddPriorNarrativeContext(
                snapshot,
                selectedMatches);
        }
        selectedMatches = AddGeneralGameContext(snapshot, selectedMatches);
        return selectedMatches.Count == 0
            ? NoRelevant(snapshot)
            : new ClipGameKnowledgeContext(
                snapshot.GameName,
                snapshot,
                selectedMatches);
    }

    private static bool IsDiscriminative(
        string term,
        IReadOnlyDictionary<string, int> documentFrequency,
        int documentCount) =>
        term.Length >= 4 &&
        !GenericVisualTerms.Contains(term) &&
        documentFrequency.GetValueOrDefault(term, int.MaxValue) <=
            Math.Max(1, documentCount / 8);

    private static IEnumerable<GameKnowledgeMatch>
        SelectVisualGroundingCandidates(GameKnowledgeSnapshot snapshot)
    {
        int characters = 0;
        int count = 0;
        foreach (GameKnowledgePassage passage in snapshot.Passages
                     .Where(static passage => IsNarrativeSection(
                         passage.Section)))
        {
            if (count >= MaximumVisualGroundingCandidates ||
                characters + passage.Text.Length > MaximumTotalCharacters)
            {
                continue;
            }
            yield return new GameKnowledgeMatch(
                passage,
                GameKnowledgeMatchStrength.CandidateForVisualGrounding,
                relevance: 0,
                matchedTerms: []);
            count++;
            characters += passage.Text.Length;
        }
    }

    private static List<GameKnowledgeMatch> AddGeneralGameContext(
        GameKnowledgeSnapshot snapshot,
        IReadOnlyList<GameKnowledgeMatch> selectedMatches)
    {
        var result = selectedMatches.ToList();
        if (result.Count >= MaximumMatches)
        {
            result.RemoveAt(result.Count - 1);
        }
        int characters = result.Sum(static match => match.Passage.Text.Length);
        var retainedIds = result
            .Select(static match => match.Passage.Id)
            .ToHashSet(StringComparer.Ordinal);
        IReadOnlyDictionary<string, GameKnowledgeSourceRole> sourceRoles =
            snapshot.Sources.ToDictionary(
                static source => source.Id,
                static source => source.Role,
                StringComparer.Ordinal);

        foreach (GameKnowledgePassage passage in snapshot.Passages
                     .Where(static passage => IsGeneralContextSection(
                         passage.Section))
                     .OrderBy(passage => GeneralContextRank(
                         passage,
                         sourceRoles[passage.SourceId])))
        {
            if (result.Count >= MaximumMatches ||
                characters + passage.Text.Length > MaximumTotalCharacters)
            {
                continue;
            }
            if (!retainedIds.Add(passage.Id))
            {
                continue;
            }
            result.Add(new GameKnowledgeMatch(
                passage,
                GameKnowledgeMatchStrength.GeneralContext,
                relevance: 0,
                matchedTerms: [],
                temporalRelation: GameKnowledgeTemporalRelation.Unspecified));
            characters += passage.Text.Length;
        }
        return result;
    }

    private static int GeneralContextRank(
        GameKnowledgePassage passage,
        GameKnowledgeSourceRole sourceRole)
    {
        int roleRank = sourceRole switch
        {
            GameKnowledgeSourceRole.StructuredIdentity => 0,
            GameKnowledgeSourceRole.PrimaryArticle => 1,
            _ => 2,
        };
        string text = passage.Text;
        bool identityBearing =
            text.Contains("players control", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("player controls", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("takes control of", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("protagonist", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("set in", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("takes place", StringComparison.OrdinalIgnoreCase);
        int sectionRank = passage.Section.Contains(
                "identity",
                StringComparison.OrdinalIgnoreCase)
            ? 0
            : passage.Section.Contains("overview", StringComparison.OrdinalIgnoreCase)
                ? 1
                : passage.Section.Contains("gameplay", StringComparison.OrdinalIgnoreCase)
                    ? 2
                    : 3;
        return roleRank * 100 + (identityBearing ? 0 : 10) + sectionRank;
    }

    private static bool IsGeneralContextSection(string section) =>
        section.Contains("identity", StringComparison.OrdinalIgnoreCase) ||
        section.Contains("overview", StringComparison.OrdinalIgnoreCase) ||
        section.Contains("gameplay", StringComparison.OrdinalIgnoreCase) ||
        section.Contains("setting", StringComparison.OrdinalIgnoreCase);

    private static List<GameKnowledgeMatch> AddPriorNarrativeContext(
        GameKnowledgeSnapshot snapshot,
        IReadOnlyList<GameKnowledgeMatch> selectedMatches)
    {
        var result = new List<GameKnowledgeMatch>();
        int characters = 0;
        var retainedIds = selectedMatches
            .Select(static match => match.Passage.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (GameKnowledgeMatch match in selectedMatches)
        {
            if (result.Count >= MaximumMatches ||
                characters + match.Passage.Text.Length >
                    MaximumTotalCharacters)
            {
                continue;
            }

            result.Add(match);
            characters += match.Passage.Text.Length;
            if (match.Strength is not (
                    GameKnowledgeMatchStrength.CandidateForVisualGrounding or
                    GameKnowledgeMatchStrength.ClipLinked))
            {
                continue;
            }

            GameKnowledgePassage? prior = FindPriorNarrativePassage(
                snapshot,
                match.Passage);
            if (prior is null || retainedIds.Contains(prior.Id) ||
                result.Any(value => value.Passage.Id.Equals(
                    prior.Id,
                    StringComparison.Ordinal)) ||
                result.Count >= MaximumMatches ||
                characters + prior.Text.Length > MaximumTotalCharacters)
            {
                continue;
            }

            result.Add(new GameKnowledgeMatch(
                prior,
                GameKnowledgeMatchStrength.CandidateForVisualGrounding,
                relevance: 0,
                matchedTerms: [],
                clipEvidenceIds: null,
                temporalRelation:
                    GameKnowledgeTemporalRelation.ImmediatelyPriorContext));
            characters += prior.Text.Length;
        }

        return result;
    }

    private static GameKnowledgePassage? FindPriorNarrativePassage(
        GameKnowledgeSnapshot snapshot,
        GameKnowledgePassage passage)
    {
        int currentIndex = -1;
        for (int index = 0; index < snapshot.Passages.Count; index++)
        {
            if (snapshot.Passages[index].Id.Equals(
                    passage.Id,
                    StringComparison.Ordinal))
            {
                currentIndex = index;
                break;
            }
        }

        for (int index = currentIndex - 1; index >= 0; index--)
        {
            GameKnowledgePassage candidate = snapshot.Passages[index];
            if (candidate.SourceId.Equals(
                    passage.SourceId,
                    StringComparison.Ordinal) &&
                IsNarrativeSection(candidate.Section))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsNarrativeSection(string section) =>
        section.Contains("plot", StringComparison.OrdinalIgnoreCase) ||
        section.Contains("story", StringComparison.OrdinalIgnoreCase) ||
        section.Contains("premise", StringComparison.OrdinalIgnoreCase) ||
        section.Contains("synopsis", StringComparison.OrdinalIgnoreCase);

    private static ClipGameKnowledgeContext NoRelevant(
        GameKnowledgeSnapshot snapshot) =>
        new(
            snapshot.GameName,
            snapshot,
            warnings:
            [
                new GameKnowledgeWarning(
                    GameKnowledgeWarningCode.NoRelevantPassage,
                    "Open game knowledge was cached, but no passage had enough local evidence overlap to ground clip-specific story context."),
            ]);

    private static RetrievalAnchor[] BuildAnchors(
        ClipEditorialContext context)
    {
        var anchors = new List<RetrievalAnchor>();
        if (!string.IsNullOrWhiteSpace(context.GameContext.ContextNotes))
        {
            anchors.Add(new RetrievalAnchor(
                context.GameContext.ContextNotes,
                "game-context",
                3,
                MayClipLink: true,
                MayProposeVisualGrounding: true,
                MaySupplyGeneralContext: true,
                Tokens: []));
        }
        anchors.AddRange(context.Transcripts.Select(transcript =>
            new RetrievalAnchor(
                transcript.Text,
                $"stream-{transcript.AbsoluteAudioStreamIndex}",
                transcript.Authority is
                    ClipEditorialTranscriptAuthority.UserCorrected or
                    ClipEditorialTranscriptAuthority.HumanReviewed
                        ? 2.7
                        : 0.85,
                transcript.Authority is
                    ClipEditorialTranscriptAuthority.UserCorrected or
                    ClipEditorialTranscriptAuthority.HumanReviewed,
                MayProposeVisualGrounding: true,
                MaySupplyGeneralContext: transcript.Authority is
                    ClipEditorialTranscriptAuthority.UserCorrected or
                    ClipEditorialTranscriptAuthority.HumanReviewed,
                Tokens: [])));
        anchors.AddRange(context.Evidence
            .Where(static evidence => evidence.Kind is
                ClipEditorialEvidenceKind.VisualObservation or
                ClipEditorialEvidenceKind.DeterministicMoment)
            .Select(evidence => new RetrievalAnchor(
                evidence.Description,
                evidence.Id,
                evidence.Kind == ClipEditorialEvidenceKind.VisualObservation
                    ? 2.35
                    : 0.65,
                evidence.Kind == ClipEditorialEvidenceKind.VisualObservation,
                evidence.Kind == ClipEditorialEvidenceKind.VisualObservation,
                MaySupplyGeneralContext: true,
                Tokens: [])));
        if (context.VisualText is not null)
        {
            anchors.AddRange(context.VisualText.GroundingAnchors.Select(anchor =>
                new RetrievalAnchor(
                    anchor.DisplayText,
                    anchor.EvidenceId,
                    2.8,
                    MayClipLink: true,
                    MayProposeVisualGrounding: true,
                    MaySupplyGeneralContext: true,
                    Tokens: [])));
        }
        return anchors.ToArray();
    }

    private static IReadOnlyDictionary<string, int> BuildDocumentFrequency(
        IEnumerable<GameKnowledgePassage> passages,
        IReadOnlyCollection<string> gameTokens)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (GameKnowledgePassage passage in passages)
        {
            foreach (string token in Tokenize(passage.Text)
                         .Except(gameTokens, StringComparer.Ordinal)
                         .Distinct(StringComparer.Ordinal))
            {
                counts[token] = counts.GetValueOrDefault(token) + 1;
            }
        }
        return counts;
    }

    internal static string[] Tokenize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return TokenPattern().Matches(
                value.Normalize(NormalizationForm.FormKC))
            .Select(static match => NormalizeToken(
                match.Value.ToLowerInvariant()))
            .Where(static token => token.Length >= 3 &&
                !StopWords.Contains(token) &&
                !token.All(char.IsDigit))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string NormalizeToken(string token)
    {
        if (token.EndsWith("'s", StringComparison.Ordinal) ||
            token.EndsWith("’s", StringComparison.Ordinal))
        {
            token = token[..^2];
        }
        if (token.Length >= 6 && token.EndsWith("ed", StringComparison.Ordinal))
        {
            return token[..^2];
        }
        if (token.Length >= 7 && token.EndsWith("ing", StringComparison.Ordinal))
        {
            return token[..^3];
        }
        if (token.Length >= 6 && token.EndsWith("ies", StringComparison.Ordinal))
        {
            return token[..^3] + "y";
        }
        if (token.Length >= 5 && token.EndsWith('s'))
        {
            return token[..^1];
        }
        return token;
    }

    [GeneratedRegex(
        @"[\p{L}\p{Nd}][\p{L}\p{Nd}'’_-]*",
        RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    private sealed record RetrievalAnchor(
        string Text,
        string EvidenceId,
        double Weight,
        bool MayClipLink,
        bool MayProposeVisualGrounding,
        bool MaySupplyGeneralContext,
        string[] Tokens);

    private sealed record ScoredPassage(
        GameKnowledgePassage Passage,
        GameKnowledgeMatchStrength Strength,
        double Relevance,
        IReadOnlyList<string> MatchedTerms,
        IReadOnlyList<string> ClipEvidenceIds);
}
