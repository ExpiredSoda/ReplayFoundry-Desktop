using ReplayFoundry.Desktop.Features.Publish.YouTube;

namespace ReplayFoundry.Desktop.Features.Publish;

internal static class PublishOptionCatalog
{
    public static IReadOnlyList<PublishCalendarModeItem> CreateCalendarModes() =>
    [
        new(PublishCalendarMode.Month, "Month"),
        new(PublishCalendarMode.Week, "Week"),
    ];

    public static IReadOnlyList<PublishCalendarPlatformItem>
        CreateCalendarPlatformFilters() =>
    [
        new(PublishCalendarPlatform.All, "All YouTube plans", "Icon.Grid"),
        new(PublishCalendarPlatform.YouTube, "Scheduled videos", "Icon.Play"),
    ];

    public static IReadOnlyList<PublishChoiceItem<YouTubeVideoVisibility>>
        CreateVisibilityOptions() =>
        Enum.GetValues<YouTubeVideoVisibility>()
            .Select(static value => new PublishChoiceItem<YouTubeVideoVisibility>(
                value,
                GetVisibilityLabel(value),
                GetVisibilityDescription(value)))
            .ToArray();

    public static IReadOnlyList<PublishChoiceItem<YouTubePublishTiming>>
        CreateTimingOptions() =>
    [
        new(
            YouTubePublishTiming.PublishNow,
            "Upload now",
            "YouTube applies the visibility you choose."),
        new(
            YouTubePublishTiming.Schedule,
            "Schedule release",
            "Upload privately now and let YouTube publish it later."),
    ];

    public static IReadOnlyList<PublishChoiceItem<YouTubeAudience>>
        CreateAudienceOptions() =>
    [
        new(
            YouTubeAudience.NotMadeForKids,
            "No, it is not made for kids",
            "Choose this only when it accurately describes the video."),
        new(
            YouTubeAudience.MadeForKids,
            "Yes, it is made for kids",
            "YouTube limits some features on child-directed videos."),
    ];

    private static string GetVisibilityLabel(YouTubeVideoVisibility visibility) =>
        visibility switch
        {
            YouTubeVideoVisibility.Public => "Public",
            YouTubeVideoVisibility.Unlisted => "Unlisted",
            _ => "Private",
        };

    private static string GetVisibilityDescription(
        YouTubeVideoVisibility visibility) =>
        visibility switch
        {
            YouTubeVideoVisibility.Public =>
                "Anyone can watch when the upload is ready.",
            YouTubeVideoVisibility.Unlisted =>
                "Only people with the link can watch.",
            _ => "Only you and invited viewers can watch.",
        };
}
