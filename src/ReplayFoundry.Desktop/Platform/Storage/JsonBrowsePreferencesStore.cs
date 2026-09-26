using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Platform.Diagnostics;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonBrowsePreferencesStore : IBrowsePreferencesStore
{
    private readonly string _path;
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    public JsonBrowsePreferencesStore(string? path = null)
    {
        _path = ReplayFoundryLocalDataPaths.Resolve(path, "browse-preferences.json");
        try
        {
            if (File.Exists(_path))
            {
                if (new FileInfo(_path).Length > 131072) throw new InvalidDataException("Browse preferences exceed their supported size.");
                _values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? [];
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { SafeDiagnosticTrace.Write("Browse preferences could not be read", exception); }
    }
    public string? Get(string key) => _values.GetValueOrDefault(key);
    public void Set(string key, string value)
    {
        if (Get(key) == value) return;
        _values[key] = value;
        try { AtomicJsonFile.Write(_path, _values, ReplayFoundryLocalJsonPolicy.IndentedCamelCase); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { SafeDiagnosticTrace.Write("Browse preferences could not be saved", exception); }
    }
}
