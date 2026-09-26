using System.IO;

namespace ReplayFoundry.Desktop.Features.Publish;

public enum PublishCalendarMode
{
    Month,
    Week,
}

public enum PublishCalendarPlatform
{
    All,
    YouTube,
}

public enum PublishDestination
{
    YouTube,
}

public sealed record PublishCalendarModeItem(
    PublishCalendarMode Key,
    string Label);
public sealed record PublishCalendarPlatformItem(
    PublishCalendarPlatform Key,
    string Label,
    string Glyph);
public sealed record PublishCalendarSlot(
    DateTime ScheduledFor,
    PublishCalendarPlatform Platform,
    string PlatformLabel,
    string Title,
    string TimeLabel,
    string Status,
    string Glyph,
    PublicationStage? PublicationStage = null);
public sealed record PublishCalendarDay(
    DateTime Date,
    string DayNumber,
    string AccessibleLabel,
    bool IsInActiveRange,
    bool IsToday,
    IReadOnlyList<PublishCalendarSlot> Slots)
{
    public string MonthLabel => IsInActiveRange ? string.Empty :
        Date.ToString("MMM", System.Globalization.CultureInfo.CurrentCulture);
    public string SlotCountLabel => Slots.Count == 0 ? string.Empty : Slots.Count.ToString();
    public string AgendaSummary => Slots.Count == 0 ? AccessibleLabel :
        AccessibleLabel + "\n" + string.Join("\n", Slots.Select(slot => $"{slot.TimeLabel} · {slot.Title} · {slot.Status}"));
    public IReadOnlyList<PublishCalendarMarker> Markers => Slots
        .GroupBy(slot => (slot.PublicationStage, slot.Glyph))
        .Select(group => new PublishCalendarMarker(group.Key.PublicationStage, group.Key.Glyph))
        .Take(3).ToArray();
}
public sealed record PublishCalendarMarker(PublicationStage? Stage, string Glyph);
public sealed record PublishPlanningItem(
    string Title,
    string Detail,
    string Status,
    string Glyph,
    ReplayFoundry.Desktop.Features.Library.LibraryMediaAsset? Asset = null);

public sealed record PublishLibraryItem(
    ReplayFoundry.Desktop.Features.Library.LibraryMediaAsset Asset,
    string Title,
    string Detail,
    string CollectionDetail,
    string Status,
    string? ThumbnailFullPath,
    PublicationStatus? Publication = null)
{
    public string ActionLabel => Publication?.ActionLabel ?? "Prepare";
    public bool HasRecordedUpload => !string.IsNullOrWhiteSpace(Publication?.VideoUrl);
    public bool HasThumbnail =>
        ThumbnailFullPath is not null &&
        File.Exists(ThumbnailFullPath);
}

public sealed record PublishLibraryFolderItem(
    string? FullPath,
    string Label)
{
    public override string ToString() => Label;
}
public sealed record PublishDestinationItem(
    PublishDestination Key,
    string Label,
    string Status,
    string Description,
    string Glyph,
    bool IsConnected);
public sealed record PublishChecklistItem(
    string Label,
    string Value,
    string State);
public sealed record PublishJobItem(
    string Title,
    string Status,
    string Detail,
    string? Url = null);

public sealed record PublishChoiceItem<T>(
    T Key,
    string Label,
    string Description)
    where T : struct, Enum
{
    public override string ToString() => Label;
}

public sealed record PublishPlaylistItem(
    string? Id,
    string Label,
    bool IsPrivate)
{
    public override string ToString() => Label;
}

public interface IPublishPreparationDialogService
{
    void Show(PublishViewModel viewModel);
}

public interface IPublishBulkConfirmation
{
    bool ConfirmPublishAllNow(int videoCount);
}
