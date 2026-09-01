using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReplayFoundry.RuntimePacks;

public static class RuntimePackManifestJson
{
    public const string FileName = "runtime-pack-manifest.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<ReplayFoundryRuntimePackManifest> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using FileStream stream = new(
            Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        ManifestDto? dto = await JsonSerializer.DeserializeAsync<ManifestDto>(
            stream, ReadOptions, cancellationToken);
        return dto?.ToManifest() ??
            throw new InvalidDataException("The runtime pack manifest is empty.");
    }

    public static async Task WriteAsync(
        ReplayFoundryRuntimePackManifest manifest,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        string json = CanonicalJson(manifest, includeHash: true);
        await File.WriteAllTextAsync(
            Path.GetFullPath(path), json + Environment.NewLine,
            new UTF8Encoding(false), cancellationToken);
    }

    public static string ComputeHash(ReplayFoundryRuntimePackManifest manifest) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(CanonicalJson(manifest, includeHash: false))));

    internal static string CanonicalJson(
        ReplayFoundryRuntimePackManifest manifest,
        bool includeHash)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", manifest.SchemaVersion);
            writer.WritePropertyName("identity");
            writer.WriteStartObject();
            writer.WriteString("packageId", manifest.Identity.PackageId);
            writer.WriteString("kind", manifest.Identity.Kind.ToString());
            writer.WriteString("semanticVersion", manifest.Identity.SemanticVersion);
            writer.WriteEndObject();
            writer.WriteString("displayName", manifest.DisplayName);
            writer.WriteString("platform", manifest.Platform);
            writer.WriteString("backend", manifest.Backend.ToString());
            writer.WritePropertyName("files");
            writer.WriteStartArray();
            foreach (ReplayFoundryRuntimePackFile file in manifest.Files)
            {
                writer.WriteStartObject();
                writer.WriteString("relativePath", file.RelativePath);
                writer.WriteNumber("byteLength", file.ByteLength);
                writer.WriteString("sha256", file.Sha256);
                writer.WriteString("role", file.Role.ToString());
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("dependencies");
            writer.WriteStartArray();
            foreach (ReplayFoundryRuntimePackDependency dependency in manifest.Dependencies)
            {
                writer.WriteStartObject();
                writer.WriteString("packageId", dependency.PackageId);
                writer.WriteString("minimumVersion", dependency.MinimumVersion);
                if (dependency.RequiredManifestHash is not null)
                    writer.WriteString("requiredManifestHash", dependency.RequiredManifestHash);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("licenses");
            writer.WriteStartArray();
            foreach (ReplayFoundryRuntimePackLicense license in manifest.Licenses)
            {
                writer.WriteStartObject();
                writer.WriteString("componentName", license.ComponentName);
                writer.WriteString("licenseIdentifier", license.LicenseIdentifier);
                writer.WriteString("textRelativePath", license.TextRelativePath);
                writer.WriteString("textSha256", license.TextSha256);
                writer.WriteString("sourceUrl", license.SourceUrl);
                writer.WriteString("redistributionNotes", license.RedistributionNotes);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("sources");
            writer.WriteStartArray();
            foreach (ReplayFoundryRuntimePackSource source in manifest.Sources)
            {
                writer.WriteStartObject();
                writer.WriteString("officialUrl", source.OfficialUrl);
                writer.WriteString("revision", source.Revision);
                writer.WriteString("artifactSha256", source.ArtifactSha256);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteString("replayFoundryMinimumVersion", manifest.ReplayFoundryMinimumVersion);
            writer.WriteString("replayFoundryMaximumVersionExclusive", manifest.ReplayFoundryMaximumVersionExclusive);
            writer.WriteString("createdAtUtc", manifest.CreatedAtUtc.ToString("O"));
            if (includeHash)
                writer.WriteString("manifestHash", manifest.ManifestHash);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private sealed class ManifestDto
    {
        public string? SchemaVersion { get; set; }
        public IdentityDto? Identity { get; set; }
        public string? DisplayName { get; set; }
        public string? Platform { get; set; }
        public ReplayFoundryRuntimeBackend Backend { get; set; }
        public FileDto[]? Files { get; set; }
        public DependencyDto[]? Dependencies { get; set; }
        public LicenseDto[]? Licenses { get; set; }
        public SourceDto[]? Sources { get; set; }
        public string? ReplayFoundryMinimumVersion { get; set; }
        public string? ReplayFoundryMaximumVersionExclusive { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }
        public string? ManifestHash { get; set; }

        public ReplayFoundryRuntimePackManifest ToManifest()
        {
            if (!string.Equals(Platform, "win-x64", StringComparison.Ordinal))
                throw new InvalidDataException("Only the win-x64 runtime pack platform is supported.");
            return new ReplayFoundryRuntimePackManifest(
                SchemaVersion!, Identity!.ToIdentity(), DisplayName!, Backend,
                Files!.Select(file => file.ToFile()),
                (Dependencies ?? []).Select(dependency => dependency.ToDependency()),
                Licenses!.Select(license => license.ToLicense()),
                Sources!.Select(source => source.ToSource()),
                ReplayFoundryMinimumVersion!, ReplayFoundryMaximumVersionExclusive!,
                CreatedAtUtc, ManifestHash!);
        }
    }

    private sealed class IdentityDto
    {
        public string? PackageId { get; set; }
        public ReplayFoundryRuntimePackKind Kind { get; set; }
        public string? SemanticVersion { get; set; }
        public ReplayFoundryRuntimePackIdentity ToIdentity() => new(PackageId!, Kind, SemanticVersion!);
    }
    private sealed class FileDto
    {
        public string? RelativePath { get; set; }
        public long ByteLength { get; set; }
        public string? Sha256 { get; set; }
        public ReplayFoundryRuntimeFileRole Role { get; set; }
        public ReplayFoundryRuntimePackFile ToFile() => new(RelativePath!, ByteLength, Sha256!, Role);
    }
    private sealed class DependencyDto
    {
        public string? PackageId { get; set; }
        public string? MinimumVersion { get; set; }
        public string? RequiredManifestHash { get; set; }
        public ReplayFoundryRuntimePackDependency ToDependency() => new(PackageId!, MinimumVersion!, RequiredManifestHash);
    }
    private sealed class LicenseDto
    {
        public string? ComponentName { get; set; }
        public string? LicenseIdentifier { get; set; }
        public string? TextRelativePath { get; set; }
        public string? TextSha256 { get; set; }
        public string? SourceUrl { get; set; }
        public string? RedistributionNotes { get; set; }
        public ReplayFoundryRuntimePackLicense ToLicense() => new(ComponentName!, LicenseIdentifier!, TextRelativePath!, TextSha256!, SourceUrl!, RedistributionNotes!);
    }
    private sealed class SourceDto
    {
        public string? OfficialUrl { get; set; }
        public string? Revision { get; set; }
        public string? ArtifactSha256 { get; set; }
        public ReplayFoundryRuntimePackSource ToSource() => new(OfficialUrl!, Revision!, ArtifactSha256!);
    }
}
