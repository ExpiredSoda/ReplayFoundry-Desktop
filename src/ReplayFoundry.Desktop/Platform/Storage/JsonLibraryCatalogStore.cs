using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Publish.YouTube;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonLibraryCatalogStore : ILibraryCatalogStore
{
    private const string SchemaVersion = "replayfoundry-library-catalog-1.2";
    private const string PreviousSchemaVersion =
        "replayfoundry-library-catalog-1.1";
    private const string LegacySchemaVersion =
        "replayfoundry-library-catalog-1.0";
    private static readonly JsonSerializerOptions JsonOptions =
        ReplayFoundryLocalJsonPolicy.IndentedCamelCase;
    private readonly object _gate = new();
    private readonly string _path;
    private LibraryMediaAsset[] _current;
    private IReadOnlyList<LibraryMediaAsset> _snapshot;

    public JsonLibraryCatalogStore(string? path = null)
    {
        _path = ReplayFoundryLocalDataPaths.Resolve(
            path,
            "library-catalog.json");
        LibraryCatalogLoadResult loaded = Load(_path);
        _current = loaded.Assets;
        _snapshot = Array.AsReadOnly(_current);
        if (loaded.RequiresMigration)
        {
            Write(_current);
        }
    }

    public IReadOnlyList<LibraryMediaAsset> Current
    {
        get { lock (_gate) return _snapshot; }
    }

    public void Replace(IReadOnlyList<LibraryMediaAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        LibraryMediaAsset[] snapshot = assets.ToArray();
        lock (_gate)
        {
            Write(snapshot);
            _current = snapshot;
            _snapshot = Array.AsReadOnly(snapshot);
        }
    }

    private static LibraryCatalogLoadResult Load(string path)
    {
        if (!File.Exists(path)) return new([], RequiresMigration: false);
        try
        {
            Document? document = JsonSerializer.Deserialize<Document>(
                File.ReadAllText(path), JsonOptions);
            if (document?.SchemaVersion is not (
                    SchemaVersion or
                    PreviousSchemaVersion or
                    LegacySchemaVersion) ||
                document.Assets is null)
            {
                throw new InvalidDataException("The Library catalog schema is unsupported.");
            }
            bool requiresMigration = !document.SchemaVersion.Equals(
                SchemaVersion,
                StringComparison.Ordinal);
            if (!requiresMigration && document.Assets.Any(static item =>
                    string.IsNullOrWhiteSpace(item.LibraryLabel)))
            {
                throw new InvalidDataException(
                    "The current Library catalog requires a durable Library label for every asset.");
            }

            LibraryMediaAsset[] assets = document.Assets.Select(item =>
                new LibraryMediaAsset(
                item.Id,
                item.ProjectId,
                Enum.Parse<GenerationMode>(item.Mode),
                item.Rank,
                item.OutputFullPath,
                item.ThumbnailFullPath,
                TimeSpan.FromTicks(item.DurationTicks),
                item.OutputWidth,
                item.OutputHeight,
                item.Title,
                item.Description,
                item.Tags ?? [],
                item.AddedAtUtc,
                item.ContributingCandidateCount,
                item.SourceCandidateIds is { Length: > 0 }
                    ? item.SourceCandidateIds
                    : null,
                requiresMigration
                    ? DeriveLegacyLibraryLabel(item)
                    : item.LibraryLabel,
                item.SourceProvenance)).ToArray();
            return new(assets, requiresMigration);
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException)
        {
            throw new InvalidDataException("The Library catalog is invalid.", exception);
        }
    }

    private void Write(LibraryMediaAsset[] assets)
    {
        var document = new Document
        {
            SchemaVersion = SchemaVersion,
            Assets = assets.Select(static value => new Asset
            {
                Id = value.Id,
                ProjectId = value.ProjectId,
                Mode = value.Mode.ToString(),
                Rank = value.Rank,
                OutputFullPath = value.OutputFullPath,
                ThumbnailFullPath = value.ThumbnailFullPath,
                DurationTicks = value.Duration.Ticks,
                OutputWidth = value.OutputWidth,
                OutputHeight = value.OutputHeight,
                Title = value.Title,
                LibraryLabel = value.LibraryLabel,
                Description = value.Description,
                Tags = value.Tags.ToArray(),
                AddedAtUtc = value.AddedAtUtc,
                ContributingCandidateCount = value.ContributingCandidateCount,
                SourceCandidateIds = value.SourceCandidateIds.ToArray(),
                SourceProvenance = value.SourceProvenance,
            }).ToArray(),
        };
        AtomicJsonFile.Write(_path, document, JsonOptions);
    }

    private static string DeriveLegacyLibraryLabel(Asset asset) =>
        string.IsNullOrWhiteSpace(asset.LibraryLabel)
            ? asset.Title
            : asset.LibraryLabel.Trim();

    private sealed record LibraryCatalogLoadResult(
        LibraryMediaAsset[] Assets,
        bool RequiresMigration);

    private sealed class Document
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public Asset[]? Assets { get; set; }
    }
    private sealed class Asset
    {
        public string Id { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public int Rank { get; set; }
        public string OutputFullPath { get; set; } = string.Empty;
        public string? ThumbnailFullPath { get; set; }
        public long DurationTicks { get; set; }
        public int OutputWidth { get; set; }
        public int OutputHeight { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? LibraryLabel { get; set; }
        public string Description { get; set; } = string.Empty;
        public string[]? Tags { get; set; }
        public DateTimeOffset AddedAtUtc { get; set; }
        public int ContributingCandidateCount { get; set; }
        public string[]? SourceCandidateIds { get; set; }
        public YouTubePublishProvenance? SourceProvenance { get; set; }
    }
}
