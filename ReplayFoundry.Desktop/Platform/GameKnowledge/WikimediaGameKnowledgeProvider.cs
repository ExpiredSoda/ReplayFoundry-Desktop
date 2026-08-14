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
        string confirmedGameName,
        CancellationToken cancellationToken);
}

public sealed partial class WikimediaGameKnowledgeProvider :
    IGameKnowledgeSnapshotProvider
{
    private const string WikipediaApi =
        "https://en.wikipedia.org/w/api.php";
    private const string WikidataApi =
        "https://www.wikidata.org/w/api.php";
    private const int MaximumPrimaryWikipediaPassages = 72;
    private const int MaximumRelatedWikipediaPassages = 20;
    private const int MaximumRelatedWikipediaSources = 2;
    private static readonly string[] RelatedArticleTopics =
        ["characters", "plot", "setting"];
    private static readonly HttpClient SharedClient = CreateClient();
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
    private readonly HttpClient _httpClient;
    private readonly string _wikipediaApi;
    private readonly string _wikidataApi;

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
        new("Wikimedia open game knowledge", "1.3.0");

    public async Task<GameKnowledgeSnapshot> AcquireAsync(
        string confirmedGameName,
        CancellationToken cancellationToken)
    {
        string gameName = GameKnowledgeSource.Required(
            ExternalTextSecurity.SingleLine(
                confirmedGameName,
                int.MaxValue),
            120,
            nameof(confirmedGameName));
        cancellationToken.ThrowIfCancellationRequested();

        WikipediaDocument wikipedia = await FetchWikipediaAsync(
            gameName,
            cancellationToken);
        var sources = new List<GameKnowledgeSource>();
        var passages = new List<GameKnowledgePassage>();
        AddWikipedia(
            wikipedia,
            GameKnowledgeSourceRole.PrimaryArticle,
            MaximumPrimaryWikipediaPassages,
            sources,
            passages);

        WikipediaDocument[] related = [];
        try
        {
            related = await FetchRelatedWikipediaAsync(
                gameName,
                wikipedia,
                cancellationToken);
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
            // The exact primary revision remains useful when optional related
            // article discovery is unavailable.
        }
        foreach (WikipediaDocument document in related)
        {
            AddWikipedia(
                document,
                GameKnowledgeSourceRole.RelatedArticle,
                MaximumRelatedWikipediaPassages,
                sources,
                passages);
        }

        if (!string.IsNullOrWhiteSpace(wikipedia.WikidataEntityId))
        {
            try
            {
                WikidataDocument? wikidata = await FetchWikidataAsync(
                    wikipedia.WikidataEntityId,
                    cancellationToken);
                if (wikidata is not null)
                {
                    AddWikidata(wikidata, sources, passages);
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
                // Wikipedia remains a complete licensed source. Wikidata is
                // supplementary structured naming context and may degrade.
            }
        }

        return new GameKnowledgeSnapshot(
            gameName,
            Identity,
            DateTimeOffset.UtcNow,
            sources,
            passages);
    }

    private async Task<WikipediaDocument> FetchWikipediaAsync(
        string gameName,
        CancellationToken cancellationToken)
    {
        var candidates = new List<WikipediaDocument>();
        WikipediaDocument? exact = await TryFetchWikipediaTitleAsync(
            gameName,
            cancellationToken);
        if (exact is not null)
        {
            candidates.Add(exact);
        }
        string[] discoveredTitles = await SearchWikipediaTitlesAsync(
            gameName,
            cancellationToken);
        foreach (string title in discoveredTitles)
        {
            if (candidates.Any(value => value.Title.Equals(
                    title,
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            WikipediaDocument? candidate = await TryFetchWikipediaTitleAsync(
                title,
                cancellationToken);
            if (candidate is not null && candidates.All(value =>
                    !value.PageUri.Equals(candidate.PageUri)))
            {
                candidates.Add(candidate);
            }
        }
        if (candidates.Count == 0)
        {
            throw new InvalidDataException(
                $"Wikimedia did not find an encyclopedia page for the confirmed game '{gameName}'.");
        }
        string[] requestedTokens = LexicalTokens(gameName);
        return candidates
            .OrderByDescending(value => ArticleScore(
                value,
                gameName,
                requestedTokens))
            .ThenBy(static value => value.Title, StringComparer.Ordinal)
            .First();
    }

    private async Task<WikipediaDocument?> TryFetchWikipediaTitleAsync(
        string title,
        CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string>
        {
            ["action"] = "query",
            ["format"] = "json",
            ["formatversion"] = "2",
            ["prop"] = "extracts|info|revisions|pageprops",
            ["titles"] = title,
            ["redirects"] = "1",
            ["explaintext"] = "1",
            ["exsectionformat"] = "plain",
            ["inprop"] = "url",
            ["rvprop"] = "ids|timestamp",
        };
        using JsonDocument json = await GetJsonAsync(
            BuildUri(_wikipediaApi, query),
            cancellationToken);
        JsonElement pages = Property(
            Property(json.RootElement, "query"),
            "pages");
        if (pages.ValueKind != JsonValueKind.Array ||
            pages.GetArrayLength() != 1)
        {
            throw new InvalidDataException(
                "Wikipedia returned an unexpected page result.");
        }
        JsonElement page = pages[0];
        if (page.TryGetProperty("missing", out _))
        {
            return null;
        }
        string extract = Text(page, "extract", allowBlank: false);
        JsonElement revisions = Property(page, "revisions");
        if (revisions.ValueKind != JsonValueKind.Array ||
            revisions.GetArrayLength() != 1)
        {
            throw new InvalidDataException(
                "Wikipedia did not return one exact revision.");
        }
        JsonElement revision = revisions[0];
        long revisionId = Integer(revision, "revid");
        DateTimeOffset revisionTimestamp = UtcTimestamp(
            Text(revision, "timestamp", allowBlank: false));
        string fullUrl = Text(page, "fullurl", allowBlank: false);
        string? entityId = null;
        if (page.TryGetProperty("pageprops", out JsonElement pageProps) &&
            pageProps.ValueKind == JsonValueKind.Object &&
            pageProps.TryGetProperty(
                "wikibase_item",
                out JsonElement entityElement) &&
            entityElement.ValueKind == JsonValueKind.String)
        {
            entityId = entityElement.GetString();
        }
        return new WikipediaDocument(
            Text(page, "title", allowBlank: false),
            new Uri(AppendOldId(fullUrl, revisionId), UriKind.Absolute),
            revisionId.ToString(CultureInfo.InvariantCulture),
            revisionTimestamp,
            extract,
            entityId);
    }

    private async Task<string[]> SearchWikipediaTitlesAsync(
        string gameName,
        CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string>
        {
            ["action"] = "query",
            ["format"] = "json",
            ["formatversion"] = "2",
            ["list"] = "search",
            ["srsearch"] = $"intitle:\"{gameName}\" video game",
            ["srnamespace"] = "0",
            ["srlimit"] = "5",
        };
        using JsonDocument json = await GetJsonAsync(
            BuildUri(_wikipediaApi, query),
            cancellationToken);
        JsonElement results = Property(
            Property(json.RootElement, "query"),
            "search");
        if (results.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "Wikipedia search returned an unexpected result.");
        }
        string[] requestedTokens = LexicalTokens(gameName);
        return results.EnumerateArray()
            .Select(static value => Text(value, "title", allowBlank: false))
            .Select(title => new
            {
                Title = title,
                Tokens = LexicalTokens(title),
            })
            .Where(value => requestedTokens.All(token =>
                value.Tokens.Contains(token, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(value => value.Tokens.Length - requestedTokens.Length)
            .ThenBy(static value => value.Title, StringComparer.Ordinal)
            .Select(static value => value.Title)
            .ToArray();
    }

    private async Task<WikipediaDocument[]> FetchRelatedWikipediaAsync(
        string gameName,
        WikipediaDocument primary,
        CancellationToken cancellationToken)
    {
        string[] gameTokens = LexicalTokens(gameName);
        var documents = new List<WikipediaDocument>();
        foreach (string topic in RelatedArticleTopics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = new Dictionary<string, string>
            {
                ["action"] = "query",
                ["format"] = "json",
                ["formatversion"] = "2",
                ["list"] = "search",
                ["srsearch"] = $"\"{gameName}\" {topic}",
                ["srnamespace"] = "0",
                ["srlimit"] = "3",
            };
            using JsonDocument json = await GetJsonAsync(
                BuildUri(_wikipediaApi, query),
                cancellationToken);
            JsonElement results = Property(
                Property(json.RootElement, "query"),
                "search");
            if (results.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "Wikipedia related-page search returned an unexpected result.");
            }
            foreach (string title in results.EnumerateArray()
                         .Select(static value => Text(
                             value,
                             "title",
                             allowBlank: false)))
            {
                if (title.Equals(primary.Title, StringComparison.OrdinalIgnoreCase) ||
                    documents.Any(value => value.Title.Equals(
                        title,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                WikipediaDocument? document = await TryFetchWikipediaTitleAsync(
                    title,
                    cancellationToken);
                if (document is null || document.PageUri.Equals(primary.PageUri) ||
                    !IsRelatedGameArticle(document, gameTokens, topic))
                {
                    continue;
                }
                documents.Add(document);
            }
        }
        return documents
            .OrderByDescending(value => RelatedArticleScore(value, gameTokens))
            .ThenBy(static value => value.Title, StringComparer.Ordinal)
            .Take(MaximumRelatedWikipediaSources)
            .ToArray();
    }

    private static bool IsRelatedGameArticle(
        WikipediaDocument document,
        IReadOnlyCollection<string> gameTokens,
        string topic)
    {
        string searchable = document.Title + " " +
            (document.Extract.Length <= 1_200
                ? document.Extract
                : document.Extract[..1_200]);
        string[] tokens = LexicalTokens(searchable);
        return gameTokens.All(token => tokens.Contains(
                token,
                StringComparer.OrdinalIgnoreCase)) &&
            (tokens.Contains(topic, StringComparer.OrdinalIgnoreCase) ||
             document.Extract.Contains(
                 "video game",
                 StringComparison.OrdinalIgnoreCase));
    }

    private static int RelatedArticleScore(
        WikipediaDocument document,
        IReadOnlyCollection<string> gameTokens)
    {
        string[] titleTokens = LexicalTokens(document.Title);
        int score = gameTokens.Count(token => titleTokens.Contains(
            token,
            StringComparer.OrdinalIgnoreCase)) * 5;
        score += RelatedArticleTopics.Count(topic => titleTokens.Contains(
            topic,
            StringComparer.OrdinalIgnoreCase)) * 3;
        return score;
    }

    private static int ArticleScore(
        WikipediaDocument document,
        string requestedName,
        IReadOnlyCollection<string> requestedTokens)
    {
        string title = document.Title;
        string introduction = document.Extract.Length <= 800
            ? document.Extract
            : document.Extract[..800];
        int score = requestedTokens.All(token =>
                LexicalTokens(title).Contains(token, StringComparer.Ordinal))
            ? 10
            : 0;
        if (title.Equals(requestedName, StringComparison.OrdinalIgnoreCase))
        {
            score += 2;
        }
        if (title.Contains("(video game)", StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }
        if (introduction.Contains("video game", StringComparison.OrdinalIgnoreCase))
        {
            score += 6;
        }
        if (introduction.Contains("series and media franchise", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("television", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("soundtrack", StringComparison.OrdinalIgnoreCase))
        {
            score -= 16;
        }
        return score;
    }

    private async Task<WikidataDocument?> FetchWikidataAsync(
        string entityId,
        CancellationToken cancellationToken)
    {
        if (!EntityIdPattern().IsMatch(entityId))
        {
            throw new InvalidDataException(
                "Wikipedia supplied an invalid Wikidata entity identity.");
        }
        var query = new Dictionary<string, string>
        {
            ["action"] = "wbgetentities",
            ["format"] = "json",
            ["formatversion"] = "2",
            ["ids"] = entityId,
            ["props"] = "labels|descriptions|aliases|info",
            ["languages"] = "en",
            ["languagefallback"] = "1",
        };
        using JsonDocument json = await GetJsonAsync(
            BuildUri(_wikidataApi, query),
            cancellationToken);
        JsonElement entities = Property(json.RootElement, "entities");
        if (entities.ValueKind != JsonValueKind.Object ||
            !entities.TryGetProperty(entityId, out JsonElement entity) ||
            entity.TryGetProperty("missing", out _))
        {
            return null;
        }
        string label = LanguageValue(entity, "labels") ?? entityId;
        string? description = LanguageValue(entity, "descriptions");
        string[] aliases = LanguageValues(entity, "aliases");
        string revisionId = entity.TryGetProperty(
                "lastrevid",
                out JsonElement revisionElement) &&
            revisionElement.TryGetInt64(out long revision)
                ? revision.ToString(CultureInfo.InvariantCulture)
                : "unknown";
        string content = string.Join(
            ". ",
            new[]
            {
                label,
                description,
                aliases.Length == 0
                    ? null
                    : "Also known as " + string.Join(", ", aliases),
            }.Where(static value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(content)
            ? null
            : new WikidataDocument(
                entityId,
                label,
                new Uri(
                    $"https://www.wikidata.org/wiki/Special:EntityData/{entityId}.json",
                    UriKind.Absolute),
                revisionId,
                DateTimeOffset.UtcNow,
                content);
    }

    private static void AddWikipedia(
        WikipediaDocument document,
        GameKnowledgeSourceRole role,
        int maximumPassages,
        ICollection<GameKnowledgeSource> sources,
        ICollection<GameKnowledgePassage> passages)
    {
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
            GameKnowledgePassage.ComputeSha256(document.Extract),
            role));
        foreach ((string Section, string Text) in SplitWikipediaExtract(
                     document.Extract).Take(maximumPassages))
        {
            AddPassage(sourceId, Section, Text, passages);
        }
    }

    private static void AddWikidata(
        WikidataDocument document,
        ICollection<GameKnowledgeSource> sources,
        ICollection<GameKnowledgePassage> passages)
    {
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
            GameKnowledgePassage.ComputeSha256(document.Content),
            GameKnowledgeSourceRole.StructuredIdentity));
        AddPassage(sourceId, "Identity", document.Content, passages);
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

    private async Task<JsonDocument> GetJsonAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        return await JsonDocument.ParseAsync(
            stream,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64,
            },
            cancellationToken);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "ReplayFoundry/1.0 (local creator tool; game knowledge attribution)");
        return client;
    }

    private static string RequireHttpsEndpoint(
        string value,
        string parameterName) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? uri.AbsoluteUri.TrimEnd('?')
            : throw new ArgumentException(
                "Wikimedia API endpoints must be absolute HTTPS URIs.",
                parameterName);

    private static Uri BuildUri(
        string endpoint,
        IReadOnlyDictionary<string, string> values) =>
        new(
            endpoint + "?" + string.Join(
                "&",
                values.Select(value =>
                    Uri.EscapeDataString(value.Key) + "=" +
                    Uri.EscapeDataString(value.Value))),
            UriKind.Absolute);

    private static string AppendOldId(string url, long revisionId) =>
        url + (url.Contains('?', StringComparison.Ordinal) ? "&" : "?") +
        "oldid=" + revisionId.ToString(CultureInfo.InvariantCulture);

    private static string StableSourceId(
        GameKnowledgeSourceKind kind,
        Uri pageUri,
        string revisionId)
    {
        string hash = GameKnowledgePassage.ComputeSha256(
            $"{kind}|{pageUri.AbsoluteUri}|{revisionId}");
        return "gks-" + hash[..20];
    }

    private static JsonElement Property(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement property)
            ? property
            : throw new InvalidDataException(
                $"Wikimedia response is missing '{name}'.");

    private static string Text(
        JsonElement value,
        string name,
        bool allowBlank)
    {
        JsonElement property = Property(value, name);
        string? text = property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
        if (text is null || !allowBlank && string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException(
                $"Wikimedia response '{name}' must be text.");
        }
        return text;
    }

    private static long Integer(JsonElement value, string name)
    {
        JsonElement property = Property(value, name);
        return property.TryGetInt64(out long result) && result >= 0
            ? result
            : throw new InvalidDataException(
                $"Wikimedia response '{name}' must be a non-negative integer.");
    }

    private static DateTimeOffset UtcTimestamp(string value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset result) && result.Offset == TimeSpan.Zero
                ? result
                : throw new InvalidDataException(
                    "Wikimedia revision timestamps must be UTC.");

    private static string? LanguageValue(JsonElement entity, string name)
    {
        if (!entity.TryGetProperty(name, out JsonElement values) ||
            values.ValueKind != JsonValueKind.Object ||
            !values.TryGetProperty("en", out JsonElement english) ||
            english.ValueKind != JsonValueKind.Object ||
            !english.TryGetProperty("value", out JsonElement value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        return value.GetString();
    }

    private static string[] LanguageValues(JsonElement entity, string name)
    {
        if (!entity.TryGetProperty(name, out JsonElement values) ||
            values.ValueKind != JsonValueKind.Object ||
            !values.TryGetProperty("en", out JsonElement english) ||
            english.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        return english.EnumerateArray()
            .Where(static value => value.ValueKind == JsonValueKind.Object &&
                value.TryGetProperty("value", out JsonElement text) &&
                text.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(text.GetString()))
            .Select(static value => value.GetProperty("value").GetString()!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();
    }

    internal static string[] LexicalTokens(string value) =>
        TokenPattern().Matches(value.Normalize(NormalizationForm.FormKC))
            .Select(static match => match.Value.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    [GeneratedRegex(@"^Q[1-9][0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();

    [GeneratedRegex(@"^=+\s*(.+?)\s*=+$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"(?<=[.!?])\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceBoundaryPattern();

    [GeneratedRegex(@"[\p{L}\p{Nd}][\p{L}\p{Nd}'’_-]*", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    private sealed record WikipediaDocument(
        string Title,
        Uri PageUri,
        string RevisionId,
        DateTimeOffset RevisionTimestampUtc,
        string Extract,
        string? WikidataEntityId);

    private sealed record WikidataDocument(
        string EntityId,
        string Label,
        Uri PageUri,
        string RevisionId,
        DateTimeOffset RevisionTimestampUtc,
        string Content);
}
