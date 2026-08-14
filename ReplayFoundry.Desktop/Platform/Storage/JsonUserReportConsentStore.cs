using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Diagnostics;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonUserReportConsentStore : IUserReportConsentStore
{
    private const string SchemaVersion = "bug-report-consent-1.0";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private readonly string _path;
    private UserReportConsentSnapshot _current;

    public JsonUserReportConsentStore(string? path = null)
    {
        _path = ReplayFoundryLocalDataPaths.Resolve(
            path,
            "bug-report-consent.json");
        _current = Load(_path);
    }

    public bool IsPersistent => true;
    public UserReportConsentSnapshot Current => _current;

    public void Replace(UserReportConsentSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        AtomicJsonFile.Write(_path, new Document
        {
            SchemaVersion = SchemaVersion,
            IsEnabled = value.IsEnabled,
            EnabledAtUtc = value.EnabledAtUtc,
            NoticeVersion = value.NoticeVersion,
        }, JsonOptions);
        _current = value;
    }

    private static UserReportConsentSnapshot Load(string path)
    {
        if (!File.Exists(path)) return UserReportConsentSnapshot.Disabled;
        try
        {
            Document? document = JsonSerializer.Deserialize<Document>(
                File.ReadAllText(path), JsonOptions);
            if (document?.SchemaVersion != SchemaVersion)
            {
                throw new InvalidDataException(
                    "The bug-report consent schema is unsupported.");
            }
            return new UserReportConsentSnapshot(
                document.IsEnabled,
                document.EnabledAtUtc,
                document.NoticeVersion);
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException)
        {
            throw new InvalidDataException(
                "The bug-report consent file is invalid.",
                exception);
        }
    }

    private sealed class Document
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public DateTimeOffset? EnabledAtUtc { get; set; }
        public string NoticeVersion { get; set; } = string.Empty;
    }
}
