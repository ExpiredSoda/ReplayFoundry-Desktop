using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReplayFoundry.RuntimePacks;

public sealed record ReplayFoundryRuntimePackRecipe(
    string PackageId,
    ReplayFoundryRuntimePackKind Kind,
    string SemanticVersion,
    string DisplayName,
    ReplayFoundryRuntimeBackend Backend,
    IReadOnlyDictionary<ReplayFoundryRuntimeFileRole, string> Entries,
    IReadOnlyList<ReplayFoundryRuntimePackDependency> Dependencies,
    IReadOnlyList<ReplayFoundryRuntimePackLicense> Licenses,
    IReadOnlyList<ReplayFoundryRuntimePackSource> Sources,
    string ReplayFoundryMinimumVersion,
    string ReplayFoundryMaximumVersionExclusive,
    DateTimeOffset CreatedAtUtc);

public static class ReplayFoundryRuntimePackBuilder
{
    private static readonly JsonSerializerOptions RecipeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<ReplayFoundryRuntimePackRecipe> ReadRecipeAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using FileStream stream = new(
            Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        RecipeDto? dto = await JsonSerializer.DeserializeAsync<RecipeDto>(
            stream, RecipeOptions, cancellationToken);
        return dto?.ToRecipe() ?? throw new InvalidDataException("The runtime pack recipe is empty.");
    }

    public static async Task<ReplayFoundryRuntimePackManifest> BuildAsync(
        string sourceRoot,
        ReplayFoundryRuntimePackRecipe recipe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        string root = Path.GetFullPath(sourceRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Runtime pack source does not exist: {root}");

        var entries = new Dictionary<string, ReplayFoundryRuntimeFileRole>(StringComparer.OrdinalIgnoreCase);
        foreach ((ReplayFoundryRuntimeFileRole role, string path) in recipe.Entries)
        {
            if (!Enum.IsDefined(role) || role is ReplayFoundryRuntimeFileRole.Asset or ReplayFoundryRuntimeFileRole.License or ReplayFoundryRuntimeFileRole.Notice)
                throw new InvalidDataException("A recipe entry must use a typed runtime role.");
            string relative = RuntimePackValidation.RelativePath(path);
            if (!entries.TryAdd(relative, role))
                throw new InvalidDataException("Recipe entries must use unique paths.");
        }

        var licensePaths = recipe.Licenses.ToDictionary(
            license => license.TextRelativePath,
            _ => ReplayFoundryRuntimeFileRole.License,
            StringComparer.OrdinalIgnoreCase);
        var files = new List<ReplayFoundryRuntimePackFile>();
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (string.Equals(relative, RuntimePackManifestJson.FileName, StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith(".staging-", StringComparison.OrdinalIgnoreCase))
                continue;
            FileInfo info = new(path);
            files.Add(new ReplayFoundryRuntimePackFile(
                relative,
                info.Length,
                await ComputeSha256Async(path, cancellationToken),
                entries.TryGetValue(relative, out ReplayFoundryRuntimeFileRole entryRole)
                    ? entryRole
                    : licensePaths.TryGetValue(relative, out ReplayFoundryRuntimeFileRole licenseRole)
                        ? licenseRole
                        : IsNotice(relative)
                            ? ReplayFoundryRuntimeFileRole.Notice
                            : ReplayFoundryRuntimeFileRole.Asset));
        }

        foreach ((string entry, _) in entries)
            if (!files.Any(file => string.Equals(file.RelativePath, entry, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Recipe entry '{entry}' does not exist in the source.");

        return ReplayFoundryRuntimePackManifest.Create(
            new ReplayFoundryRuntimePackIdentity(recipe.PackageId, recipe.Kind, recipe.SemanticVersion),
            recipe.DisplayName,
            recipe.Backend,
            files,
            recipe.Dependencies,
            recipe.Licenses,
            recipe.Sources,
            recipe.ReplayFoundryMinimumVersion,
            recipe.ReplayFoundryMaximumVersionExclusive,
            recipe.CreatedAtUtc);
    }

    internal static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            256 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static bool IsNotice(string path) =>
        Path.GetFileName(path).StartsWith("NOTICE", StringComparison.OrdinalIgnoreCase) ||
        Path.GetFileName(path).StartsWith("THIRD-PARTY", StringComparison.OrdinalIgnoreCase);

    private sealed class RecipeDto
    {
        public string? PackageId { get; set; }
        public ReplayFoundryRuntimePackKind Kind { get; set; }
        public string? SemanticVersion { get; set; }
        public string? DisplayName { get; set; }
        public ReplayFoundryRuntimeBackend Backend { get; set; }
        public Dictionary<ReplayFoundryRuntimeFileRole, string>? Entries { get; set; }
        public DependencyDto[]? Dependencies { get; set; }
        public LicenseDto[]? Licenses { get; set; }
        public SourceDto[]? Sources { get; set; }
        public string? ReplayFoundryMinimumVersion { get; set; }
        public string? ReplayFoundryMaximumVersionExclusive { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }

        public ReplayFoundryRuntimePackRecipe ToRecipe() => new(
            PackageId!, Kind, SemanticVersion!, DisplayName!, Backend,
            new Dictionary<ReplayFoundryRuntimeFileRole, string>(Entries ?? []),
            (Dependencies ?? []).Select(value => value.ToValue()).ToArray(),
            (Licenses ?? []).Select(value => value.ToValue()).ToArray(),
            (Sources ?? []).Select(value => value.ToValue()).ToArray(),
            ReplayFoundryMinimumVersion!, ReplayFoundryMaximumVersionExclusive!, CreatedAtUtc);
    }
    private sealed class DependencyDto
    {
        public string? PackageId { get; set; }
        public string? MinimumVersion { get; set; }
        public string? RequiredManifestHash { get; set; }
        public ReplayFoundryRuntimePackDependency ToValue() => new(PackageId!, MinimumVersion!, RequiredManifestHash);
    }
    private sealed class LicenseDto
    {
        public string? ComponentName { get; set; }
        public string? LicenseIdentifier { get; set; }
        public string? TextRelativePath { get; set; }
        public string? TextSha256 { get; set; }
        public string? SourceUrl { get; set; }
        public string? RedistributionNotes { get; set; }
        public ReplayFoundryRuntimePackLicense ToValue() => new(ComponentName!, LicenseIdentifier!, TextRelativePath!, TextSha256!, SourceUrl!, RedistributionNotes!);
    }
    private sealed class SourceDto
    {
        public string? OfficialUrl { get; set; }
        public string? Revision { get; set; }
        public string? ArtifactSha256 { get; set; }
        public ReplayFoundryRuntimePackSource ToValue() => new(OfficialUrl!, Revision!, ArtifactSha256!);
    }
}
