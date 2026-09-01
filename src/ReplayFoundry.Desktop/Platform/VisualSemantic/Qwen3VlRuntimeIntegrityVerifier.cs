using System.IO;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal sealed class Qwen3VlRuntimeIntegrityVerifier
{
    private readonly Qwen3VlBatchHostSettings _settings;

    internal Qwen3VlRuntimeIntegrityVerifier(
        Qwen3VlBatchHostSettings settings)
    {
        _settings = settings;
    }

    internal static async Task VerifyInputsAsync(
        VisualSemanticBatchRequest request,
        CancellationToken cancellationToken)
    {
        VisualSemanticRequest[] uniqueInputs =
            request.Requests
                .DistinctBy(
                    static value => value.Input.ReviewVideoPath,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        foreach (IGrouping<string, VisualSemanticRequest> group in
                 request.Requests.GroupBy(
                     static value => value.Input.ReviewVideoPath,
                     StringComparer.OrdinalIgnoreCase))
        {
            VisualSemanticInputManifest first = group.First().Input;

            if (group.Any(
                    value =>
                        value.Input.ReviewVideoByteLength !=
                            first.ReviewVideoByteLength ||
                        value.Input.ReviewVideoLastWriteTimeUtc !=
                            first.ReviewVideoLastWriteTimeUtc ||
                        !string.Equals(
                            value.Input.ReviewVideoSha256,
                            first.ReviewVideoSha256,
                            StringComparison.Ordinal)))
            {
                throw new Qwen3VlInferenceException(
                    $"The batch contains conflicting integrity snapshots for '{first.ReviewVideoPath}'.");
            }
        }

        foreach (VisualSemanticRequest item in uniqueInputs)
        {
            await item.Input.VerifyIntegrityAsync(
                cancellationToken);
        }
    }

    internal void VerifyRuntimeIntegrity(
        Qwen3VlInitialization initialization)
    {
        if (!string.Equals(
                ModelArtifactManifest.ComputeSha256(
                    _settings.PythonExecutablePath),
                initialization.PythonExecutableSha256,
                StringComparison.Ordinal) ||
            !string.Equals(
                ModelArtifactManifest.ComputeSha256(
                    _settings.HostScriptPath),
                initialization.HostScriptSha256,
                StringComparison.Ordinal))
        {
            throw new Qwen3VlInferenceException(
                "The configured Python executable or Qwen host script changed after initialization.");
        }
    }

    internal void ValidateRequestModel(
        VisualSemanticModelManifest model)
    {
        if (!string.Equals(
                model.ModelDirectoryPath,
                _settings.ModelDirectoryPath,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                model.RepositoryId,
                Qwen3VlRuntimeContract.RepositoryId,
                StringComparison.Ordinal) ||
            !string.Equals(
                model.LicenseIdentifier,
                Qwen3VlRuntimeContract.LicenseIdentifier,
                StringComparison.Ordinal))
        {
            throw new Qwen3VlInitializationException(
                "The request must use the explicit configured directory and official Apache-2.0 Qwen/Qwen3-VL-4B-Instruct identity.");
        }
    }

    internal void ValidateRawAuditSourceSeparation(
        VisualSemanticBatchRequest request)
    {
        if (_settings.RawAuditOutputPath is null)
        {
            return;
        }

        foreach (string sourceDirectory in
                 request.Requests
                     .Select(
                         static value =>
                             Path.GetDirectoryName(
                                 value.Input
                                     .ReviewVideoPath)!)
                     .Distinct(
                         StringComparer.OrdinalIgnoreCase))
        {
            string directory =
                sourceDirectory.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            string prefix =
                directory +
                Path.DirectorySeparatorChar;

            if (string.Equals(
                    _settings.RawAuditOutputPath,
                    directory,
                    StringComparison.OrdinalIgnoreCase) ||
                _settings.RawAuditOutputPath.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new Qwen3VlInferenceException(
                    "The optional Qwen raw-audit output must remain outside every review-source directory.");
            }
        }
    }

    internal void ValidateFailureOutputSourceSeparation(
        VisualSemanticBatchRequest request)
    {
        if (_settings.FailureOutputPath is null)
        {
            return;
        }

        foreach (string sourceDirectory in
                 request.Requests
                     .Select(
                         static value =>
                             Path.GetDirectoryName(
                                 value.Input
                                     .ReviewVideoPath)!)
                     .Distinct(
                         StringComparer.OrdinalIgnoreCase))
        {
            string directory =
                sourceDirectory.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            string prefix =
                directory +
                Path.DirectorySeparatorChar;

            if (string.Equals(
                    _settings.FailureOutputPath,
                    directory,
                    StringComparison.OrdinalIgnoreCase) ||
                _settings.FailureOutputPath.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new Qwen3VlInferenceException(
                    "The optional Qwen failure-envelope output must remain outside every review-source directory.");
            }
        }
    }

    internal void VerifyFfmpegSharedLibraries()
    {
        if (!Directory.Exists(
                _settings.FfmpegSharedLibraryDirectoryPath))
        {
            throw new Qwen3VlInitializationException(
                $"The configured shared FFmpeg directory does not exist: '{_settings.FfmpegSharedLibraryDirectoryPath}'.");
        }

        string[] fileNames =
            Directory
                .EnumerateFiles(
                    _settings.FfmpegSharedLibraryDirectoryPath,
                    "*.dll",
                    SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(static value => value is not null)
                .Cast<string>()
                .ToArray();
        string[] missing =
            Qwen3VlRuntimeContract.RequiredFfmpegLibraryPrefixes
                .Where(
                    prefix =>
                        !fileNames.Any(
                            value =>
                                value.StartsWith(
                                    prefix,
                                    StringComparison.OrdinalIgnoreCase) &&
                                value.EndsWith(
                                    ".dll",
                                    StringComparison.OrdinalIgnoreCase)))
                .ToArray();

        if (missing.Length > 0)
        {
            throw new Qwen3VlInitializationException(
                "The explicit shared FFmpeg directory is incomplete. " +
                $"Missing DLL families: {string.Join(", ", missing)}.");
        }
    }

}
