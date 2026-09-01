using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;

namespace ReplayFoundry.Desktop.Platform.Storage;

public interface IGameKnowledgeSnapshotStore
{
    GameKnowledgeSnapshot? Find(GameKnowledgeCacheKey key);

    void Remember(GameKnowledgeSnapshot snapshot, string policyVersion);

    void Remove(GameKnowledgeCacheKey key);
}

public sealed class JsonGameKnowledgeSnapshotStore :
    IGameKnowledgeSnapshotStore
{
    private const int MaximumLegacyPassages = 48;
    private const int MaximumLegacySources = 6;
    private static readonly JsonSerializerOptions JsonOptions =
        ReplayFoundryLocalJsonPolicy.IndentedDeclaredPropertyNames;
    private readonly object _gate = new();
    private readonly string _rootDirectory;

    public JsonGameKnowledgeSnapshotStore(string? rootDirectory = null)
    {
        string root = ReplayFoundryLocalDataPaths.Resolve(
            rootDirectory,
            Path.Combine("Cache", "GameKnowledge"));
        if (string.IsNullOrWhiteSpace(root) ||
            !Path.IsPathFullyQualified(root))
        {
            throw new ArgumentException(
                "The game-knowledge cache root must be fully qualified.",
                nameof(rootDirectory));
        }
        _rootDirectory = Path.GetFullPath(root);
    }

    public GameKnowledgeSnapshot? Find(GameKnowledgeCacheKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate)
        {
            string currentPath = PathFor(key.CanonicalValue);
            if (File.Exists(currentPath))
            {
                return Read(currentPath, key, allowLegacy: false);
            }
            string legacyPath = LegacyPathFor(key.Identity.CanonicalTitle);
            return File.Exists(legacyPath)
                ? Read(legacyPath, key, allowLegacy: true)
                : null;
        }
    }

    public void Remember(
        GameKnowledgeSnapshot snapshot,
        string policyVersion)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.IsLegacy || snapshot.ConfirmedIdentity is null)
        {
            throw new InvalidOperationException(
                "Legacy game-knowledge cache data cannot be re-saved as schema 1.2.");
        }
        var key = new GameKnowledgeCacheKey(
            snapshot.ConfirmedIdentity,
            snapshot.Provider,
            GameKnowledgeSnapshot.SchemaVersion,
            policyVersion);
        SnapshotDocument document = CreateDocument(snapshot, key.PolicyVersion);
        lock (_gate)
        {
            AtomicJsonFile.Write(
                PathFor(key.CanonicalValue),
                document,
                JsonOptions);
        }
    }

    public void Remove(GameKnowledgeCacheKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate)
        {
            DeleteIfPresent(PathFor(key.CanonicalValue));
            DeleteIfPresent(LegacyPathFor(key.Identity.CanonicalTitle));
        }
    }

    private GameKnowledgeSnapshot Read(
        string path,
        GameKnowledgeCacheKey key,
        bool allowLegacy)
    {
        try
        {
            SnapshotDocument? document =
                JsonSerializer.Deserialize<SnapshotDocument>(
                    File.ReadAllText(path));
            return Restore(document, key, allowLegacy);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The local game-knowledge snapshot is not valid JSON.",
                exception);
        }
    }

    private static GameKnowledgeSnapshot Restore(
        SnapshotDocument? document,
        GameKnowledgeCacheKey key,
        bool allowLegacy)
    {
        if (document?.Provider is null ||
            document.Sources is null ||
            document.Passages is null ||
            string.IsNullOrWhiteSpace(document.GameName) ||
            string.IsNullOrWhiteSpace(document.RetrievedAtUtc) ||
            string.IsNullOrWhiteSpace(document.SnapshotSha256) ||
            !TryUtc(document.RetrievedAtUtc, out DateTimeOffset retrieved))
        {
            throw InvalidSnapshot();
        }
        try
        {
            GameKnowledgeSource[] sources = RestoreSources(document.Sources);
            GameKnowledgePassage[] passages = RestorePassages(document.Passages);
            var provider = new GameKnowledgeProviderIdentity(
                document.Provider.Name,
                document.Provider.Version);
            if (document.SchemaVersion == GameKnowledgeSnapshot.LegacySchemaVersion)
            {
                return allowLegacy
                    ? RestoreLegacy(
                        document,
                        key,
                        provider,
                        retrieved,
                        sources,
                        passages)
                    : throw InvalidSnapshot();
            }
            if (document.SchemaVersion != GameKnowledgeSnapshot.SchemaVersion ||
                document.ConfirmedIdentity is null ||
                document.Components is null ||
                !string.Equals(
                    document.PolicyVersion,
                    key.PolicyVersion,
                    StringComparison.Ordinal) ||
                !provider.Name.Equals(key.Provider.Name, StringComparison.Ordinal) ||
                !provider.Version.Equals(key.Provider.Version, StringComparison.Ordinal))
            {
                throw InvalidSnapshot();
            }
            ConfirmedGameIdentity identity = RestoreIdentity(
                document.ConfirmedIdentity);
            if (!identity.WikidataEntityId.Equals(
                    key.Identity.WikidataEntityId,
                    StringComparison.Ordinal) ||
                !identity.Locale.Equals(
                    key.Identity.Locale,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw InvalidSnapshot();
            }
            GameKnowledgeComponentState[] components =
                RestoreComponents(document.Components);
            var snapshot = new GameKnowledgeSnapshot(
                identity,
                provider,
                retrieved,
                sources,
                passages,
                components);
            VerifyHash(snapshot.SnapshotSha256, document.SnapshotSha256);
            return snapshot;
        }
        catch (Exception exception)
            when (exception is ArgumentException or FormatException or
                OverflowException)
        {
            throw new InvalidDataException(
                "The local game-knowledge snapshot contents are invalid.",
                exception);
        }
    }

    private static GameKnowledgeSnapshot RestoreLegacy(
        SnapshotDocument document,
        GameKnowledgeCacheKey key,
        GameKnowledgeProviderIdentity provider,
        DateTimeOffset retrieved,
        GameKnowledgeSource[] sources,
        GameKnowledgePassage[] passages)
    {
        if (!document.GameName.Equals(
                key.Identity.CanonicalTitle,
                StringComparison.OrdinalIgnoreCase) ||
            sources.Length is < 1 or > 8 ||
            passages.Length is < 1 or > 120)
        {
            throw InvalidSnapshot();
        }
        string legacyHash = GameKnowledgeSnapshot.ComputeLegacySnapshotSha256(
            document.GameName,
            provider,
            retrieved,
            sources,
            passages);
        VerifyHash(legacyHash, document.SnapshotSha256);

        GameKnowledgeSource[] boundedSources = sources
            .Take(MaximumLegacySources)
            .ToArray();
        var sourceIds = boundedSources
            .Select(static value => value.Id)
            .ToHashSet(StringComparer.Ordinal);
        GameKnowledgePassage[] boundedPassages = passages
            .Where(value => sourceIds.Contains(value.SourceId))
            .Take(MaximumLegacyPassages)
            .ToArray();
        return GameKnowledgeSnapshot.CreateLegacy(
            document.GameName,
            provider,
            retrieved,
            boundedSources,
            boundedPassages);
    }

    private static SnapshotDocument CreateDocument(
        GameKnowledgeSnapshot snapshot,
        string policyVersion) =>
        new(
            snapshot.FormatVersion,
            snapshot.GameName,
            CreateIdentityDocument(snapshot.ConfirmedIdentity!),
            new ProviderDocument(
                snapshot.Provider.Name,
                snapshot.Provider.Version),
            policyVersion,
            snapshot.RetrievedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            snapshot.Sources.Select(CreateSourceDocument).ToArray(),
            snapshot.Passages.Select(static passage => new PassageDocument(
                passage.Id,
                passage.SourceId,
                passage.Section,
                passage.Text,
                passage.ContentSha256)).ToArray(),
            snapshot.Components.Select(static component => new ComponentDocument(
                component.Kind.ToString(),
                component.Completeness.ToString(),
                component.CheckedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                component.RetryAfterUtc?.ToString(
                    "O",
                    CultureInfo.InvariantCulture),
                component.RevisionId,
                component.LicenseIdentifier,
                component.Attribution,
                component.ContentSha256)).ToArray(),
            snapshot.SnapshotSha256);

    private static IdentityDocument CreateIdentityDocument(
        ConfirmedGameIdentity identity) =>
        new(
            identity.WikidataEntityId,
            identity.CanonicalTitle,
            identity.Edition,
            identity.ReleaseYear,
            identity.Developer,
            identity.Series,
            identity.Authority.ToString(),
            identity.Source,
            identity.Locale,
            identity.UserConfirmed,
            identity.ConfirmedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            identity.SourcePermissions.ToString());

    private static SourceDocument CreateSourceDocument(
        GameKnowledgeSource source) =>
        new(
            source.Id,
            source.Kind.ToString(),
            source.Role.ToString(),
            source.Title,
            source.PageUri.AbsoluteUri,
            source.RevisionId,
            source.RevisionTimestampUtc.ToString("O", CultureInfo.InvariantCulture),
            source.LicenseIdentifier,
            source.LicenseUri.AbsoluteUri,
            source.Attribution,
            source.ContentSha256);

    private static ConfirmedGameIdentity RestoreIdentity(
        IdentityDocument identity) =>
        new(
            identity.WikidataEntityId,
            identity.CanonicalTitle,
            identity.Edition,
            identity.ReleaseYear,
            identity.Developer,
            identity.Series,
            Enum.Parse<GameIdentityAuthority>(identity.Authority),
            identity.Source,
            identity.Locale,
            identity.UserConfirmed,
            ParseUtc(identity.ConfirmedAtUtc),
            Enum.Parse<GameKnowledgeSourcePermission>(identity.SourcePermissions));

    private static GameKnowledgeSource[] RestoreSources(
        SourceDocument[] sources) =>
        sources.Select(static source => new GameKnowledgeSource(
            source.Id,
            Enum.Parse<GameKnowledgeSourceKind>(source.Kind),
            source.Title,
            new Uri(source.PageUri, UriKind.Absolute),
            source.RevisionId,
            ParseUtc(source.RevisionTimestampUtc),
            source.LicenseIdentifier,
            new Uri(source.LicenseUri, UriKind.Absolute),
            source.Attribution,
            source.ContentSha256,
            Enum.Parse<GameKnowledgeSourceRole>(source.Role))).ToArray();

    private static GameKnowledgePassage[] RestorePassages(
        PassageDocument[] passages) =>
        passages.Select(static passage => new GameKnowledgePassage(
            passage.Id,
            passage.SourceId,
            passage.Section,
            passage.Text,
            passage.ContentSha256)).ToArray();

    private static GameKnowledgeComponentState[] RestoreComponents(
        ComponentDocument[] components) =>
        components.Select(static component => new GameKnowledgeComponentState(
            Enum.Parse<GameKnowledgeComponentKind>(component.Kind),
            Enum.Parse<GameKnowledgeComponentCompleteness>(
                component.Completeness),
            ParseUtc(component.CheckedAtUtc),
            string.IsNullOrWhiteSpace(component.RetryAfterUtc)
                ? null
                : ParseUtc(component.RetryAfterUtc),
            component.RevisionId,
            component.LicenseIdentifier,
            component.Attribution,
            component.ContentSha256)).ToArray();

    private static DateTimeOffset ParseUtc(string value) =>
        TryUtc(value, out DateTimeOffset parsed)
            ? parsed
            : throw new FormatException("Expected an exact UTC timestamp.");

    private static bool TryUtc(string value, out DateTimeOffset parsed) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out parsed) && parsed.Offset == TimeSpan.Zero;

    private static void VerifyHash(string actual, string expected)
    {
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The local game-knowledge snapshot hash is invalid.");
        }
    }

    private static InvalidDataException InvalidSnapshot() =>
        new("The local game-knowledge snapshot is invalid.");

    private string PathFor(string canonicalKey) =>
        Path.Combine(_rootDirectory, Hash(canonicalKey) + ".json");

    private string LegacyPathFor(string gameName) =>
        Path.Combine(_rootDirectory, Hash(gameName.Trim().ToUpperInvariant()) + ".json");

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record SnapshotDocument(
        string SchemaVersion,
        string GameName,
        IdentityDocument? ConfirmedIdentity,
        ProviderDocument Provider,
        string? PolicyVersion,
        string RetrievedAtUtc,
        SourceDocument[] Sources,
        PassageDocument[] Passages,
        ComponentDocument[]? Components,
        string SnapshotSha256);

    private sealed record IdentityDocument(
        string WikidataEntityId,
        string CanonicalTitle,
        string? Edition,
        int? ReleaseYear,
        string? Developer,
        string? Series,
        string Authority,
        string Source,
        string Locale,
        bool UserConfirmed,
        string ConfirmedAtUtc,
        string SourcePermissions);

    private sealed record ProviderDocument(string Name, string Version);

    private sealed record SourceDocument(
        string Id,
        string Kind,
        string Role,
        string Title,
        string PageUri,
        string RevisionId,
        string RevisionTimestampUtc,
        string LicenseIdentifier,
        string LicenseUri,
        string Attribution,
        string ContentSha256);

    private sealed record PassageDocument(
        string Id,
        string SourceId,
        string Section,
        string Text,
        string ContentSha256);

    private sealed record ComponentDocument(
        string Kind,
        string Completeness,
        string CheckedAtUtc,
        string? RetryAfterUtc,
        string? RevisionId,
        string? LicenseIdentifier,
        string? Attribution,
        string? ContentSha256);
}
