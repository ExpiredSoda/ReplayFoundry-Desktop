using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Library;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonLibraryCatalogStore : ILibraryCatalogStore
{
    private const string SchemaVersion = "replayfoundry-library-catalog-1.1";
    private const string LegacySchemaVersion =
        "replayfoundry-library-catalog-1.0";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private readonly object _gate = new();
    private readonly string _path;
    private LibraryMediaAsset[] _current;

    public JsonLibraryCatalogStore(string? path = null)
    {
        _path = ReplayFoundryLocalDataPaths.Resolve(
            path,
            "library-catalog.json");
        _current = Load(_path);
    }

    public IReadOnlyList<LibraryMediaAsset> Current
    {
        get { lock (_gate) return Array.AsReadOnly(_current.ToArray()); }
    }

    public void Replace(IReadOnlyList<LibraryMediaAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        LibraryMediaAsset[] snapshot = assets.ToArray();
        lock (_gate)
        {
            Write(snapshot);
            _current = snapshot;
        }
    }

    private static LibraryMediaAsset[] Load(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            Document? document = JsonSerializer.Deserialize<Document>(
                File.ReadAllText(path), JsonOptions);
            if (document?.SchemaVersion is not (SchemaVersion or LegacySchemaVersion) ||
                document.Assets is null)
            {
                throw new InvalidDataException("The Library catalog schema is unsupported.");
            }
            return document.Assets.Select(static item => new LibraryMediaAsset(
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
                    : null)).ToArray();
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
                Description = value.Description,
                Tags = value.Tags.ToArray(),
                AddedAtUtc = value.AddedAtUtc,
                ContributingCandidateCount = value.ContributingCandidateCount,
                SourceCandidateIds = value.SourceCandidateIds.ToArray(),
            }).ToArray(),
        };
        AtomicJsonFile.Write(_path, document, JsonOptions);
    }

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
        public string Description { get; set; } = string.Empty;
        public string[]? Tags { get; set; }
        public DateTimeOffset AddedAtUtc { get; set; }
        public int ContributingCandidateCount { get; set; }
        public string[]? SourceCandidateIds { get; set; }
    }
}
