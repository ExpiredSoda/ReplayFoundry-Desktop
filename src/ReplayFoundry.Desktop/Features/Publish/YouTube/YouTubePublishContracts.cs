using System.Collections.ObjectModel;
using ReplayFoundry.Desktop.Security;
using System.IO;
using ReplayFoundry.Desktop.Features.Library;

namespace ReplayFoundry.Desktop.Features.Publish.YouTube;

public enum YouTubePublishTiming
{
    PublishNow,
    Schedule,
}

public enum YouTubeVideoVisibility
{
    Private,
    Unlisted,
    Public,
}

public enum YouTubeAudience
{
    NotMadeForKids,
    MadeForKids,
}

public enum YouTubePublishPhase
{
    Preparing,
    Uploading,
    SettingThumbnail,
    AddingToPlaylist,
    Completed,
}

public enum YouTubePublishOutcome
{
    UploadedPrivate,
    UploadedUnlisted,
    Published,
    Scheduled,
    Failed,
    Cancelled,
}

public sealed class YouTubeAccountConnection
{
    public YouTubeAccountConnection(
        string channelId,
        string channelTitle,
        DateTimeOffset connectedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(channelId) ||
            string.IsNullOrWhiteSpace(channelTitle))
        {
            throw new ArgumentException(
                "A YouTube connection requires a channel identity.");
        }
        if (connectedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(connectedAtUtc));
        }

        ChannelId = channelId.Trim();
        ChannelTitle = channelTitle.Trim();
        ConnectedAtUtc = connectedAtUtc;
    }

    public string ChannelId { get; }
    public string ChannelTitle { get; }
    public DateTimeOffset ConnectedAtUtc { get; }
}

public sealed record YouTubePlaylist(
    string Id,
    string Title,
    bool IsPrivate)
{
    public string DisplayLabel =>
        IsPrivate ? $"{Title} · Private" : Title;
}

public sealed record YouTubeVideoCategory(
    string Id,
    string Title)
{
    public override string ToString() => Title;
}

public sealed class YouTubePublishRequest
{
    private readonly ReadOnlyCollection<string> _tags;

    public YouTubePublishRequest(
        LibraryMediaAsset asset,
        string title,
        string description,
        IEnumerable<string> tags,
        string categoryId,
        YouTubeVideoVisibility visibility,
        YouTubePublishTiming timing,
        YouTubeAudience audience,
        bool containsSyntheticMedia,
        bool notifySubscribers,
        DateTimeOffset? scheduledForUtc = null,
        string? playlistId = null,
        string? thumbnailFullPath = null,
        DateTimeOffset? createdAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(tags);
        if (!asset.IsAvailable ||
            !Path.IsPathFullyQualified(asset.OutputFullPath))
        {
            throw new ArgumentException(
                "YouTube publishing requires one finalized Studio asset.",
                nameof(asset));
        }
        string securedTitle = ExternalTextSecurity.SingleLine(
            title,
            int.MaxValue);
        string securedDescription = ExternalTextSecurity.MultiLine(
            description,
            int.MaxValue);
        if (string.IsNullOrWhiteSpace(securedTitle) ||
            securedTitle.Length > 100)
        {
            throw new ArgumentException(
                "A YouTube title must contain 1 through 100 characters.",
                nameof(title));
        }
        if (string.IsNullOrWhiteSpace(categoryId) ||
            !categoryId.All(char.IsAsciiDigit))
        {
            throw new ArgumentException(
                "A YouTube category identifier must be numeric.",
                nameof(categoryId));
        }
        if (!Enum.IsDefined(visibility) ||
            !Enum.IsDefined(timing) ||
            !Enum.IsDefined(audience))
        {
            throw new ArgumentOutOfRangeException(nameof(visibility));
        }

        if (securedDescription.Length > 5_000)
        {
            throw new ArgumentException(
                "A YouTube description cannot exceed 5,000 characters.",
                nameof(description));
        }
        string[] tagSnapshot = tags
            .Select(static value => ExternalTextSecurity.SingleLine(
                value ?? string.Empty,
                int.MaxValue))
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (tagSnapshot.Any(static value => value.Length > 100) ||
            string.Join(',', tagSnapshot).Length > 500)
        {
            throw new ArgumentException(
                "YouTube tags must fit within the 500-character API limit.",
                nameof(tags));
        }

        if (timing == YouTubePublishTiming.Schedule)
        {
            if (visibility != YouTubeVideoVisibility.Public ||
                scheduledForUtc is null ||
                scheduledForUtc.Value.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException(
                    "A scheduled YouTube release must become public at an explicit UTC time.",
                    nameof(scheduledForUtc));
            }
        }
        else if (scheduledForUtc is not null)
        {
            throw new ArgumentException(
                "An immediate YouTube release cannot carry a scheduled time.",
                nameof(scheduledForUtc));
        }

        DateTimeOffset created = createdAtUtc ?? DateTimeOffset.UtcNow;
        if (created.Offset != TimeSpan.Zero ||
            scheduledForUtc.HasValue &&
            scheduledForUtc.Value < created.AddMinutes(30))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scheduledForUtc),
                "Scheduled releases require at least 30 minutes for upload and YouTube processing.");
        }

        string? normalizedPlaylist = string.IsNullOrWhiteSpace(playlistId)
            ? null
            : ExternalTextSecurity.SingleLine(playlistId, int.MaxValue);
        if (normalizedPlaylist is not null &&
            normalizedPlaylist.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '-' and not '_'))
        {
            throw new ArgumentException(
                "A YouTube playlist identifier contains unsupported characters.",
                nameof(playlistId));
        }
        if (!string.IsNullOrWhiteSpace(thumbnailFullPath) &&
            !Path.IsPathFullyQualified(thumbnailFullPath))
        {
            throw new ArgumentException(
                "A custom thumbnail path must be fully qualified.",
                nameof(thumbnailFullPath));
        }
        string? normalizedThumbnail = string.IsNullOrWhiteSpace(thumbnailFullPath)
            ? null
            : Path.GetFullPath(thumbnailFullPath);

        Asset = asset;
        Title = securedTitle;
        Description = securedDescription;
        _tags = Array.AsReadOnly(tagSnapshot);
        CategoryId = categoryId.Trim();
        Visibility = visibility;
        Timing = timing;
        Audience = audience;
        ContainsSyntheticMedia = containsSyntheticMedia;
        NotifySubscribers = notifySubscribers;
        ScheduledForUtc = scheduledForUtc;
        PlaylistId = normalizedPlaylist;
        ThumbnailFullPath = normalizedThumbnail;
        CreatedAtUtc = created;
    }

    public LibraryMediaAsset Asset { get; }
    public string Title { get; }
    public string Description { get; }
    public IReadOnlyList<string> Tags => _tags;
    public string CategoryId { get; }
    public YouTubeVideoVisibility Visibility { get; }
    public YouTubePublishTiming Timing { get; }
    public YouTubeAudience Audience { get; }
    public bool ContainsSyntheticMedia { get; }
    public bool NotifySubscribers { get; }
    public DateTimeOffset? ScheduledForUtc { get; }
    public string? PlaylistId { get; }
    public string? ThumbnailFullPath { get; }
    public DateTimeOffset CreatedAtUtc { get; }

}

public sealed record YouTubePublishProgress(
    YouTubePublishPhase Phase,
    string Title,
    string Detail,
    long BytesTransferred,
    long? TotalBytes)
{
    public double? Percentage =>
        TotalBytes is > 0
            ? Math.Clamp(
                BytesTransferred * 100d / TotalBytes.Value,
                0d,
                100d)
            : null;
}

public sealed class YouTubePublishResult
{
    public YouTubePublishResult(
        string videoId,
        string videoUrl,
        YouTubePublishOutcome outcome,
        YouTubeVideoVisibility visibility,
        DateTimeOffset completedAtUtc,
        DateTimeOffset? scheduledForUtc,
        string? playlistId,
        bool thumbnailApplied,
        IReadOnlyList<string>? warnings = null)
    {
        if (string.IsNullOrWhiteSpace(videoId) ||
            !Uri.TryCreate(videoUrl, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !Enum.IsDefined(outcome) ||
            outcome is YouTubePublishOutcome.Failed or
                YouTubePublishOutcome.Cancelled ||
            !Enum.IsDefined(visibility) ||
            completedAtUtc.Offset != TimeSpan.Zero ||
            scheduledForUtc.HasValue &&
            scheduledForUtc.Value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A completed YouTube publish result is invalid.");
        }
        string[] warningSnapshot = warnings?.ToArray() ?? [];
        if (warningSnapshot.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "YouTube result warnings cannot be blank.",
                nameof(warnings));
        }

        VideoId = videoId.Trim();
        VideoUrl = videoUrl;
        Outcome = outcome;
        Visibility = visibility;
        CompletedAtUtc = completedAtUtc;
        ScheduledForUtc = scheduledForUtc;
        PlaylistId = string.IsNullOrWhiteSpace(playlistId)
            ? null
            : playlistId.Trim();
        ThumbnailApplied = thumbnailApplied;
        Warnings = Array.AsReadOnly(warningSnapshot);
    }

    public string VideoId { get; }
    public string VideoUrl { get; }
    public YouTubePublishOutcome Outcome { get; }
    public YouTubeVideoVisibility Visibility { get; }
    public DateTimeOffset CompletedAtUtc { get; }
    public DateTimeOffset? ScheduledForUtc { get; }
    public string? PlaylistId { get; }
    public bool ThumbnailApplied { get; }
    public IReadOnlyList<string> Warnings { get; }
}

public sealed class YouTubePublishingException : Exception
{
    public YouTubePublishingException(
        string message,
        string diagnosticCode,
        string? technicalDetails = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        if (string.IsNullOrWhiteSpace(diagnosticCode))
        {
            throw new ArgumentException(
                "A publishing failure requires a diagnostic code.",
                nameof(diagnosticCode));
        }
        DiagnosticCode = diagnosticCode.Trim();
        TechnicalDetails = technicalDetails?.Trim();
    }

    public string DiagnosticCode { get; }
    public string? TechnicalDetails { get; }
}
