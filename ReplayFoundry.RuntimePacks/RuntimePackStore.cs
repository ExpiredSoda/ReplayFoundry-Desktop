using System.IO.Compression;
using System.Text.Json;

namespace ReplayFoundry.RuntimePacks;

public sealed class ReplayFoundryRuntimePackStorePaths
{
    public ReplayFoundryRuntimePackStorePaths(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string RootDirectory { get; }
    public string StagingDirectory => Path.Combine(RootDirectory, ".staging");
    public string ActiveSelectionPath => Path.Combine(RootDirectory, "active-runtime-packs.json");

    public static ReplayFoundryRuntimePackStorePaths CreateDefault() => new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ReplayFoundry", "R"));

    public string FinalDirectory(ReplayFoundryRuntimePackManifest manifest) =>
        FinalDirectory(manifest.ManifestHash);

    internal string FinalDirectory(string manifestHash)
    {
        byte[] hash = Convert.FromHexString(RuntimePackValidation.Sha256(manifestHash, nameof(manifestHash)));
        string contentId = Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return Path.Combine(RootDirectory, contentId);
    }
}

public sealed class ReplayFoundryRuntimePackStore : IDisposable
{
    private readonly ReplayFoundryRuntimePackStorePaths _paths;
    private readonly ReplayFoundryRuntimePackVerifier _verifier;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Dispose() => _gate.Dispose();

    public ReplayFoundryRuntimePackStore(
        ReplayFoundryRuntimePackStorePaths paths,
        ReplayFoundryRuntimePackVerifier? verifier = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _verifier = verifier ?? new ReplayFoundryRuntimePackVerifier();
    }

    public ReplayFoundryRuntimePackStorePaths Paths => _paths;

    public async Task<InstalledReplayFoundryRuntimePack> InstallAsync(
        string sourceDirectoryOrZip,
        bool activate,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_paths.StagingDirectory);
            string operation = Path.Combine(_paths.StagingDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(operation);
            try
            {
                string source = await MaterializeSourceAsync(sourceDirectoryOrZip, operation, cancellationToken);
                ReplayFoundryRuntimePackVerificationResult sourceVerification =
                    await _verifier.VerifyAsync(source, cancellationToken: cancellationToken);
                if (!sourceVerification.IsValid || sourceVerification.Manifest is null)
                    throw new InvalidDataException("Runtime pack verification failed: " + string.Join("; ", sourceVerification.Errors));
                await EnsureDependenciesAsync(sourceVerification.Manifest, cancellationToken);
                string final = _paths.FinalDirectory(sourceVerification.Manifest);
                if (!Directory.Exists(final))
                {
                    string stage = Path.Combine(operation, "install");
                    await CopyDeclaredFilesAsync(source, stage, sourceVerification.Manifest, cancellationToken);
                    ReplayFoundryRuntimePackVerificationResult stageVerification =
                        await _verifier.VerifyAsync(stage, cancellationToken: cancellationToken);
                    if (!stageVerification.IsValid)
                        throw new InvalidDataException("Staged runtime pack verification failed: " + string.Join("; ", stageVerification.Errors));
                    Directory.CreateDirectory(Path.GetDirectoryName(final)!);
                    cancellationToken.ThrowIfCancellationRequested();
                    Directory.Move(stage, final);
                }
                ReplayFoundryRuntimePackVerificationResult installedVerification =
                    await _verifier.VerifyAsync(final, cancellationToken: cancellationToken);
                if (!installedVerification.IsValid || installedVerification.Manifest is null)
                    throw new InvalidDataException("Installed runtime pack did not verify.");
                if (activate)
                    await SetActiveAsync(installedVerification.Manifest, cancellationToken);
                return new(installedVerification.Manifest, final);
            }
            finally
            {
                DeleteDirectory(operation);
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<InstalledReplayFoundryRuntimePack> RepairAsync(
        string sourceDirectoryOrZip,
        CancellationToken cancellationToken = default)
    {
        string workspace = Path.Combine(_paths.StagingDirectory, "repair-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            string source = await MaterializeSourceAsync(sourceDirectoryOrZip, workspace, cancellationToken);
            ReplayFoundryRuntimePackVerificationResult verification = await _verifier.VerifyAsync(source, cancellationToken: cancellationToken);
            if (!verification.IsValid || verification.Manifest is null)
                throw new InvalidDataException("Repair source is invalid.");
            string final = _paths.FinalDirectory(verification.Manifest);
            if (Directory.Exists(final))
            {
                string quarantine = final + ".repair-" + Guid.NewGuid().ToString("N");
                Directory.Move(final, quarantine);
                try
                {
                    InstalledReplayFoundryRuntimePack installed = await InstallAsync(source, activate: true, cancellationToken);
                    DeleteDirectory(quarantine);
                    return installed;
                }
                catch
                {
                    if (!Directory.Exists(final) && Directory.Exists(quarantine))
                        Directory.Move(quarantine, final);
                    throw;
                }
            }
            return await InstallAsync(source, activate: true, cancellationToken);
        }
        finally { DeleteDirectory(workspace); }
    }

    public async Task<InstalledReplayFoundryRuntimePack> ResolveActiveAsync(
        ReplayFoundryRuntimePackKind kind,
        ReplayFoundryRuntimePackVerificationMode verificationMode = ReplayFoundryRuntimePackVerificationMode.RuntimeStartup,
        CancellationToken cancellationToken = default)
    {
        ActiveSelection selection = await ReadSelectionAsync(cancellationToken);
        ActiveEntry? entry = selection.Packs.SingleOrDefault(item => item.Kind == kind);
        if (entry is null)
            throw new FileNotFoundException($"No verified {kind} runtime pack is active.");
        string root = _paths.FinalDirectory(entry.ManifestHash);
        ReplayFoundryRuntimePackVerificationResult verification = await _verifier.VerifyAsync(
            root, mode: verificationMode, cancellationToken: cancellationToken);
        if (!verification.IsValid || verification.Manifest is null ||
            !string.Equals(verification.Manifest.ManifestHash, entry.ManifestHash, StringComparison.Ordinal))
            throw new InvalidDataException($"The active {kind} runtime pack is missing or corrupt.");
        return new(verification.Manifest, root);
    }

    public async Task<IReadOnlyList<InstalledReplayFoundryRuntimePack>> ListInstalledAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_paths.RootDirectory)) return [];
        var installed = new List<InstalledReplayFoundryRuntimePack>();
        foreach (string manifestPath in Directory.EnumerateFiles(
                     _paths.RootDirectory, RuntimePackManifestJson.FileName, SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (manifestPath.StartsWith(_paths.StagingDirectory, StringComparison.OrdinalIgnoreCase)) continue;
            string root = Path.GetDirectoryName(manifestPath)!;
            ReplayFoundryRuntimePackVerificationResult verification = await _verifier.VerifyAsync(root, cancellationToken: cancellationToken);
            if (verification.IsValid && verification.Manifest is not null)
                installed.Add(new(verification.Manifest, root));
        }
        return installed.OrderBy(pack => pack.Manifest.Identity.Kind)
            .ThenBy(pack => pack.Manifest.Identity.PackageId, StringComparer.Ordinal)
            .ThenByDescending(pack => pack.Manifest.Identity.ParsedVersion).ToArray();
    }

    public async Task RemoveAsync(
        string packageId,
        string? manifestHash = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string id = RuntimePackValidation.PackageId(packageId);
            IReadOnlyList<InstalledReplayFoundryRuntimePack> all = await ListInstalledAsync(cancellationToken);
            InstalledReplayFoundryRuntimePack[] targets = all.Where(pack =>
                string.Equals(pack.Manifest.Identity.PackageId, id, StringComparison.OrdinalIgnoreCase) &&
                (manifestHash is null || string.Equals(pack.Manifest.ManifestHash, manifestHash, StringComparison.OrdinalIgnoreCase))).ToArray();
            foreach (InstalledReplayFoundryRuntimePack target in targets)
            {
                InstalledReplayFoundryRuntimePack? dependent = all.FirstOrDefault(other =>
                    !targets.Contains(other) && other.Manifest.Dependencies.Any(dependency => dependency.Accepts(target.Manifest)));
                if (dependent is not null)
                    throw new InvalidOperationException($"{target.Manifest.DisplayName} is required by {dependent.Manifest.DisplayName}.");
            }
            foreach (InstalledReplayFoundryRuntimePack target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string quarantine = target.RootDirectory + ".removing-" + Guid.NewGuid().ToString("N");
                Directory.Move(target.RootDirectory, quarantine);
                DeleteDirectory(quarantine);
            }
            ActiveSelection selection = await ReadSelectionAsync(cancellationToken);
            ActiveEntry[] retained = selection.Packs.Where(entry =>
                !string.Equals(entry.PackageId, id, StringComparison.OrdinalIgnoreCase) ||
                (manifestHash is not null && !string.Equals(entry.ManifestHash, manifestHash, StringComparison.OrdinalIgnoreCase))).ToArray();
            await WriteSelectionAsync(new(selection.SchemaVersion, retained), cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<int> PruneInactiveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            IReadOnlyList<InstalledManifestEntry> inventory =
                await ReadInstalledManifestInventoryAsync(cancellationToken);
            ActiveSelection selection = await ReadSelectionAsync(cancellationToken);
            var retainedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ActiveEntry active in selection.Packs)
            {
                InstalledManifestEntry? installed = inventory.SingleOrDefault(entry =>
                    string.Equals(
                        entry.Manifest.ManifestHash,
                        active.ManifestHash,
                        StringComparison.OrdinalIgnoreCase));
                if (installed is not null)
                    RetainDependencyClosure(installed, inventory, retainedHashes);
                else
                    retainedHashes.Add(active.ManifestHash);
            }

            InstalledManifestEntry[] inactive = inventory
                .Where(entry => !retainedHashes.Contains(entry.Manifest.ManifestHash))
                .OrderByDescending(entry => entry.Manifest.Identity.Kind)
                .ThenBy(entry => entry.Manifest.Identity.PackageId, StringComparer.Ordinal)
                .ThenBy(entry => entry.Manifest.Identity.ParsedVersion)
                .ToArray();
            foreach (InstalledManifestEntry target in inactive)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string quarantine = target.RootDirectory + ".removing-" + Guid.NewGuid().ToString("N");
                Directory.Move(target.RootDirectory, quarantine);
                DeleteDirectory(quarantine);
            }

            return inactive.Length;
        }
        finally { _gate.Release(); }
    }

    public async Task ActivateInstalledAsync(
        string packageId,
        string manifestHash,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string id = RuntimePackValidation.PackageId(packageId);
            string hash = RuntimePackValidation.Sha256(manifestHash, nameof(manifestHash));
            InstalledReplayFoundryRuntimePack? installed = (await ListInstalledAsync(cancellationToken))
                .SingleOrDefault(pack =>
                    string.Equals(pack.Manifest.Identity.PackageId, id, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(pack.Manifest.ManifestHash, hash, StringComparison.Ordinal));
            if (installed is null)
                throw new FileNotFoundException($"Verified runtime pack {id} ({hash}) is not installed.");
            await SetActiveAsync(installed.Manifest, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public Task CleanupAbandonedStagingAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_paths.StagingDirectory)) return Task.CompletedTask;
        foreach (string directory in Directory.EnumerateDirectories(_paths.StagingDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteDirectory(directory);
        }
        return Task.CompletedTask;
    }

    public async Task CleanupEmptyStoreAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await CleanupAbandonedStagingAsync(cancellationToken);
            if (!Directory.Exists(_paths.RootDirectory) ||
                (await ListInstalledAsync(cancellationToken)).Count != 0)
                return;
            ActiveSelection selection = await ReadSelectionAsync(cancellationToken);
            if (selection.Packs.Length != 0) return;
            if (File.Exists(_paths.ActiveSelectionPath)) File.Delete(_paths.ActiveSelectionPath);
            foreach (string directory in Directory.EnumerateDirectories(_paths.RootDirectory, "*", SearchOption.AllDirectories)
                         .OrderByDescending(path => path.Length))
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            if (!Directory.EnumerateFileSystemEntries(_paths.RootDirectory).Any())
                Directory.Delete(_paths.RootDirectory);
        }
        finally { _gate.Release(); }
    }

    private async Task EnsureDependenciesAsync(
        ReplayFoundryRuntimePackManifest manifest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (manifest.Dependencies.Count == 0)
            return;

        IReadOnlyList<InstalledManifestEntry> inventory =
            await ReadInstalledManifestInventoryAsync(cancellationToken);
        var verified = new Dictionary<string, InstalledReplayFoundryRuntimePack>(
            StringComparer.Ordinal);
        var validatedClosures = new HashSet<string>(StringComparer.Ordinal);
        var visitingManifestHashes = new HashSet<string>(StringComparer.Ordinal)
        {
            manifest.ManifestHash,
        };

        foreach (ReplayFoundryRuntimePackDependency dependency in manifest.Dependencies)
        {
            await VerifyDependencyClosureAsync(
                dependency,
                inventory,
                verified,
                validatedClosures,
                visitingManifestHashes,
                cancellationToken);
        }
    }

    private async Task VerifyDependencyClosureAsync(
        ReplayFoundryRuntimePackDependency dependency,
        IReadOnlyList<InstalledManifestEntry> inventory,
        IDictionary<string, InstalledReplayFoundryRuntimePack> verified,
        ISet<string> validatedClosures,
        ISet<string> visitingManifestHashes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InstalledManifestEntry[] candidates = inventory
            .Where(entry => dependency.Accepts(entry.Manifest))
            .OrderByDescending(entry => entry.Manifest.Identity.ParsedVersion)
            .ThenBy(entry => entry.Manifest.ManifestHash, StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0)
        {
            throw new InvalidOperationException(
                $"Missing required runtime pack dependency: {dependency.PackageId}.");
        }

        InstalledReplayFoundryRuntimePack? selected = null;
        foreach (InstalledManifestEntry candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (verified.TryGetValue(candidate.Manifest.ManifestHash, out selected))
                break;

            try
            {
                ReplayFoundryRuntimePackVerificationResult verification =
                    await _verifier.VerifyAsync(
                        candidate.RootDirectory,
                        cancellationToken: cancellationToken);
                if (!verification.IsValid || verification.Manifest is null ||
                    !string.Equals(
                        verification.Manifest.ManifestHash,
                        candidate.Manifest.ManifestHash,
                        StringComparison.Ordinal))
                {
                    selected = null;
                    continue;
                }

                selected = new InstalledReplayFoundryRuntimePack(
                    verification.Manifest,
                    candidate.RootDirectory);
                verified.Add(candidate.Manifest.ManifestHash, selected);
                break;
            }
            catch (IOException)
            {
                selected = null;
            }
        }

        if (selected is null)
        {
            throw new InvalidDataException(
                $"Required runtime pack dependency is corrupt: {dependency.PackageId}.");
        }

        if (visitingManifestHashes.Contains(selected.Manifest.ManifestHash))
            throw new InvalidDataException("Runtime pack dependency cycle detected.");

        if (validatedClosures.Contains(selected.Manifest.ManifestHash))
            return;

        visitingManifestHashes.Add(selected.Manifest.ManifestHash);
        try
        {
            foreach (ReplayFoundryRuntimePackDependency child in selected.Manifest.Dependencies)
            {
                await VerifyDependencyClosureAsync(
                    child,
                    inventory,
                    verified,
                    validatedClosures,
                    visitingManifestHashes,
                    cancellationToken);
            }

            validatedClosures.Add(selected.Manifest.ManifestHash);
        }
        finally
        {
            visitingManifestHashes.Remove(selected.Manifest.ManifestHash);
        }
    }

    private async Task<IReadOnlyList<InstalledManifestEntry>> ReadInstalledManifestInventoryAsync(
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_paths.RootDirectory))
            return [];

        var inventory = new List<InstalledManifestEntry>();
        foreach (string directory in Directory.EnumerateDirectories(
                     _paths.RootDirectory,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string manifestPath = Path.Combine(directory, RuntimePackManifestJson.FileName);
            if (!File.Exists(manifestPath))
                continue;

            try
            {
                ReplayFoundryRuntimePackManifest candidate =
                    await RuntimePackManifestJson.ReadAsync(manifestPath, cancellationToken);
                if (!string.Equals(
                        Path.GetFullPath(directory),
                        Path.GetFullPath(_paths.FinalDirectory(candidate)),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                inventory.Add(new InstalledManifestEntry(candidate, directory));
            }
            catch (Exception exception) when (
                exception is IOException or JsonException or InvalidDataException or
                    ArgumentException or NullReferenceException)
            {
                // Invalid or noncanonical store entries are not dependency candidates.
            }
        }

        return inventory;
    }

    private static void RetainDependencyClosure(
        InstalledManifestEntry root,
        IReadOnlyList<InstalledManifestEntry> inventory,
        ISet<string> retainedHashes)
    {
        if (!retainedHashes.Add(root.Manifest.ManifestHash))
            return;

        foreach (ReplayFoundryRuntimePackDependency dependency in root.Manifest.Dependencies)
        {
            InstalledManifestEntry? selected = inventory
                .Where(entry => dependency.Accepts(entry.Manifest))
                .OrderByDescending(entry => entry.Manifest.Identity.ParsedVersion)
                .ThenBy(entry => entry.Manifest.ManifestHash, StringComparer.Ordinal)
                .FirstOrDefault();
            if (selected is not null)
                RetainDependencyClosure(selected, inventory, retainedHashes);
        }
    }

    private async Task SetActiveAsync(ReplayFoundryRuntimePackManifest manifest, CancellationToken cancellationToken)
    {
        ActiveSelection selection = await ReadSelectionAsync(cancellationToken);
        ActiveEntry entry = new(manifest.Identity.Kind, manifest.Identity.PackageId, manifest.Identity.SemanticVersion, manifest.ManifestHash);
        ActiveEntry[] entries = selection.Packs.Where(item => item.Kind != manifest.Identity.Kind)
            .Append(entry).OrderBy(item => item.Kind).ToArray();
        await WriteSelectionAsync(new(ActiveSelection.Schema, entries), cancellationToken);
    }

    private async Task<ActiveSelection> ReadSelectionAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.ActiveSelectionPath)) return new(ActiveSelection.Schema, []);
        await using FileStream stream = new(_paths.ActiveSelectionPath, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024, FileOptions.Asynchronous);
        ActiveSelection? value = await JsonSerializer.DeserializeAsync<ActiveSelection>(stream, cancellationToken: cancellationToken);
        if (value is null || value.SchemaVersion != ActiveSelection.Schema || value.Packs is null ||
            value.Packs.Select(pack => pack.Kind).Distinct().Count() != value.Packs.Length)
            throw new InvalidDataException("The active runtime pack selection is invalid.");
        return value;
    }

    private async Task WriteSelectionAsync(ActiveSelection selection, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.RootDirectory);
        string temporary = _paths.ActiveSelectionPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(selection), cancellationToken);
            File.Move(temporary, _paths.ActiveSelectionPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<string> MaterializeSourceAsync(string sourceDirectoryOrZip, string workspace, CancellationToken cancellationToken)
    {
        string source = Path.GetFullPath(sourceDirectoryOrZip);
        if (Directory.Exists(source)) return source;
        if (!File.Exists(source) || !string.Equals(Path.GetExtension(source), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Runtime pack source must be a directory or ZIP archive.", source);
        string extracted = Path.Combine(workspace, "source");
        Directory.CreateDirectory(extracted);
        using ZipArchive archive = ZipFile.OpenRead(source);
        string prefix = Path.GetFullPath(extracted).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = Path.GetFullPath(Path.Combine(prefix, entry.FullName));
            if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Runtime pack archive contains path traversal.");
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using Stream input = entry.Open();
            await using FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous);
            await input.CopyToAsync(output, cancellationToken);
        }
        return extracted;
    }

    private static async Task CopyDeclaredFilesAsync(
        string source, string destination, ReplayFoundryRuntimePackManifest manifest, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        IEnumerable<string> paths = manifest.Files.Select(file => file.RelativePath).Append(RuntimePackManifestJson.FileName);
        foreach (string relative in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string input = ReplayFoundryRuntimePackVerifier.ResolveContainedPath(source, relative);
            string output = ReplayFoundryRuntimePackVerifier.ResolveContainedPath(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await using FileStream sourceStream = new(input, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using FileStream destinationStream = new(output, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
            await sourceStream.CopyToAsync(destinationStream, cancellationToken);
            await destinationStream.FlushAsync(cancellationToken);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, recursive: true);
    }

    private sealed record ActiveSelection(string SchemaVersion, ActiveEntry[] Packs)
    {
        public const string Schema = "replayfoundry-active-runtime-packs-1.0";
    }
    private sealed record ActiveEntry(ReplayFoundryRuntimePackKind Kind, string PackageId, string SemanticVersion, string ManifestHash);
    private sealed record InstalledManifestEntry(
        ReplayFoundryRuntimePackManifest Manifest,
        string RootDirectory);
}
