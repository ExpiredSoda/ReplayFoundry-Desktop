using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Research;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonResearchParticipationStore :
    IResearchParticipationStore
{
    private const string SchemaVersion = "research-participation-1.0";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private readonly string _path;
    private ResearchParticipationSnapshot _current;

    public JsonResearchParticipationStore(string? path = null)
    {
        _path = ReplayFoundryLocalDataPaths.Resolve(
            path,
            "research-participation.json");
        _current = Load(_path);
    }

    public bool IsPersistent => true;
    public ResearchParticipationSnapshot Current => _current;

    public void Replace(ResearchParticipationSnapshot value)
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

    private static ResearchParticipationSnapshot Load(string path)
    {
        if (!File.Exists(path)) return ResearchParticipationSnapshot.Disabled;
        try
        {
            Document? document = JsonSerializer.Deserialize<Document>(
                File.ReadAllText(path), JsonOptions);
            if (document?.SchemaVersion != SchemaVersion)
            {
                throw new InvalidDataException(
                    "The research-participation schema is unsupported.");
            }
            return new ResearchParticipationSnapshot(
                document.IsEnabled,
                document.EnabledAtUtc,
                document.NoticeVersion);
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException)
        {
            throw new InvalidDataException(
                "The research-participation file is invalid.", exception);
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
