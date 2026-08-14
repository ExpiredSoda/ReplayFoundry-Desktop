using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ReplayFoundry.RuntimePacks;

public sealed record ReplayFoundryRuntimePackCatalogItem
{
    public ReplayFoundryRuntimePackCatalogItem(
        string packageId,
        ReplayFoundryRuntimePackKind kind,
        string semanticVersion,
        Uri downloadUri,
        long byteLength,
        string sha256,
        IEnumerable<string> approvedRedirectHosts)
    {
        PackageId = RuntimePackValidation.PackageId(packageId);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Version.TryParse(semanticVersion, out _)) throw new ArgumentException("Catalog versions must be numeric.", nameof(semanticVersion));
        if (downloadUri is null || downloadUri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Runtime pack downloads require HTTPS.", nameof(downloadUri));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);
        string[] hosts = (approvedRedirectHosts ?? throw new ArgumentNullException(nameof(approvedRedirectHosts)))
            .Select(host => host.Trim().ToLowerInvariant()).ToArray();
        if (hosts.Any(host => Uri.CheckHostName(host) == UriHostNameType.Unknown) ||
            hosts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != hosts.Length)
            throw new ArgumentException("Approved redirect hosts must be valid and unique.", nameof(approvedRedirectHosts));
        Kind = kind;
        SemanticVersion = semanticVersion.Trim();
        DownloadUri = downloadUri;
        ByteLength = byteLength;
        Sha256 = RuntimePackValidation.Sha256(sha256, nameof(sha256));
        ApprovedRedirectHosts = Array.AsReadOnly(hosts);
    }

    public string PackageId { get; }
    public ReplayFoundryRuntimePackKind Kind { get; }
    public string SemanticVersion { get; }
    public Uri DownloadUri { get; }
    public long ByteLength { get; }
    public string Sha256 { get; }
    public IReadOnlyList<string> ApprovedRedirectHosts { get; }
}

public sealed class ReplayFoundryRuntimePackCatalog
{
    public const string Schema = "replayfoundry-runtime-pack-catalog-1.0";
    public ReplayFoundryRuntimePackCatalog(
        string schemaVersion,
        string profile,
        IEnumerable<ReplayFoundryRuntimePackCatalogItem> packs,
        DateTimeOffset createdAtUtc)
    {
        if (schemaVersion != Schema || profile is not ("Base" or "Advanced") || createdAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Runtime pack catalog metadata is invalid.");
        ReplayFoundryRuntimePackCatalogItem[] snapshot = packs.ToArray();
        if (snapshot.Length == 0 ||
            snapshot.Select(pack => pack.Kind).Distinct().Count() != snapshot.Length ||
            snapshot.Select(pack => pack.PackageId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.Length)
            throw new ArgumentException("A catalog requires one pack per fixed runtime kind.", nameof(packs));
        ReplayFoundryRuntimePackKind[] actualKinds = snapshot.Select(pack => pack.Kind).Order().ToArray();
        ReplayFoundryRuntimePackKind[] requiredKinds = profile == "Base"
            ? [ReplayFoundryRuntimePackKind.MediaTools]
            : Enum.GetValues<ReplayFoundryRuntimePackKind>().Order().ToArray();
        if (!actualKinds.SequenceEqual(requiredKinds))
            throw new ArgumentException($"The {profile} catalog does not contain its exact fixed runtime profile.", nameof(packs));
        SchemaVersion = schemaVersion;
        Profile = profile;
        Packs = Array.AsReadOnly(snapshot);
        CreatedAtUtc = createdAtUtc;
    }
    public string SchemaVersion { get; }
    public string Profile { get; }
    public IReadOnlyList<ReplayFoundryRuntimePackCatalogItem> Packs { get; }
    public DateTimeOffset CreatedAtUtc { get; }
}

public static class ReplayFoundryRuntimePackCatalogReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
    };

    public static async Task<ReplayFoundryRuntimePackCatalog> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
        CatalogDto? dto = await JsonSerializer.DeserializeAsync<CatalogDto>(
            stream,
            JsonOptions,
            cancellationToken);
        if (dto is null || dto.Packs is null) throw new InvalidDataException("Runtime pack catalog is empty.");
        return new ReplayFoundryRuntimePackCatalog(
            dto.SchemaVersion!, dto.Profile!,
            dto.Packs.Select(item => new ReplayFoundryRuntimePackCatalogItem(
                item.PackageId!, Enum.Parse<ReplayFoundryRuntimePackKind>(item.Kind!, ignoreCase: false), item.SemanticVersion!,
                new Uri(item.DownloadUrl!, UriKind.Absolute), item.ByteLength, item.Sha256!, item.ApprovedRedirectHosts ?? [])),
            dto.CreatedAtUtc);
    }

    private sealed class CatalogDto
    {
        public string? SchemaVersion { get; set; }
        public string? Profile { get; set; }
        public CatalogItemDto[]? Packs { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }
    }
    private sealed class CatalogItemDto
    {
        public string? PackageId { get; set; }
        public string? Kind { get; set; }
        public string? SemanticVersion { get; set; }
        public string? DownloadUrl { get; set; }
        public long ByteLength { get; set; }
        public string? Sha256 { get; set; }
        public string[]? ApprovedRedirectHosts { get; set; }
    }
}

public sealed class ReplayFoundryRuntimePackCatalogInstaller
{
    private readonly HttpClient _client;
    private readonly ReplayFoundryRuntimePackStore _store;

    public ReplayFoundryRuntimePackCatalogInstaller(HttpClient client, ReplayFoundryRuntimePackStore store)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task InstallAsync(
        ReplayFoundryRuntimePackCatalog catalog,
        string downloadRoot,
        IProgress<(int Completed, int Total, string PackageId)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(downloadRoot);
        Directory.CreateDirectory(root);
        IReadOnlyList<InstalledReplayFoundryRuntimePack> before = await _store.ListInstalledAsync(cancellationToken);
        var previousActive = new List<InstalledReplayFoundryRuntimePack>();
        foreach (ReplayFoundryRuntimePackKind kind in catalog.Packs.Select(pack => pack.Kind))
        {
            try { previousActive.Add(await _store.ResolveActiveAsync(kind, cancellationToken: cancellationToken)); }
            catch (FileNotFoundException)
            {
                // This kind had no prior active pack, so rollback has no
                // activation to restore.
            }
        }
        var completed = new List<InstalledReplayFoundryRuntimePack>();
        int completedCount = 0;
        try
        {
            foreach (ReplayFoundryRuntimePackCatalogItem item in catalog.Packs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string partial = Path.Combine(root, item.PackageId + ".partial-" + Guid.NewGuid().ToString("N"));
                string archive = Path.Combine(root, item.PackageId + "-" + item.Sha256 + ".zip");
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, item.DownloadUri);
                    using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    Uri final = response.RequestMessage?.RequestUri ?? throw new InvalidDataException("A runtime download did not report its final URL.");
                    if (final.Scheme != Uri.UriSchemeHttps ||
                        (!string.Equals(final.Host, item.DownloadUri.Host, StringComparison.OrdinalIgnoreCase) &&
                         !item.ApprovedRedirectHosts.Contains(final.Host, StringComparer.OrdinalIgnoreCase)))
                        throw new InvalidDataException($"Runtime download redirected to unapproved host {final.Host}.");
                    await using (FileStream output = new(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 256 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
                    {
                        await response.Content.CopyToAsync(output, cancellationToken);
                        await output.FlushAsync(cancellationToken);
                    }
                    FileInfo info = new(partial);
                    if (info.Length != item.ByteLength) throw new InvalidDataException($"Downloaded {item.PackageId} has the wrong size.");
                    string hash = await ReplayFoundryRuntimePackBuilder.ComputeSha256Async(partial, cancellationToken);
                    if (!string.Equals(hash, item.Sha256, StringComparison.Ordinal)) throw new InvalidDataException($"Downloaded {item.PackageId} failed SHA-256 verification.");
                    File.Move(partial, archive, overwrite: true);
                    InstalledReplayFoundryRuntimePack installed = await _store.InstallAsync(archive, activate: true, cancellationToken);
                    if (installed.Manifest.Identity.Kind != item.Kind ||
                        !string.Equals(installed.Manifest.Identity.PackageId, item.PackageId, StringComparison.Ordinal) ||
                        !string.Equals(installed.Manifest.Identity.SemanticVersion, item.SemanticVersion, StringComparison.Ordinal))
                        throw new InvalidDataException($"Downloaded {item.PackageId} did not match its catalog identity.");
                    completed.Add(installed);
                    completedCount++;
                    progress?.Report((completedCount, catalog.Packs.Count, item.PackageId));
                }
                finally
                {
                    if (File.Exists(partial)) File.Delete(partial);
                    if (File.Exists(archive)) File.Delete(archive);
                }
            }
        }
        catch
        {
            foreach (InstalledReplayFoundryRuntimePack installed in completed.AsEnumerable().Reverse())
            {
                bool existed = before.Any(pack =>
                    string.Equals(pack.Manifest.ManifestHash, installed.Manifest.ManifestHash, StringComparison.Ordinal));
                if (!existed)
                    await _store.RemoveAsync(installed.Manifest.Identity.PackageId, installed.Manifest.ManifestHash, CancellationToken.None);
            }
            foreach (InstalledReplayFoundryRuntimePack active in previousActive)
                await _store.ActivateInstalledAsync(active.Manifest.Identity.PackageId, active.Manifest.ManifestHash, CancellationToken.None);
            throw;
        }
    }
}
