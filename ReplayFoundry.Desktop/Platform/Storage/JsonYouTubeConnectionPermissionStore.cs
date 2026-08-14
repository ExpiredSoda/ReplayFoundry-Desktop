using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Settings;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonYouTubeConnectionPermissionStore :
    IYouTubeConnectionPermissionStore
{
    private const string SchemaVersion =
        "replayfoundry-youtube-connection-permission-1.0";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly string _path;
    private YouTubeConnectionPermissionSnapshot _current;

    public JsonYouTubeConnectionPermissionStore(string? path = null)
    {
        _path = ReplayFoundryLocalDataPaths.Resolve(
            path,
            "youtube-connection-permission.json");
        _current = Load(_path);
    }

    public bool IsPersistent => true;

    public YouTubeConnectionPermissionSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Replace(YouTubeConnectionPermissionSnapshot permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        lock (_gate)
        {
            Write(permission);
            _current = permission;
        }
    }

    private static YouTubeConnectionPermissionSnapshot Load(string path)
    {
        if (!File.Exists(path))
        {
            return YouTubeConnectionPermissionSnapshot.Disabled;
        }

        try
        {
            PermissionDocument? document =
                JsonSerializer.Deserialize<PermissionDocument>(
                    File.ReadAllText(path),
                    JsonOptions);
            if (document is null ||
                document.SchemaVersion != SchemaVersion)
            {
                throw new InvalidDataException(
                    "The local YouTube connection-permission schema is unsupported.");
            }

            DateTimeOffset? enabledAtUtc = null;
            if (document.EnabledAtUtc is not null)
            {
                if (!DateTimeOffset.TryParse(
                        document.EnabledAtUtc,
                        out DateTimeOffset parsed) ||
                    parsed.Offset != TimeSpan.Zero)
                {
                    throw new InvalidDataException(
                        "The local YouTube connection-permission time is invalid.");
                }
                enabledAtUtc = parsed;
            }

            return new YouTubeConnectionPermissionSnapshot(
                document.IsEnabled,
                enabledAtUtc);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The local YouTube connection-permission file is not valid JSON.",
                exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "The local YouTube connection-permission values are inconsistent.",
                exception);
        }
    }

    private void Write(YouTubeConnectionPermissionSnapshot permission)
    {
        var document = new PermissionDocument
        {
            SchemaVersion = SchemaVersion,
            IsEnabled = permission.IsEnabled,
            EnabledAtUtc = permission.EnabledAtUtc?.ToString("O"),
        };
        AtomicJsonFile.Write(_path, document, JsonOptions);
    }

    private sealed class PermissionDocument
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public string? EnabledAtUtc { get; set; }
    }
}
