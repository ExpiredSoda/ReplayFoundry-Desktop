using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Settings;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonEditorialRerollPreferenceStore :
    IEditorialRerollPreferenceStore
{
    private const string SchemaVersion =
        "replayfoundry-editorial-reroll-preference-1.0";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly string _path;
    private EditorialRerollPreferenceSnapshot _current;

    public JsonEditorialRerollPreferenceStore(string? path = null)
    {
        _path = ReplayFoundryLocalDataPaths.Resolve(
            path,
            "editorial-reroll-preference.json");
        _current = Load(_path);
    }

    public bool IsPersistent => true;

    public EditorialRerollPreferenceSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Replace(EditorialRerollPreferenceSnapshot preference)
    {
        ArgumentNullException.ThrowIfNull(preference);
        lock (_gate)
        {
            Write(preference);
            _current = preference;
        }
    }

    private static EditorialRerollPreferenceSnapshot Load(string path)
    {
        if (!File.Exists(path))
        {
            return new EditorialRerollPreferenceSnapshot(UseLocalAi: false);
        }

        try
        {
            Document? document = JsonSerializer.Deserialize<Document>(
                File.ReadAllText(path),
                JsonOptions);
            if (document?.SchemaVersion != SchemaVersion)
            {
                throw new InvalidDataException(
                    "The editorial reroll preference schema is unsupported.");
            }

            return new EditorialRerollPreferenceSnapshot(
                document.UseLocalAi);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The editorial reroll preference is not valid JSON.",
                exception);
        }
    }

    private void Write(EditorialRerollPreferenceSnapshot preference)
    {
        var document = new Document
        {
            SchemaVersion = SchemaVersion,
            UseLocalAi = preference.UseLocalAi,
        };
        AtomicJsonFile.Write(_path, document, JsonOptions);
    }

    private sealed class Document
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public bool UseLocalAi { get; set; }
    }
}
