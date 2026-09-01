using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;
using ReplayFoundry.Desktop.Security;

namespace ReplayFoundry.Desktop.Platform.GameKnowledge;

public interface IGameKnowledgeSnapshotProvider
{
    GameKnowledgeProviderIdentity Identity { get; }

    Task<GameKnowledgeSnapshot> AcquireAsync(
        GameKnowledgeRefreshRequest request,
        CancellationToken cancellationToken);
}

public interface IGameIdentityCandidateProvider
{
    Task<GameIdentityCandidateSet> DiscoverAsync(
        GameIdentityDiscoveryRequest request,
        CancellationToken cancellationToken);
}

internal sealed class WikimediaRetryAfterException : HttpRequestException
{
    public WikimediaRetryAfterException(DateTimeOffset retryAfterUtc)
        : base("Wikimedia asked Replay Foundry to retry later.")
    {
        RetryAfterUtc = retryAfterUtc;
    }

    public DateTimeOffset RetryAfterUtc { get; }
}

internal sealed class WikimediaApiException : HttpRequestException
{
    public WikimediaApiException(string errorCode)
        : base("Wikimedia returned an application-level API error.")
    {
        ErrorCode = ExternalTextSecurity.SingleLine(errorCode, 80);
    }

    public string ErrorCode { get; }
}

public sealed partial class WikimediaGameKnowledgeProvider :
    IGameKnowledgeSnapshotProvider,
    IGameIdentityCandidateProvider
{
    public const string RetrievalPolicyVersion = "wikimedia-bounded-context-1.0";
    private const int MaximumPrimaryWikipediaPassages = 28;
    private const int MaximumRelatedWikipediaPassages = 10;
    private static readonly TimeSpan ComponentRetryDelay = TimeSpan.FromHours(24);
    private static readonly HashSet<string> ExcludedSections = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "References",
        "External links",
        "Further reading",
        "Notes",
        "Sources",
        "Bibliography",
    };
    private static readonly HashSet<string> AllowedSections = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "Overview",
        "Gameplay",
        "Plot",
        "Story",
        "Premise",
        "Synopsis",
        "Setting",
        "Characters",
    };
    public WikimediaGameKnowledgeProvider()
        : this(SharedClient, WikipediaApi, WikidataApi)
    {
    }

    internal WikimediaGameKnowledgeProvider(
        HttpClient httpClient,
        string wikipediaApi,
        string wikidataApi)
    {
        _httpClient = httpClient ??
            throw new ArgumentNullException(nameof(httpClient));
        _wikipediaApi = RequireHttpsEndpoint(
            wikipediaApi,
            nameof(wikipediaApi));
        _wikidataApi = RequireHttpsEndpoint(
            wikidataApi,
            nameof(wikidataApi));
    }

    public GameKnowledgeProviderIdentity Identity { get; } =
        new("Wikimedia open game knowledge", "2.0.0");

    public async Task<GameKnowledgeSnapshot> AcquireAsync(
        GameKnowledgeRefreshRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ConfirmedGameIdentity identity = request.Identity;
        RequirePermission(
            identity,
            GameKnowledgeSourcePermission.WikidataClaims);
        RequirePermission(
            identity,
            GameKnowledgeSourcePermission.WikipediaExcerpts);
        string gameName = GameKnowledgeSource.Required(
            ExternalTextSecurity.SingleLine(identity.CanonicalTitle, int.MaxValue),
            160,
            nameof(request));
        if (!gameName.Equals(identity.CanonicalTitle, StringComparison.Ordinal))
        {
            identity = new ConfirmedGameIdentity(
                identity.WikidataEntityId,
                gameName,
                identity.Edition,
                identity.ReleaseYear,
                identity.Developer,
                identity.Series,
                identity.Authority,
                identity.Source,
                identity.Locale,
                identity.UserConfirmed,
                identity.ConfirmedAtUtc,
                identity.SourcePermissions);
        }
        cancellationToken.ThrowIfCancellationRequested();

        DateTimeOffset retrievedAtUtc = DateTimeOffset.UtcNow;
        var sources = request.ExistingSnapshot?.Sources.ToList() ?? [];
        var passages = request.ExistingSnapshot?.Passages.ToList() ?? [];
        var components = request.ExistingSnapshot?.Components.ToList() ?? [];
        Exception? firstFailure = null;
        WikidataDocument? wikidata = null;
        try
        {
            wikidata = await FetchWikidataAsync(
                identity.WikidataEntityId,
                identity.Locale,
                WikimediaRequestMode.Background,
                cancellationToken);
            RemoveSourceGroup(
                static source => source.Kind == GameKnowledgeSourceKind.Wikidata,
                sources,
                passages);
            RemoveComponent(
                GameKnowledgeComponentKind.WikidataIdentity,
                components);
            RemoveComponent(
                GameKnowledgeComponentKind.WikidataClaims,
                components);
            if (wikidata is null)
            {
                components.Add(RetryComponent(
                    GameKnowledgeComponentKind.WikidataIdentity,
                    GameKnowledgeComponentCompleteness.Missing,
                    retrievedAtUtc));
                components.Add(RetryComponent(
                    GameKnowledgeComponentKind.WikidataClaims,
                    GameKnowledgeComponentCompleteness.Missing,
                    retrievedAtUtc));
            }
            else
            {
                AddWikidata(
                    wikidata,
                    sources,
                    passages,
                    components,
                    retrievedAtUtc);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or JsonException or
                InvalidDataException)
        {
            firstFailure = exception;
            MarkTransient(
                GameKnowledgeComponentKind.WikidataIdentity,
                exception,
                retrievedAtUtc,
                components);
            MarkTransient(
                GameKnowledgeComponentKind.WikidataClaims,
                exception,
                retrievedAtUtc,
                components);
        }

        GameKnowledgeSource? existingPrimary = sources.FirstOrDefault(
            static source =>
                source.Role == GameKnowledgeSourceRole.PrimaryArticle);
        string? wikipediaTitle = wikidata?.EnglishWikipediaTitle ??
            existingPrimary?.Title;
        Uri? primaryUri = existingPrimary?.PageUri;
        if (!string.IsNullOrWhiteSpace(wikipediaTitle))
        {
            try
            {
                WikipediaDocument wikipedia =
                    await FetchWikipediaTitleRequiredAsync(
                        wikipediaTitle,
                        cancellationToken);
                RemoveSourceGroup(
                    static source =>
                        source.Role == GameKnowledgeSourceRole.PrimaryArticle,
                    sources,
                    passages);
                RemoveComponent(
                    GameKnowledgeComponentKind.WikipediaPrimary,
                    components);
                AddWikipedia(
                    wikipedia,
                    GameKnowledgeSourceRole.PrimaryArticle,
                    MaximumPrimaryWikipediaPassages,
                    sources,
                    passages,
                    components,
                    GameKnowledgeComponentKind.WikipediaPrimary,
                    retrievedAtUtc);
                wikipediaTitle = wikipedia.Title;
                primaryUri = wikipedia.PageUri;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
                when (exception is HttpRequestException or JsonException or
                    InvalidDataException)
            {
                firstFailure ??= exception;
                MarkTransient(
                    GameKnowledgeComponentKind.WikipediaPrimary,
                    exception,
                    retrievedAtUtc,
                    components);
            }
        }
        else
        {
            ReplaceComponent(
                RetryComponent(
                    GameKnowledgeComponentKind.WikipediaPrimary,
                    GameKnowledgeComponentCompleteness.Missing,
                    retrievedAtUtc),
                components);
        }

        if (!string.IsNullOrWhiteSpace(wikipediaTitle) && primaryUri is not null)
        {
            try
            {
                WikipediaDocument[] related =
                    await FetchRelatedWikipediaAsync(
                        gameName,
                        wikipediaTitle,
                        primaryUri,
                        cancellationToken);
                RemoveSourceGroup(
                    static source =>
                        source.Role == GameKnowledgeSourceRole.RelatedArticle,
                    sources,
                    passages);
                RemoveComponent(
                    GameKnowledgeComponentKind.WikipediaRelated,
                    components);
                foreach (WikipediaDocument document in related)
                {
                    AddWikipedia(
                        document,
                        GameKnowledgeSourceRole.RelatedArticle,
                        MaximumRelatedWikipediaPassages,
                        sources,
                        passages,
                        components: null,
                        GameKnowledgeComponentKind.WikipediaRelated,
                        retrievedAtUtc);
                }
                components.Add(related.Length == 0
                    ? RetryComponent(
                        GameKnowledgeComponentKind.WikipediaRelated,
                        GameKnowledgeComponentCompleteness.Missing,
                        retrievedAtUtc)
                    : ComponentFromSources(
                        GameKnowledgeComponentKind.WikipediaRelated,
                        sources.Where(static source =>
                            source.Role ==
                                GameKnowledgeSourceRole.RelatedArticle),
                        retrievedAtUtc));
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
                when (exception is HttpRequestException or JsonException or
                    InvalidDataException)
            {
                firstFailure ??= exception;
                MarkTransient(
                    GameKnowledgeComponentKind.WikipediaRelated,
                    exception,
                    retrievedAtUtc,
                    components);
            }
        }
        else
        {
            ReplaceComponent(
                RetryComponent(
                    GameKnowledgeComponentKind.WikipediaRelated,
                    GameKnowledgeComponentCompleteness.TransientFailure,
                    retrievedAtUtc),
                components);
        }

        ReplaceComponent(
            StrategyWikiGameKnowledgeCapability.ComponentState(retrievedAtUtc),
            components);
        if (sources.Count == 0 || passages.Count == 0)
        {
            throw firstFailure ?? new InvalidDataException(
                "No bounded game-knowledge component could be refreshed or retained.");
        }
        return new GameKnowledgeSnapshot(
            identity,
            Identity,
            retrievedAtUtc,
            sources.Take(GameKnowledgeSnapshot.MaximumSources),
            passages.Take(GameKnowledgeSnapshot.MaximumPassages),
            components);
    }

    public async Task<GameIdentityCandidateSet> DiscoverAsync(
        GameIdentityDiscoveryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string confirmedTitle = GameKnowledgeSource.Required(
            ExternalTextSecurity.SingleLine(
                request.UserConfirmedTitle,
                int.MaxValue),
            120,
            nameof(request));
        var query = new Dictionary<string, string>
        {
            ["action"] = "wbsearchentities",
            ["format"] = "json",
            ["formatversion"] = "2",
            ["search"] = confirmedTitle,
            ["language"] = request.Locale,
            ["uselang"] = request.Locale,
            ["type"] = "item",
            ["limit"] = GameIdentityCandidateSet.MaximumCandidates.ToString(
                CultureInfo.InvariantCulture),
        };
        using JsonDocument json = await GetJsonAsync(
            BuildUri(
                _wikidataApi,
                query,
                WikimediaRequestMode.Interactive),
            cancellationToken);
        JsonElement search = Property(json.RootElement, "search");
        if (search.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "Wikidata candidate discovery returned an unexpected result.");
        }

        var candidates = new List<GameIdentityCandidate>();
        foreach (string qid in search.EnumerateArray()
                     .Where(static value => value.ValueKind == JsonValueKind.Object)
                     .Select(static value => Text(value, "id", allowBlank: false))
                     .Where(static value => EntityIdPattern().IsMatch(value))
                     .Distinct(StringComparer.Ordinal)
                     .Take(GameIdentityCandidateSet.MaximumCandidates))
        {
            WikidataDocument? document = await FetchWikidataAsync(
                qid,
                request.Locale,
                WikimediaRequestMode.Interactive,
                cancellationToken);
            if (document is null || !document.IsVideoGame)
            {
                continue;
            }
            candidates.Add(new GameIdentityCandidate(
                document.EntityId,
                document.Label,
                CandidateEdition(document.Description),
                document.ReleaseYear,
                document.NamedClaims.GetValueOrDefault("Developer")?.FirstOrDefault(),
                document.NamedClaims.GetValueOrDefault("Series")?.FirstOrDefault(),
                GameIdentityAuthority.Wikidata,
                "Wikidata",
                request.Locale));
        }
        return new GameIdentityCandidateSet(request, candidates);
    }

    private static string? CandidateEdition(string? description) =>
        description is not null &&
        (description.Contains("edition", StringComparison.OrdinalIgnoreCase) ||
         description.Contains("remaster", StringComparison.OrdinalIgnoreCase) ||
         description.Contains("remake", StringComparison.OrdinalIgnoreCase))
            ? description
            : null;

    private static void AddWikipedia(
        WikipediaDocument document,
        GameKnowledgeSourceRole role,
        int maximumPassages,
        ICollection<GameKnowledgeSource> sources,
        ICollection<GameKnowledgePassage> passages,
        ICollection<GameKnowledgeComponentState>? components,
        GameKnowledgeComponentKind componentKind,
        DateTimeOffset checkedAtUtc)
    {
        (string Section, string Text)[] retained = SplitWikipediaExtract(
                document.Extract)
            .Where(value => AllowedSections.Contains(value.Section))
            .SelectMany(value => Chunk(
                WhitespacePattern().Replace(value.Text, " ").Trim())
                .Select(text => (value.Section, text)))
            .Where(static value => value.text.Length >= 20)
            .Take(maximumPassages)
            .Select(static value => (value.Section, value.text))
            .ToArray();
        string retainedContent = string.Join(
            "\n",
            retained.Select(static value => $"{value.Section}: {value.Text}"));
        string sourceId = StableSourceId(
            GameKnowledgeSourceKind.Wikipedia,
            document.PageUri,
            document.RevisionId);
        sources.Add(new GameKnowledgeSource(
            sourceId,
            GameKnowledgeSourceKind.Wikipedia,
            document.Title,
            document.PageUri,
            document.RevisionId,
            document.RevisionTimestampUtc,
            "CC-BY-SA-4.0",
            new Uri(
                "https://creativecommons.org/licenses/by-sa/4.0/",
                UriKind.Absolute),
            $"Wikipedia contributors, {document.Title}, revision {document.RevisionId}.",
            GameKnowledgePassage.ComputeSha256(retainedContent),
            role));
        foreach ((string Section, string Text) in retained)
        {
            AddPassage(sourceId, Section, Text, passages);
        }
        components?.Add(ComponentFromSources(
            componentKind,
            [sources.Last()],
            checkedAtUtc));
    }

    private static void AddWikidata(
        WikidataDocument document,
        ICollection<GameKnowledgeSource> sources,
        ICollection<GameKnowledgePassage> passages,
        ICollection<GameKnowledgeComponentState> components,
        DateTimeOffset checkedAtUtc)
    {
        string claimsContent = string.Join(
            "\n",
            document.NamedClaims.Select(value =>
                $"{value.Key}: {string.Join(", ", value.Value)}."));
        string retainedContent = document.IdentityContent + "\n" + claimsContent;
        string sourceId = StableSourceId(
            GameKnowledgeSourceKind.Wikidata,
            document.PageUri,
            document.RevisionId);
        sources.Add(new GameKnowledgeSource(
            sourceId,
            GameKnowledgeSourceKind.Wikidata,
            document.Label,
            document.PageUri,
            document.RevisionId,
            document.RevisionTimestampUtc,
            "CC0-1.0",
            new Uri(
                "https://creativecommons.org/publicdomain/zero/1.0/",
                UriKind.Absolute),
            $"Wikidata contributors, {document.EntityId}, revision {document.RevisionId}.",
            GameKnowledgePassage.ComputeSha256(retainedContent),
            GameKnowledgeSourceRole.StructuredIdentity));
        AddPassage(sourceId, "Identity", document.IdentityContent, passages);
        foreach ((string section, string[] values) in document.NamedClaims)
        {
            AddPassage(
                sourceId,
                section,
                $"{section}: {string.Join(", ", values)}.",
                passages);
        }
        GameKnowledgeSource source = sources.Last();
        components.Add(ComponentFromSource(
            GameKnowledgeComponentKind.WikidataIdentity,
            source,
            GameKnowledgePassage.ComputeSha256(document.IdentityContent),
            checkedAtUtc));
        components.Add(ComponentFromSource(
            GameKnowledgeComponentKind.WikidataClaims,
            source,
            GameKnowledgePassage.ComputeSha256(claimsContent),
            checkedAtUtc));
    }

    private static IReadOnlyDictionary<string, string[]> ReadEntityClaims(
        JsonElement entity)
    {
        if (!entity.TryGetProperty("claims", out JsonElement claims) ||
            claims.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string[]>(StringComparer.Ordinal);
        }
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (string propertyId in AllowlistedClaimLabels.Keys.Append("P31"))
        {
            if (!claims.TryGetProperty(propertyId, out JsonElement statements) ||
                statements.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            string[] ids = statements.EnumerateArray()
                .Select(TryReadEntityId)
                .Where(static value => value is not null)
                .Select(static value => value!)
                .Distinct(StringComparer.Ordinal)
                .Take(8)
                .ToArray();
            if (ids.Length > 0)
            {
                result[propertyId] = ids;
            }
        }
        return result;
    }

    private static string? TryReadEntityId(JsonElement statement)
    {
        if (statement.ValueKind != JsonValueKind.Object ||
            !statement.TryGetProperty("mainsnak", out JsonElement mainsnak) ||
            mainsnak.ValueKind != JsonValueKind.Object ||
            !mainsnak.TryGetProperty("datavalue", out JsonElement dataValue) ||
            dataValue.ValueKind != JsonValueKind.Object ||
            !dataValue.TryGetProperty("value", out JsonElement value) ||
            value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("id", out JsonElement id) ||
            id.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        string? result = id.GetString();
        return result is not null && EntityIdPattern().IsMatch(result)
            ? result
            : null;
    }

    private static int? ReadReleaseYear(JsonElement entity)
    {
        if (!entity.TryGetProperty("claims", out JsonElement claims) ||
            claims.ValueKind != JsonValueKind.Object ||
            !claims.TryGetProperty("P577", out JsonElement statements) ||
            statements.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        foreach (JsonElement statement in statements.EnumerateArray())
        {
            if (statement.TryGetProperty("mainsnak", out JsonElement mainsnak) &&
                mainsnak.TryGetProperty("datavalue", out JsonElement dataValue) &&
                dataValue.TryGetProperty("value", out JsonElement value) &&
                value.ValueKind == JsonValueKind.Object &&
                value.TryGetProperty("time", out JsonElement time) &&
                time.ValueKind == JsonValueKind.String)
            {
                Match match = WikidataTimeYearPattern().Match(time.GetString()!);
                if (match.Success && int.TryParse(
                        match.Groups[1].Value,
                        CultureInfo.InvariantCulture,
                        out int year) && year is >= 1950 and <= 2200)
                {
                    return year;
                }
            }
        }
        return null;
    }

    private static string? EnglishWikipediaTitle(JsonElement entity)
    {
        if (!entity.TryGetProperty("sitelinks", out JsonElement sitelinks) ||
            sitelinks.ValueKind != JsonValueKind.Object ||
            !sitelinks.TryGetProperty("enwiki", out JsonElement enwiki) ||
            enwiki.ValueKind != JsonValueKind.Object ||
            !enwiki.TryGetProperty("title", out JsonElement title) ||
            title.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        return title.GetString();
    }

    private static GameKnowledgeComponentState ComponentFromSource(
        GameKnowledgeComponentKind kind,
        GameKnowledgeSource source,
        string contentSha256,
        DateTimeOffset checkedAtUtc) =>
        new(
            kind,
            GameKnowledgeComponentCompleteness.Complete,
            checkedAtUtc,
            revisionId: source.RevisionId,
            licenseIdentifier: source.LicenseIdentifier,
            attribution: source.Attribution,
            contentSha256: contentSha256);

    private static GameKnowledgeComponentState ComponentFromSources(
        GameKnowledgeComponentKind kind,
        IEnumerable<GameKnowledgeSource> sources,
        DateTimeOffset checkedAtUtc)
    {
        GameKnowledgeSource[] snapshot = sources.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentException(
                "A complete component requires at least one retained source.",
                nameof(sources));
        }
        return new GameKnowledgeComponentState(
            kind,
            GameKnowledgeComponentCompleteness.Complete,
            checkedAtUtc,
            revisionId: string.Join(',', snapshot.Select(static value =>
                value.RevisionId)),
            licenseIdentifier: string.Join(',', snapshot.Select(static value =>
                    value.LicenseIdentifier)
                .Distinct(StringComparer.Ordinal)),
            attribution: string.Join(' ', snapshot.Select(static value =>
                value.Attribution)),
            contentSha256: GameKnowledgePassage.ComputeSha256(string.Join(
                "|",
                snapshot.Select(static value => value.ContentSha256))));
    }

    private static GameKnowledgeComponentState RetryComponent(
        GameKnowledgeComponentKind kind,
        GameKnowledgeComponentCompleteness completeness,
        DateTimeOffset checkedAtUtc) =>
        new(
            kind,
            completeness,
            checkedAtUtc,
            checkedAtUtc.Add(ComponentRetryDelay));

    private static void MarkTransient(
        GameKnowledgeComponentKind kind,
        Exception failure,
        DateTimeOffset checkedAtUtc,
        ICollection<GameKnowledgeComponentState> components)
    {
        GameKnowledgeComponentState? retained = components.SingleOrDefault(
            value => value.Kind == kind);
        DateTimeOffset retryAfterUtc = checkedAtUtc.Add(ComponentRetryDelay);
        if (failure is WikimediaRetryAfterException directed &&
            directed.RetryAfterUtc > retryAfterUtc)
        {
            retryAfterUtc = directed.RetryAfterUtc;
        }
        ReplaceComponent(
            new GameKnowledgeComponentState(
                kind,
                GameKnowledgeComponentCompleteness.TransientFailure,
                checkedAtUtc,
                retryAfterUtc,
                retained?.RevisionId,
                retained?.LicenseIdentifier,
                retained?.Attribution,
                retained?.ContentSha256),
            components);
    }

    private static void ReplaceComponent(
        GameKnowledgeComponentState replacement,
        ICollection<GameKnowledgeComponentState> components)
    {
        RemoveComponent(replacement.Kind, components);
        components.Add(replacement);
    }

    private static void RemoveComponent(
        GameKnowledgeComponentKind kind,
        ICollection<GameKnowledgeComponentState> components)
    {
        foreach (GameKnowledgeComponentState component in components
                     .Where(value => value.Kind == kind)
                     .ToArray())
        {
            components.Remove(component);
        }
    }

    private static void RemoveSourceGroup(
        Func<GameKnowledgeSource, bool> predicate,
        ICollection<GameKnowledgeSource> sources,
        ICollection<GameKnowledgePassage> passages)
    {
        string[] removedIds = sources.Where(predicate)
            .Select(static source => source.Id)
            .ToArray();
        foreach (GameKnowledgeSource source in sources
                     .Where(predicate)
                     .ToArray())
        {
            sources.Remove(source);
        }
        foreach (GameKnowledgePassage passage in passages
                     .Where(value => removedIds.Contains(
                         value.SourceId,
                         StringComparer.Ordinal))
                     .ToArray())
        {
            passages.Remove(passage);
        }
    }

    private static void RequirePermission(
        ConfirmedGameIdentity identity,
        GameKnowledgeSourcePermission permission)
    {
        if (!identity.UserConfirmed || !identity.Allows(permission))
        {
            throw new InvalidOperationException(
                $"The confirmed identity does not permit {permission}.");
        }
    }

    private static void AddPassage(
        string sourceId,
        string section,
        string text,
        ICollection<GameKnowledgePassage> passages)
    {
        string normalized = WhitespacePattern().Replace(text, " ").Trim();
        if (normalized.Length < 20)
        {
            return;
        }
        foreach (string chunk in Chunk(normalized))
        {
            string contentHash = GameKnowledgePassage.ComputeSha256(chunk);
            string id = "gkp-" + contentHash[..20];
            if (passages.Any(value => value.Id.Equals(
                    id,
                    StringComparison.Ordinal)))
            {
                continue;
            }
            passages.Add(new GameKnowledgePassage(
                id,
                sourceId,
                string.IsNullOrWhiteSpace(section) ? "Overview" : section,
                chunk,
                contentHash));
        }
    }

    internal static IEnumerable<(string Section, string Text)>
        SplitWikipediaExtract(string extract)
    {
        string section = "Overview";
        var paragraph = new StringBuilder();
        foreach (string rawLine in extract.Replace("\r\n", "\n")
                     .Replace('\r', '\n').Split('\n'))
        {
            string line = rawLine.Trim();
            Match heading = HeadingPattern().Match(line);
            if (heading.Success)
            {
                if (paragraph.Length > 0 && !ExcludedSections.Contains(section))
                {
                    yield return (section, paragraph.ToString().Trim());
                }
                paragraph.Clear();
                section = heading.Groups[1].Value.Trim();
                continue;
            }
            if (LooksLikeSectionHeading(line))
            {
                if (paragraph.Length > 0 && !ExcludedSections.Contains(section))
                {
                    yield return (section, paragraph.ToString().Trim());
                }
                paragraph.Clear();
                section = line;
                continue;
            }
            if (line.Length == 0)
            {
                if (paragraph.Length > 0 && !ExcludedSections.Contains(section))
                {
                    yield return (section, paragraph.ToString().Trim());
                }
                paragraph.Clear();
                continue;
            }
            if (paragraph.Length > 0)
            {
                paragraph.Append(' ');
            }
            paragraph.Append(line);
        }
        if (paragraph.Length > 0 && !ExcludedSections.Contains(section))
        {
            yield return (section, paragraph.ToString().Trim());
        }
    }

    private static bool LooksLikeSectionHeading(string line)
    {
        if (line.Length is < 2 or > 100 ||
            line.EndsWith('.') || line.EndsWith('!') || line.EndsWith('?') ||
            line.Contains(';') || line.Contains(','))
        {
            return false;
        }
        string[] words = line.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
        return words.Length is >= 1 and <= 12 &&
            words.Any(static word => word.Any(char.IsLetter));
    }

    private static IEnumerable<string> Chunk(string value)
    {
        if (value.Length <= GameKnowledgePassage.MaximumTextLength)
        {
            yield return value;
            yield break;
        }
        string[] sentences = SentenceBoundaryPattern().Split(value);
        var chunk = new StringBuilder();
        foreach (string sentence in sentences)
        {
            if (sentence.Length > GameKnowledgePassage.MaximumTextLength)
            {
                if (chunk.Length > 0)
                {
                    yield return chunk.ToString();
                    chunk.Clear();
                }
                for (int offset = 0; offset < sentence.Length;
                     offset += GameKnowledgePassage.MaximumTextLength)
                {
                    yield return sentence.Substring(
                        offset,
                        Math.Min(
                            GameKnowledgePassage.MaximumTextLength,
                            sentence.Length - offset));
                }
                continue;
            }
            if (chunk.Length > 0 &&
                chunk.Length + 1 + sentence.Length >
                    GameKnowledgePassage.MaximumTextLength)
            {
                yield return chunk.ToString();
                chunk.Clear();
            }
            if (chunk.Length > 0)
            {
                chunk.Append(' ');
            }
            chunk.Append(sentence.Trim());
        }
        if (chunk.Length > 0)
        {
            yield return chunk.ToString();
        }
    }
}
