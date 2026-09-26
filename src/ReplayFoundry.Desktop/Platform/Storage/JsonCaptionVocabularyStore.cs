using System.IO;
using System.Text.Json;

namespace ReplayFoundry.Desktop.Platform.Storage;

/// <summary>Only explicit creator vocabulary and optional manual corrections enter this store.</summary>
public sealed class JsonCaptionVocabularyStore
{
    private readonly string _path;
    public JsonCaptionVocabularyStore(string? path = null) => _path = ReplayFoundryLocalDataPaths.Resolve(path, "caption-vocabulary.json");
    public IReadOnlyList<string> Load() => File.Exists(_path) ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(_path)) ?? [] : [];
    public string? ReadPrompt()
    {
        string terms = string.Join(", ", Load());
        return terms.Length == 0 ? null : "Names and vocabulary: " + terms;
    }
    public void Save(IEnumerable<string> vocabulary)
    {
        string[] words = vocabulary.Select(s => s.Trim()).Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (words.Length > 100 || words.Any(s => s.Length > 80 || s.Any(char.IsControl)) || string.Join(", ", words).Length > 1900)
            throw new ArgumentException("Keep vocabulary to 100 short terms and 1,900 characters.");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(words)); File.Move(temporary, _path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
