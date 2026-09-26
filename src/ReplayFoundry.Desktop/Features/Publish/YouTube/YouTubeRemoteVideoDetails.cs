namespace ReplayFoundry.Desktop.Features.Publish.YouTube;

/// <summary>The last observation from YouTube, separate from the original upload request.</summary>
public sealed record YouTubeRemoteVideoDetails(
    string? ChannelId,
    YouTubeVideoVisibility? Visibility,
    string? UploadStatus,
    string? ProcessingStatus,
    DateTimeOffset? PublishAtUtc);
