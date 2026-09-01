using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public sealed record VisualSemanticModelFile
{
    public VisualSemanticModelFile(
        string relativePath,
        string sha256,
        long byteLength)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathFullyQualified(relativePath) ||
            relativePath
                .Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(static segment => segment == "..") ||
            byteLength < 0)
        {
            throw new ArgumentException(
                "Model files require a safe relative path and non-negative size.");
        }

        RelativePath = relativePath
            .Replace(Path.AltDirectorySeparatorChar, '/')
            .Replace(Path.DirectorySeparatorChar, '/');
        Sha256 = ModelArtifactManifest.Sha256Value(
            sha256,
            nameof(sha256));
        ByteLength = byteLength;
    }

    public string RelativePath { get; }

    public string Sha256 { get; }

    public long ByteLength { get; }
}

public sealed class VisualSemanticModelManifest
{
    public const string SupportedSchemaVersion =
        "visual-semantic-model-manifest-1.0";

    private readonly ReadOnlyCollection<VisualSemanticModelFile> _files;

    public VisualSemanticModelManifest(
        string schemaVersion,
        string repositoryId,
        string revision,
        string modelDirectoryPath,
        string licenseIdentifier,
        string sourceUrl,
        IEnumerable<VisualSemanticModelFile> files,
        string manifestSha256)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (string.IsNullOrWhiteSpace(modelDirectoryPath) ||
            !Path.IsPathFullyQualified(modelDirectoryPath))
        {
            throw new ArgumentException(
                "A visual-semantic model directory must be fully qualified.",
                nameof(modelDirectoryPath));
        }

        VisualSemanticModelFile[] snapshot =
            files
                .OrderBy(
                    static value => value.RelativePath,
                    StringComparer.Ordinal)
                .ToArray();

        if (snapshot.Length == 0 ||
            snapshot.Any(static value => value is null) ||
            snapshot
                .GroupBy(
                    static value => value.RelativePath,
                    StringComparer.OrdinalIgnoreCase)
                .Any(static group => group.Count() > 1))
        {
            throw new ArgumentException(
                "A model manifest requires unique ordered files.",
                nameof(files));
        }

        SchemaVersion = VisualSemanticContractText.Required(
            schemaVersion,
            nameof(schemaVersion),
            64);

        if (!string.Equals(
                SchemaVersion,
                SupportedSchemaVersion,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The model manifest schema must be '{SupportedSchemaVersion}'.",
                nameof(schemaVersion));
        }
        RepositoryId = VisualSemanticContractText.Required(
            repositoryId,
            nameof(repositoryId),
            256);
        Revision = VisualSemanticContractText.Required(
            revision,
            nameof(revision),
            256);
        ModelDirectoryPath = Path.GetFullPath(modelDirectoryPath);
        LicenseIdentifier = VisualSemanticContractText.Required(
            licenseIdentifier,
            nameof(licenseIdentifier),
            128);
        SourceUrl = VisualSemanticContractText.Required(
            sourceUrl,
            nameof(sourceUrl),
            2048);
        _files = Array.AsReadOnly(snapshot);
        string expected =
            ComputeManifestSha256(
                SchemaVersion,
                RepositoryId,
                Revision,
                LicenseIdentifier,
                SourceUrl,
                _files);
        string supplied = ModelArtifactManifest.Sha256Value(
            manifestSha256,
            nameof(manifestSha256));

        if (!string.Equals(expected, supplied, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The visual-semantic model manifest hash does not match its canonical content.",
                nameof(manifestSha256));
        }

        ManifestSha256 = supplied;
    }

    public string SchemaVersion { get; }

    public string RepositoryId { get; }

    public string Revision { get; }

    public string ModelDirectoryPath { get; }

    public string LicenseIdentifier { get; }

    public string SourceUrl { get; }

    public IReadOnlyList<VisualSemanticModelFile> Files => _files;

    public string ManifestSha256 { get; }

    public string ComputeManifestSha256()
        =>
        ComputeManifestSha256(
            SchemaVersion,
            RepositoryId,
            Revision,
            LicenseIdentifier,
            SourceUrl,
            _files);

    public static string ComputeManifestSha256(
        string schemaVersion,
        string repositoryId,
        string revision,
        string licenseIdentifier,
        string sourceUrl,
        IEnumerable<VisualSemanticModelFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var canonical = new StringBuilder();
        Append(
            canonical,
            VisualSemanticContractText.Required(
                schemaVersion,
                nameof(schemaVersion),
                64));
        Append(
            canonical,
            VisualSemanticContractText.Required(
                repositoryId,
                nameof(repositoryId),
                256));
        Append(
            canonical,
            VisualSemanticContractText.Required(
                revision,
                nameof(revision),
                256));
        Append(
            canonical,
            VisualSemanticContractText.Required(
                licenseIdentifier,
                nameof(licenseIdentifier),
                128));
        Append(
            canonical,
            VisualSemanticContractText.Required(
                sourceUrl,
                nameof(sourceUrl),
                2048));

        foreach (VisualSemanticModelFile file in
                 files.OrderBy(
                     static value => value.RelativePath,
                     StringComparer.Ordinal))
        {
            ArgumentNullException.ThrowIfNull(file);
            Append(canonical, file.RelativePath);
            Append(canonical, file.Sha256);
            Append(
                canonical,
                file.ByteLength.ToString(CultureInfo.InvariantCulture));
        }

        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    public void VerifyInstalledFiles(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(ModelDirectoryPath))
        {
            throw new DirectoryNotFoundException(
                $"The configured model directory does not exist: '{ModelDirectoryPath}'.");
        }

        foreach (VisualSemanticModelFile file in _files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.GetFullPath(
                Path.Combine(
                    ModelDirectoryPath,
                    file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));

            if (!path.StartsWith(
                    Path.TrimEndingDirectorySeparator(ModelDirectoryPath) +
                    Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path))
            {
                throw new FileNotFoundException(
                    "A model-manifest file is missing or escaped the model directory.",
                    path);
            }

            var info = new FileInfo(path);

            if (info.Length != file.ByteLength ||
                !string.Equals(
                    ModelArtifactManifest.ComputeSha256(path),
                    file.Sha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Model file integrity failed for '{file.RelativePath}'.");
            }
        }
    }

    private static void Append(StringBuilder target, string value)
    {
        target.Append(
            value.Length.ToString(CultureInfo.InvariantCulture));
        target.Append(':');
        target.Append(value);
        target.Append('\n');
    }
}

public sealed record VisualSemanticPromptManifest
{
    public const string SupportedSchemaVersion =
        "visual-semantic-prompt-manifest-1.0";

    public const string SupportedName =
        "ReplayFoundry Visual Semantic Observation Prompt";

    public const string SupportedVersion = "1.0";
    public const string QualifiedEditorialSchemaVersion =
        "visual-semantic-prompt-manifest-2.0";
    public const string QualifiedEditorialName =
        "ReplayFoundry Visual Semantic Editorial Observation Prompt";
    public const string QualifiedEditorialVersion = "2.7";

    public VisualSemanticPromptManifest(
        string schemaVersion,
        string name,
        string version,
        string text,
        string sha256,
        DateTimeOffset frozenAtUtc)
    {
        ModelArtifactManifest.RequireUtc(
            frozenAtUtc,
            nameof(frozenAtUtc));
        SchemaVersion = VisualSemanticContractText.Required(
            schemaVersion,
            nameof(schemaVersion),
            64);
        Name = VisualSemanticContractText.Required(
            name,
            nameof(name),
            256);
        Version = VisualSemanticContractText.Required(
            version,
            nameof(version),
            64);

        bool legacy = string.Equals(
                          SchemaVersion,
                          SupportedSchemaVersion,
                          StringComparison.Ordinal) &&
                      string.Equals(Name, SupportedName, StringComparison.Ordinal) &&
                      string.Equals(Version, SupportedVersion, StringComparison.Ordinal);
        bool qualified = string.Equals(
                             SchemaVersion,
                             QualifiedEditorialSchemaVersion,
                             StringComparison.Ordinal) &&
                         string.Equals(Name, QualifiedEditorialName, StringComparison.Ordinal) &&
                         string.Equals(Version, QualifiedEditorialVersion, StringComparison.Ordinal);
        if (!legacy && !qualified)
        {
            throw new ArgumentException(
                "The visual-semantic prompt must use a frozen ReplayFoundry prompt identity.");
        }
        Text = VisualSemanticContractText.Required(
            text,
            nameof(text),
            32 * 1024);
        string expected = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Text)));
        string supplied = ModelArtifactManifest.Sha256Value(
            sha256,
            nameof(sha256));

        if (!string.Equals(expected, supplied, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The prompt hash does not match the frozen prompt text.",
                nameof(sha256));
        }

        Sha256 = supplied;
        FrozenAtUtc = frozenAtUtc;
    }

    public string SchemaVersion { get; }

    public string Name { get; }

    public string Version { get; }

    public string Text { get; }

    public string Sha256 { get; }

    public DateTimeOffset FrozenAtUtc { get; }
}
