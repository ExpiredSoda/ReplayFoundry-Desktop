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
    string Glyph);
public sealed record PublishCalendarDay(
    DateTime Date,
    string DayNumber,
    string AccessibleLabel,
    bool IsInActiveRange,
    bool IsToday,
    IReadOnlyList<PublishCalendarSlot> Slots);
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
    string? ThumbnailFullPath)
{
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
