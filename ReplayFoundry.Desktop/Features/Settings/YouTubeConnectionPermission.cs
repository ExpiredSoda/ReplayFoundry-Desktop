using System;

namespace ReplayFoundry.Desktop.Features.Settings;

public sealed class YouTubeConnectionPermissionSnapshot
{
    public YouTubeConnectionPermissionSnapshot(
        bool isEnabled,
        DateTimeOffset? enabledAtUtc)
    {
        if (isEnabled != enabledAtUtc.HasValue)
        {
            throw new ArgumentException(
                "An enabled YouTube connection permission requires the UTC time it was enabled.",
                nameof(enabledAtUtc));
        }
        if (enabledAtUtc.HasValue && enabledAtUtc.Value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The YouTube connection permission time must use UTC.",
                nameof(enabledAtUtc));
        }

        IsEnabled = isEnabled;
        EnabledAtUtc = enabledAtUtc;
    }

    public bool IsEnabled { get; }
    public DateTimeOffset? EnabledAtUtc { get; }

    public static YouTubeConnectionPermissionSnapshot Disabled { get; } =
        new(false, null);
}

public interface IYouTubeConnectionPermissionStore
{
    bool IsPersistent { get; }
    YouTubeConnectionPermissionSnapshot Current { get; }
    void Replace(YouTubeConnectionPermissionSnapshot permission);
}

public sealed class InMemoryYouTubeConnectionPermissionStore :
    IYouTubeConnectionPermissionStore
{
    private YouTubeConnectionPermissionSnapshot _current;

    public InMemoryYouTubeConnectionPermissionStore(
        YouTubeConnectionPermissionSnapshot? initial = null)
    {
        _current = initial ?? YouTubeConnectionPermissionSnapshot.Disabled;
    }

    public bool IsPersistent => false;
    public YouTubeConnectionPermissionSnapshot Current => _current;

    public void Replace(YouTubeConnectionPermissionSnapshot permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        _current = permission;
    }
}

public interface IYouTubeConnectionPermission
{
    event EventHandler? Changed;

    bool IsEnabled { get; }
    bool IsPersistent { get; }
    DateTimeOffset? EnabledAtUtc { get; }
}

public sealed class YouTubeConnectionPermissionState :
    IYouTubeConnectionPermission
{
    private readonly IYouTubeConnectionPermissionStore _store;
    private YouTubeConnectionPermissionSnapshot _current;

    public YouTubeConnectionPermissionState(
        IYouTubeConnectionPermissionStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _current = store.Current;
    }

    public event EventHandler? Changed;

    public bool IsEnabled => _current.IsEnabled;
    public bool IsPersistent => _store.IsPersistent;
    public DateTimeOffset? EnabledAtUtc => _current.EnabledAtUtc;

    public void Enable(DateTimeOffset enabledAtUtc)
    {
        if (enabledAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The YouTube connection permission time must use UTC.",
                nameof(enabledAtUtc));
        }
        if (IsEnabled)
        {
            return;
        }

        Replace(new YouTubeConnectionPermissionSnapshot(
            true,
            enabledAtUtc));
    }

    public void Disable()
    {
        if (!IsEnabled)
        {
            return;
        }

        Replace(YouTubeConnectionPermissionSnapshot.Disabled);
    }

    private void Replace(YouTubeConnectionPermissionSnapshot permission)
    {
        _store.Replace(permission);
        _current = permission;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
