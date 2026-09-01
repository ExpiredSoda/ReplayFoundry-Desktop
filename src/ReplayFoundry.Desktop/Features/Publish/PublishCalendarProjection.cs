using System.Globalization;

namespace ReplayFoundry.Desktop.Features.Publish;

internal sealed record PublishCalendarProjection(
    IReadOnlyList<PublishCalendarDay> Days,
    PublishCalendarDay SelectedDay);

internal static class PublishCalendarProjector
{
    public static PublishCalendarProjection Build(
        PublishCalendarMode mode,
        PublishCalendarPlatform platform,
        DateTime anchor,
        DateTime today,
        IReadOnlyList<PublishCalendarSlot> previewSlots,
        DateTime? preferredDate)
    {
        ArgumentNullException.ThrowIfNull(previewSlots);
        DateTime firstVisible;
        int dayCount;
        if (mode == PublishCalendarMode.Month)
        {
            DateTime firstOfMonth = new(anchor.Year, anchor.Month, 1);
            firstVisible = firstOfMonth.AddDays(
                -DaysSinceMonday(firstOfMonth.DayOfWeek));
            dayCount = 42;
        }
        else
        {
            firstVisible = GetWeekStart(anchor);
            dayCount = 7;
        }

        var days = new List<PublishCalendarDay>(dayCount);
        for (int index = 0; index < dayCount; index++)
        {
            DateTime date = firstVisible.AddDays(index).Date;
            IReadOnlyList<PublishCalendarSlot> slots = previewSlots
                .Where(slot =>
                    slot.ScheduledFor.Date == date &&
                    (platform == PublishCalendarPlatform.All ||
                     slot.Platform == platform))
                .OrderBy(static slot => slot.ScheduledFor)
                .ToArray();
            bool isInActiveRange =
                mode == PublishCalendarMode.Week ||
                date.Month == anchor.Month && date.Year == anchor.Year;
            days.Add(new PublishCalendarDay(
                date,
                date.Day.ToString(CultureInfo.CurrentCulture),
                date.ToString(
                    "dddd, MMMM d, yyyy",
                    CultureInfo.CurrentCulture),
                isInActiveRange,
                date == today.Date,
                slots));
        }

        DateTime selectedDate = preferredDate?.Date ?? today.Date;
        PublishCalendarDay selected =
            days.FirstOrDefault(day => day.Date == selectedDate) ??
            days.FirstOrDefault(static day => day.IsInActiveRange) ??
            days[0];
        return new PublishCalendarProjection(days, selected);
    }

    public static PublishCalendarSlot CreatePreviewSlot(
        DateTime scheduledFor,
        PublishCalendarPlatform platform,
        string platformLabel,
        string title,
        string status,
        string glyph) =>
        new(
            scheduledFor,
            platform,
            platformLabel,
            title,
            scheduledFor.ToString("h:mm tt", CultureInfo.CurrentCulture),
            status,
            glyph);

    public static DateTime GetWeekStart(DateTime date) =>
        date.Date.AddDays(-DaysSinceMonday(date.DayOfWeek));

    public static string FormatWeekRange(DateTime weekStart)
    {
        DateTime weekEnd = weekStart.AddDays(6);
        return weekStart.Month == weekEnd.Month
            ? $"{weekStart:MMM d}–{weekEnd:d, yyyy}"
            : $"{weekStart:MMM d}–{weekEnd:MMM d, yyyy}";
    }

    public static string FormatUtcOffset(TimeSpan offset)
    {
        string sign = offset < TimeSpan.Zero ? "−" : "+";
        TimeSpan absolute = offset.Duration();
        return $"{sign}{absolute.Hours:00}:{absolute.Minutes:00}";
    }

    private static int DaysSinceMonday(DayOfWeek dayOfWeek) =>
        ((int)dayOfWeek + 6) % 7;
}
