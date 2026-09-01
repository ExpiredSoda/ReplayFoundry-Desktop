using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ReplayFoundry.RuntimePacks;

public enum ReplayFoundryRuntimePackKind
{
    MediaTools,
    SpeechActivity,
    TranscriptionRuntime,
    TranscriptionModel,
    VisualRuntime,
    VisualModel,
}

public enum ReplayFoundryRuntimeBackend
{
    Cpu,
    Cuda,
    General,
}

public enum ReplayFoundryRuntimeFileRole
{
    Asset,
    License,
    Notice,
    FfmpegExecutable,
    FfprobeExecutable,
    SpeechActivityModel,
    WhisperExecutable,
    WhisperModel,
    PythonExecutable,
    VisualHostScript,
    QwenModelManifest,
    QwenPromptManifest,
    QwenQualificationLock,
    WhisperVadModel,
}

public sealed record ReplayFoundryRuntimePackIdentity
{
    public ReplayFoundryRuntimePackIdentity(
        string packageId,
        ReplayFoundryRuntimePackKind kind,
        string semanticVersion)
    {
        PackageId = RuntimePackValidation.PackageId(packageId);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (!Version.TryParse(semanticVersion, out Version? version) ||
            version.Major < 0)
        {
            throw new ArgumentException(
                "Runtime pack versions must be valid semantic numeric versions.",
                nameof(semanticVersion));
        }

        Kind = kind;
        SemanticVersion = semanticVersion.Trim();
        ParsedVersion = version;
    }

    public string PackageId { get; }
    public ReplayFoundryRuntimePackKind Kind { get; }
    public string SemanticVersion { get; }
    internal Version ParsedVersion { get; }
}

public sealed record ReplayFoundryRuntimePackFile
{
    public ReplayFoundryRuntimePackFile(
        string relativePath,
        long byteLength,
        string sha256,
        ReplayFoundryRuntimeFileRole role = ReplayFoundryRuntimeFileRole.Asset)
    {
        RelativePath = RuntimePackValidation.RelativePath(relativePath);
        ArgumentOutOfRangeException.ThrowIfNegative(byteLength);

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        ByteLength = byteLength;
        Sha256 = RuntimePackValidation.Sha256(sha256, nameof(sha256));
        Role = role;
    }

    public string RelativePath { get; }
    public long ByteLength { get; }
    public string Sha256 { get; }
    public ReplayFoundryRuntimeFileRole Role { get; }
}

public sealed record ReplayFoundryRuntimePackDependency
{
    public ReplayFoundryRuntimePackDependency(
        string packageId,
        string minimumVersion,
        string? requiredManifestHash = null)
    {
        PackageId = RuntimePackValidation.PackageId(packageId);
        if (!Version.TryParse(minimumVersion, out Version? version))
        {
            throw new ArgumentException(
                "A dependency requires a valid minimum version.",
                nameof(minimumVersion));
        }

        MinimumVersion = minimumVersion.Trim();
        ParsedMinimumVersion = version;
        RequiredManifestHash = requiredManifestHash is null
            ? null
            : RuntimePackValidation.Sha256(
                requiredManifestHash,
                nameof(requiredManifestHash));
    }

    public string PackageId { get; }
    public string MinimumVersion { get; }
    public string? RequiredManifestHash { get; }
    internal Version ParsedMinimumVersion { get; }

    public bool Accepts(ReplayFoundryRuntimePackManifest manifest) =>
        string.Equals(
            PackageId,
            manifest.Identity.PackageId,
            StringComparison.OrdinalIgnoreCase) &&
        manifest.Identity.ParsedVersion >= ParsedMinimumVersion &&
        (RequiredManifestHash is null ||
         string.Equals(
             RequiredManifestHash,
             manifest.ManifestHash,
             StringComparison.Ordinal));
}

public sealed record ReplayFoundryRuntimePackLicense
{
    public ReplayFoundryRuntimePackLicense(
        string componentName,
        string licenseIdentifier,
        string textRelativePath,
        string textSha256,
        string sourceUrl,
        string redistributionNotes)
    {
        ComponentName = RuntimePackValidation.Required(componentName, nameof(componentName));
        LicenseIdentifier = RuntimePackValidation.Required(licenseIdentifier, nameof(licenseIdentifier));
        TextRelativePath = RuntimePackValidation.RelativePath(textRelativePath);
        TextSha256 = RuntimePackValidation.Sha256(textSha256, nameof(textSha256));
        SourceUrl = RuntimePackValidation.Https(sourceUrl, nameof(sourceUrl));
        RedistributionNotes = RuntimePackValidation.Required(redistributionNotes, nameof(redistributionNotes));
    }

    public string ComponentName { get; }
    public string LicenseIdentifier { get; }
    public string TextRelativePath { get; }
    public string TextSha256 { get; }
    public string SourceUrl { get; }
    public string RedistributionNotes { get; }
}

public sealed record ReplayFoundryRuntimePackSource
{
    public ReplayFoundryRuntimePackSource(
        string officialUrl,
        string revision,
        string artifactSha256)
    {
        OfficialUrl = RuntimePackValidation.Https(officialUrl, nameof(officialUrl));
        Revision = RuntimePackValidation.Required(revision, nameof(revision));
        ArtifactSha256 = RuntimePackValidation.Sha256(artifactSha256, nameof(artifactSha256));
    }

    public string OfficialUrl { get; }
    public string Revision { get; }
    public string ArtifactSha256 { get; }
}

public sealed class ReplayFoundryRuntimePackManifest
{
    public const string CurrentSchemaVersion = "replayfoundry-runtime-pack-1.0";

    private readonly ReadOnlyCollection<ReplayFoundryRuntimePackFile> _files;
    private readonly ReadOnlyCollection<ReplayFoundryRuntimePackDependency> _dependencies;
    private readonly ReadOnlyCollection<ReplayFoundryRuntimePackLicense> _licenses;
    private readonly ReadOnlyCollection<ReplayFoundryRuntimePackSource> _sources;

    public ReplayFoundryRuntimePackManifest(
        string schemaVersion,
        ReplayFoundryRuntimePackIdentity identity,
        string displayName,
        ReplayFoundryRuntimeBackend backend,
        IEnumerable<ReplayFoundryRuntimePackFile> files,
        IEnumerable<ReplayFoundryRuntimePackDependency>? dependencies,
        IEnumerable<ReplayFoundryRuntimePackLicense> licenses,
        IEnumerable<ReplayFoundryRuntimePackSource> sources,
        string replayFoundryMinimumVersion,
        string replayFoundryMaximumVersionExclusive,
        DateTimeOffset createdAtUtc,
        string manifestHash)
        : this(
            schemaVersion,
            identity,
            displayName,
            backend,
            files,
            dependencies,
            licenses,
            sources,
            replayFoundryMinimumVersion,
            replayFoundryMaximumVersionExclusive,
            createdAtUtc,
            manifestHash,
            validateManifestHash: true)
    {
    }

    private ReplayFoundryRuntimePackManifest(
        string schemaVersion,
        ReplayFoundryRuntimePackIdentity identity,
        string displayName,
        ReplayFoundryRuntimeBackend backend,
        IEnumerable<ReplayFoundryRuntimePackFile> files,
        IEnumerable<ReplayFoundryRuntimePackDependency>? dependencies,
        IEnumerable<ReplayFoundryRuntimePackLicense> licenses,
        IEnumerable<ReplayFoundryRuntimePackSource> sources,
        string replayFoundryMinimumVersion,
        string replayFoundryMaximumVersionExclusive,
        DateTimeOffset createdAtUtc,
        string manifestHash,
        bool validateManifestHash)
    {
        if (!string.Equals(schemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unsupported runtime pack schema.", nameof(schemaVersion));
        }

        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        DisplayName = RuntimePackValidation.Required(displayName, nameof(displayName));
        if (!Enum.IsDefined(backend))
        {
            throw new ArgumentOutOfRangeException(nameof(backend));
        }

        Backend = backend;
        _files = Array.AsReadOnly(SnapshotFiles(files));
        _dependencies = Array.AsReadOnly(SnapshotDependencies(dependencies ?? []));
        _licenses = Array.AsReadOnly(SnapshotRequired(licenses, nameof(licenses)));
        _sources = Array.AsReadOnly(SnapshotRequired(sources, nameof(sources)));
        if (!Version.TryParse(replayFoundryMinimumVersion, out Version? minimum) ||
            !Version.TryParse(replayFoundryMaximumVersionExclusive, out Version? maximum) ||
            minimum >= maximum)
        {
            throw new ArgumentException("Runtime pack Replay Foundry compatibility is invalid.");
        }

        ReplayFoundryMinimumVersion = replayFoundryMinimumVersion.Trim();
        ReplayFoundryMaximumVersionExclusive = replayFoundryMaximumVersionExclusive.Trim();
        if (createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Runtime pack timestamps must use UTC.", nameof(createdAtUtc));
        }

        CreatedAtUtc = createdAtUtc;
        ManifestHash = RuntimePackValidation.Sha256(manifestHash, nameof(manifestHash));
        ValidateRolesAndLicenses();
        if (validateManifestHash &&
            !string.Equals(
                RuntimePackManifestJson.ComputeHash(this),
                ManifestHash,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The runtime pack manifest hash is invalid.");
        }
    }

    public static ReplayFoundryRuntimePackManifest Create(
        ReplayFoundryRuntimePackIdentity identity,
        string displayName,
        ReplayFoundryRuntimeBackend backend,
        IEnumerable<ReplayFoundryRuntimePackFile> files,
        IEnumerable<ReplayFoundryRuntimePackDependency>? dependencies,
        IEnumerable<ReplayFoundryRuntimePackLicense> licenses,
        IEnumerable<ReplayFoundryRuntimePackSource> sources,
        string replayFoundryMinimumVersion,
        string replayFoundryMaximumVersionExclusive,
        DateTimeOffset createdAtUtc)
    {
        ReplayFoundryRuntimePackFile[] fileSnapshot = files.ToArray();
        ReplayFoundryRuntimePackDependency[] dependencySnapshot =
            (dependencies ?? []).ToArray();
        ReplayFoundryRuntimePackLicense[] licenseSnapshot = licenses.ToArray();
        ReplayFoundryRuntimePackSource[] sourceSnapshot = sources.ToArray();
        var unhashed = new ReplayFoundryRuntimePackManifest(
            CurrentSchemaVersion,
            identity,
            displayName,
            backend,
            fileSnapshot,
            dependencySnapshot,
            licenseSnapshot,
            sourceSnapshot,
            replayFoundryMinimumVersion,
            replayFoundryMaximumVersionExclusive,
            createdAtUtc,
            new string('0', 64),
            validateManifestHash: false);
        return new ReplayFoundryRuntimePackManifest(
            CurrentSchemaVersion,
            identity,
            displayName,
            backend,
            fileSnapshot,
            dependencySnapshot,
            licenseSnapshot,
            sourceSnapshot,
            replayFoundryMinimumVersion,
            replayFoundryMaximumVersionExclusive,
            createdAtUtc,
            RuntimePackManifestJson.ComputeHash(unhashed));
    }

    public string SchemaVersion => CurrentSchemaVersion;
    public ReplayFoundryRuntimePackIdentity Identity { get; }
    public string DisplayName { get; }
    public string Platform => "win-x64";
    public ReplayFoundryRuntimeBackend Backend { get; }
    public IReadOnlyList<ReplayFoundryRuntimePackFile> Files => _files;
    public IReadOnlyList<ReplayFoundryRuntimePackDependency> Dependencies => _dependencies;
    public IReadOnlyList<ReplayFoundryRuntimePackLicense> Licenses => _licenses;
    public IReadOnlyList<ReplayFoundryRuntimePackSource> Sources => _sources;
    public string ReplayFoundryMinimumVersion { get; }
    public string ReplayFoundryMaximumVersionExclusive { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public string ManifestHash { get; }

    public ReplayFoundryRuntimePackFile? Entry(ReplayFoundryRuntimeFileRole role) =>
        _files.SingleOrDefault(file => file.Role == role);

    private static ReplayFoundryRuntimePackFile[] SnapshotFiles(
        IEnumerable<ReplayFoundryRuntimePackFile> files)
    {
        ReplayFoundryRuntimePackFile[] snapshot =
            (files ?? throw new ArgumentNullException(nameof(files)))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        if (snapshot.Length == 0 || snapshot.Any(file => file is null) ||
            snapshot.Select(file => file.RelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.Length)
        {
            throw new ArgumentException("Runtime pack files must be nonempty and unique.", nameof(files));
        }

        return snapshot;
    }

    private static ReplayFoundryRuntimePackDependency[] SnapshotDependencies(
        IEnumerable<ReplayFoundryRuntimePackDependency> dependencies)
    {
        ReplayFoundryRuntimePackDependency[] snapshot = dependencies
            .OrderBy(dependency => dependency.PackageId, StringComparer.Ordinal)
            .ToArray();
        if (snapshot.Any(dependency => dependency is null) ||
            snapshot.Select(dependency => dependency.PackageId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.Length)
        {
            throw new ArgumentException("Runtime pack dependencies must be unique.", nameof(dependencies));
        }

        return snapshot;
    }

    private static T[] SnapshotRequired<T>(IEnumerable<T> values, string parameterName)
    {
        T[] snapshot = (values ?? throw new ArgumentNullException(parameterName)).ToArray();
        if (snapshot.Length == 0 || snapshot.Any(value => value is null))
        {
            throw new ArgumentException("Runtime pack provenance collections cannot be empty.", parameterName);
        }

        return snapshot;
    }

    private void ValidateRolesAndLicenses()
    {
        if (_dependencies.Any(dependency =>
                string.Equals(dependency.PackageId, Identity.PackageId, StringComparison.OrdinalIgnoreCase) &&
                (dependency.RequiredManifestHash is null ||
                 string.Equals(dependency.RequiredManifestHash, ManifestHash, StringComparison.Ordinal))))
        {
            throw new ArgumentException(
                "A same-package runtime dependency must identify a different manifest hash.");
        }

        ReplayFoundryRuntimeFileRole[] roles = _files
            .Where(file => file.Role is not ReplayFoundryRuntimeFileRole.Asset and
                                      not ReplayFoundryRuntimeFileRole.License and
                                      not ReplayFoundryRuntimeFileRole.Notice)
            .Select(file => file.Role)
            .ToArray();
        if (roles.Distinct().Count() != roles.Length)
        {
            throw new ArgumentException("Runtime pack entry roles must be unique.");
        }

        ReplayFoundryRuntimeFileRole[] required = Identity.Kind switch
        {
            ReplayFoundryRuntimePackKind.MediaTools =>
                [ReplayFoundryRuntimeFileRole.FfmpegExecutable, ReplayFoundryRuntimeFileRole.FfprobeExecutable],
            ReplayFoundryRuntimePackKind.SpeechActivity =>
                [ReplayFoundryRuntimeFileRole.SpeechActivityModel],
            ReplayFoundryRuntimePackKind.TranscriptionRuntime =>
                [ReplayFoundryRuntimeFileRole.WhisperExecutable],
            ReplayFoundryRuntimePackKind.TranscriptionModel =>
                [ReplayFoundryRuntimeFileRole.WhisperModel],
            ReplayFoundryRuntimePackKind.VisualRuntime =>
                [ReplayFoundryRuntimeFileRole.PythonExecutable, ReplayFoundryRuntimeFileRole.VisualHostScript],
            ReplayFoundryRuntimePackKind.VisualModel =>
                [ReplayFoundryRuntimeFileRole.QwenModelManifest,
                 ReplayFoundryRuntimeFileRole.QwenPromptManifest,
                 ReplayFoundryRuntimeFileRole.QwenQualificationLock],
            _ => throw new ArgumentOutOfRangeException(),
        };
        if (required.Any(role => !roles.Contains(role)))
        {
            throw new ArgumentException("The runtime pack is missing a required typed entry.");
        }

        foreach (ReplayFoundryRuntimePackLicense license in _licenses)
        {
            ReplayFoundryRuntimePackFile? file = _files.SingleOrDefault(item =>
                string.Equals(item.RelativePath, license.TextRelativePath, StringComparison.OrdinalIgnoreCase));
            if (file is null || file.Role != ReplayFoundryRuntimeFileRole.License ||
                !string.Equals(file.Sha256, license.TextSha256, StringComparison.Ordinal))
            {
                throw new ArgumentException("Runtime pack license provenance does not match a license file.");
            }
        }
    }
}

internal static class RuntimePackValidation
{
    public static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A nonblank value is required.", parameterName)
            : value.Trim();

    public static string PackageId(string value)
    {
        string result = Required(value, nameof(value));
        if (result.Length > 96 || result.Any(character =>
                !(char.IsLower(character) || char.IsDigit(character) || character is '-' or '.')))
        {
            throw new ArgumentException("Runtime pack IDs must be lowercase ASCII identifiers.", nameof(value));
        }

        return result;
    }

    public static string Sha256(string value, string parameterName)
    {
        string result = Required(value, parameterName).ToUpperInvariant();
        if (result.Length != 64 || result.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("A SHA-256 value requires 64 hexadecimal characters.", parameterName);
        }

        return result;
    }

    public static string RelativePath(string value)
    {
        string path = Required(value, nameof(value)).Replace('\\', '/');
        if (Path.IsPathFullyQualified(path) || path.StartsWith('/') ||
            path.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(part => part is "." or "..") ||
            path.Contains(':') || path.EndsWith('/'))
        {
            throw new ArgumentException("Runtime pack file paths must be contained relative paths.", nameof(value));
        }

        return path;
    }

    public static string Https(string value, string parameterName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Runtime pack sources must use HTTPS.", parameterName);
        }

        return uri.AbsoluteUri;
    }
}
