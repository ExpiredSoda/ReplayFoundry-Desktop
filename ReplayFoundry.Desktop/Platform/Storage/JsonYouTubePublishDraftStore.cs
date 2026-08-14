using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Publish.YouTube;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonYouTubePublishDraftStore : IYouTubePublishDraftStore
{
    private const string SchemaVersion = "replayfoundry-youtube-publish-drafts-1.3";
    private const string PreviousSchemaVersion =
        "replayfoundry-youtube-publish-drafts-1.2";
    private const string LegacySchemaVersion =
        "replayfoundry-youtube-publish-drafts-1.1";
    private const string InitialSchemaVersion =
        "replayfoundry-youtube-publish-drafts-1.0";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private readonly object _gate = new();
    private readonly string _path;
    private YouTubePublishDraft[] _current;

    public JsonYouTubePublishDraftStore(string? path = null)
    {
        _path = ReplayFoundryLocalDataPaths.Resolve(
            path,
            "youtube-publish-drafts.json");
        _current = Load(_path);
    }

    public IReadOnlyList<YouTubePublishDraft> Current
    {
        get
        {
            lock (_gate) return Array.AsReadOnly(_current.ToArray());
        }
    }

    public void Upsert(YouTubePublishDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        lock (_gate)
        {
            YouTubePublishDraft[] updated =
            [
                draft,
                .. _current.Where(value => !value.AssetId.Equals(
                    draft.AssetId,
                    StringComparison.Ordinal)),
            ];
            Write(updated);
            _current = updated;
        }
    }

    public void Remove(string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        lock (_gate)
        {
            YouTubePublishDraft[] updated = _current
                .Where(value => !value.AssetId.Equals(assetId, StringComparison.Ordinal))
                .ToArray();
            Write(updated);
            _current = updated;
        }
    }

    private static YouTubePublishDraft[] Load(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            Document? document = JsonSerializer.Deserialize<Document>(
                File.ReadAllText(path),
                JsonOptions);
            if (document is null ||
                document.SchemaVersion is not SchemaVersion and
                    not PreviousSchemaVersion and
                    not LegacySchemaVersion and
                    not InitialSchemaVersion ||
                document.Drafts is null)
            {
                throw new InvalidDataException("The local YouTube draft schema is unsupported.");
            }
            YouTubePublishDraft[] drafts = document.Drafts
                .Select(ToDraft)
                .ToArray();
            if (drafts.Select(static value => value.AssetId)
                    .Distinct(StringComparer.Ordinal).Count() != drafts.Length)
            {
                throw new InvalidDataException("Local YouTube draft asset identifiers must be unique.");
            }
            return drafts;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The local YouTube draft file is not valid JSON.", exception);
        }
    }

    private void Write(IReadOnlyList<YouTubePublishDraft> drafts)
    {
        var document = new Document
        {
            SchemaVersion = SchemaVersion,
            Drafts = drafts.Select(static draft => new DraftDocument
            {
                AssetId = draft.AssetId,
                Title = draft.Title,
                Description = draft.Description,
                Tags = draft.Tags,
                Visibility = draft.Visibility.ToString(),
                Timing = draft.Timing.ToString(),
                Audience = draft.Audience.ToString(),
                ContainsSyntheticMedia = draft.ContainsSyntheticMedia,
                NotifySubscribers = draft.NotifySubscribers,
                ScheduledForUtc = draft.ScheduledForUtc,
                PlaylistId = draft.PlaylistId,
                CategoryId = draft.CategoryId,
                ThumbnailFullPath = draft.ThumbnailFullPath,
                SavedAtUtc = draft.SavedAtUtc,
                AudienceAddress = draft.AudienceAddress,
                NamingGuidance = draft.NamingGuidance,
                DescriptionSignature = draft.DescriptionSignature,
                LastCompletedEditorialRerollAttempt =
                    draft.LastCompletedEditorialRerollAttempt,
                PriorAcceptedTitles = draft.PriorAcceptedTitles.ToArray(),
            }).ToArray(),
        };
        AtomicJsonFile.Write(_path, document, JsonOptions);
    }

    private static YouTubePublishDraft ToDraft(DraftDocument value)
    {
        if (!Enum.TryParse(value.Visibility, false, out YouTubeVideoVisibility visibility) ||
            !Enum.TryParse(value.Timing, false, out YouTubePublishTiming timing) ||
            !Enum.TryParse(value.Audience, false, out YouTubeAudience audience))
        {
            throw new InvalidDataException("A local YouTube draft contains an unsupported choice.");
        }
        return new YouTubePublishDraft(
            value.AssetId,
            value.Title,
            value.Description,
            value.Tags,
            visibility,
            timing,
            audience,
            value.ContainsSyntheticMedia,
            value.NotifySubscribers,
            value.ScheduledForUtc,
            value.PlaylistId,
            value.CategoryId,
            value.ThumbnailFullPath,
            value.SavedAtUtc,
            string.IsNullOrWhiteSpace(value.AudienceAddress)
                ? "Chat"
                : value.AudienceAddress,
            value.NamingGuidance,
            value.DescriptionSignature,
            value.LastCompletedEditorialRerollAttempt,
            value.PriorAcceptedTitles);
    }

    private sealed class Document
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public DraftDocument[]? Drafts { get; set; }
    }

    private sealed class DraftDocument
    {
        public string AssetId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Tags { get; set; } = string.Empty;
        public string Visibility { get; set; } = string.Empty;
        public string Timing { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;
        public bool ContainsSyntheticMedia { get; set; }
        public bool NotifySubscribers { get; set; }
        public DateTimeOffset? ScheduledForUtc { get; set; }
        public string? PlaylistId { get; set; }
        public string? CategoryId { get; set; }
        public string? ThumbnailFullPath { get; set; }
        public DateTimeOffset SavedAtUtc { get; set; }
        public string? AudienceAddress { get; set; }
        public string? NamingGuidance { get; set; }
        public string? DescriptionSignature { get; set; }
        public int? LastCompletedEditorialRerollAttempt { get; set; }
        public string[]? PriorAcceptedTitles { get; set; }
    }
}
