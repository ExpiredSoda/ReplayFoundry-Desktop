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

}
