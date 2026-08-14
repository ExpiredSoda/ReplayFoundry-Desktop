using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;

namespace ReplayFoundry.Desktop.Platform.Storage;

public interface IGameKnowledgeSnapshotStore
{
    GameKnowledgeSnapshot? Find(string confirmedGameName);

    void Remember(GameKnowledgeSnapshot snapshot);
}

public sealed class JsonGameKnowledgeSnapshotStore :
    IGameKnowledgeSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };
    private readonly object _gate = new();
    private readonly string _rootDirectory;

    public JsonGameKnowledgeSnapshotStore(string? rootDirectory = null)
    {
        string root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "ReplayFoundry",
            "Cache",
            "GameKnowledge");
        if (string.IsNullOrWhiteSpace(root) ||
            !Path.IsPathFullyQualified(root))
        {
            throw new ArgumentException(
                "The game-knowledge cache root must be fully qualified.",
                nameof(rootDirectory));
        }
        _rootDirectory = Path.GetFullPath(root);
    }

    public GameKnowledgeSnapshot? Find(string confirmedGameName)
    {
        string gameName = GameKnowledgeSource.Required(
            confirmedGameName,
            120,
            nameof(confirmedGameName));
        string path = PathFor(gameName);
        lock (_gate)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                SnapshotDocument? document =
                    JsonSerializer.Deserialize<SnapshotDocument>(
                        File.ReadAllText(path));
                return Restore(document, gameName);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "The local game-knowledge snapshot is not valid JSON.",
                    exception);
            }
        }
    }

    public void Remember(GameKnowledgeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var document = new SnapshotDocument(
            GameKnowledgeSnapshot.SchemaVersion,
            snapshot.GameName,
            new ProviderDocument(
                snapshot.Provider.Name,
                snapshot.Provider.Version),
            snapshot.RetrievedAtUtc.ToString(
                "O",
                CultureInfo.InvariantCulture),
            snapshot.Sources.Select(source => new SourceDocument(
                source.Id,
                source.Kind.ToString(),
                source.Role.ToString(),
                source.Title,
                source.PageUri.AbsoluteUri,
                source.RevisionId,
                source.RevisionTimestampUtc.ToString(
                    "O",
                    CultureInfo.InvariantCulture),
                source.LicenseIdentifier,
                source.LicenseUri.AbsoluteUri,
                source.Attribution,
                source.ContentSha256)).ToArray(),
            snapshot.Passages.Select(passage => new PassageDocument(
                passage.Id,
                passage.SourceId,
                passage.Section,
                passage.Text,
                passage.ContentSha256)).ToArray(),
            snapshot.SnapshotSha256);
        lock (_gate)
        {
            AtomicJsonFile.Write(
                PathFor(snapshot.GameName),
                document,
                JsonOptions);
        }
    }

    private static GameKnowledgeSnapshot Restore(
        SnapshotDocument? document,
        string expectedGameName)
    {
        if (document is null ||
            document.SchemaVersion != GameKnowledgeSnapshot.SchemaVersion ||
            !document.GameName.Equals(
                expectedGameName,
                StringComparison.OrdinalIgnoreCase) ||
            document.Provider is null ||
            document.Sources is null ||
            document.Passages is null ||
            !DateTimeOffset.TryParse(
                document.RetrievedAtUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset retrieved) ||
            retrieved.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException(
                "The local game-knowledge snapshot is invalid.");
        }
        try
        {
            var snapshot = new GameKnowledgeSnapshot(
                document.GameName,
                new GameKnowledgeProviderIdentity(
                    document.Provider.Name,
                    document.Provider.Version),
                retrieved,
                document.Sources.Select(source =>
                    new GameKnowledgeSource(
                        source.Id,
                        Enum.Parse<GameKnowledgeSourceKind>(
                            source.Kind,
                            ignoreCase: false),
                        source.Title,
                        new Uri(source.PageUri, UriKind.Absolute),
                        source.RevisionId,
                        DateTimeOffset.Parse(
                            source.RevisionTimestampUtc,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind),
                        source.LicenseIdentifier,
                        new Uri(source.LicenseUri, UriKind.Absolute),
                        source.Attribution,
                        source.ContentSha256,
                        Enum.Parse<GameKnowledgeSourceRole>(
                            source.Role,
                            ignoreCase: false))),
                document.Passages.Select(passage =>
                    new GameKnowledgePassage(
                        passage.Id,
                        passage.SourceId,
                        passage.Section,
                        passage.Text,
                        passage.ContentSha256)));
            if (!snapshot.SnapshotSha256.Equals(
                    document.SnapshotSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The local game-knowledge snapshot hash is invalid.");
            }
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

    private string PathFor(string gameName)
    {
        string normalized = gameName.Trim().ToUpperInvariant();
        string hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return Path.Combine(_rootDirectory, hash + ".json");
    }

    private sealed record SnapshotDocument(
        string SchemaVersion,
        string GameName,
        ProviderDocument Provider,
        string RetrievedAtUtc,
        SourceDocument[] Sources,
        PassageDocument[] Passages,
        string SnapshotSha256);

    private sealed record ProviderDocument(
        string Name,
        string Version);

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
}
