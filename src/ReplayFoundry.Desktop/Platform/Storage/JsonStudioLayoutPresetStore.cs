using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Studio.Editing;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonStudioLayoutPresetStore : IStudioLayoutPresetStore
{
    private const string Schema = "replayfoundry-studio-layout-presets-1.0";
    private readonly string _path;
    public JsonStudioLayoutPresetStore(string? path = null) => _path = ReplayFoundryLocalDataPaths.Resolve(path, "studio-layout-presets.json");
    public IReadOnlyList<StudioLayoutPreset> Load()
    {
        if (!File.Exists(_path)) return [];
        if (new FileInfo(_path).Length > 1024 * 1024) throw new InvalidDataException("Saved layouts exceeded the local size limit.");
        Document? document = JsonSerializer.Deserialize<Document>(File.ReadAllText(_path));
        if (document?.SchemaVersion != Schema || document.Presets is null || document.Presets.Length > 100 || document.Presets.Any(preset => preset is null))
            throw new InvalidDataException("The saved layout document is invalid or unsupported.");
        return document.Presets;
    }
    public void Save(StudioLayoutPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var entries = Load().Where(value => !value.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(value.GameName, preset.GameName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (entries.Count >= 100) throw new InvalidOperationException("The local library already contains 100 layouts. Reuse an existing name to replace one.");
        entries.Add(preset);
        AtomicJsonFile.Write(_path, new Document { SchemaVersion = Schema, Presets = entries.ToArray() }, new JsonSerializerOptions { WriteIndented = true });
    }
    private sealed class Document
    {
        public string SchemaVersion { get; set; } = "";
        public StudioLayoutPreset[]? Presets { get; set; }
    }
}
