using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Features.Publish.YouTube;

public enum YouTubeRemoteVideoStatus
{
    NotChecked,
    Exists,
    NotFoundOrInaccessible,
}

public sealed class YouTubePublishHistoryEntry
{
    public YouTubePublishHistoryEntry(
        string id,
        string assetId,
        string title,
        string? videoId,
        string? videoUrl,
        YouTubePublishOutcome outcome,
        YouTubeVideoVisibility visibility,
        DateTimeOffset attemptedAtUtc,
        DateTimeOffset? scheduledForUtc,
        string? failureCode = null,
        string? failureMessage = null,
        YouTubeRemoteVideoStatus remoteStatus =
            YouTubeRemoteVideoStatus.NotChecked,
        DateTimeOffset? remoteCheckedAtUtc = null)
    {
        bool requiresFailure = outcome is
            YouTubePublishOutcome.Failed or
            YouTubePublishOutcome.Cancelled;
        if (string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(assetId) ||
            string.IsNullOrWhiteSpace(title) ||
            !Enum.IsDefined(outcome) ||
            !Enum.IsDefined(visibility) ||
            !Enum.IsDefined(remoteStatus) ||
            attemptedAtUtc.Offset != TimeSpan.Zero ||
            scheduledForUtc.HasValue &&
            scheduledForUtc.Value.Offset != TimeSpan.Zero ||
            requiresFailure == string.IsNullOrWhiteSpace(failureCode) ||
            remoteCheckedAtUtc.HasValue !=
                (remoteStatus != YouTubeRemoteVideoStatus.NotChecked) ||
            remoteCheckedAtUtc.HasValue &&
            remoteCheckedAtUtc.Value.Offset != TimeSpan.Zero ||
            remoteStatus != YouTubeRemoteVideoStatus.NotChecked &&
            string.IsNullOrWhiteSpace(videoId))
        {
            throw new ArgumentException(
                "The YouTube history entry is inconsistent.");
        }
        if (videoUrl is not null &&
            (!Uri.TryCreate(videoUrl, UriKind.Absolute, out Uri? uri) ||
             uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "A YouTube history URL must use HTTPS.",
                nameof(videoUrl));
        }

        Id = id.Trim();
        AssetId = assetId.Trim();
        Title = title.Trim();
        VideoId = string.IsNullOrWhiteSpace(videoId) ? null : videoId.Trim();
        VideoUrl = videoUrl;
        Outcome = outcome;
        Visibility = visibility;
        AttemptedAtUtc = attemptedAtUtc;
        ScheduledForUtc = scheduledForUtc;
        FailureCode = failureCode?.Trim();
        FailureMessage = failureMessage?.Trim();
        RemoteStatus = remoteStatus;
        RemoteCheckedAtUtc = remoteCheckedAtUtc;
    }

    public string Id { get; }
    public string AssetId { get; }
    public string Title { get; }
    public string? VideoId { get; }
    public string? VideoUrl { get; }
    public YouTubePublishOutcome Outcome { get; }
    public YouTubeVideoVisibility Visibility { get; }
    public DateTimeOffset AttemptedAtUtc { get; }
    public DateTimeOffset? ScheduledForUtc { get; }
    public string? FailureCode { get; }
    public string? FailureMessage { get; }
    public YouTubeRemoteVideoStatus RemoteStatus { get; }
    public DateTimeOffset? RemoteCheckedAtUtc { get; }

    public YouTubePublishHistoryEntry WithRemoteStatus(
        YouTubeRemoteVideoStatus status,
        DateTimeOffset checkedAtUtc) => new(
            Id,
            AssetId,
            Title,
            VideoId,
            VideoUrl,
            Outcome,
            Visibility,
            AttemptedAtUtc,
            ScheduledForUtc,
            FailureCode,
            FailureMessage,
            status,
            checkedAtUtc);
}

public interface IYouTubePublishHistoryStore
{
    IReadOnlyList<YouTubePublishHistoryEntry> Current { get; }
    void Append(YouTubePublishHistoryEntry entry);
    void Replace(IReadOnlyList<YouTubePublishHistoryEntry> entries);
    void Clear();
}

public sealed class InMemoryYouTubePublishHistoryStore :
    IYouTubePublishHistoryStore
{
    private readonly List<YouTubePublishHistoryEntry> _entries = [];

    public IReadOnlyList<YouTubePublishHistoryEntry> Current =>
        new ReadOnlyCollection<YouTubePublishHistoryEntry>(_entries.ToArray());

    public void Append(YouTubePublishHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_entries.Any(value =>
                value.Id.Equals(entry.Id, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "YouTube history identifiers must be unique.",
                nameof(entry));
        }
        _entries.Insert(0, entry);
    }

    public void Clear() => _entries.Clear();

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
        _entries.Clear();
        _entries.AddRange(snapshot);
    }
}
