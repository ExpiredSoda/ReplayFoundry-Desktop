using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;

public enum GameKnowledgeSourceKind
{
    Wikipedia,
    Wikidata,
    StrategyWiki,
}

public enum GameKnowledgeSourceRole
{
    PrimaryArticle,
    RelatedArticle,
    StructuredIdentity,
}

public enum GameKnowledgeMatchStrength
{
    GeneralContext,
    CandidateForVisualGrounding,
    ClipLinked,
}

public enum GameKnowledgeTemporalRelation
{
    Unspecified,
    CurrentEventCandidate,
    ImmediatelyPriorContext,
}

public enum GameKnowledgeWarningCode
{
    Unavailable,
    NoRelevantPassage,
}

public sealed record GameKnowledgeProviderIdentity
{
    public GameKnowledgeProviderIdentity(string name, string version)
    {
        Name = Required(name, 120, nameof(name));
        Version = Required(version, 40, nameof(version));
    }

    public string Name { get; }

    public string Version { get; }

    private static string Required(
        string value,
        int maximum,
        string parameterName) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximum
            ? throw new ArgumentException(
                $"{parameterName} must contain at most {maximum} characters.",
                parameterName)
            : value.Trim();
}

public sealed class GameKnowledgeSource
{
    public GameKnowledgeSource(
        string id,
        GameKnowledgeSourceKind kind,
        string title,
        Uri pageUri,
        string revisionId,
        DateTimeOffset revisionTimestampUtc,
        string licenseIdentifier,
        Uri licenseUri,
        string attribution,
        string contentSha256,
        GameKnowledgeSourceRole role =
            GameKnowledgeSourceRole.PrimaryArticle)
    {
        Id = StableId(id, nameof(id));
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }
        ArgumentNullException.ThrowIfNull(pageUri);
        ArgumentNullException.ThrowIfNull(licenseUri);
        if (!pageUri.IsAbsoluteUri ||
            !pageUri.Scheme.Equals(Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            !licenseUri.IsAbsoluteUri ||
            !licenseUri.Scheme.Equals(Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Game-knowledge sources and licenses require absolute HTTPS URIs.");
        }
        if (revisionTimestampUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Game-knowledge revision timestamps must be UTC.",
                nameof(revisionTimestampUtc));
        }

        Kind = kind;
        Role = role;
        Title = Required(title, 240, nameof(title));
        PageUri = pageUri;
        RevisionId = Required(revisionId, 120, nameof(revisionId));
        RevisionTimestampUtc = revisionTimestampUtc;
        LicenseIdentifier = Required(
            licenseIdentifier,
            80,
            nameof(licenseIdentifier));
        LicenseUri = licenseUri;
        Attribution = Required(attribution, 500, nameof(attribution));
        ContentSha256 = Sha256(contentSha256, nameof(contentSha256));
    }

    public string Id { get; }

    public GameKnowledgeSourceKind Kind { get; }

    public GameKnowledgeSourceRole Role { get; }

    public string Title { get; }

    public Uri PageUri { get; }

    public string RevisionId { get; }

    public DateTimeOffset RevisionTimestampUtc { get; }

    public string LicenseIdentifier { get; }

    public Uri LicenseUri { get; }

    public string Attribution { get; }

    public string ContentSha256 { get; }

    internal static string StableId(string value, string parameterName)
    {
        string result = Required(value, 160, parameterName);
        return result.All(character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or '.')
            ? result
            : throw new ArgumentException(
                "Stable game-knowledge IDs may contain only ASCII letters, digits, periods, dashes, and underscores.",
                parameterName);
    }

    internal static string Sha256(string value, string parameterName) =>
        value is not null && value.Length == 64 && value.All(Uri.IsHexDigit)
            ? value.ToLowerInvariant()
            : throw new ArgumentException(
                "Game-knowledge hashes must be SHA-256 hexadecimal values.",
                parameterName);

    internal static string Required(
        string value,
        int maximum,
        string parameterName) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximum
            ? throw new ArgumentException(
                $"{parameterName} must contain at most {maximum} characters.",
                parameterName)
            : value.Trim();
}

public sealed class GameKnowledgePassage
{
    public const int MaximumTextLength = 700;

    public GameKnowledgePassage(
        string id,
        string sourceId,
        string section,
        string text,
        string contentSha256)
    {
        Id = GameKnowledgeSource.StableId(id, nameof(id));
        SourceId = GameKnowledgeSource.StableId(
            sourceId,
            nameof(sourceId));
        Section = GameKnowledgeSource.Required(
            section,
            160,
            nameof(section));
        Text = GameKnowledgeSource.Required(
            text,
            MaximumTextLength,
            nameof(text));
        ContentSha256 = GameKnowledgeSource.Sha256(
            contentSha256,
            nameof(contentSha256));
        string actual = ComputeSha256(Text);
        if (!actual.Equals(ContentSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The game-knowledge passage hash does not match its text.",
                nameof(contentSha256));
        }
    }

    public string Id { get; }

    public string SourceId { get; }

    public string Section { get; }

    public string Text { get; }

    public string ContentSha256 { get; }

    public static string ComputeSha256(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}

public enum GameKnowledgeComponentKind
{
    WikipediaPrimary,
    WikipediaRelated,
    WikidataIdentity,
    WikidataClaims,
    StrategyWiki,
}

public enum GameKnowledgeComponentCompleteness
{
    Complete,
    Missing,
    TransientFailure,
    Disabled,
    Legacy,
}

public sealed class GameKnowledgeComponentState
{
    public GameKnowledgeComponentState(
        GameKnowledgeComponentKind kind,
        GameKnowledgeComponentCompleteness completeness,
        DateTimeOffset checkedAtUtc,
        DateTimeOffset? retryAfterUtc = null,
        string? revisionId = null,
        string? licenseIdentifier = null,
        string? attribution = null,
        string? contentSha256 = null)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(completeness))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        RequireUtc(checkedAtUtc, nameof(checkedAtUtc));
        if (retryAfterUtc is not null)
        {
            RequireUtc(retryAfterUtc.Value, nameof(retryAfterUtc));
            if (retryAfterUtc <= checkedAtUtc)
            {
                throw new ArgumentException(
                    "A component retry must be scheduled after its check.",
                    nameof(retryAfterUtc));
            }
        }
        bool complete = completeness == GameKnowledgeComponentCompleteness.Complete;
        if (complete &&
            (string.IsNullOrWhiteSpace(revisionId) ||
             string.IsNullOrWhiteSpace(licenseIdentifier) ||
             string.IsNullOrWhiteSpace(attribution) ||
             contentSha256 is null))
        {
            throw new ArgumentException(
                "Complete game-knowledge components require revision, license, attribution, and content hash metadata.");
        }
        if (completeness is GameKnowledgeComponentCompleteness.Missing or
                GameKnowledgeComponentCompleteness.TransientFailure &&
            retryAfterUtc is null)
        {
            throw new ArgumentException(
                "Missing and transient game-knowledge components require a bounded retry time.",
                nameof(retryAfterUtc));
        }

        Kind = kind;
        Completeness = completeness;
        CheckedAtUtc = checkedAtUtc;
        RetryAfterUtc = retryAfterUtc;
        RevisionId = Optional(revisionId, 240, nameof(revisionId));
        LicenseIdentifier = Optional(
            licenseIdentifier,
            120,
            nameof(licenseIdentifier));
        Attribution = Optional(attribution, 1_500, nameof(attribution));
        ContentSha256 = contentSha256 is null
            ? null
            : GameKnowledgeSource.Sha256(contentSha256, nameof(contentSha256));
    }

    public GameKnowledgeComponentKind Kind { get; }

    public GameKnowledgeComponentCompleteness Completeness { get; }

    public DateTimeOffset CheckedAtUtc { get; }

    public DateTimeOffset? RetryAfterUtc { get; }

    public string? RevisionId { get; }

    public string? LicenseIdentifier { get; }

    public string? Attribution { get; }

    public string? ContentSha256 { get; }

    public bool IsRefreshDue(DateTimeOffset nowUtc, TimeSpan freshFor)
    {
        RequireUtc(nowUtc, nameof(nowUtc));
        if (Completeness == GameKnowledgeComponentCompleteness.Disabled)
        {
            return false;
        }
        if (Completeness == GameKnowledgeComponentCompleteness.Legacy)
        {
            return true;
        }
        if (Completeness is GameKnowledgeComponentCompleteness.Missing or
                GameKnowledgeComponentCompleteness.TransientFailure)
        {
            return RetryAfterUtc <= nowUtc;
        }
        return nowUtc - CheckedAtUtc >= freshFor;
    }

    private static string? Optional(
        string? value,
        int maximum,
        string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : GameKnowledgeSource.Required(value, maximum, parameterName);

    private static void RequireUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Game-knowledge component timestamps must be UTC.",
                parameterName);
        }
    }
}

public sealed class GameKnowledgeSnapshot
{
    public const string SchemaVersion = "replayfoundry-game-knowledge-snapshot-1.2";
    public const string LegacySchemaVersion =
        "replayfoundry-game-knowledge-snapshot-1.1";
    public const int MaximumSources = 8;
    public const int MaximumPassages = 64;
    private readonly ReadOnlyCollection<GameKnowledgeSource> _sources;
    private readonly ReadOnlyCollection<GameKnowledgePassage> _passages;
    private readonly ReadOnlyCollection<GameKnowledgeComponentState> _components;

    public GameKnowledgeSnapshot(
        ConfirmedGameIdentity confirmedIdentity,
        GameKnowledgeProviderIdentity provider,
        DateTimeOffset retrievedAtUtc,
        IEnumerable<GameKnowledgeSource> sources,
        IEnumerable<GameKnowledgePassage> passages,
        IEnumerable<GameKnowledgeComponentState> components)
        : this(
            SchemaVersion,
            confirmedIdentity?.CanonicalTitle ?? throw new ArgumentNullException(
                nameof(confirmedIdentity)),
            confirmedIdentity,
            provider,
            retrievedAtUtc,
            sources,
            passages,
            components)
    {
    }

    public GameKnowledgeSnapshot(
        string gameName,
        GameKnowledgeProviderIdentity provider,
        DateTimeOffset retrievedAtUtc,
        IEnumerable<GameKnowledgeSource> sources,
        IEnumerable<GameKnowledgePassage> passages)
        : this(
            LegacySchemaVersion,
            gameName,
            confirmedIdentity: null,
            provider,
            retrievedAtUtc,
            sources,
            passages,
            CreateLegacyComponents(retrievedAtUtc))
    {
    }

    private GameKnowledgeSnapshot(
        string formatVersion,
        string gameName,
        ConfirmedGameIdentity? confirmedIdentity,
        GameKnowledgeProviderIdentity provider,
        DateTimeOffset retrievedAtUtc,
        IEnumerable<GameKnowledgeSource> sources,
        IEnumerable<GameKnowledgePassage> passages,
        IEnumerable<GameKnowledgeComponentState> components)
    {
        FormatVersion = formatVersion is SchemaVersion or LegacySchemaVersion
            ? formatVersion
            : throw new ArgumentException(
                "The game-knowledge snapshot schema is not supported.",
                nameof(formatVersion));
        if (FormatVersion == SchemaVersion && confirmedIdentity is null)
        {
            throw new ArgumentException(
                "Schema 1.2 snapshots require a confirmed QID identity.",
                nameof(confirmedIdentity));
        }
        GameName = GameKnowledgeSource.Required(gameName, 160, nameof(gameName));
        ConfirmedIdentity = confirmedIdentity;
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        if (retrievedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Game-knowledge retrieval timestamps must be UTC.",
                nameof(retrievedAtUtc));
        }
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(passages);
        ArgumentNullException.ThrowIfNull(components);
        GameKnowledgeSource[] sourceSnapshot = sources.ToArray();
        GameKnowledgePassage[] passageSnapshot = passages.ToArray();
        GameKnowledgeComponentState[] componentSnapshot = components.ToArray();
        if (sourceSnapshot.Length is < 1 or > MaximumSources ||
            passageSnapshot.Length is < 1 or > MaximumPassages ||
            componentSnapshot.Length is < 1 or > 8 ||
            sourceSnapshot.Any(static value => value is null) ||
            passageSnapshot.Any(static value => value is null) ||
            componentSnapshot.Any(static value => value is null) ||
            sourceSnapshot.Select(static value => value.Id)
                .Distinct(StringComparer.Ordinal).Count() != sourceSnapshot.Length ||
            passageSnapshot.Select(static value => value.Id)
                .Distinct(StringComparer.Ordinal).Count() != passageSnapshot.Length ||
            componentSnapshot.Select(static value => value.Kind)
                .Distinct().Count() != componentSnapshot.Length)
        {
            throw new ArgumentException(
                "Game-knowledge snapshots require bounded, non-null, stable-ID-unique sources, passages, and components.");
        }
        var sourceIds = sourceSnapshot
            .Select(static value => value.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (passageSnapshot.Any(value => !sourceIds.Contains(value.SourceId)))
        {
            throw new ArgumentException(
                "Every game-knowledge passage must belong to a retained source.",
                nameof(passages));
        }

        RetrievedAtUtc = retrievedAtUtc;
        _sources = Array.AsReadOnly(sourceSnapshot);
        _passages = Array.AsReadOnly(passageSnapshot);
        _components = Array.AsReadOnly(componentSnapshot);
        SnapshotSha256 = ComputeSnapshotSha256(
            FormatVersion,
            GameName,
            ConfirmedIdentity,
            Provider,
            RetrievedAtUtc,
            sourceSnapshot,
            passageSnapshot,
            componentSnapshot);
    }

    public string FormatVersion { get; }

    public bool IsLegacy => FormatVersion == LegacySchemaVersion;

    public string GameName { get; }

    public ConfirmedGameIdentity? ConfirmedIdentity { get; }

    public GameKnowledgeProviderIdentity Provider { get; }

    public DateTimeOffset RetrievedAtUtc { get; }

    public IReadOnlyList<GameKnowledgeSource> Sources => _sources;

    public IReadOnlyList<GameKnowledgePassage> Passages => _passages;

    public IReadOnlyList<GameKnowledgeComponentState> Components => _components;

    public string SnapshotSha256 { get; }

    internal static GameKnowledgeSnapshot CreateLegacy(
        string gameName,
        GameKnowledgeProviderIdentity provider,
        DateTimeOffset retrievedAtUtc,
        IEnumerable<GameKnowledgeSource> sources,
        IEnumerable<GameKnowledgePassage> passages) =>
        new(
            LegacySchemaVersion,
            gameName,
            confirmedIdentity: null,
            provider,
            retrievedAtUtc,
            sources,
            passages,
            CreateLegacyComponents(retrievedAtUtc));

    internal static string ComputeLegacySnapshotSha256(
        string gameName,
        GameKnowledgeProviderIdentity provider,
        DateTimeOffset retrievedAtUtc,
        IEnumerable<GameKnowledgeSource> sources,
        IEnumerable<GameKnowledgePassage> passages) =>
        ComputeSnapshotSha256(
            LegacySchemaVersion,
            gameName,
            confirmedIdentity: null,
            provider,
            retrievedAtUtc,
            sources,
            passages,
            components: []);

    private static string ComputeSnapshotSha256(
        string formatVersion,
        string gameName,
        ConfirmedGameIdentity? confirmedIdentity,
        GameKnowledgeProviderIdentity provider,
        DateTimeOffset retrievedAtUtc,
        IEnumerable<GameKnowledgeSource> sources,
        IEnumerable<GameKnowledgePassage> passages,
        IEnumerable<GameKnowledgeComponentState> components)
    {
        var value = new StringBuilder();
        value.Append(formatVersion).Append('\n')
            .Append(gameName).Append('\n')
            .Append(provider.Name).Append('\n')
            .Append(provider.Version).Append('\n')
            .Append(retrievedAtUtc.ToString("O", CultureInfo.InvariantCulture))
            .Append('\n');
        if (formatVersion == SchemaVersion && confirmedIdentity is not null)
        {
            value.Append(confirmedIdentity.WikidataEntityId).Append('|')
                .Append(confirmedIdentity.CanonicalTitle).Append('|')
                .Append(confirmedIdentity.Edition).Append('|')
                .Append(confirmedIdentity.ReleaseYear).Append('|')
                .Append(confirmedIdentity.Developer).Append('|')
                .Append(confirmedIdentity.Series).Append('|')
                .Append(confirmedIdentity.Authority).Append('|')
                .Append(confirmedIdentity.Source).Append('|')
                .Append(confirmedIdentity.Locale).Append('|')
                .Append(confirmedIdentity.UserConfirmed).Append('|')
                .Append(confirmedIdentity.ConfirmedAtUtc.ToString(
                    "O",
                    CultureInfo.InvariantCulture)).Append('|')
                .Append(confirmedIdentity.SourcePermissions).Append('\n');
        }
        foreach (GameKnowledgeSource source in sources)
        {
            value.Append(source.Id).Append('|')
                .Append(source.Kind).Append('|')
                .Append(source.Role).Append('|')
                .Append(source.Title).Append('|')
                .Append(source.PageUri.AbsoluteUri).Append('|')
                .Append(source.RevisionId).Append('|')
                .Append(source.RevisionTimestampUtc.ToString(
                    "O",
                    CultureInfo.InvariantCulture)).Append('|')
                .Append(source.LicenseIdentifier).Append('|')
                .Append(source.LicenseUri.AbsoluteUri).Append('|')
                .Append(source.Attribution).Append('|')
                .Append(source.ContentSha256).Append('\n');
        }
        foreach (GameKnowledgePassage passage in passages)
        {
            value.Append(passage.Id).Append('|')
                .Append(passage.SourceId).Append('|')
                .Append(passage.Section).Append('|')
                .Append(passage.ContentSha256).Append('\n');
        }
        if (formatVersion == SchemaVersion)
        {
            foreach (GameKnowledgeComponentState component in components)
            {
                value.Append(component.Kind).Append('|')
                    .Append(component.Completeness).Append('|')
                    .Append(component.CheckedAtUtc.ToString(
                        "O",
                        CultureInfo.InvariantCulture)).Append('|')
                    .Append(component.RetryAfterUtc?.ToString(
                        "O",
                        CultureInfo.InvariantCulture)).Append('|')
                    .Append(component.RevisionId).Append('|')
                    .Append(component.LicenseIdentifier).Append('|')
                    .Append(component.Attribution).Append('|')
                    .Append(component.ContentSha256).Append('\n');
            }
        }
        return GameKnowledgePassage.ComputeSha256(value.ToString());
    }

    private static GameKnowledgeComponentState[] CreateLegacyComponents(
        DateTimeOffset retrievedAtUtc) =>
    [
        new(
            GameKnowledgeComponentKind.WikipediaPrimary,
            GameKnowledgeComponentCompleteness.Legacy,
            retrievedAtUtc),
    ];
}

public sealed class GameKnowledgeMatch
{
    private readonly ReadOnlyCollection<string> _matchedTerms;
    private readonly ReadOnlyCollection<string> _clipEvidenceIds;

    public GameKnowledgeMatch(
        GameKnowledgePassage passage,
        GameKnowledgeMatchStrength strength,
        double relevance,
        IEnumerable<string> matchedTerms,
        IEnumerable<string>? clipEvidenceIds = null,
        GameKnowledgeTemporalRelation temporalRelation =
            GameKnowledgeTemporalRelation.Unspecified)
    {
        Passage = passage ?? throw new ArgumentNullException(nameof(passage));
        if (!Enum.IsDefined(strength) ||
            !Enum.IsDefined(temporalRelation) ||
            !double.IsFinite(relevance) ||
            relevance is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(relevance));
        }
        ArgumentNullException.ThrowIfNull(matchedTerms);
        string[] terms = matchedTerms
            .Select(static value => value?.Trim() ?? string.Empty)
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        string[] evidenceIds = (clipEvidenceIds ?? [])
            .Select(value => GameKnowledgeSource.StableId(
                value,
                nameof(clipEvidenceIds)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        if (terms.Length == 0 &&
            strength == GameKnowledgeMatchStrength.ClipLinked ||
            strength == GameKnowledgeMatchStrength.ClipLinked &&
            evidenceIds.Length == 0 ||
            temporalRelation ==
                GameKnowledgeTemporalRelation.ImmediatelyPriorContext &&
            strength !=
                GameKnowledgeMatchStrength.CandidateForVisualGrounding)
        {
            throw new ArgumentException(
                "Clip-linked game-knowledge matches require terms and clip evidence.");
        }

        Strength = strength;
        TemporalRelation = temporalRelation;
        Relevance = relevance;
        _matchedTerms = Array.AsReadOnly(terms);
        _clipEvidenceIds = Array.AsReadOnly(evidenceIds);
    }

    public GameKnowledgePassage Passage { get; }

    public GameKnowledgeMatchStrength Strength { get; }

    public GameKnowledgeTemporalRelation TemporalRelation { get; }

    public double Relevance { get; }

    public IReadOnlyList<string> MatchedTerms => _matchedTerms;

    public IReadOnlyList<string> ClipEvidenceIds => _clipEvidenceIds;
}

public sealed record GameKnowledgeWarning
{
    public GameKnowledgeWarning(
        GameKnowledgeWarningCode code,
        string message)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }
        Code = code;
        Message = GameKnowledgeSource.Required(
            message,
            500,
            nameof(message));
    }

    public GameKnowledgeWarningCode Code { get; }

    public string Message { get; }
}

public sealed class ClipGameKnowledgeContext
{
    private readonly ReadOnlyCollection<GameKnowledgeMatch> _matches;
    private readonly ReadOnlyCollection<GameKnowledgeWarning> _warnings;

    public ClipGameKnowledgeContext(
        string gameName,
        GameKnowledgeSnapshot? snapshot,
        IEnumerable<GameKnowledgeMatch>? matches = null,
        IEnumerable<GameKnowledgeWarning>? warnings = null)
    {
        GameName = GameKnowledgeSource.Required(
            gameName,
            120,
            nameof(gameName));
        if (snapshot is not null &&
            !snapshot.GameName.Equals(GameName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Clip game knowledge must belong to the same confirmed game.",
                nameof(snapshot));
        }
        GameKnowledgeMatch[] matchSnapshot = matches?.ToArray() ?? [];
        GameKnowledgeWarning[] warningSnapshot = warnings?.ToArray() ?? [];
        if (matchSnapshot.Any(static value => value is null) ||
            warningSnapshot.Any(static value => value is null) ||
            matchSnapshot.Select(static value => value.Passage.Id)
                .Distinct(StringComparer.Ordinal).Count() != matchSnapshot.Length)
        {
            throw new ArgumentException(
                "Clip game knowledge requires non-null, passage-unique matches and warnings.");
        }
        if (snapshot is null && matchSnapshot.Length > 0)
        {
            throw new ArgumentException(
                "Clip game-knowledge matches require a retained snapshot.",
                nameof(matches));
        }
        if (snapshot is not null)
        {
            var passageIds = snapshot.Passages
                .Select(static value => value.Id)
                .ToHashSet(StringComparer.Ordinal);
            if (matchSnapshot.Any(value => !passageIds.Contains(value.Passage.Id)))
            {
                throw new ArgumentException(
                    "Clip game-knowledge matches must come from the retained snapshot.",
                    nameof(matches));
            }
        }

        Snapshot = snapshot;
        _matches = Array.AsReadOnly(matchSnapshot);
        _warnings = Array.AsReadOnly(warningSnapshot);
    }

    public string GameName { get; }

    public GameKnowledgeSnapshot? Snapshot { get; }

    public IReadOnlyList<GameKnowledgeMatch> Matches => _matches;

    public IReadOnlyList<GameKnowledgeWarning> Warnings => _warnings;

    public bool HasClipLinkedKnowledge => _matches.Any(
        static value => value.Strength == GameKnowledgeMatchStrength.ClipLinked);
}
