using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReplayFoundry.Desktop.Features.Studio.CreativePacks;

public enum StudioCreativeAssetKind
{
    Sticker,
    Sound,
    MontageTemplate,
}

public enum StudioCreativePackAcquisitionKind
{
    BuiltIn,
    Free,
    Purchased,
}

public sealed class StudioCreativePackAsset
{
    private static readonly IReadOnlyDictionary<
        StudioCreativeAssetKind,
        IReadOnlyDictionary<string, string[]>> AllowedFormats =
        new Dictionary<
            StudioCreativeAssetKind,
            IReadOnlyDictionary<string, string[]>>
        {
            [StudioCreativeAssetKind.Sticker] =
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    [".png"] = ["image/png"],
                    [".webp"] = ["image/webp"],
                    [".gif"] = ["image/gif"],
                },
            [StudioCreativeAssetKind.Sound] =
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    [".wav"] = ["audio/wav", "audio/x-wav"],
                    [".flac"] = ["audio/flac"],
                    [".mp3"] = ["audio/mpeg"],
                    [".m4a"] = ["audio/mp4"],
                    [".ogg"] = ["audio/ogg"],
                },
            [StudioCreativeAssetKind.MontageTemplate] =
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    [".json"] =
                    [
                        "application/vnd.replayfoundry.montage-template+json",
                    ],
                },
        };

    public StudioCreativePackAsset(
        string assetId,
        StudioCreativeAssetKind kind,
        string relativePath,
        string contentType,
        long byteLength,
        string sha256)
    {
        AssetId = CreativePackValidation.Identifier(assetId, nameof(assetId));
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Kind = kind;
        RelativePath = CreativePackValidation.RelativePath(
            relativePath,
            nameof(relativePath));
        ContentType = CreativePackValidation.Required(
            contentType,
            nameof(contentType));
        if (byteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteLength),
                "Creative-pack assets must contain at least one byte.");
        }

        ByteLength = byteLength;
        Sha256 = CreativePackValidation.Sha256(sha256, nameof(sha256));
        ValidateFormat();
    }

    public string AssetId { get; }

    public StudioCreativeAssetKind Kind { get; }

    public string RelativePath { get; }

    public string ContentType { get; }

    public long ByteLength { get; }

    public string Sha256 { get; }

    private void ValidateFormat()
    {
        string extension = Path.GetExtension(RelativePath);
        if (!AllowedFormats[Kind].TryGetValue(extension, out string[]? types) ||
            !types.Contains(ContentType, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"{Kind} assets do not support '{extension}' with content type '{ContentType}'.",
                nameof(RelativePath));
        }
    }
}

public sealed class StudioCreativePackManifest
{
    public const string SupportedSchemaVersion =
        "replayfoundry-studio-creative-pack-1.0";

    private readonly ReadOnlyCollection<StudioCreativePackAsset> _assets;

    public StudioCreativePackManifest(
        string schemaVersion,
        string packId,
        string version,
        string title,
        string description,
        string publisher,
        DateTimeOffset createdAtUtc,
        IEnumerable<StudioCreativePackAsset> assets)
    {
        if (!string.Equals(
                schemaVersion,
                SupportedSchemaVersion,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Creative-pack schema must be exactly '{SupportedSchemaVersion}'.",
                nameof(schemaVersion));
        }

        SchemaVersion = schemaVersion;
        PackId = CreativePackValidation.Identifier(packId, nameof(packId));
        Version = CreativePackValidation.Version(version, nameof(version));
        Title = CreativePackValidation.Required(title, nameof(title));
        Description = CreativePackValidation.Required(
            description,
            nameof(description));
        Publisher = CreativePackValidation.Required(publisher, nameof(publisher));
        if (createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Creative-pack creation time must use UTC.",
                nameof(createdAtUtc));
        }

        CreatedAtUtc = createdAtUtc;
        ArgumentNullException.ThrowIfNull(assets);
        StudioCreativePackAsset[] snapshot = assets.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(static asset => asset is null))
        {
            throw new ArgumentException(
                "A creative pack must contain at least one non-null asset.",
                nameof(assets));
        }

        if (snapshot.Select(static asset => asset.AssetId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != snapshot.Length)
        {
            throw new ArgumentException(
                "Creative-pack asset IDs must be unique ignoring case.",
                nameof(assets));
        }

        if (snapshot.Select(static asset => asset.RelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != snapshot.Length)
        {
            throw new ArgumentException(
                "Creative-pack paths must be unique ignoring case.",
                nameof(assets));
        }

        _assets = Array.AsReadOnly(snapshot);
        ManifestSha256 = ComputeManifestSha256();
    }

    public string SchemaVersion { get; }

    public string PackId { get; }

    public string Version { get; }

    public string Title { get; }

    public string Description { get; }

    public string Publisher { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public IReadOnlyList<StudioCreativePackAsset> Assets => _assets;

    public string ManifestSha256 { get; }

    private string ComputeManifestSha256()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", SchemaVersion);
            writer.WriteString("packId", PackId);
            writer.WriteString("version", Version);
            writer.WriteString("title", Title);
            writer.WriteString("description", Description);
            writer.WriteString("publisher", Publisher);
            writer.WriteString("createdAtUtc", CreatedAtUtc);
            writer.WriteStartArray("assets");
            foreach (StudioCreativePackAsset asset in Assets)
            {
                writer.WriteStartObject();
                writer.WriteString("assetId", asset.AssetId);
                writer.WriteString("kind", asset.Kind.ToString());
                writer.WriteString("relativePath", asset.RelativePath);
                writer.WriteString("contentType", asset.ContentType);
                writer.WriteNumber("byteLength", asset.ByteLength);
                writer.WriteString("sha256", asset.Sha256);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
}

public sealed class StudioCreativePackOffer
{
    public StudioCreativePackOffer(
        string packId,
        string title,
        string description,
        StudioCreativePackAcquisitionKind acquisitionKind,
        string? commerceProviderDisplayName = null,
        Uri? checkoutUri = null)
    {
        PackId = CreativePackValidation.Identifier(packId, nameof(packId));
        Title = CreativePackValidation.Required(title, nameof(title));
        Description = CreativePackValidation.Required(
            description,
            nameof(description));
        if (!Enum.IsDefined(acquisitionKind))
        {
            throw new ArgumentOutOfRangeException(nameof(acquisitionKind));
        }

        AcquisitionKind = acquisitionKind;
        if (acquisitionKind == StudioCreativePackAcquisitionKind.Purchased)
        {
            CommerceProviderDisplayName = CreativePackValidation.Required(
                commerceProviderDisplayName,
                nameof(commerceProviderDisplayName));
            if (checkoutUri is null ||
                !string.Equals(
                    checkoutUri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Purchased creative packs require an HTTPS hosted checkout.",
                    nameof(checkoutUri));
            }

            CheckoutUri = checkoutUri;
        }
        else if (commerceProviderDisplayName is not null || checkoutUri is not null)
        {
            throw new ArgumentException(
                "Built-in and free creative packs cannot carry checkout data.",
                nameof(checkoutUri));
        }
    }

    public string PackId { get; }

    public string Title { get; }

    public string Description { get; }

    public StudioCreativePackAcquisitionKind AcquisitionKind { get; }

    public string? CommerceProviderDisplayName { get; }

    public Uri? CheckoutUri { get; }
}

internal static partial class CreativePackValidation
{
    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(
        "^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex("^[0-9A-Fa-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    public static string Required(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        return value.Trim();
    }

    public static string Identifier(string? value, string parameterName)
    {
        string required = Required(value, parameterName);
        if (!IdentifierPattern().IsMatch(required))
        {
            throw new ArgumentException(
                "Identifiers must be lowercase ASCII letters, digits, periods, or hyphens.",
                parameterName);
        }

        return required;
    }

    public static string Version(string? value, string parameterName)
    {
        string required = Required(value, parameterName);
        if (!VersionPattern().IsMatch(required))
        {
            throw new ArgumentException(
                "Creative-pack versions must use a three-part semantic version.",
                parameterName);
        }

        return required;
    }

    public static string Sha256(string? value, string parameterName)
    {
        string required = Required(value, parameterName);
        if (!Sha256Pattern().IsMatch(required))
        {
            throw new ArgumentException(
                "A 64-character SHA-256 value is required.",
                parameterName);
        }

        return required.ToUpperInvariant();
    }

    public static string RelativePath(string? value, string parameterName)
    {
        string required = Required(value, parameterName);
        if (required.Contains('\\') ||
            Path.IsPathRooted(required) ||
            required.Contains(':'))
        {
            throw new ArgumentException(
                "Creative-pack paths must be forward-slash relative paths.",
                parameterName);
        }

        string[] parts = required.Split('/');
        if (parts.Any(static part =>
                string.IsNullOrWhiteSpace(part) ||
                part is "." or ".."))
        {
            throw new ArgumentException(
                "Creative-pack paths cannot be empty or contain traversal segments.",
                parameterName);
        }

        return required;
    }
}
