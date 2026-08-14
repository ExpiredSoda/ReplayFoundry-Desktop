using System.Globalization;
using System.IO;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Presentation;

namespace ReplayFoundry.Desktop.Features.Publish;

internal static class PublishPresentationRules
{
    public static IReadOnlyList<string> ParseTags(string text) =>
        text.Split(
                [',', '\n', '\r'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(static value => value.TrimStart('#'))
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static bool TryParseTime(string text, out TimeOnly time) =>
        TimeOnly.TryParse(
            text,
            CultureInfo.CurrentCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out time) ||
        TimeOnly.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out time);

    public static string ValidateThumbnail(string? fullPath)
    {
        if (fullPath is null)
        {
            return "YouTube will choose a thumbnail.";
        }

        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            return "The selected thumbnail no longer exists.";
        }
        if (file.Length is <= 0 or > 2_000_000)
        {
            return "The thumbnail must be nonempty and no larger than 2 MB.";
        }
        return file.Extension.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png"
            ? "Thumbnail ready."
            : "Choose a JPEG or PNG thumbnail.";
    }

    public static string FormatDuration(TimeSpan duration) =>
        MediaTimeFormatter.Format(duration);

    public static string FormatCount(int count, string singular) =>
        $"{count} {singular}{(count == 1 ? string.Empty : "s")}";

    public static string FormatOutcome(YouTubePublishOutcome outcome) =>
        outcome switch
        {
            YouTubePublishOutcome.UploadedPrivate => "Private",
            YouTubePublishOutcome.UploadedUnlisted => "Unlisted",
            YouTubePublishOutcome.Published => "Published",
            YouTubePublishOutcome.Scheduled => "Scheduled",
            YouTubePublishOutcome.Cancelled => "Cancelled",
            _ => "Failed",
        };

    public static string BuildHistoryDetail(YouTubePublishHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.FailureMessage is not null)
        {
            return entry.FailureMessage;
        }

        string timing = entry.ScheduledForUtc.HasValue
            ? $"Scheduled for {entry.ScheduledForUtc.Value.ToLocalTime():f}"
            : $"Completed {entry.AttemptedAtUtc.ToLocalTime():g}";
        string remote = entry.RemoteStatus switch
        {
            YouTubeRemoteVideoStatus.Exists =>
                "verified on YouTube " +
                entry.RemoteCheckedAtUtc!.Value.ToLocalTime().ToString("g"),
            YouTubeRemoteVideoStatus.NotFoundOrInaccessible =>
                "not found or no longer accessible to this channel",
            _ => "online status not checked",
        };
        return timing + " · " + remote;
    }
}
