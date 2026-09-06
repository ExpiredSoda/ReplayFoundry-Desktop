using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Studio.Editing;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed record StudioCaptionLookDocument(int SchemaVersion, StudioNamedCaptionLook[] Looks);
public sealed class JsonStudioCaptionLookStore : IStudioCaptionLookStore
{
    public const int CurrentSchemaVersion = 1;
    private readonly string _path;
    public JsonStudioCaptionLookStore(string? path = null) => _path = ReplayFoundryLocalDataPaths.Resolve(path, "caption-looks.json");
    public IReadOnlyList<StudioNamedCaptionLook> Load()
    {
        if (!File.Exists(_path)) return [];
        if (new FileInfo(_path).Length > 2 * 1024 * 1024)
            throw new InvalidDataException("The saved caption look catalog is too large.");
        using var json = JsonDocument.Parse(File.ReadAllText(_path));
        // Legacy arrays remain readable without rewriting a catalog during startup.
        // The next explicit save atomically migrates the complete catalog to v1.
        StudioNamedCaptionLook[] looks;
        if (json.RootElement.ValueKind == JsonValueKind.Array)
            looks = json.RootElement.Deserialize<StudioNamedCaptionLook[]>() ?? [];
        else if (json.RootElement.ValueKind == JsonValueKind.Object)
        {
            var document = json.RootElement.Deserialize<StudioCaptionLookDocument>();
            if (document is null || document.SchemaVersion != CurrentSchemaVersion || document.Looks is null)
                throw new InvalidDataException("The saved caption look catalog uses an unsupported schema version.");
            looks = document.Looks;
        }
        else throw new InvalidDataException("The saved caption look catalog is not a supported document.");
        if (looks.Length > 1000 || looks.Any(static item => item is null || item.Look is null ||
                string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 80))
            throw new InvalidDataException("The saved caption look catalog contains an invalid entry.");
        return Array.AsReadOnly(looks);
    }
    public void Save(string name, StudioCaptionLook look)
    {
        ArgumentNullException.ThrowIfNull(look);
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80) throw new ArgumentException("Name the look using 1–80 characters.");
        var looks = Load().Where(x => !x.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (looks.Count >= 1000) throw new InvalidOperationException("The caption look catalog already contains 1,000 named looks.");
        looks.Add(new(name.Trim(), look));
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(new StudioCaptionLookDocument(CurrentSchemaVersion, looks.ToArray()), new JsonSerializerOptions { WriteIndented = true })); File.Move(temporary, _path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
