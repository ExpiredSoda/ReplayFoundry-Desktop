using System;
using System.IO;
using ReplayFoundry.Desktop.Presentation;

namespace ReplayFoundry.Desktop.Features.Library;

public enum LibraryCategory
{
    Projects,
    GeneratedClips,
    Montages,
}

public enum LibraryViewMode
{
    Grid,
    List,
}

public enum LibraryOrganizationMode
{
    Date,
    Folder,
    Project,
}

public sealed record LibraryCategoryItem(
    LibraryCategory Key,
    string Label,
    string Glyph);

public sealed record LibraryOrganizationOption(
    LibraryOrganizationMode Key,
    string Label);

public sealed class LibraryItem : ObservableObject
{
    private bool _isMarked;
    private bool _hasThumbnail;
    private string _status;
    private int _thumbnailRevision;
    private string _organizationGroup = string.Empty;

    public LibraryItem(
        string title,
        string type,
        string duration,
        string modified,
        string status,
        string aspectRatio,
        string detail,
        string glyph,
        string? thumbnailFullPath = null,
        LibraryMediaAsset? asset = null)
    {
        Title = title;
        Type = type;
        Duration = duration;
        Modified = modified;
        _status = status;
        AspectRatio = aspectRatio;
        Detail = detail;
        Glyph = glyph;
        ThumbnailFullPath = thumbnailFullPath;
        Asset = asset;
        _hasThumbnail = thumbnailFullPath is not null &&
            File.Exists(thumbnailFullPath);
    }

    public string Title { get; }
    public string Type { get; }
    public string Duration { get; }
    public string Modified { get; }
    public string Status => _status;
    public string AspectRatio { get; }
    public string Detail { get; }
    public string Glyph { get; }
    public string? ThumbnailFullPath { get; }
    public LibraryMediaAsset? Asset { get; }
    public bool HasThumbnail => _hasThumbnail;
    public int ThumbnailRevision => _thumbnailRevision;

    public string OrganizationGroup
    {
        get => _organizationGroup;
        internal set
        {
            if (_organizationGroup == value) return;
            _organizationGroup = value;
            OnPropertyChanged();
        }
    }

    public bool IsMarked
    {
        get => _isMarked;
        set
        {
            if (_isMarked == value) return;
            _isMarked = value;
            OnPropertyChanged();
        }
    }

    internal bool RefreshFileAvailability(bool thumbnailTouched)
    {
        if (Asset is null)
        {
            return false;
        }

        string status = Asset.IsAvailable ? "Ready" : "Missing locally";
        bool statusChanged = !_status.Equals(status, StringComparison.Ordinal);
        if (statusChanged)
        {
            _status = status;
            OnPropertyChanged(nameof(Status));
        }

        bool hasThumbnail = ThumbnailFullPath is not null &&
            File.Exists(ThumbnailFullPath);
        if (_hasThumbnail != hasThumbnail)
        {
            _hasThumbnail = hasThumbnail;
            OnPropertyChanged(nameof(HasThumbnail));
        }

        if (thumbnailTouched)
        {
            _thumbnailRevision++;
            OnPropertyChanged(nameof(ThumbnailRevision));
        }

        return statusChanged;
    }
}
