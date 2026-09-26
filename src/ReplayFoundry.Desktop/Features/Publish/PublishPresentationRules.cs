using System.Globalization;
using System.IO;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Presentation;

namespace ReplayFoundry.Desktop.Features.Publish;

internal static class PublishPresentationRules
{
    internal static readonly TimeSpan MinimumScheduleLeadTime = TimeSpan.FromMinutes(30);
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

    public static string BuildHistoryDetail(YouTubePublishHistoryEntry entry) =>
        BuildHistoryDetail(entry, TimeZoneInfo.Local);

    public static string BuildHistoryDetail(
        YouTubePublishHistoryEntry entry,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(timeZone);
        if (entry.FailureMessage is not null)
        {
            return entry.FailureMessage;
        }

        string timing = entry.ScheduledForUtc.HasValue
            ? $"Scheduled for {TimeZoneInfo.ConvertTime(entry.ScheduledForUtc.Value, timeZone):f}"
            : $"Completed {TimeZoneInfo.ConvertTime(entry.AttemptedAtUtc, timeZone):g}";
        string remote = entry.RemoteStatus switch
        {
            YouTubeRemoteVideoStatus.Exists =>
                "verified on YouTube " +
                TimeZoneInfo.ConvertTime(
                    entry.RemoteCheckedAtUtc!.Value,
                    timeZone).ToString("g"),
            YouTubeRemoteVideoStatus.NotFoundOrInaccessible =>
                "not found or no longer accessible to this channel",
            _ => "online status not checked",
        };
        return timing + " · " + remote;
    }
    internal static bool TryGetScheduledUtc(
        DateTime? date, string text, TimeZoneInfo timeZone, DateTimeOffset now,
        out DateTimeOffset scheduledUtc,
        out string error)
    {
        scheduledUtc = default;
        if (date is null)
        {
            error = "Choose a release date.";
            return false;
        }
        if (!TryParseTime(text, out TimeOnly time))
        {
            error = "Enter a release time such as 6:00 PM.";
            return false;
        }
        try
        {
            scheduledUtc = YouTubeSchedulePlanner.ToUtc(
                DateOnly.FromDateTime(date.Value),
                time,
                timeZone);
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
        if (scheduledUtc < now + MinimumScheduleLeadTime)
        {
            error =
                "Choose a release at least 30 minutes from now so YouTube has time to receive and process the video.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    internal static string ScheduleSummary(bool scheduled, YouTubeVideoVisibility visibility, DateTime? date,
        string text, TimeZoneInfo timeZone, DateTimeOffset now)
    {
        if (!scheduled) return visibility switch
        {
            YouTubeVideoVisibility.Public => "The video becomes public after YouTube accepts and processes it.",
            YouTubeVideoVisibility.Unlisted => "The video is available to anyone with the link.",
            _ => "The video remains private in YouTube Studio.",
        };
        return TryGetScheduledUtc(date, text, timeZone, now, out var utc, out string error)
            ? $"YouTube will publish at {TimeZoneInfo.ConvertTime(utc, timeZone):f} (UTC{TimeZoneInfo.ConvertTime(utc, timeZone):zzz})."
            : error;
    }
}
