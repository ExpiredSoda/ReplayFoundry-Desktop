using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonGenerationGameContextMemory :
    IGenerationGameContextMemory,
    IGameKnowledgePermissionStatus
{
    internal const string DefaultFileName = "game-context-memory.json";
    private const string SchemaPrefix =
        "replayfoundry-game-context-memory-";
    private const string QuarantineDirectoryName = "GameContextMemory";
    private const string SchemaVersion =
        SchemaPrefix + "1.2";
    private const string PreviousSchemaVersion =
        SchemaPrefix + "1.1";
    private const string OriginalSchemaVersion =
        SchemaPrefix + "1.0";
    private static readonly TimeSpan ReadTransactionWait = TimeSpan.Zero;
    private static readonly TimeSpan WriteTransactionWait =
        TimeSpan.FromMilliseconds(250);
    private static readonly JsonSerializerOptions JsonOptions =
        ReplayFoundryLocalJsonPolicy.IndentedDeclaredPropertyNames;
    private readonly object _gate = new();
    private readonly string _path;
    private readonly string _transactionMutexName;

    public JsonGenerationGameContextMemory(string? path = null)
    {
        _path = ReplayFoundryLocalDataPaths.Resolve(
            path,
            DefaultFileName);
        _transactionMutexName = CreateTransactionMutexName(_path);
    }

    public event EventHandler? Changed;

    public bool HasRememberedWikimediaPermission
    {
        get
        {
            lock (_gate)
            {
                using MemoryTransactionLease? transaction =
                    MemoryTransactionLease.TryAcquire(
                        _transactionMutexName,
                        ReadTransactionWait);
                return transaction is not null &&
                    ReadExclusive().Document.Entries.Any(static entry =>
                        entry.UseOpenGameKnowledge == true &&
                        entry.ConfirmedIdentity is not null);
            }
        }
    }

    public GenerationSourceGameContext? Find(string sourceFullPath)
    {
        string key = SourceDirectoryKey(sourceFullPath);
        lock (_gate)
        {
            using MemoryTransactionLease? transaction =
                MemoryTransactionLease.TryAcquire(
                    _transactionMutexName,
                    ReadTransactionWait);
            if (transaction is null)
            {
                return null;
            }
            MemoryDocument document = ReadExclusive().Document;
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
                    entry.UseOpenGameKnowledge ?? false,
                    RestoreIdentity(entry.ConfirmedIdentity));
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

        bool changed = false;
        lock (_gate)
        {
            using MemoryTransactionLease? transaction =
                MemoryTransactionLease.TryAcquire(
                    _transactionMutexName,
                    WriteTransactionWait);
            if (transaction is null)
            {
                return;
            }
            MemoryReadResult read = ReadExclusive();
            if (!read.CanWrite)
            {
                return;
            }
            MemoryDocument current = read.Document;
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
                    context.ConfirmedIdentity is null
                        ? null
                        : CreateIdentityDocument(context.ConfirmedIdentity),
                    updatedUtc);
            }

            try
            {
                Write(
                    new MemoryDocument(
                        SchemaVersion,
                        entries.Values
                            .OrderBy(static value => value.SourceDirectoryKey,
                                StringComparer.Ordinal)
                            .ToArray()),
                    overwrite: !read.RequiresNewFile);
                changed = true;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                    SecurityException)
            {
                // Remembered setup choices are optional. A transient storage
                // failure must keep the prior file and never block Generate.
            }
        }
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    // Callers hold both the instance gate and the path-scoped process mutex.
    private MemoryReadResult ReadExclusive()
    {
        ActiveReadOutcome readOutcome = TryReadActiveJson(out string json);
        if (readOutcome == ActiveReadOutcome.Missing)
        {
            PendingRecoveryOutcome recovery = TryRestorePendingQuarantine();
            if (recovery == PendingRecoveryOutcome.None)
            {
                return WritableEmptyResult();
            }
            if (recovery == PendingRecoveryOutcome.Blocked)
            {
                return PreservedEmptyResult();
            }
            readOutcome = TryReadActiveJson(out json);
        }
        if (readOutcome != ActiveReadOutcome.Success)
        {
            return PreservedEmptyResult();
        }

        MemoryDocument? document;
        try
        {
            using JsonDocument probe = JsonDocument.Parse(json);
            SchemaCompatibility compatibility = ClassifySchema(
                probe.RootElement);
            if (compatibility == SchemaCompatibility.Unsupported)
            {
                return PreservedEmptyResult();
            }
            if (compatibility == SchemaCompatibility.Invalid)
            {
                return RecoverInvalidDocument(json);
            }

            document = JsonSerializer.Deserialize<MemoryDocument>(
                json);
        }
        catch (JsonException)
        {
            return RecoverInvalidDocument(json);
        }

        return IsValid(document)
            ? new MemoryReadResult(
                Normalize(document!),
                CanWrite: true,
                RequiresNewFile: false)
            : RecoverInvalidDocument(json);
    }

    private ActiveReadOutcome TryReadActiveJson(out string json)
    {
        try
        {
            json = File.ReadAllText(_path);
            return ActiveReadOutcome.Success;
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException)
        {
            json = string.Empty;
            return ActiveReadOutcome.Missing;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                SecurityException)
        {
            json = string.Empty;
            return ActiveReadOutcome.Unavailable;
        }
    }

    private PendingRecoveryOutcome TryRestorePendingQuarantine()
    {
        string? directory = Path.GetDirectoryName(_path);
        if (directory is null)
        {
            return PendingRecoveryOutcome.Blocked;
        }
        string quarantineDirectory = Path.Combine(
            directory,
            "Diagnostics",
            QuarantineDirectoryName);
        string[] pending;
        try
        {
            pending = Directory.GetFiles(
                quarantineDirectory,
                Path.GetFileName(_path) + ".*.recovery-pending.json");
        }
        catch (DirectoryNotFoundException)
        {
            return PendingRecoveryOutcome.None;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                SecurityException)
        {
            return PendingRecoveryOutcome.Blocked;
        }
        if (pending.Length == 0)
        {
            return PendingRecoveryOutcome.None;
        }

        Array.Sort(pending, StringComparer.Ordinal);
        try
        {
            File.Move(pending[0], _path, overwrite: false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                SecurityException)
        {
            // The active path may have reappeared. The caller retries that path
            // and stays non-writable if neither version can be read.
        }
        return PendingRecoveryOutcome.RetryActiveRead;
    }

    private static MemoryDocument EmptyDocument() =>
        new(SchemaVersion, []);

    private static MemoryReadResult WritableEmptyResult() =>
        new(EmptyDocument(), CanWrite: true, RequiresNewFile: true);

    private static MemoryReadResult PreservedEmptyResult() =>
        new(EmptyDocument(), CanWrite: false, RequiresNewFile: false);

    private static SchemaCompatibility ClassifySchema(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("SchemaVersion", out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return SchemaCompatibility.Invalid;
        }

        string? schema = property.GetString();
        if (schema is SchemaVersion or PreviousSchemaVersion or
            OriginalSchemaVersion)
        {
            return SchemaCompatibility.Supported;
        }
        if (string.IsNullOrWhiteSpace(schema))
        {
            return SchemaCompatibility.Invalid;
        }
        // Any nonempty identifier not explicitly supported belongs to another
        // reader. Preserve it byte-for-byte, including preview and renamed
        // future formats that cannot be parsed as System.Version values.
        return SchemaCompatibility.Unsupported;
    }

    private static bool IsValid(MemoryDocument? document) =>
        document is not null &&
        (document.SchemaVersion is SchemaVersion or PreviousSchemaVersion or
            OriginalSchemaVersion) &&
        document.Entries is not null &&
        document.Entries.All(IsValidEntry) &&
        !document.Entries.GroupBy(
                static value => value.SourceDirectoryKey,
                StringComparer.Ordinal)
            .Any(static group => group.Count() != 1);

    private static bool IsValidEntry(MemoryEntry? entry) =>
        entry is not null &&
        entry.SourceDirectoryKey is { Length: 64 } key &&
        key.All(Uri.IsHexDigit) &&
        !string.IsNullOrWhiteSpace(entry.GameName) &&
        entry.GameName.Length <=
            GenerationSourceGameContext.MaximumGameNameLength &&
        (entry.ContextNotes is null ||
            entry.ContextNotes.Length <=
                GenerationSourceGameContext.MaximumNotesLength) &&
        IsValidIdentity(entry.ConfirmedIdentity) &&
        DateTimeOffset.TryParse(
            entry.UpdatedAtUtc,
            out DateTimeOffset updatedAtUtc) &&
        updatedAtUtc.Offset == TimeSpan.Zero;

    private MemoryReadResult RecoverInvalidDocument(string expectedJson)
    {
        bool canWrite = TryQuarantineInvalidDocument(expectedJson);
        return new MemoryReadResult(
            EmptyDocument(),
            canWrite,
            RequiresNewFile: canWrite);
    }

    private bool TryQuarantineInvalidDocument(string expectedJson)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (directory is null)
            {
                return false;
            }
            string quarantineDirectory = Path.Combine(
                directory,
                "Diagnostics",
                QuarantineDirectoryName);
            // This payload is local recovery evidence only. User reports build
            // their own sanitized attachment and never enumerate this folder.
            Directory.CreateDirectory(quarantineDirectory);
            string currentJson = File.ReadAllText(_path);
            if (!string.Equals(
                    expectedJson,
                    currentJson,
                    StringComparison.Ordinal))
            {
                // An older app or external tool replaced the file without
                // participating in our mutex. Never move different bytes than
                // the document that was actually classified as invalid.
                return false;
            }
            string recoveryId = Guid.NewGuid().ToString("N");
            string pendingPath = Path.Combine(
                quarantineDirectory,
                Path.GetFileName(_path) + "." +
                    recoveryId + ".recovery-pending.json");
            string quarantinePath = Path.Combine(
                quarantineDirectory,
                Path.GetFileName(_path) + "." +
                    recoveryId + ".invalid.json");
            File.Move(_path, pendingPath);
            string quarantinedJson;
            try
            {
                quarantinedJson = File.ReadAllText(pendingPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                    SecurityException)
            {
                TryRestoreUnexpectedQuarantine(pendingPath);
                return false;
            }
            if (!string.Equals(
                    expectedJson,
                    quarantinedJson,
                    StringComparison.Ordinal))
            {
                // A nonparticipating older app replaced the path after the
                // pre-move check. Do not authorize a fresh write. Restore its
                // file when the active path is still free; otherwise retain
                // both versions rather than overwriting either one.
                TryRestoreUnexpectedQuarantine(pendingPath);
                return false;
            }
            File.Move(pendingPath, quarantinePath, overwrite: false);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                SecurityException)
        {
            // Leave the unreadable or changed path non-writable. A later call
            // can start fresh only after absence is confirmed by an actual
            // FileNotFoundException or DirectoryNotFoundException.
            return false;
        }
    }

    private void TryRestoreUnexpectedQuarantine(string quarantinePath)
    {
        try
        {
            File.Move(quarantinePath, _path, overwrite: false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                SecurityException)
        {
            // A newer active file may already exist. The moved version remains
            // preserved in local recovery storage and no write is authorized.
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

    private void Write(MemoryDocument document, bool overwrite)
    {
        AtomicJsonFile.Write(_path, document, JsonOptions, overwrite);
    }

    private static IdentityDocument CreateIdentityDocument(
        ConfirmedGameIdentity identity) =>
        new(
            identity.WikidataEntityId,
            identity.CanonicalTitle,
            identity.Edition,
            identity.ReleaseYear,
            identity.Developer,
            identity.Series,
            identity.Authority.ToString(),
            identity.Source,
            identity.Locale,
            identity.UserConfirmed,
            identity.ConfirmedAtUtc.ToString("O"),
            identity.SourcePermissions.ToString());

    private static ConfirmedGameIdentity? RestoreIdentity(
        IdentityDocument? identity) =>
        identity is null
            ? null
            : new ConfirmedGameIdentity(
                identity.WikidataEntityId,
                identity.CanonicalTitle,
                identity.Edition,
                identity.ReleaseYear,
                identity.Developer,
                identity.Series,
                Enum.Parse<GameIdentityAuthority>(identity.Authority),
                identity.Source,
                identity.Locale,
                identity.UserConfirmed,
                DateTimeOffset.Parse(identity.ConfirmedAtUtc),
                Enum.Parse<GameKnowledgeSourcePermission>(
                    identity.SourcePermissions));

    private static bool IsValidIdentity(IdentityDocument? identity)
    {
        if (identity is null)
        {
            return true;
        }
        try
        {
            _ = RestoreIdentity(identity);
            return true;
        }
        catch (Exception exception)
            when (exception is ArgumentException or FormatException or
                OverflowException)
        {
            return false;
        }
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

    internal static string CreateTransactionMutexName(string path)
    {
        string canonical = Path.GetFullPath(path).ToUpperInvariant();
        string pathKey = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return "Local\\ReplayFoundry.GameContextMemory." + pathKey;
    }

    private sealed record MemoryReadResult(
        MemoryDocument Document,
        bool CanWrite,
        bool RequiresNewFile);

    private enum SchemaCompatibility
    {
        Supported,
        Unsupported,
        Invalid,
    }

    private enum ActiveReadOutcome
    {
        Success,
        Missing,
        Unavailable,
    }

    private enum PendingRecoveryOutcome
    {
        None,
        RetryActiveRead,
        Blocked,
    }

    private sealed class MemoryTransactionLease : IDisposable
    {
        private Mutex? _mutex;

        private MemoryTransactionLease(Mutex mutex)
        {
            _mutex = mutex;
        }

        public static MemoryTransactionLease? TryAcquire(
            string name,
            TimeSpan timeout)
        {
            Mutex? mutex = null;
            try
            {
                mutex = new Mutex(initiallyOwned: false, name);
                bool ownsMutex;
                try
                {
                    ownsMutex = mutex.WaitOne(timeout);
                }
                catch (AbandonedMutexException)
                {
                    ownsMutex = true;
                }
                if (!ownsMutex)
                {
                    mutex.Dispose();
                    return null;
                }
                return new MemoryTransactionLease(mutex);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                    SecurityException or WaitHandleCannotBeOpenedException)
            {
                mutex?.Dispose();
                return null;
            }
        }

        public void Dispose()
        {
            Mutex? mutex = Interlocked.Exchange(ref _mutex, null);
            if (mutex is null)
            {
                return;
            }
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Ownership was already lost; disposal is still required.
            }
            finally
            {
                mutex.Dispose();
            }
        }
    }

    private sealed record MemoryDocument(
        string SchemaVersion,
        MemoryEntry[] Entries);

    private sealed record MemoryEntry(
        string SourceDirectoryKey,
        string GameName,
        string? ContextNotes,
        bool? UseOpenGameKnowledge,
        IdentityDocument? ConfirmedIdentity,
        string UpdatedAtUtc);

    private sealed record IdentityDocument(
        string WikidataEntityId,
        string CanonicalTitle,
        string? Edition,
        int? ReleaseYear,
        string? Developer,
        string? Series,
        string Authority,
        string Source,
        string Locale,
        bool UserConfirmed,
        string ConfirmedAtUtc,
        string SourcePermissions);
}
