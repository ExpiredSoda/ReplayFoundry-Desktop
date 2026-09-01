using ReplayFoundry.Desktop.Features.Publish.YouTube;

namespace ReplayFoundry.Desktop.Features.Publish;

internal sealed class PublishCalendarState
{
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly TimeZoneInfo _timeZone;

    public PublishCalendarState(
        Func<DateTimeOffset> utcNow,
        TimeZoneInfo timeZone)
    {
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _timeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
        DateTime today = LocalToday;
        Anchor = new DateTime(today.Year, today.Month, 1);
    }

    public PublishCalendarMode Mode { get; private set; } =
        PublishCalendarMode.Month;
    public PublishCalendarPlatform Platform { get; private set; } =
        PublishCalendarPlatform.All;
    public DateTime Anchor { get; private set; }
    public IReadOnlyList<PublishCalendarDay> Days { get; private set; } = [];
    public PublishCalendarDay? SelectedDay { get; private set; }
    public DateTime LocalToday =>
        TimeZoneInfo.ConvertTime(_utcNow(), _timeZone).Date;

    public bool SetMode(PublishCalendarMode mode, DateTime focus)
    {
        if (Mode == mode) return false;
        Mode = mode;
        Anchor = mode == PublishCalendarMode.Month
            ? new DateTime(focus.Year, focus.Month, 1)
            : focus;
        return true;
    }

    public bool SetPlatform(PublishCalendarPlatform platform)
    {
        if (Platform == platform) return false;
        Platform = platform;
        return true;
    }

    public bool SelectDay(PublishCalendarDay? day)
    {
        if (ReferenceEquals(SelectedDay, day)) return false;
        SelectedDay = day;
        return true;
    }

    public void Move(int direction)
    {
        Anchor = Mode == PublishCalendarMode.Month
            ? Anchor.AddMonths(direction)
            : Anchor.AddDays(direction * 7);
    }

    public void ReturnToToday()
    {
        DateTime today = LocalToday;
        Anchor = Mode == PublishCalendarMode.Month
            ? new DateTime(today.Year, today.Month, 1)
            : today;
    }

    public void Rebuild(
        IReadOnlyList<YouTubePublishHistoryEntry> history,
        IReadOnlyList<YouTubePublishDraft> drafts,
        IReadOnlyList<YouTubePreferredScheduleSlot> preferredSlots,
        DateTime? preferredDate)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(drafts);
        ArgumentNullException.ThrowIfNull(preferredSlots);
        PublishCalendarProjection projection = PublishCalendarProjector.Build(
            Mode,
            Platform,
            Anchor,
            LocalToday,
            BuildSlots(history, drafts, preferredSlots),
            preferredDate);
        Days = projection.Days;
        SelectedDay = projection.SelectedDay;
    }

    private IReadOnlyList<PublishCalendarSlot> BuildSlots(
        IReadOnlyList<YouTubePublishHistoryEntry> history,
        IReadOnlyList<YouTubePublishDraft> drafts,
        IReadOnlyList<YouTubePreferredScheduleSlot> preferredSlots)
    {
        var slots = new List<PublishCalendarSlot>();
        foreach (YouTubePublishHistoryEntry entry in history)
        {
            if (entry.ScheduledForUtc is not { } scheduledUtc) continue;
            DateTime local = TimeZoneInfo.ConvertTime(
                scheduledUtc,
                _timeZone).DateTime;
            slots.Add(PublishCalendarProjector.CreatePreviewSlot(
                local,
                PublishCalendarPlatform.YouTube,
                "YouTube",
                entry.Title,
                PublishPresentationRules.FormatOutcome(entry.Outcome),
                "Icon.Play"));
        }
        foreach (YouTubePublishDraft draft in drafts)
        {
            if (draft.ScheduledForUtc is not { } scheduledUtc) continue;
            DateTime local = TimeZoneInfo.ConvertTime(
                scheduledUtc,
                _timeZone).DateTime;
            slots.Add(PublishCalendarProjector.CreatePreviewSlot(
                local,
                PublishCalendarPlatform.YouTube,
                "Saved draft",
                draft.Title,
                "Not uploaded",
                "Icon.Edit"));
        }
        AddPreferredSlots(slots, preferredSlots);
        return slots;
    }

    private void AddPreferredSlots(
        ICollection<PublishCalendarSlot> slots,
        IReadOnlyList<YouTubePreferredScheduleSlot> preferredSlots)
    {
        DateTime rangeStart = Mode == PublishCalendarMode.Month
            ? new DateTime(Anchor.Year, Anchor.Month, 1).AddDays(-7)
            : PublishCalendarProjector.GetWeekStart(Anchor);
        int dayCount = Mode == PublishCalendarMode.Month ? 56 : 7;
        for (DateTime date = rangeStart.Date;
             date < rangeStart.Date.AddDays(dayCount);
             date = date.AddDays(1))
        {
            foreach (YouTubePreferredScheduleSlot preferred in
                     preferredSlots.Where(value => value.Day == date.DayOfWeek))
            {
                slots.Add(PublishCalendarProjector.CreatePreviewSlot(
                    date.Add(preferred.LocalTime.ToTimeSpan()),
                    PublishCalendarPlatform.All,
                    "Preferred time",
                    "User-chosen release window",
                    "Planning preference",
                    "Icon.Clock"));
            }
        }
    }
}
