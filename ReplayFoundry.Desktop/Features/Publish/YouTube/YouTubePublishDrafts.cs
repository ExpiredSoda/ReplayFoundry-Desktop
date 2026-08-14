using System.Collections.ObjectModel;
using System.IO;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.Desktop.Features.Publish.YouTube;

public sealed class YouTubePublishDraft
{
    public YouTubePublishDraft(
        string assetId,
        string title,
        string description,
        string tags,
        YouTubeVideoVisibility visibility,
        YouTubePublishTiming timing,
        YouTubeAudience audience,
        bool containsSyntheticMedia,
        bool notifySubscribers,
        DateTimeOffset? scheduledForUtc,
        string? playlistId,
        string? categoryId,
        string? thumbnailFullPath,
        DateTimeOffset savedAtUtc,
        string audienceAddress = "Chat",
        string? namingGuidance = null,
        string? descriptionSignature = null,
        int? lastCompletedEditorialRerollAttempt = null,
        IEnumerable<string>? priorAcceptedTitles = null)
    {
        if (string.IsNullOrWhiteSpace(assetId) ||
            string.IsNullOrWhiteSpace(title) ||
            title.Length > 100 ||
            description?.Length > 5000 ||
            tags?.Length > 500 ||
            !Enum.IsDefined(visibility) ||
            !Enum.IsDefined(timing) ||
            !Enum.IsDefined(audience) ||
            savedAtUtc.Offset != TimeSpan.Zero ||
            scheduledForUtc.HasValue && scheduledForUtc.Value.Offset != TimeSpan.Zero ||
            timing == YouTubePublishTiming.Schedule != scheduledForUtc.HasValue ||
            lastCompletedEditorialRerollAttempt is < 0)
        {
            throw new ArgumentException("The local YouTube draft is invalid.");
        }
        AssetId = assetId.Trim();
        Title = title.Trim();
        Description = description?.Trim() ?? string.Empty;
        Tags = tags?.Trim() ?? string.Empty;
        Visibility = visibility;
        Timing = timing;
        Audience = audience;
        ContainsSyntheticMedia = containsSyntheticMedia;
        NotifySubscribers = notifySubscribers;
        ScheduledForUtc = scheduledForUtc;
        PlaylistId = string.IsNullOrWhiteSpace(playlistId) ? null : playlistId.Trim();
        CategoryId = string.IsNullOrWhiteSpace(categoryId) ? null : categoryId.Trim();
        ThumbnailFullPath = string.IsNullOrWhiteSpace(thumbnailFullPath)
            ? null
            : Path.GetFullPath(thumbnailFullPath);
        SavedAtUtc = savedAtUtc;
        var editorialProfile = new ClipEditorialProfile(
            audienceAddress,
            namingGuidance,
            descriptionSignature);
        AudienceAddress = editorialProfile.AudienceAddress;
        NamingGuidance = editorialProfile.NamingGuidance ?? string.Empty;
        DescriptionSignature =
            editorialProfile.ReusableDescriptionSignature ?? string.Empty;
        LastCompletedEditorialRerollAttempt =
            lastCompletedEditorialRerollAttempt;
        PriorAcceptedTitles =
            ClipEditorialPriorTitleExclusion.MergeTitleHistory(
                priorAcceptedTitles);
    }

    public string AssetId { get; }
    public string Title { get; }
    public string Description { get; }
    public string Tags { get; }
    public YouTubeVideoVisibility Visibility { get; }
    public YouTubePublishTiming Timing { get; }
    public YouTubeAudience Audience { get; }
    public bool ContainsSyntheticMedia { get; }
    public bool NotifySubscribers { get; }
    public DateTimeOffset? ScheduledForUtc { get; }
    public string? PlaylistId { get; }
    public string? CategoryId { get; }
    public string? ThumbnailFullPath { get; }
    public DateTimeOffset SavedAtUtc { get; }
    public string AudienceAddress { get; }
    public string NamingGuidance { get; }
    public string DescriptionSignature { get; }
    public int? LastCompletedEditorialRerollAttempt { get; }
    public IReadOnlyList<string> PriorAcceptedTitles { get; }
}

public interface IYouTubePublishDraftStore
{
    IReadOnlyList<YouTubePublishDraft> Current { get; }
    void Upsert(YouTubePublishDraft draft);
    void Remove(string assetId);
}

public sealed class InMemoryYouTubePublishDraftStore : IYouTubePublishDraftStore
{
    private readonly List<YouTubePublishDraft> _drafts = [];
    public IReadOnlyList<YouTubePublishDraft> Current =>
        new ReadOnlyCollection<YouTubePublishDraft>(_drafts.ToArray());

    public void Upsert(YouTubePublishDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        _drafts.RemoveAll(value => value.AssetId.Equals(
            draft.AssetId,
            StringComparison.Ordinal));
        _drafts.Add(draft);
        _drafts.Sort(static (left, right) =>
            right.SavedAtUtc.CompareTo(left.SavedAtUtc));
    }

    public void Remove(string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        _drafts.RemoveAll(value => value.AssetId.Equals(
            assetId,
            StringComparison.Ordinal));
    }
}
