using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;

namespace ReplayFoundry.Desktop.Platform.GameKnowledge;

public sealed partial class WikimediaGameKnowledgeProvider
{
    private enum WikimediaRequestMode
    {
        Background,
        Interactive,
    }

    private const string WikipediaApi =
        "https://en.wikipedia.org/w/api.php";
    private const string WikidataApi =
        "https://www.wikidata.org/w/api.php";
    private const int MaximumRelatedWikipediaSources = 2;
    private const int MaximumClaimTargets = 24;
    private static readonly string[] RelatedArticleTopics =
        ["characters", "plot", "setting"];
    private static readonly HttpClient SharedClient = CreateClient();
    private static readonly SemaphoreSlim RequestConcurrency = new(3, 3);
    private static readonly object RateGate = new();
    private static DateTimeOffset _nextProductionRequestUtc =
        DateTimeOffset.MinValue;
    private static readonly IReadOnlyDictionary<string, string>
        AllowlistedClaimLabels = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["P178"] = "Developer",
            ["P123"] = "Publisher",
            ["P179"] = "Series",
            ["P400"] = "Platform",
            ["P136"] = "Genre",
            ["P674"] = "Characters",
            ["P840"] = "Narrative location",
            ["P155"] = "Follows",
            ["P156"] = "Followed by",
        };
    private readonly HttpClient _httpClient;
    private readonly string _wikipediaApi;
    private readonly string _wikidataApi;

    private async Task<WikipediaDocument> FetchWikipediaTitleRequiredAsync(
        string title,
        CancellationToken cancellationToken) =>
        await TryFetchWikipediaTitleAsync(title, cancellationToken) ??
        throw new InvalidDataException(
            "The selected game's English Wikipedia article is unavailable.");

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

    private async Task<WikipediaDocument[]> FetchRelatedWikipediaAsync(
        string gameName,
        string primaryTitle,
        Uri primaryUri,
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
                if (title.Equals(primaryTitle, StringComparison.OrdinalIgnoreCase) ||
                    documents.Any(value => value.Title.Equals(
                        title,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                WikipediaDocument? document = await TryFetchWikipediaTitleAsync(
                    title,
                    cancellationToken);
                if (document is null || document.PageUri.Equals(primaryUri) ||
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
            searchable.Contains(
                "video game",
                StringComparison.OrdinalIgnoreCase) &&
            tokens.Contains(topic, StringComparer.OrdinalIgnoreCase);
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

    private async Task<WikidataDocument?> FetchWikidataAsync(
        string entityId,
        string locale,
        WikimediaRequestMode requestMode,
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
            ["props"] = "labels|descriptions|aliases|claims|info|sitelinks",
            ["languages"] = locale + "|en",
            ["languagefallback"] = "1",
            ["sitefilter"] = "enwiki",
        };
        using JsonDocument json = await GetJsonAsync(
            BuildUri(_wikidataApi, query, requestMode),
            cancellationToken);
        JsonElement entities = Property(json.RootElement, "entities");
        if (entities.ValueKind != JsonValueKind.Object ||
            !entities.TryGetProperty(entityId, out JsonElement entity) ||
            entity.TryGetProperty("missing", out _))
        {
            return null;
        }
        string label = LanguageValue(entity, "labels", locale) ?? entityId;
        string? description = LanguageValue(entity, "descriptions", locale);
        string[] aliases = LanguageValues(entity, "aliases", locale);
        string revisionId = entity.TryGetProperty(
                "lastrevid",
                out JsonElement revisionElement) &&
            revisionElement.TryGetInt64(out long revision)
                ? revision.ToString(CultureInfo.InvariantCulture)
                : "unknown";
        DateTimeOffset revisionTimestamp = entity.TryGetProperty(
                "modified",
                out JsonElement modified) &&
            modified.ValueKind == JsonValueKind.String
                ? UtcTimestamp(modified.GetString()!)
                : DateTimeOffset.UtcNow;
        string? englishWikipediaTitle = EnglishWikipediaTitle(entity);
        IReadOnlyDictionary<string, string[]> rawClaims = ReadEntityClaims(entity);
        string[] targetIds = rawClaims.Values
            .SelectMany(static values => values)
            .Distinct(StringComparer.Ordinal)
            .Take(MaximumClaimTargets)
            .ToArray();
        IReadOnlyDictionary<string, string> targetLabels =
            await FetchEntityLabelsAsync(
                targetIds,
                locale,
                requestMode,
                cancellationToken);
        var namedClaims = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach ((string propertyId, string displayName) in AllowlistedClaimLabels)
        {
            string[] values = rawClaims.GetValueOrDefault(propertyId, [])
                .Where(targetLabels.ContainsKey)
                .Select(value => targetLabels[value])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToArray();
            if (values.Length > 0)
            {
                namedClaims[displayName] = values;
            }
        }
        string identityContent = string.Join(
            ". ",
            new[]
            {
                label,
                description,
                aliases.Length == 0
                    ? null
                    : "Also known as " + string.Join(", ", aliases),
            }.Where(static value => !string.IsNullOrWhiteSpace(value)));
        bool isVideoGame = rawClaims.GetValueOrDefault("P31", [])
                .Contains("Q7889", StringComparer.Ordinal) ||
            description?.Contains(
                "video game",
                StringComparison.OrdinalIgnoreCase) == true;
        return string.IsNullOrWhiteSpace(identityContent)
            ? null
            : new WikidataDocument(
                entityId,
                label,
                new Uri(
                    $"https://www.wikidata.org/w/index.php?title={entityId}&oldid={revisionId}",
                    UriKind.Absolute),
                revisionId,
                revisionTimestamp,
                description,
                aliases,
                identityContent,
                namedClaims,
                englishWikipediaTitle,
                ReadReleaseYear(entity),
                isVideoGame);
    }

    private async Task<IReadOnlyDictionary<string, string>>
        FetchEntityLabelsAsync(
            IReadOnlyCollection<string> entityIds,
            string locale,
            WikimediaRequestMode requestMode,
            CancellationToken cancellationToken)
    {
        if (entityIds.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
        var query = new Dictionary<string, string>
        {
            ["action"] = "wbgetentities",
            ["format"] = "json",
            ["formatversion"] = "2",
            ["ids"] = string.Join('|', entityIds.Take(MaximumClaimTargets)),
            ["props"] = "labels",
            ["languages"] = locale + "|en",
            ["languagefallback"] = "1",
        };
        using JsonDocument json = await GetJsonAsync(
            BuildUri(_wikidataApi, query, requestMode),
            cancellationToken);
        JsonElement entities = Property(json.RootElement, "entities");
        if (entities.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "Wikidata claim-label lookup returned an unexpected result.");
        }
        return entities.EnumerateObject()
            .Where(static value => value.Value.ValueKind == JsonValueKind.Object)
            .Select(value => new
            {
                value.Name,
                Label = LanguageValue(value.Value, "labels", locale),
            })
            .Where(static value => !string.IsNullOrWhiteSpace(value.Label))
            .ToDictionary(
                static value => value.Name,
                static value => value.Label!,
                StringComparer.Ordinal);
    }

    private async Task<JsonDocument> GetJsonAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        await RequestConcurrency.WaitAsync(cancellationToken);
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (ReferenceEquals(_httpClient, SharedClient))
                {
                    await WaitForProductionRateSlotAsync(cancellationToken);
                }
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd(
                    "ReplayFoundry/2.0 (+https://replayfoundry.com; support@replayfoundry.com)");
                using HttpResponseMessage response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (attempt == 0 && IsTransient(response.StatusCode))
                {
                    TimeSpan? directedDelay = ServerRetryDelay(response);
                    if (directedDelay > TimeSpan.FromSeconds(5))
                    {
                        throw new WikimediaRetryAfterException(
                            DateTimeOffset.UtcNow.Add(directedDelay.Value));
                    }
                    await Task.Delay(
                        directedDelay ?? TimeSpan.FromMilliseconds(250),
                        cancellationToken);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                await using Stream stream =
                    await response.Content.ReadAsStreamAsync(cancellationToken);
                JsonDocument document = await JsonDocument.ParseAsync(
                    stream,
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = false,
                        CommentHandling = JsonCommentHandling.Disallow,
                        MaxDepth = 64,
                    },
                    cancellationToken);
                bool hasApiError;
                string errorCode;
                try
                {
                    hasApiError = TryReadApiErrorCode(
                        document.RootElement,
                        out errorCode);
                }
                catch
                {
                    document.Dispose();
                    throw;
                }
                if (!hasApiError)
                {
                    return document;
                }
                document.Dispose();
                if (!errorCode.Equals(
                        "maxlag",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new WikimediaApiException(errorCode);
                }
                TimeSpan retryDelay = ServerRetryDelay(response) ??
                    TimeSpan.FromMilliseconds(250);
                if (attempt == 0 && retryDelay <= TimeSpan.FromSeconds(5))
                {
                    await Task.Delay(retryDelay, cancellationToken);
                    continue;
                }
                throw new WikimediaRetryAfterException(
                    DateTimeOffset.UtcNow.Add(retryDelay));
            }
            throw new HttpRequestException(
                "Wikimedia request retries were exhausted.");
        }
        finally
        {
            RequestConcurrency.Release();
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "ReplayFoundry/2.0 (+https://replayfoundry.com; support@replayfoundry.com)");
        return client;
    }

    private static async Task WaitForProductionRateSlotAsync(
        CancellationToken cancellationToken)
    {
        TimeSpan delay;
        lock (RateGate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset slot = _nextProductionRequestUtc > now
                ? _nextProductionRequestUtc
                : now;
            delay = slot - now;
            _nextProductionRequestUtc = slot.AddMilliseconds(300);
        }
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;

    private static TimeSpan? ServerRetryDelay(HttpResponseMessage response)
    {
        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is not null)
        {
            retryAfter = response.Headers.RetryAfter.Date.Value -
                DateTimeOffset.UtcNow;
        }
        return retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero
            ? retryAfter
            : null;
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
        IReadOnlyDictionary<string, string> values,
        WikimediaRequestMode requestMode = WikimediaRequestMode.Background)
    {
        IEnumerable<KeyValuePair<string, string>> boundedValues =
            requestMode == WikimediaRequestMode.Interactive ||
            values.ContainsKey("maxlag")
                ? values
                : values.Append(new KeyValuePair<string, string>("maxlag", "5"));
        return new Uri(
            endpoint + "?" + string.Join(
                "&",
                boundedValues.Select(value =>
                    Uri.EscapeDataString(value.Key) + "=" +
                    Uri.EscapeDataString(value.Value))),
            UriKind.Absolute);
    }

    private static bool TryReadApiErrorCode(
        JsonElement root,
        out string errorCode)
    {
        errorCode = string.Empty;
        if (!root.TryGetProperty("error", out JsonElement error))
        {
            return false;
        }
        if (error.ValueKind != JsonValueKind.Object ||
            !error.TryGetProperty("code", out JsonElement code) ||
            code.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(code.GetString()))
        {
            throw new InvalidDataException(
                "Wikimedia returned a malformed application-level API error.");
        }
        errorCode = code.GetString()!;
        return true;
    }

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

    private static string? LanguageValue(
        JsonElement entity,
        string name,
        string locale)
    {
        if (!entity.TryGetProperty(name, out JsonElement values) ||
            values.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        foreach (string language in new[] { locale, "en" }
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (values.TryGetProperty(language, out JsonElement localized) &&
                localized.ValueKind == JsonValueKind.Object &&
                localized.TryGetProperty("value", out JsonElement value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        return null;
    }

    private static string[] LanguageValues(
        JsonElement entity,
        string name,
        string locale)
    {
        if (!entity.TryGetProperty(name, out JsonElement values) ||
            values.ValueKind != JsonValueKind.Object)
        {
            return [];
        }
        foreach (string language in new[] { locale, "en" }
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (values.TryGetProperty(language, out JsonElement localized) &&
                localized.ValueKind == JsonValueKind.Array)
            {
                return localized.EnumerateArray()
                    .Where(static value =>
                        value.ValueKind == JsonValueKind.Object &&
                        value.TryGetProperty("value", out JsonElement text) &&
                        text.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(text.GetString()))
                    .Select(static value =>
                        value.GetProperty("value").GetString()!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(20)
                    .ToArray();
            }
        }
        return [];
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

    [GeneratedRegex(@"^[+-]([0-9]{4})-", RegexOptions.CultureInvariant)]
    private static partial Regex WikidataTimeYearPattern();

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
        string? Description,
        string[] Aliases,
        string IdentityContent,
        IReadOnlyDictionary<string, string[]> NamedClaims,
        string? EnglishWikipediaTitle,
        int? ReleaseYear,
        bool IsVideoGame);
}
