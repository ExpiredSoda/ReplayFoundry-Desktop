using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonGenerationGameContextMemory :
    IGenerationGameContextMemory
{
    private const string SchemaVersion =
        "replayfoundry-game-context-memory-1.1";
    private const string PreviousSchemaVersion =
        "replayfoundry-game-context-memory-1.0";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };
    private readonly object _gate = new();
    private readonly string _path;

    public JsonGenerationGameContextMemory(string? path = null)
    {
        _path = ReplayFoundryLocalDataPaths.Resolve(
            path,
            "game-context-memory.json");
    }

    public GenerationSourceGameContext? Find(string sourceFullPath)
    {
        string key = SourceDirectoryKey(sourceFullPath);
        lock (_gate)
        {
            MemoryDocument document = Read();
            MemoryEntry? entry = document.Entries.SingleOrDefault(value =>
                value.SourceDirectoryKey.Equals(
                    key,
                    StringComparison.Ordinal));
            return entry is null
                ? null
                : new GenerationSourceGameContext(
                    sourceFullPath,
                    entry.GameName,
                    entry.ContextNotes,
                    GenerationGameContextOrigin.ReusedUserMemory,
                    entry.UseOpenGameKnowledge ?? false);
        }
    }

    public void Remember(IEnumerable<GenerationSourceGameContext> contexts)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        GenerationSourceGameContext[] supplied = contexts.ToArray();
        if (supplied.Any(static value => value is null))
        {
            throw new ArgumentException(
                "Game context memory cannot retain null entries.",
                nameof(contexts));
        }

        lock (_gate)
        {
            MemoryDocument current = Read();
            var entries = current.Entries.ToDictionary(
                static value => value.SourceDirectoryKey,
                StringComparer.Ordinal);
            string updatedUtc = DateTimeOffset.UtcNow.ToString("O");
            foreach (GenerationSourceGameContext context in supplied.Where(
                         static value => value.Origin is
                             GenerationGameContextOrigin.UserConfirmed or
                             GenerationGameContextOrigin.ReusedUserMemory))
            {
                string key = SourceDirectoryKey(context.SourceFullPath);
                entries[key] = new MemoryEntry(
                    key,
                    context.GameName,
                    context.ContextNotes,
                    context.UseOpenGameKnowledge,
                    updatedUtc);
            }

            Write(new MemoryDocument(
                SchemaVersion,
                entries.Values
                    .OrderBy(static value => value.SourceDirectoryKey,
                        StringComparer.Ordinal)
                    .ToArray()));
        }
    }

    private MemoryDocument Read()
    {
        if (!File.Exists(_path))
        {
            return new MemoryDocument(SchemaVersion, []);
        }

        try
        {
            MemoryDocument? document = JsonSerializer.Deserialize<MemoryDocument>(
                File.ReadAllText(_path));
            if (document is null ||
                document.SchemaVersion is not SchemaVersion and
                    not PreviousSchemaVersion ||
                document.Entries is null ||
                document.Entries.Any(value =>
                    value is null ||
                    value.SourceDirectoryKey.Length != 64 ||
                    !value.SourceDirectoryKey.All(Uri.IsHexDigit) ||
                    string.IsNullOrWhiteSpace(value.GameName) ||
                    value.GameName.Length >
                        GenerationSourceGameContext.MaximumGameNameLength ||
                    value.ContextNotes?.Length >
                        GenerationSourceGameContext.MaximumNotesLength ||
                    !DateTimeOffset.TryParse(value.UpdatedAtUtc, out DateTimeOffset parsed) ||
                    parsed.Offset != TimeSpan.Zero) ||
                document.Entries.GroupBy(
                        static value => value.SourceDirectoryKey,
                        StringComparer.Ordinal)
                    .Any(static group => group.Count() != 1))
            {
                throw new InvalidDataException(
                    "The local game-context memory file is invalid.");
            }
            return Normalize(document);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The local game-context memory file is not valid JSON.",
                exception);
        }
    }

    private static MemoryDocument Normalize(MemoryDocument document) =>
        new(
            SchemaVersion,
            document.Entries
                .Select(static value => value with
                {
                    // Version 1.0 did not persist this setting. One interim
                    // 1.1 writer retained those missing values while updating
                    // the document schema. Both representations mean the user
                    // had not enabled open game knowledge for that entry.
                    UseOpenGameKnowledge =
                        value.UseOpenGameKnowledge ?? false,
                })
                .ToArray());

    private void Write(MemoryDocument document)
    {
        AtomicJsonFile.Write(_path, document, JsonOptions);
    }

    private static string SourceDirectoryKey(string sourceFullPath)
    {
        if (string.IsNullOrWhiteSpace(sourceFullPath) ||
            !Path.IsPathFullyQualified(sourceFullPath))
        {
            throw new ArgumentException(
                "Game context memory requires a fully qualified source path.",
                nameof(sourceFullPath));
        }
        DirectoryInfo? directory = Directory.GetParent(sourceFullPath);
        if (directory?.Name.Equals(
                "Vertical",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            directory = directory.Parent;
        }
        string canonical = (directory?.FullName ??
            Path.GetDirectoryName(Path.GetFullPath(sourceFullPath)) ??
            Path.GetFullPath(sourceFullPath)).ToUpperInvariant();
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private sealed record MemoryDocument(
        string SchemaVersion,
        MemoryEntry[] Entries);

    private sealed record MemoryEntry(
        string SourceDirectoryKey,
        string GameName,
        string? ContextNotes,
        bool? UseOpenGameKnowledge,
        string UpdatedAtUtc);
}
