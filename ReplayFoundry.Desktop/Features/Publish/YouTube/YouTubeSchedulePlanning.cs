using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Features.Publish.YouTube;

public sealed record YouTubePreferredScheduleSlot(
    DayOfWeek Day,
    TimeOnly LocalTime)
{
    public string Id => $"{(int)Day}:{LocalTime:HHmm}";
    public string Label => $"{Day} at {LocalTime:h:mm tt}";
}

public interface IYouTubePublishPreferencesStore
{
    IReadOnlyList<YouTubePreferredScheduleSlot> PreferredSlots { get; }
    void Replace(IEnumerable<YouTubePreferredScheduleSlot> slots);
}

public sealed class InMemoryYouTubePublishPreferencesStore :
    IYouTubePublishPreferencesStore
{
    private IReadOnlyList<YouTubePreferredScheduleSlot> _slots =
        Array.Empty<YouTubePreferredScheduleSlot>();

    public IReadOnlyList<YouTubePreferredScheduleSlot> PreferredSlots =>
        _slots;

    public void Replace(IEnumerable<YouTubePreferredScheduleSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        YouTubePreferredScheduleSlot[] snapshot = slots
            .DistinctBy(static value => value.Id)
            .OrderBy(static value => value.Day)
            .ThenBy(static value => value.LocalTime)
            .ToArray();
        if (snapshot.Any(static value => !Enum.IsDefined(value.Day)))
        {
            throw new ArgumentException(
                "Preferred YouTube schedule days must be defined.",
                nameof(slots));
        }
        _slots = Array.AsReadOnly(snapshot);
    }
}

public static class YouTubeSchedulePlanner
{
    public static DateTimeOffset ToUtc(
        DateOnly date,
        TimeOnly localTime,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        DateTime local = DateTime.SpecifyKind(
            date.ToDateTime(localTime),
            DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(local))
        {
            throw new ArgumentException(
                "The selected local time does not exist because of a daylight-saving transition.",
                nameof(localTime));
        }
        if (timeZone.IsAmbiguousTime(local))
        {
            TimeSpan laterOffset = timeZone
                .GetAmbiguousTimeOffsets(local)
                .Min();
            return new DateTimeOffset(local, laterOffset)
                .ToUniversalTime();
        }
        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    public static DateTimeOffset? FindNext(
        IEnumerable<YouTubePreferredScheduleSlot> slots,
        DateTimeOffset nowUtc,
        TimeZoneInfo timeZone,
        TimeSpan minimumLeadTime)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(timeZone);
        if (nowUtc.Offset != TimeSpan.Zero || minimumLeadTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(nowUtc));
        }
        YouTubePreferredScheduleSlot[] snapshot = slots
            .DistinctBy(static value => value.Id)
            .ToArray();
        if (snapshot.Length == 0)
        {
            return null;
        }

        DateTimeOffset threshold = nowUtc + minimumLeadTime;
        DateTime localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZone).DateTime;
        var candidates = new List<DateTimeOffset>();
        for (int offset = 0; offset < 15; offset++)
        {
            DateOnly date = DateOnly.FromDateTime(localNow.Date.AddDays(offset));
            foreach (YouTubePreferredScheduleSlot slot in snapshot.Where(
                         value => value.Day == date.DayOfWeek))
            {
                try
                {
                    DateTimeOffset utc = ToUtc(date, slot.LocalTime, timeZone);
                    if (utc >= threshold)
                    {
                        candidates.Add(utc);
                    }
                }
                catch (ArgumentException)
                {
                    // Skip the one nonexistent DST wall-clock occurrence.
                }
            }
        }
        return candidates.Count == 0 ? null : candidates.Min();
    }
}
