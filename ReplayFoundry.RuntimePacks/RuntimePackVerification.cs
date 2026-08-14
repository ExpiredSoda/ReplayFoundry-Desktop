using System.Text.Json;

namespace ReplayFoundry.RuntimePacks;

public sealed record ReplayFoundryRuntimePackVerificationResult(
    bool IsValid,
    ReplayFoundryRuntimePackManifest? Manifest,
    IReadOnlyList<string> Errors);

public enum ReplayFoundryRuntimePackVerificationMode
{
    Full,
    RuntimeStartup,
}

public sealed record InstalledReplayFoundryRuntimePack(
    ReplayFoundryRuntimePackManifest Manifest,
    string RootDirectory)
{
    public string Resolve(ReplayFoundryRuntimeFileRole role)
    {
        ReplayFoundryRuntimePackFile file = Manifest.Entry(role) ??
            throw new InvalidDataException($"Pack {Manifest.Identity.PackageId} does not expose {role}.");
        return ReplayFoundryRuntimePackVerifier.ResolveContainedPath(RootDirectory, file.RelativePath);
    }
}

public sealed class ReplayFoundryRuntimePackVerifier
{
    public async Task<ReplayFoundryRuntimePackVerificationResult> VerifyAsync(
        string rootDirectory,
        bool rejectExtraFiles = true,
        ReplayFoundryRuntimePackVerificationMode mode = ReplayFoundryRuntimePackVerificationMode.Full,
        CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(rootDirectory);
        var errors = new List<string>();
        ReplayFoundryRuntimePackManifest? manifest;
        try
        {
            manifest = await RuntimePackManifestJson.ReadAsync(
                Path.Combine(root, RuntimePackManifestJson.FileName), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or JsonException or ArgumentException)
        {
            return new(false, null, Array.AsReadOnly(new[] { exception.Message }));
        }

        foreach (ReplayFoundryRuntimePackFile file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ShouldInspect(manifest, file, mode))
                continue;
            string path;
            try { path = ResolveContainedPath(root, file.RelativePath); }
            catch (InvalidDataException exception) { errors.Add(exception.Message); continue; }
            if (!File.Exists(path))
            {
                errors.Add($"Missing file: {file.RelativePath}");
                continue;
            }
            FileInfo info = new(path);
            if (info.Length != file.ByteLength)
            {
                errors.Add($"Wrong size: {file.RelativePath}");
                continue;
            }
            if (ShouldHash(manifest, file, mode))
            {
                string hash = await ReplayFoundryRuntimePackBuilder.ComputeSha256Async(path, cancellationToken);
                if (!string.Equals(hash, file.Sha256, StringComparison.Ordinal))
                    errors.Add($"Wrong SHA-256: {file.RelativePath}");
            }
        }

        if (rejectExtraFiles && mode == ReplayFoundryRuntimePackVerificationMode.Full)
        {
            HashSet<string> expected = manifest.Files.Select(file => file.RelativePath)
                .Append(RuntimePackManifestJson.FileName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (!expected.Contains(relative))
                    errors.Add($"Unexpected file: {relative}");
            }
        }

        return new(errors.Count == 0, manifest, Array.AsReadOnly(errors.ToArray()));
    }

    private static bool ShouldHash(
        ReplayFoundryRuntimePackManifest manifest,
        ReplayFoundryRuntimePackFile file,
        ReplayFoundryRuntimePackVerificationMode mode)
    {
        if (mode == ReplayFoundryRuntimePackVerificationMode.Full)
            return true;
        return manifest.Identity.Kind switch
        {
            ReplayFoundryRuntimePackKind.TranscriptionModel =>
                file.Role is not ReplayFoundryRuntimeFileRole.WhisperModel,
            ReplayFoundryRuntimePackKind.VisualRuntime =>
                file.Role is not ReplayFoundryRuntimeFileRole.Asset,
            ReplayFoundryRuntimePackKind.VisualModel =>
                file.Role is not ReplayFoundryRuntimeFileRole.Asset,
            _ => true,
        };
    }

    private static bool ShouldInspect(
        ReplayFoundryRuntimePackManifest manifest,
        ReplayFoundryRuntimePackFile file,
        ReplayFoundryRuntimePackVerificationMode mode)
    {
        if (mode == ReplayFoundryRuntimePackVerificationMode.Full)
            return true;
        return manifest.Identity.Kind switch
        {
            ReplayFoundryRuntimePackKind.VisualRuntime or
            ReplayFoundryRuntimePackKind.VisualModel =>
                file.Role is not ReplayFoundryRuntimeFileRole.Asset,
            _ => true,
        };
    }

    public static string ResolveContainedPath(string rootDirectory, string relativePath)
    {
        string root = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(Path.Combine(root, RuntimePackValidation.RelativePath(relativePath)));
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A runtime pack path escaped its root.");
        return candidate;
    }
}
