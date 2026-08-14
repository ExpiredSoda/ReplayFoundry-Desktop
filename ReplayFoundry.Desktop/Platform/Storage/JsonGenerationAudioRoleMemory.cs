using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Preparation;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonGenerationAudioRoleMemory : IGenerationAudioRoleMemory
{
    private const string SchemaVersion = "1.0";
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public JsonGenerationAudioRoleMemory(string? path = null)
    {
        _path = Path.GetFullPath(path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ReplayFoundry",
            "GenerationAudioRoles.json"));
    }

    public RememberedGenerationAudioRole? Find(PreparedGenerationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        string fingerprint = GenerationAudioLayoutFingerprint.Create(source.Media);
        Entry? entry = Read().Entries.LastOrDefault(value =>
            value.LayoutFingerprint.Equals(fingerprint, StringComparison.Ordinal));
        if (entry is null ||
            !source.Media.AudioStreams.Any(stream =>
                stream.Index == entry.AbsoluteAudioStreamIndex) ||
            !Enum.TryParse(entry.ContentRole, ignoreCase: false,
                out CaptionAudioContentRole role) ||
            !Enum.TryParse(entry.LanguagePolicy, ignoreCase: false,
                out GenerationCaptionLanguagePolicy language) ||
            !Enum.IsDefined(role) || !Enum.IsDefined(language))
        {
            return null;
        }
        return new RememberedGenerationAudioRole(
            entry.AbsoluteAudioStreamIndex,
            role,
            language);
    }

    public void Remember(
        IEnumerable<PreparedGenerationSource> sources,
        IEnumerable<GenerationCaptionSourceSelection> selections)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(selections);
        PreparedGenerationSource[] sourceSnapshot = sources.ToArray();
        GenerationCaptionSourceSelection[] selectionSnapshot = selections.ToArray();
        Document document = Read();
        var entries = document.Entries.ToList();
        foreach (GenerationCaptionSourceSelection selection in selectionSnapshot)
        {
            PreparedGenerationSource? source = sourceSnapshot.SingleOrDefault(value =>
                value.Media.FullPath.Equals(
                    selection.SourceFullPath,
                    StringComparison.OrdinalIgnoreCase));
            if (source is null)
            {
                continue;
            }
            string fingerprint = GenerationAudioLayoutFingerprint.Create(source.Media);
            entries.RemoveAll(value =>
                value.LayoutFingerprint.Equals(fingerprint, StringComparison.Ordinal));
            entries.Add(new Entry(
                fingerprint,
                selection.AbsoluteAudioStreamIndex,
                selection.ContentRole.ToString(),
                selection.LanguagePolicy.ToString(),
                DateTimeOffset.UtcNow));
        }
        Write(new Document(SchemaVersion, entries
            .OrderBy(static value => value.LayoutFingerprint, StringComparer.Ordinal)
            .ToArray()));
    }

    private Document Read()
    {
        if (!File.Exists(_path))
        {
            return new Document(SchemaVersion, []);
        }
        try
        {
            Document? document = JsonSerializer.Deserialize<Document>(
                File.ReadAllText(_path));
            return document is not null &&
                   document.SchemaVersion == SchemaVersion &&
                   document.Entries is not null
                ? document
                : new Document(SchemaVersion, []);
        }
        catch (JsonException)
        {
            return new Document(SchemaVersion, []);
        }
        catch (IOException)
        {
            return new Document(SchemaVersion, []);
        }
    }

    private void Write(Document document)
    {
        string? directory = Path.GetDirectoryName(_path);
        Directory.CreateDirectory(directory!);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(document, IndentedJson));
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private sealed record Document(string SchemaVersion, Entry[] Entries);

    private sealed record Entry(
        string LayoutFingerprint,
        int AbsoluteAudioStreamIndex,
        string ContentRole,
        string LanguagePolicy,
        DateTimeOffset ConfirmedAtUtc);
}
