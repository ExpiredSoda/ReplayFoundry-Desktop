using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Rendering;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonGenerationOutputLocationStore :
    IGenerationOutputLocationStore
{
    private const string SchemaVersion =
        "replayfoundry-generation-output-location-1.0";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private readonly string _path;

    public JsonGenerationOutputLocationStore(string? path = null)
    {
        _path = ReplayFoundryLocalDataPaths.Resolve(
            path,
            "generation-output-location.json");
        CustomRootDirectory = Load(_path);
    }

    public string? CustomRootDirectory { get; private set; }
    public bool IsPersistent => true;

    public void Save(string? customRootDirectory)
    {
        if (!string.IsNullOrWhiteSpace(customRootDirectory) &&
            !Path.IsPathFullyQualified(customRootDirectory))
        {
            throw new ArgumentException(
                "The generation output root must be fully qualified.",
                nameof(customRootDirectory));
        }
        string? normalized = string.IsNullOrWhiteSpace(customRootDirectory)
            ? null
            : Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(customRootDirectory));
        var document = new Document
        {
            SchemaVersion = SchemaVersion,
            CustomRootDirectory = normalized,
        };
        AtomicJsonFile.Write(_path, document, JsonOptions);
        CustomRootDirectory = normalized;
    }

    private static string? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            Document? document = JsonSerializer.Deserialize<Document>(
                File.ReadAllText(path),
                JsonOptions);
            if (document?.SchemaVersion != SchemaVersion)
            {
                throw new InvalidDataException(
                    "The generation output-location schema is unsupported.");
            }
            if (string.IsNullOrWhiteSpace(document.CustomRootDirectory))
            {
                return null;
            }
            if (!Path.IsPathFullyQualified(document.CustomRootDirectory))
            {
                throw new InvalidDataException(
                    "The stored generation output root is not fully qualified.");
            }
            return Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(document.CustomRootDirectory));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The generation output-location settings are invalid.",
                exception);
        }
    }

    private sealed class Document
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public string? CustomRootDirectory { get; set; }
    }
}
