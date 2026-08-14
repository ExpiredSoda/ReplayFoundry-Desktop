using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.Projects;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonStudioProjectStore : IStudioProjectStore
{
    private const string FileName = "studio-project.json";
    private const string BackupFileName = "studio-project.json.bak";
    private readonly string _root;
    private readonly JsonSerializerOptions _options;

    public JsonStudioProjectStore(string? root = null)
    {
        _root = ReplayFoundryLocalDataPaths.Resolve(
            root,
            Path.Combine("Projects", "placeholder"));
        if (root is null)
        {
            _root = Path.GetDirectoryName(_root)!;
        }
        else
        {
            _root = Path.GetFullPath(root);
        }
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            MaxDepth = 128,
        };
        _options.Converters.Add(new JsonStringEnumConverter());
    }

    public string Root => _root;

    public void Save(
        GenerationOutputProject project,
        long revision,
        StudioProjectRecoveryState? recovery = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (revision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        string directory = ResolveProjectDirectory(project.Id);
        Directory.CreateDirectory(directory);
        string contentId = Path.GetFileName(directory);
        using var saveMutex = new Mutex(
            initiallyOwned: false,
            "Local\\ReplayFoundry.StudioProject." + contentId);
        bool ownsMutex = false;
        try
        {
            try
            {
                ownsMutex = saveMutex.WaitOne(TimeSpan.FromSeconds(15));
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }
            if (!ownsMutex)
            {
                throw new IOException(
                    "Another Replay Foundry process is still saving this Studio project.");
            }

            SaveExclusive(project, revision, recovery, directory);
        }
        finally
        {
            if (ownsMutex)
            {
                saveMutex.ReleaseMutex();
            }
        }
    }

    private void SaveExclusive(
        GenerationOutputProject project,
        long revision,
        StudioProjectRecoveryState? recovery,
        string directory)
    {
        string target = Path.Combine(directory, FileName);
        string backup = Path.Combine(directory, BackupFileName);
        CleanupAbandonedStaging(directory);
        string temporary = Path.Combine(
            directory,
            FileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            StudioProjectDocument? existing = null;
            if (File.Exists(target))
            {
                try
                {
                    existing = ReadSupportedDocument(target);
                }
                catch (UnsupportedStudioProjectSchemaException exception)
                {
                    throw new InvalidOperationException(
                        "Replay Foundry will not overwrite a Studio project from a newer schema.",
                        exception);
                }
                catch (Exception exception) when (IsInvalidDocument(exception))
                {
                    // A new valid save may repair a corrupt primary while the
                    // atomic replacement retains it as diagnostic history.
                }
            }
            if (existing is not null && existing.Revision >= revision)
            {
                throw new InvalidOperationException(
                    "A Studio project save cannot replace the same or a newer revision.");
            }

            StudioProjectDocument document =
                StudioProjectDocumentMapper.Capture(
                    project,
                    revision,
                    DateTimeOffset.UtcNow,
                    recovery);
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
                document,
                _options);
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 64 * 1024,
                       FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }

            StudioProjectDocument staged = ReadSupportedDocument(temporary);
            _ = StudioProjectDocumentMapper.Restore(staged);
            if (File.Exists(target))
            {
                File.Replace(
                    temporary,
                    target,
                    backup,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, target);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public StudioProjectLoadResult Load(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        string directory = ResolveProjectDirectory(projectId);
        string target = Path.Combine(directory, FileName);
        string backup = Path.Combine(directory, BackupFileName);
        if (!File.Exists(target))
        {
            return new StudioProjectLoadResult(
                StudioProjectLoadOutcome.NotFound,
                "No durable Studio project was found.");
        }

        try
        {
            StudioProjectDocument document = ReadSupportedDocument(target);
            return ValidateSources(document, recovered: false);
        }
        catch (UnsupportedStudioProjectSchemaException exception)
        {
            return new StudioProjectLoadResult(
                StudioProjectLoadOutcome.UnsupportedSchema,
                exception.Message);
        }
        catch (Exception exception) when (IsInvalidDocument(exception))
        {
            if (File.Exists(backup))
            {
                try
                {
                    StudioProjectDocument recovered =
                        ReadSupportedDocument(backup);
                    return ValidateSources(recovered, recovered: true);
                }
                catch (UnsupportedStudioProjectSchemaException backupException)
                {
                    return new StudioProjectLoadResult(
                        StudioProjectLoadOutcome.UnsupportedSchema,
                        backupException.Message);
                }
                catch (Exception backupException)
                    when (IsInvalidDocument(backupException))
                {
                }
            }

            return new StudioProjectLoadResult(
                StudioProjectLoadOutcome.Corrupt,
                "The Studio project and its previous-save backup are unreadable. The files were preserved for diagnosis.");
        }
    }

    public bool Exists(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return false;
        }
        return File.Exists(Path.Combine(
            ResolveProjectDirectory(projectId),
            FileName));
    }

    public void Delete(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        string directory = ResolveProjectDirectory(projectId);
        if (!Directory.Exists(directory))
        {
            return;
        }
        Directory.Delete(directory, recursive: true);
    }

    public string ResolveProjectDirectory(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        string hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(projectId.Trim()))).ToLowerInvariant();
        string path = Path.GetFullPath(Path.Combine(_root, hash));
        string normalizedRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(_root)) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The Studio project identity resolved outside its local store.");
        }
        return path;
    }

    private StudioProjectLoadResult ValidateSources(
        StudioProjectDocument document,
        bool recovered)
    {
        GenerationOutputProject project =
            StudioProjectDocumentMapper.Restore(document);
        string[] missing = document.Sources
            .Where(static source => !File.Exists(source.FullPath))
            .Select(static source => source.FullPath)
            .ToArray();
        if (missing.Length > 0)
        {
            return new StudioProjectLoadResult(
                StudioProjectLoadOutcome.MissingSource,
                "The Studio project was preserved, but one or more source files are missing.",
                project,
                document,
                missing);
        }

        string[] changed = document.Sources.Where(source =>
        {
            var info = new FileInfo(source.FullPath);
            return info.Length != source.Length ||
                   new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero) !=
                   source.LastWriteUtc;
        }).Select(static source => source.FullPath).ToArray();
        if (changed.Length > 0)
        {
            return new StudioProjectLoadResult(
                StudioProjectLoadOutcome.ChangedSource,
                "The Studio project was preserved, but one or more source files changed after generation.",
                project,
                document,
                changed);
        }

        return new StudioProjectLoadResult(
            recovered
                ? StudioProjectLoadOutcome.RecoveredPreviousSave
                : StudioProjectLoadOutcome.Loaded,
            recovered
                ? "Studio recovered the last valid previous save."
                : "Studio loaded the durable project without rerunning analysis.",
            project,
            document);
    }

    private StudioProjectDocument ReadSupportedDocument(string path)
    {
        using JsonDocument envelope = JsonDocument.Parse(
            File.ReadAllBytes(path),
            new JsonDocumentOptions
            {
                MaxDepth = 128,
                CommentHandling = JsonCommentHandling.Disallow,
            });
        if (!envelope.RootElement.TryGetProperty(
                "schemaVersion",
                out JsonElement schema) ||
            schema.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                "The Studio project has no schema identity.");
        }
        string? value = schema.GetString();
        if (value is not StudioProjectDocument.CurrentSchemaVersion and
                not StudioProjectDocument.PreviousSchemaVersion)
        {
            throw new UnsupportedStudioProjectSchemaException(value);
        }

        StudioProjectDocument? document = JsonSerializer.Deserialize<
            StudioProjectDocument>(
            envelope.RootElement.GetRawText(),
            _options);
        return document ?? throw new InvalidDataException(
            "The Studio project document was empty.");
    }

    private static void CleanupAbandonedStaging(string directory)
    {
        foreach (string path in Directory.EnumerateFiles(
                     directory,
                     FileName + ".*.tmp",
                     SearchOption.TopDirectoryOnly))
        {
            File.Delete(path);
        }
    }

    private static bool IsInvalidDocument(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or
            JsonException or InvalidDataException or ArgumentException or
            InvalidOperationException;

    private sealed class UnsupportedStudioProjectSchemaException :
        Exception
    {
        public UnsupportedStudioProjectSchemaException(string? value)
            : base(
                "The Studio project uses an unsupported schema" +
                (string.IsNullOrWhiteSpace(value)
                    ? "."
                    : $" ({value}).") +
                " Replay Foundry left it unchanged.")
        {
        }
    }
}
