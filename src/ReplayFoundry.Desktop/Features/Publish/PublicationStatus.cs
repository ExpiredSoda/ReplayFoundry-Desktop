using ReplayFoundry.Desktop.Features.Publish.YouTube;

namespace ReplayFoundry.Desktop.Features.Publish;

public enum PublicationStage { Ready, Draft, Processing, Scheduled, Public, Private, Unlisted, Attention, Unconfirmed }

/// <summary>One publication vocabulary for Library, the publishing queue and history.</summary>
public sealed record PublicationStatus(PublicationStage Stage, string Label, string ActionLabel,
    string Icon, string Detail, string? VideoUrl = null)
{
    public static PublicationStatus Ready { get; } = new(PublicationStage.Ready,
        "Ready to publish", "Prepare", "Icon.Media", "No recorded upload for this video.");
    public static PublicationStatus Draft { get; } = new(PublicationStage.Draft,
        "Draft saved", "Continue draft", "Icon.Edit", "Saved on this PC. Nothing has been uploaded.");

    public bool Matches(string filter) => filter switch
    {
        "Ready to publish" => Stage is PublicationStage.Ready or PublicationStage.Draft,
        "Scheduled" => Stage == PublicationStage.Scheduled,
        "Published" => Stage is PublicationStage.Public or PublicationStage.Private or PublicationStage.Unlisted,
        "Needs attention" => Stage is PublicationStage.Attention or PublicationStage.Unconfirmed,
        _ => true,
    };

    public static PublicationStatus FromHistory(YouTubePublishHistoryEntry entry, DateTimeOffset now)
    {
        string checkedText = entry.RemoteCheckedAtUtc is { } checkedAt
            ? $"Last checked {checkedAt.ToLocalTime():MMM d, h:mm tt}." : "Status has not been checked with YouTube.";
        string channel = entry.RemoteDetails?.ChannelId ?? entry.Provenance?.ChannelId ?? "Channel not recorded";
        string detail = $"{channel} · {checkedText}";
        PublicationStatus State(PublicationStage stage, string label, string icon) =>
            new(stage, label, entry.VideoUrl is null ? "View details" :
                stage == PublicationStage.Scheduled ? "View schedule" : "View on YouTube", icon, detail, entry.VideoUrl);
        if (entry.RemoteStatus == YouTubeRemoteVideoStatus.NotFoundOrInaccessible)
            return State(PublicationStage.Attention, "YouTube copy needs attention", "Icon.Warning");
        if (entry.VideoId is null)
            return new(PublicationStage.Attention, entry.Outcome == YouTubePublishOutcome.Cancelled ? "Upload cancelled" : "Last upload failed",
                "Review and retry", "Icon.Warning", entry.FailureMessage ?? detail);
        if (entry.RemoteDetails is { } remote)
        {
            if (remote.UploadStatus is "failed" or "rejected" or "deleted" || remote.ProcessingStatus is "failed" or "terminated")
                return State(PublicationStage.Attention, "YouTube processing needs attention", "Icon.Warning");
            if (remote.UploadStatus == "uploaded" || remote.ProcessingStatus == "processing")
                return State(PublicationStage.Processing, "Processing on YouTube", "Icon.Clock");
            if (remote.Visibility == YouTubeVideoVisibility.Private && remote.PublishAtUtc is { } release)
                return State(PublicationStage.Scheduled, release > now
                    ? $"Scheduled · {release.ToLocalTime():MMM d, h:mm tt}" : "Scheduled · awaiting YouTube release", "Icon.Clock");
            if (remote.UploadStatus == "processed" || remote.ProcessingStatus == "succeeded")
            {
                return remote.Visibility switch
                {
                    YouTubeVideoVisibility.Public => State(PublicationStage.Public, "Public on YouTube", "Icon.Check"),
                    YouTubeVideoVisibility.Private => State(PublicationStage.Private, "Private on YouTube", "Icon.Lock"),
                    YouTubeVideoVisibility.Unlisted => State(PublicationStage.Unlisted, "Unlisted on YouTube", "Icon.Link"),
                    _ => State(PublicationStage.Unconfirmed, "Uploaded · visibility unconfirmed", "Icon.Clock"),
                };
            }
        }
        return entry.Outcome == YouTubePublishOutcome.Scheduled && entry.ScheduledForUtc > now
            ? State(PublicationStage.Scheduled, $"Scheduled · {entry.ScheduledForUtc.Value.ToLocalTime():MMM d, h:mm tt} · check status", "Icon.Clock")
            : State(PublicationStage.Unconfirmed, "Uploaded · check status", "Icon.Clock");
    }
}
