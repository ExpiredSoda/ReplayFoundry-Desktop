using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public sealed class VisualSemanticExecutionManifest
{
    private readonly ReadOnlyCollection<VisualSemanticWarning> _warnings;

    public VisualSemanticExecutionManifest(
        InferenceProviderIdentity provider,
        string pythonExecutablePath,
        string pythonExecutableSha256,
        string hostScriptPath,
        string hostScriptSha256,
        string modelManifestSha256,
        string promptSha256,
        string probeOutput,
        string device,
        string backend,
        long? peakAllocatedGpuBytes,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        TimeSpan elapsed,
        VisualSemanticExecutionTimingManifest executionTiming,
        IEnumerable<VisualSemanticWarning>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(executionTiming);
        RequireFilePath(pythonExecutablePath, nameof(pythonExecutablePath));
        RequireFilePath(hostScriptPath, nameof(hostScriptPath));
        ModelArtifactManifest.RequireUtc(startedAtUtc, nameof(startedAtUtc));
        ModelArtifactManifest.RequireUtc(completedAtUtc, nameof(completedAtUtc));

        if (completedAtUtc < startedAtUtc ||
            elapsed < TimeSpan.Zero ||
            peakAllocatedGpuBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completedAtUtc));
        }

        VisualSemanticWarning[] warningSnapshot =
            warnings?.ToArray() ??
            [];

        if (warningSnapshot.Any(static value => value is null))
        {
            throw new ArgumentException(
                "Execution warnings cannot contain null entries.",
                nameof(warnings));
        }

        Provider = provider;
        PythonExecutablePath = Path.GetFullPath(pythonExecutablePath);
        PythonExecutableSha256 = ModelArtifactManifest.Sha256Value(
            pythonExecutableSha256,
            nameof(pythonExecutableSha256));
        HostScriptPath = Path.GetFullPath(hostScriptPath);
        HostScriptSha256 = ModelArtifactManifest.Sha256Value(
            hostScriptSha256,
            nameof(hostScriptSha256));
        ModelManifestSha256 = ModelArtifactManifest.Sha256Value(
            modelManifestSha256,
            nameof(modelManifestSha256));
        PromptSha256 = ModelArtifactManifest.Sha256Value(
            promptSha256,
            nameof(promptSha256));
        ProbeOutput = VisualSemanticContractText.Required(
            probeOutput,
            nameof(probeOutput),
            64 * 1024);
        Device = VisualSemanticContractText.Required(
            device,
            nameof(device),
            256);
        Backend = VisualSemanticContractText.Required(
            backend,
            nameof(backend),
            128);
        PeakAllocatedGpuBytes = peakAllocatedGpuBytes;
        StartedAtUtc = startedAtUtc;
        CompletedAtUtc = completedAtUtc;
        Elapsed = elapsed;
        ExecutionTiming = executionTiming;
        _warnings = Array.AsReadOnly(warningSnapshot);
    }

    public InferenceProviderIdentity Provider { get; }

    public string PythonExecutablePath { get; }

    public string PythonExecutableSha256 { get; }

    public string HostScriptPath { get; }

    public string HostScriptSha256 { get; }

    public string ModelManifestSha256 { get; }

    public string PromptSha256 { get; }

    public string ProbeOutput { get; }

    public string Device { get; }

    public string Backend { get; }

    public long? PeakAllocatedGpuBytes { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset CompletedAtUtc { get; }

    public TimeSpan Elapsed { get; }

    public VisualSemanticExecutionTimingManifest ExecutionTiming { get; }

    public IReadOnlyList<VisualSemanticWarning> Warnings => _warnings;

    private static void RequireFilePath(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Path.IsPathFullyQualified(value))
        {
            throw new ArgumentException(
                "Execution paths must be fully qualified.",
                parameterName);
        }
    }
}
