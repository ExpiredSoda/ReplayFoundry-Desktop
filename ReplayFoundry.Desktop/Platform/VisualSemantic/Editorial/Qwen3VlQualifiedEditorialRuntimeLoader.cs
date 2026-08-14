using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

public sealed record Qwen3VlQualifiedEditorialRuntime(
    IVisualSemanticEditorialProvider Provider,
    VisualSemanticPromptManifest Prompt,
    VisualSemanticModelManifest Model,
    VisualSemanticVideoInputPolicy VideoPolicy,
    Qwen3VlBatchHostSettings Host,
    string QualificationLockPath,
    string QualificationLockCanonicalHash);

public static class Qwen3VlQualifiedEditorialRuntimeLoader
{
    public static Qwen3VlQualifiedEditorialRuntime Load(
        string pythonExecutablePath,
        string hostScriptPath,
        string ffmpegSharedLibraryDirectoryPath,
        string modelManifestPath,
        string promptManifestPath,
        string qualificationLockPath,
        TimeSpan processTimeout,
        string? modelDirectoryOverride = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        string[] files =
        [
            pythonExecutablePath,
            hostScriptPath,
            modelManifestPath,
            promptManifestPath,
            qualificationLockPath,
        ];
        if (files.Any(path => string.IsNullOrWhiteSpace(path) ||
                !Path.IsPathFullyQualified(path) || !File.Exists(path)) ||
            string.IsNullOrWhiteSpace(ffmpegSharedLibraryDirectoryPath) ||
            !Path.IsPathFullyQualified(ffmpegSharedLibraryDirectoryPath) ||
            !Directory.Exists(ffmpegSharedLibraryDirectoryPath))
        {
            throw new ArgumentException(
                "Qualified Qwen runtime paths must be explicit existing files and directories.");
        }

        VisualSemanticPromptManifest prompt = LoadPrompt(promptManifestPath);
        VisualSemanticModelManifest model = LoadModel(
            modelManifestPath,
            modelDirectoryOverride);
        string qualificationLockCanonicalHash = LoadQualificationLock(
            qualificationLockPath,
            pythonExecutablePath,
            prompt,
            model);
        var host = new Qwen3VlBatchHostSettings(
            pythonExecutablePath,
            hostScriptPath,
            model.ModelDirectoryPath,
            Qwen3VlBatchHostSettings.SupportedVideoBackend,
            ffmpegSharedLibraryDirectoryPath,
            processTimeout,
            environmentVariables: environmentVariables);
        var settings = new Qwen3VlQualifiedEditorialSettings(
            host,
            qualificationLockPath,
            qualificationLockCanonicalHash);
        return new Qwen3VlQualifiedEditorialRuntime(
            new Qwen3VlQualifiedEditorialProvider(settings),
            prompt,
            model,
            VisualSemanticVideoInputPolicy.CreateV05A1(),
            host,
            Path.GetFullPath(qualificationLockPath),
            qualificationLockCanonicalHash);
    }

    private static string LoadQualificationLock(
        string path,
        string pythonExecutablePath,
        VisualSemanticPromptManifest prompt,
        VisualSemanticModelManifest model)
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllBytes(path));
        JsonElement root = document.RootElement;
        string canonicalHash = Text(root, "canonicalHash");
        string computedHash = Qwen3VlCanonicalJson.ComputeObjectSha256(
            root,
            "canonicalHash");
        string pythonHash = ModelArtifactManifest.ComputeSha256(
            pythonExecutablePath);
        if (!Text(root, "schemaVersion").Equals(
                Qwen3VlQualifiedEditorialProtocol
                    .QualificationLockSchemaVersion,
                StringComparison.Ordinal) ||
            !Text(root, "policyVersion").Equals(
                Qwen3VlEditorialStructuredDecodingPolicy.Version,
                StringComparison.Ordinal) ||
            !Text(root, "backendName").Equals(
                Qwen3VlEditorialStructuredDecodingPolicy.BackendName,
                StringComparison.Ordinal) ||
            !Text(root, "backendVersion").Equals(
                Qwen3VlEditorialStructuredDecodingPolicy.BackendVersion,
                StringComparison.Ordinal) ||
            !Text(root, "representation").Equals(
                nameof(Qwen3VlEditorialStructuredDecodingRepresentation
                    .JsonSchema),
                StringComparison.Ordinal) ||
            !Text(root, "cudaMaskBackend").Equals(
                Qwen3VlEditorialStructuredDecodingPolicy.CudaMaskBackend,
                StringComparison.Ordinal) ||
            !Text(root, "pythonExecutableSha256").Equals(
                pythonHash,
                StringComparison.OrdinalIgnoreCase) ||
            !Text(root, "promptSha256").Equals(
                prompt.Sha256,
                StringComparison.OrdinalIgnoreCase) ||
            !Text(root, "modelManifestSha256").Equals(
                model.ManifestSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !canonicalHash.Equals(computedHash, StringComparison.OrdinalIgnoreCase) ||
            Property(root, "capabilitySucceeded").ValueKind !=
                JsonValueKind.True ||
            Property(root, "unconstrainedFallbackPermitted").ValueKind !=
                JsonValueKind.False ||
            Property(root, "semanticRepairPermitted").ValueKind !=
                JsonValueKind.False)
        {
            throw new InvalidDataException(
                "The qualified Qwen lock does not authorize the exact runtime, model, prompt, and strict structured-decoding policy.");
        }

        return canonicalHash.ToLowerInvariant();
    }

    private static VisualSemanticPromptManifest LoadPrompt(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        return new VisualSemanticPromptManifest(
            Text(root, "schemaVersion"),
            Text(root, "name"),
            Text(root, "version"),
            Text(root, "text"),
            Text(root, "sha256"),
            DateTimeOffset.Parse(
                Text(root, "frozenAtUtc"),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind));
    }

    private static VisualSemanticModelManifest LoadModel(
        string path,
        string? modelDirectoryOverride)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        JsonElement files = Property(root, "files");
        if (files.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "The qualified Qwen model manifest files must be an array.");
        }
        return new VisualSemanticModelManifest(
            Text(root, "schemaVersion"),
            Text(root, "repositoryId"),
            Text(root, "revision"),
            modelDirectoryOverride is null
                ? Text(root, "modelDirectoryPath")
                : RequireExistingDirectory(modelDirectoryOverride),
            Text(root, "licenseIdentifier"),
            Text(root, "sourceUrl"),
            files.EnumerateArray().Select(file =>
                new VisualSemanticModelFile(
                    Text(file, "relativePath"),
                    Text(file, "sha256"),
                    Property(file, "byteLength").GetInt64())),
            Text(root, "manifestSha256"));
    }

    private static string RequireExistingDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !Path.IsPathFullyQualified(path) ||
            !Directory.Exists(path))
        {
            throw new ArgumentException(
                "The qualified Qwen model override must be an existing absolute directory.",
                nameof(path));
        }

        return Path.GetFullPath(path);
    }

    private static JsonElement Property(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out JsonElement property))
        {
            throw new InvalidDataException(
                $"The qualified Qwen manifest is missing '{name}'.");
        }
        return property;
    }

    private static string Text(JsonElement value, string name)
    {
        JsonElement property = Property(value, name);
        if (property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException(
                $"The qualified Qwen manifest '{name}' must be nonblank text.");
        }
        return property.GetString()!;
    }
}
