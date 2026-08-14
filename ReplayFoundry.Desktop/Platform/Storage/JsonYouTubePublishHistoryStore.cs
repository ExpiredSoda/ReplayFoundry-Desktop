using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Publish.YouTube;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonYouTubePublishHistoryStore :
    IYouTubePublishHistoryStore
{
    private const string SchemaVersion =
        "replayfoundry-youtube-publish-history-1.1";
    private const string PreviousSchemaVersion =
        "replayfoundry-youtube-publish-history-1.0";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly string _path;
    private List<YouTubePublishHistoryEntry> _entries;

    public JsonYouTubePublishHistoryStore(string? path = null)
    {
        _path = ReplayFoundryLocalDataPaths.Resolve(
            path,
            "youtube-publish-history.json");
        _entries = Load(_path);
    }

    public IReadOnlyList<YouTubePublishHistoryEntry> Current
    {
        get
        {
            lock (_gate)
            {
                return Array.AsReadOnly(_entries.ToArray());
            }
        }
    }

    public void Append(YouTubePublishHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            if (_entries.Any(value =>
                    value.Id.Equals(entry.Id, StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    "YouTube history identifiers must be unique.",
                    nameof(entry));
            }
            var updated = new List<YouTubePublishHistoryEntry>(_entries.Count + 1)
            {
                entry,
            };
            updated.AddRange(_entries);
            Write(updated);
            _entries = updated;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
            _entries = [];
        }
    }

    public void Replace(IReadOnlyList<YouTubePublishHistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        YouTubePublishHistoryEntry[] snapshot = entries.ToArray();
        if (snapshot.Select(static value => value.Id)
                .Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
        {
            throw new ArgumentException(
                "YouTube history identifiers must be unique.",
                nameof(entries));
        }
        lock (_gate)
        {
            Write(snapshot);
            _entries = snapshot.ToList();
        }
    }

    private static List<YouTubePublishHistoryEntry> Load(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }
        try
        {
            HistoryDocument? document = JsonSerializer.Deserialize<HistoryDocument>(
                File.ReadAllText(path),
                JsonOptions);
            if (document is null ||
                document.SchemaVersion != SchemaVersion &&
                document.SchemaVersion != PreviousSchemaVersion ||
                document.Entries is null)
            {
                throw new InvalidDataException(
                    "The local YouTube publish-history schema is unsupported.");
            }
            var result = new List<YouTubePublishHistoryEntry>();
            foreach (HistoryEntryDocument entry in document.Entries)
            {
                if (!Enum.TryParse(
                        entry.Outcome,
                        ignoreCase: false,
                        out YouTubePublishOutcome outcome) ||
                    !Enum.TryParse(
                        entry.Visibility,
                        ignoreCase: false,
                        out YouTubeVideoVisibility visibility))
                {
                    throw new InvalidDataException(
                        "A local YouTube publish-history entry contains an unknown value.");
                }
                result.Add(new YouTubePublishHistoryEntry(
                    entry.Id,
                    entry.AssetId,
                    entry.Title,
                    entry.VideoId,
                    entry.VideoUrl,
                    outcome,
                    visibility,
                    entry.AttemptedAtUtc,
                    entry.ScheduledForUtc,
                    entry.FailureCode,
                    entry.FailureMessage,
                    string.IsNullOrWhiteSpace(entry.RemoteStatus)
                        ? YouTubeRemoteVideoStatus.NotChecked
                        : Enum.Parse<YouTubeRemoteVideoStatus>(entry.RemoteStatus),
                    entry.RemoteCheckedAtUtc));
            }
            if (result.Select(static value => value.Id)
                    .Distinct(StringComparer.Ordinal).Count() != result.Count)
            {
                throw new InvalidDataException(
                    "The local YouTube publish-history identifiers are not unique.");
            }
            return result;
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException)
        {
            throw new InvalidDataException(
                "The local YouTube publish-history file is invalid.",
                exception);
        }
    }

    private void Write(IReadOnlyList<YouTubePublishHistoryEntry> entries)
    {
        var document = new HistoryDocument
        {
            SchemaVersion = SchemaVersion,
            Entries = entries.Select(static entry =>
                new HistoryEntryDocument
                {
                    Id = entry.Id,
                    AssetId = entry.AssetId,
                    Title = entry.Title,
                    VideoId = entry.VideoId,
                    VideoUrl = entry.VideoUrl,
                    Outcome = entry.Outcome.ToString(),
                    Visibility = entry.Visibility.ToString(),
                    AttemptedAtUtc = entry.AttemptedAtUtc,
                    ScheduledForUtc = entry.ScheduledForUtc,
                    FailureCode = entry.FailureCode,
                    FailureMessage = entry.FailureMessage,
                    RemoteStatus = entry.RemoteStatus.ToString(),
                    RemoteCheckedAtUtc = entry.RemoteCheckedAtUtc,
                }).ToArray(),
        };
        AtomicJsonFile.Write(_path, document, JsonOptions);
    }

    private sealed class HistoryDocument
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public HistoryEntryDocument[]? Entries { get; set; }
    }

    private sealed class HistoryEntryDocument
    {
        public string Id { get; set; } = string.Empty;
        public string AssetId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? VideoId { get; set; }
        public string? VideoUrl { get; set; }
        public string Outcome { get; set; } = string.Empty;
        public string Visibility { get; set; } = string.Empty;
        public DateTimeOffset AttemptedAtUtc { get; set; }
        public DateTimeOffset? ScheduledForUtc { get; set; }
        public string? FailureCode { get; set; }
        public string? FailureMessage { get; set; }
        public string? RemoteStatus { get; set; }
        public DateTimeOffset? RemoteCheckedAtUtc { get; set; }
    }
}
