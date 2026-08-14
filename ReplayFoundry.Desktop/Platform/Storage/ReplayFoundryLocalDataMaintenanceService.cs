using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Settings;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class ReplayFoundryLocalDataMaintenanceService :
    IReplayFoundryLocalDataMaintenance
{
    private const string ResetMarkerName = "pending-local-data-reset.json";
    private static readonly string[] PreferenceAndHistoryFiles =
    [
        "clip-preferences.json",
        "editorial-metadata-preference-consent.json",
        "editorial-metadata-preferences.json",
        "editorial-reroll-preference.json",
        "bug-report-consent.json",
        "game-context-memory.json",
        "generation-output-location.json",
        "GenerationAudioRoles.json",
        "hidden-moment-decisions.json",
        "RecentGenerationProjects.json",
        "research-feedback.json",
        "research-participation.json",
        "studio-candidate-decisions.json",
        "youtube-connection-permission.json",
        "youtube-publish-drafts.json",
        "youtube-publish-history.json",
        "youtube-publish-preferences.json",
    ];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private readonly string _root;
    private readonly string _temporaryRoot;
    private readonly DateTime _processStartedUtc;

    public ReplayFoundryLocalDataMaintenanceService(
        string? rootDirectory = null,
        string? temporaryRoot = null)
    {
        bool usesDefaultRoot = rootDirectory is null;
        _root = Path.GetFullPath(rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "ReplayFoundry"));
        if (!Path.IsPathFullyQualified(_root) ||
            Path.GetPathRoot(_root)?.TrimEnd(Path.DirectorySeparatorChar) ==
                _root.TrimEnd(Path.DirectorySeparatorChar))
        {
            throw new ArgumentException(
                "The Replay Foundry local-data root must be a focused fully qualified directory.",
                nameof(rootDirectory));
        }
        _temporaryRoot = Path.GetFullPath(temporaryRoot ??
            (usesDefaultRoot
                ? Path.Combine(Path.GetTempPath(), "ReplayFoundry")
                : Path.Combine(_root, "TempWorkspaces")));
        if (!Path.IsPathFullyQualified(_temporaryRoot))
        {
            throw new ArgumentException(
                "The Replay Foundry temporary root must be fully qualified.",
                nameof(temporaryRoot));
        }
        _processStartedUtc = System.Diagnostics.Process
            .GetCurrentProcess().StartTime.ToUniversalTime();
    }

    public IReadOnlyList<ReplayFoundryLocalDataUsage> Inspect() =>
        Enum.GetValues<ReplayFoundryLocalDataKind>()
            .Select(kind => Usage(kind))
            .ToArray();

    public Task<ReplayFoundryLocalDataCleanupResult> ClearDerivedCachesAsync(
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        ReplayFoundryLocalDataCleanupResult persistent = DeleteKinds(
            new ReplayFoundryLocalDataResetRequest(
                [ReplayFoundryLocalDataKind.DerivedCaches]),
            cancellationToken);
        ReplayFoundryLocalDataCleanupResult temporary =
            DeleteAbandonedTemporaryWorkspaces(cancellationToken);
        return new ReplayFoundryLocalDataCleanupResult(
            persistent.DeletedBytes + temporary.DeletedBytes,
            persistent.DeletedFiles + temporary.DeletedFiles,
            Array.AsReadOnly(persistent.Warnings
                .Concat(temporary.Warnings)
                .ToArray()));
    }, cancellationToken);

    public void ScheduleReset(ReplayFoundryLocalDataResetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Directory.CreateDirectory(_root);
        AtomicJsonFile.Write(
            Child(ResetMarkerName),
            new ResetDocument
            {
                SchemaVersion = "local-data-reset-1.0",
                RequestedAtUtc = DateTimeOffset.UtcNow,
                Kinds = request.Kinds.Select(static kind => kind.ToString()).ToArray(),
            },
            JsonOptions);
    }

    public Task<ReplayFoundryLocalDataCleanupResult> ApplyScheduledResetAsync(
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        string marker = Child(ResetMarkerName);
        if (!File.Exists(marker))
        {
            return new ReplayFoundryLocalDataCleanupResult(0, 0, []);
        }
        ReplayFoundryLocalDataResetRequest request = ReadMarker(marker);
        ReplayFoundryLocalDataCleanupResult result = DeleteKinds(
            request,
            cancellationToken);
        if (result.Succeeded && File.Exists(marker)) File.Delete(marker);
        return result;
    }, cancellationToken);

    private ReplayFoundryLocalDataUsage Usage(ReplayFoundryLocalDataKind kind)
    {
        (long bytes, int files) = kind switch
        {
            ReplayFoundryLocalDataKind.DerivedCaches => Sum(
                [Child("Cache"), Child("game-knowledge"), Child("Installers"), _temporaryRoot]),
            ReplayFoundryLocalDataKind.DiagnosticsAndReports => Sum(
                [Child("Diagnostics")]),
            ReplayFoundryLocalDataKind.PreferencesAndHistory => Sum(
                PreferenceAndHistoryFiles.Select(Child)),
            ReplayFoundryLocalDataKind.LibraryCatalog => Sum(
                [Child("library-catalog.json")]),
            ReplayFoundryLocalDataKind.StudioProjects => Sum(
                [Child("Projects")]),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return new ReplayFoundryLocalDataUsage(kind, bytes, files);
    }

    private ReplayFoundryLocalDataCleanupResult DeleteKinds(
        ReplayFoundryLocalDataResetRequest request,
        CancellationToken cancellationToken)
    {
        long bytes = 0;
        int files = 0;
        var warnings = new List<string>();
        foreach (ReplayFoundryLocalDataKind kind in request.Kinds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<string> targets = kind switch
            {
                ReplayFoundryLocalDataKind.DerivedCaches =>
                    [Child("Cache"), Child("game-knowledge"), Child("Installers")],
                ReplayFoundryLocalDataKind.DiagnosticsAndReports =>
                    [Child("Diagnostics")],
                ReplayFoundryLocalDataKind.PreferencesAndHistory =>
                    PreferenceAndHistoryFiles.Select(Child),
                ReplayFoundryLocalDataKind.LibraryCatalog =>
                    [Child("library-catalog.json")],
                ReplayFoundryLocalDataKind.StudioProjects =>
                    [Child("Projects")],
                _ => throw new ArgumentOutOfRangeException(
                    nameof(request),
                    kind,
                    "The requested local-data category is not supported."),
            };
            foreach (string target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    (long targetBytes, int targetFiles) = Sum([target]);
                    if (File.Exists(target)) File.Delete(target);
                    else if (Directory.Exists(target))
                        Directory.Delete(target, recursive: true);
                    bytes += targetBytes;
                    files += targetFiles;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    warnings.Add(
                        $"{kind}: {exception.GetType().Name}");
                }
            }
        }
        return new ReplayFoundryLocalDataCleanupResult(
            bytes,
            files,
            Array.AsReadOnly(warnings.ToArray()));
    }

    private ReplayFoundryLocalDataCleanupResult DeleteAbandonedTemporaryWorkspaces(
        CancellationToken cancellationToken)
    {
        long bytes = 0;
        int files = 0;
        var warnings = new List<string>();
        if (!Directory.Exists(_temporaryRoot))
        {
            return new ReplayFoundryLocalDataCleanupResult(0, 0, []);
        }
        foreach (string category in SafeEnumerateDirectories(_temporaryRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (string workspace in SafeEnumerateDirectories(category))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var info = new DirectoryInfo(workspace);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                        info.CreationTimeUtc >= _processStartedUtc)
                    {
                        continue;
                    }
                    (long workspaceBytes, int workspaceFiles) = Sum([workspace]);
                    Directory.Delete(workspace, recursive: true);
                    bytes += workspaceBytes;
                    files += workspaceFiles;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    warnings.Add(
                        $"Abandoned temporary workspace: {exception.GetType().Name}");
                }
            }
            TryDeleteEmpty(category);
        }
        TryDeleteEmpty(_temporaryRoot);
        return new ReplayFoundryLocalDataCleanupResult(
            bytes,
            files,
            Array.AsReadOnly(warnings.ToArray()));
    }

    private static IReadOnlyList<string> SafeEnumerateDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(
                root,
                "*",
                SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void TryDeleteEmpty(string directory)
    {
        try
        {
            if (Directory.Exists(directory) &&
                !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Another live workspace may have appeared; leave it untouched.
        }
    }

    private ReplayFoundryLocalDataResetRequest ReadMarker(string marker)
    {
        try
        {
            ResetDocument? document = JsonSerializer.Deserialize<ResetDocument>(
                File.ReadAllText(marker), JsonOptions);
            if (document?.SchemaVersion != "local-data-reset-1.0" ||
                document.RequestedAtUtc.Offset != TimeSpan.Zero ||
                document.Kinds is null)
            {
                throw new InvalidDataException(
                    "The pending local-data reset is invalid.");
            }
            return new ReplayFoundryLocalDataResetRequest(
                document.Kinds.Select(static value =>
                    Enum.Parse<ReplayFoundryLocalDataKind>(value)));
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException)
        {
            throw new InvalidDataException(
                "The pending local-data reset is invalid.",
                exception);
        }
    }

    private string Child(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (Path.GetFileName(name) != name)
        {
            throw new ArgumentException(
                "A local-data target must be an immediate child name.",
                nameof(name));
        }
        string path = Path.GetFullPath(Path.Combine(_root, name));
        string prefix = _root.TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "A local-data target escaped the Replay Foundry root.");
        }
        return path;
    }

    private static (long Bytes, int Files) Sum(IEnumerable<string> targets)
    {
        long bytes = 0;
        int files = 0;
        foreach (string target in targets)
        {
            if (File.Exists(target))
            {
                var file = new FileInfo(target);
                bytes += file.Length;
                files++;
            }
            else if (Directory.Exists(target))
            {
                foreach (string path in SafeEnumerateFiles(target))
                {
                    try
                    {
                        var file = new FileInfo(path);
                        bytes += file.Length;
                        files++;
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException)
                    {
                        // Storage usage is advisory; inaccessible files are
                        // reported during cleanup rather than crashing Settings.
                    }
                }
            }
        }
        return (bytes, files);
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            FileSystemInfo[] entries;
            try
            {
                entries = new DirectoryInfo(current).GetFileSystemInfos();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (FileSystemInfo entry in entries)
            {
                if (entry is FileInfo file)
                {
                    yield return file.FullName;
                }
                else if (entry is DirectoryInfo directory &&
                         (directory.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(directory.FullName);
                }
            }
        }
    }

    private sealed class ResetDocument
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public DateTimeOffset RequestedAtUtc { get; set; }
        public string[]? Kinds { get; set; }
    }
}
