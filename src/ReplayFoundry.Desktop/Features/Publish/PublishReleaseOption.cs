using ReplayFoundry.Desktop.Features.Publish.YouTube;

namespace ReplayFoundry.Desktop.Features.Publish;

public sealed record PublishReleaseOption(
    string Label,
    string Description,
    YouTubePublishTiming Timing,
    YouTubeVideoVisibility Visibility)
{
    public static IReadOnlyList<PublishReleaseOption> All { get; } =
    [
        new("Public · publish now", "Anyone can watch once the upload is ready.",
            YouTubePublishTiming.PublishNow, YouTubeVideoVisibility.Public),
        new("Unlisted · upload now", "Only people with the link can watch.",
            YouTubePublishTiming.PublishNow, YouTubeVideoVisibility.Unlisted),
        new("Private · upload now", "Only you and invited viewers can watch.",
            YouTubePublishTiming.PublishNow, YouTubeVideoVisibility.Private),
        new("Public · schedule for later", "Upload privately first. YouTube makes it public at the time below.",
            YouTubePublishTiming.Schedule, YouTubeVideoVisibility.Public),
    ];
}
