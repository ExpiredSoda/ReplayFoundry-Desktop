using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Publish.YouTube;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonYouTubePublishPreferencesStore :
    IYouTubePublishPreferencesStore
{
    private const string SchemaVersion =
        "replayfoundry-youtube-publish-preferences-1.0";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly string _path;
    private IReadOnlyList<YouTubePreferredScheduleSlot> _slots;

    public JsonYouTubePublishPreferencesStore(string? path = null)
    {
        _path = ReplayFoundryLocalDataPaths.Resolve(
            path,
            "youtube-publish-preferences.json");
        _slots = Load(_path);
    }

    public IReadOnlyList<YouTubePreferredScheduleSlot> PreferredSlots
    {
        get
        {
            lock (_gate)
            {
                return _slots;
            }
        }
    }

    public void Replace(IEnumerable<YouTubePreferredScheduleSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        YouTubePreferredScheduleSlot[] snapshot = slots
            .DistinctBy(static value => value.Id)
            .OrderBy(static value => value.Day)
            .ThenBy(static value => value.LocalTime)
            .ToArray();
        if (snapshot.Any(static value => !Enum.IsDefined(value.Day)))
        {
            throw new ArgumentException(
                "Preferred YouTube schedule days must be defined.",
                nameof(slots));
        }
        lock (_gate)
        {
            Write(snapshot);
            _slots = Array.AsReadOnly(snapshot);
        }
    }

    private static IReadOnlyList<YouTubePreferredScheduleSlot> Load(
        string path)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<YouTubePreferredScheduleSlot>();
        }
        try
        {
            PreferencesDocument? document =
                JsonSerializer.Deserialize<PreferencesDocument>(
                    File.ReadAllText(path),
                    JsonOptions);
            if (document is null ||
                document.SchemaVersion != SchemaVersion ||
                document.Slots is null)
            {
                throw new InvalidDataException(
                    "The local YouTube publish-preferences schema is unsupported.");
            }
            YouTubePreferredScheduleSlot[] slots = document.Slots
                .Select(static value =>
                {
                    if (!Enum.TryParse(
                            value.Day,
                            ignoreCase: false,
                            out DayOfWeek day) ||
                        !TimeOnly.TryParseExact(
                            value.LocalTime,
                            "HH:mm",
                            out TimeOnly localTime))
                    {
                        throw new InvalidDataException(
                            "A preferred YouTube release slot is invalid.");
                    }
                    return new YouTubePreferredScheduleSlot(day, localTime);
                })
                .DistinctBy(static value => value.Id)
                .OrderBy(static value => value.Day)
                .ThenBy(static value => value.LocalTime)
                .ToArray();
            return Array.AsReadOnly(slots);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The local YouTube publish-preferences file is not valid JSON.",
                exception);
        }
    }

    private void Write(IReadOnlyList<YouTubePreferredScheduleSlot> slots)
    {
        var document = new PreferencesDocument
        {
            SchemaVersion = SchemaVersion,
            Slots = slots.Select(static value =>
                new SlotDocument
                {
                    Day = value.Day.ToString(),
                    LocalTime = value.LocalTime.ToString("HH:mm"),
                }).ToArray(),
        };
        AtomicJsonFile.Write(_path, document, JsonOptions);
    }

    private sealed class PreferencesDocument
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public SlotDocument[]? Slots { get; set; }
    }

    private sealed class SlotDocument
    {
        public string Day { get; set; } = string.Empty;
        public string LocalTime { get; set; } = string.Empty;
    }
}
