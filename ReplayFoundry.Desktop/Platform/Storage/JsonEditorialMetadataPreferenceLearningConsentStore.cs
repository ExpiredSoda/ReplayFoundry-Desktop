using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Settings;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonEditorialMetadataPreferenceLearningConsentStore :
    IEditorialMetadataPreferenceLearningConsentStore
{
    public const string SchemaVersion =
        "editorial-metadata-preference-learning-consent-1.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly string _path;
    private EditorialMetadataPreferenceLearningConsentSnapshot _current;

    public JsonEditorialMetadataPreferenceLearningConsentStore(
        string? path = null)
    {
        _path = ReplayFoundryLocalDataPaths.Resolve(
            path,
            "editorial-metadata-preference-consent.json");
        _current = Load(_path);
    }

    public bool IsPersistent => true;

    public EditorialMetadataPreferenceLearningConsentSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Replace(
        EditorialMetadataPreferenceLearningConsentSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            AtomicJsonFile.Write(_path, new ConsentDocument
            {
                SchemaVersion = SchemaVersion,
                IsEnabled = value.IsEnabled,
                EnabledAtUtc = value.EnabledAtUtc,
                NoticeVersion = value.NoticeVersion,
            }, JsonOptions);
            _current = value;
        }
    }

    private static EditorialMetadataPreferenceLearningConsentSnapshot Load(
        string path)
    {
        if (!File.Exists(path))
        {
            return EditorialMetadataPreferenceLearningConsentSnapshot
                .Disabled;
        }

        try
        {
            ConsentDocument? document =
                JsonSerializer.Deserialize<ConsentDocument>(
                    File.ReadAllText(path),
                    JsonOptions);
            if (document?.SchemaVersion != SchemaVersion)
            {
                throw new InvalidDataException(
                    "The editorial metadata preference-learning consent schema is unsupported.");
            }
            return new EditorialMetadataPreferenceLearningConsentSnapshot(
                document.IsEnabled,
                document.EnabledAtUtc,
                document.NoticeVersion);
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException)
        {
            throw new InvalidDataException(
                "The editorial metadata preference-learning consent file is invalid.",
                exception);
        }
    }

    private sealed class ConsentDocument
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public DateTimeOffset? EnabledAtUtc { get; set; }
        public string NoticeVersion { get; set; } = string.Empty;
    }
}
