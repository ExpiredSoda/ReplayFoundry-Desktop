namespace ReplayFoundry.Desktop.Platform.YouTube;

internal static class YouTubeApiResponses
{
    internal sealed class ChannelListResponse
    {
        public ChannelItem[]? Items { get; set; }
    }

    internal sealed class ChannelItem
    {
        public string Id { get; set; } = string.Empty;
        public Snippet? Snippet { get; set; }
    }

    internal sealed class PlaylistListResponse
    {
        public PlaylistItem[]? Items { get; set; }
        public string? NextPageToken { get; set; }
    }

    internal sealed class PlaylistItem
    {
        public string Id { get; set; } = string.Empty;
        public Snippet? Snippet { get; set; }
        public Status? Status { get; set; }
    }

    internal sealed class CategoryListResponse
    {
        public CategoryItem[]? Items { get; set; }
    }

    internal sealed class CategoryItem
    {
        public string Id { get; set; } = string.Empty;
        public CategorySnippet? Snippet { get; set; }
    }

    internal sealed class CategorySnippet
    {
        public string Title { get; set; } = string.Empty;
        public bool Assignable { get; set; }
    }

    internal sealed class Snippet
    {
        public string Title { get; set; } = string.Empty;
    }

    internal sealed class Status
    {
        public string? PrivacyStatus { get; set; }
    }

    internal sealed class VideoInsertResponse
    {
        public string Id { get; set; } = string.Empty;
    }

    internal sealed class VideoListResponse
    {
        public VideoIdentity[]? Items { get; set; }
    }

    internal sealed class VideoIdentity
    {
        public string Id { get; set; } = string.Empty;
        public RemoteVideoSnippet? Snippet { get; set; }
        public RemoteVideoState? Status { get; set; }
        public RemoteVideoProcessing? ProcessingDetails { get; set; }
    }

    internal sealed class RemoteVideoSnippet { public string? ChannelId { get; set; } }
    internal sealed class RemoteVideoState
    {
        public string? PrivacyStatus { get; set; }
        public string? UploadStatus { get; set; }
        public DateTimeOffset? PublishAt { get; set; }
    }
    internal sealed class RemoteVideoProcessing { public string? ProcessingStatus { get; set; } }
}
