using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;

namespace ReplayFoundry.Desktop.Media.Intelligence;

public enum InferenceWarningCode
{
    ProviderCapabilityUnavailable,
    ProviderVersionUnavailable,
    ExecutionBackendUnavailable,
    ProviderReportedWarning,
    CleanupFailure,
}

public sealed record InferenceWarning
{
    public InferenceWarning(
        InferenceWarningCode code,
        string message)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "An inference warning requires a message.",
                nameof(message));
        }

        Code = code;
        Message = message.Trim();
    }

    public InferenceWarningCode Code { get; }

    public string Message { get; }
}

public sealed record InferenceProviderIdentity
{
    public InferenceProviderIdentity(
        string providerName,
        string providerSemanticVersion,
        string adapterVersion)
    {
        ProviderName = Required(providerName, nameof(providerName));
        ProviderSemanticVersion =
            Required(
                providerSemanticVersion,
                nameof(providerSemanticVersion));
        AdapterVersion =
            Required(adapterVersion, nameof(adapterVersion));
    }

    public string ProviderName { get; }

    public string ProviderSemanticVersion { get; }

    public string AdapterVersion { get; }

    private static string Required(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Inference identity values cannot be blank.",
                parameterName);
        }

        return value.Trim();
    }
}

public sealed class ModelArtifactManifest
{
    public ModelArtifactManifest(
        string displayName,
        string path,
        string sha256,
        long byteLength,
        DateTimeOffset lastWriteTimeUtc,
        string modelFormat,
        string? licenseIdentifier = null,
        string? sourceUrlOrNote = null,
        string? languageCapabilityDescription = null)
    {
        DisplayName = Required(displayName, nameof(displayName));
        Path = FullPath(path, nameof(path));
        Sha256 = Sha256Value(sha256, nameof(sha256));

        if (byteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        }

        RequireUtc(lastWriteTimeUtc, nameof(lastWriteTimeUtc));

        ModelFormat = Required(modelFormat, nameof(modelFormat));
        ByteLength = byteLength;
        LastWriteTimeUtc = lastWriteTimeUtc;
        LicenseIdentifier = Optional(licenseIdentifier);
        SourceUrlOrNote = Optional(sourceUrlOrNote);
        LanguageCapabilityDescription =
            Optional(languageCapabilityDescription);
    }

    public string DisplayName { get; }

    public string Path { get; }

    public string Sha256 { get; }

    public long ByteLength { get; }

    public DateTimeOffset LastWriteTimeUtc { get; }

    public string? LicenseIdentifier { get; }

    public string? SourceUrlOrNote { get; }

    public string ModelFormat { get; }

    public string? LanguageCapabilityDescription { get; }

    internal static string ComputeSha256(string path)
    {
        using FileStream stream =
            new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        return Convert.ToHexString(
            SHA256.HashData(stream));
    }

    internal static string Sha256Value(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length != 64 ||
            value.Any(
                static character =>
                    !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "A SHA-256 value must contain exactly 64 hexadecimal characters.",
                parameterName);
        }

        return value.ToUpperInvariant();
    }

    internal static void RequireUtc(
        DateTimeOffset value,
        string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Inference timestamps must use a zero UTC offset.",
                parameterName);
        }
    }

    private static string Required(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Model manifest values cannot be blank.",
                parameterName);
        }

        return value.Trim();
    }

    private static string FullPath(
        string value,
        string parameterName)
    {
        string result = Required(value, parameterName);

        if (!System.IO.Path.IsPathFullyQualified(result))
        {
            throw new ArgumentException(
                "Model paths must be fully qualified.",
                parameterName);
        }

        return System.IO.Path.GetFullPath(result);
    }

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}

public sealed class InferenceExecutionManifest
{
    private readonly ReadOnlyDictionary<string, string>
        _normalizedOptions;

    private readonly ReadOnlyCollection<InferenceWarning>
        _warnings;

    public InferenceExecutionManifest(
        InferenceProviderIdentity provider,
        string executablePath,
        string executableSha256,
        string executableVersionOutput,
        ModelArtifactManifest model,
        IReadOnlyDictionary<string, string> normalizedOptions,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        TimeSpan elapsed,
        bool wasCancelled,
        string? executionBackend = null,
        IEnumerable<InferenceWarning>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(normalizedOptions);

        if (string.IsNullOrWhiteSpace(executablePath) ||
            !System.IO.Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException(
                "Inference executable paths must be fully qualified.",
                nameof(executablePath));
        }

        if (string.IsNullOrWhiteSpace(executableVersionOutput))
        {
            throw new ArgumentException(
                "The provider must preserve its executable version output.",
                nameof(executableVersionOutput));
        }

        ModelArtifactManifest.RequireUtc(
            startedAtUtc,
            nameof(startedAtUtc));
        ModelArtifactManifest.RequireUtc(
            completedAtUtc,
            nameof(completedAtUtc));

        if (completedAtUtc < startedAtUtc ||
            elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedAtUtc),
                "Inference completion and elapsed values cannot move backwards.");
        }

        var optionSnapshot =
            normalizedOptions
                .OrderBy(
                    static pair =>
                        pair.Key,
                    StringComparer.Ordinal)
                .ToDictionary(
                    static pair =>
                        string.IsNullOrWhiteSpace(pair.Key)
                            ? throw new ArgumentException(
                                "Inference option names cannot be blank.",
                                nameof(normalizedOptions))
                            : pair.Key.Trim(),
                    static pair =>
                        pair.Value ??
                        throw new ArgumentException(
                            "Inference option values cannot be null.",
                            nameof(normalizedOptions)),
                    StringComparer.Ordinal);

        InferenceWarning[] warningSnapshot =
            warnings?.ToArray() ??
            [];

        if (warningSnapshot.Any(static warning => warning is null))
        {
            throw new ArgumentException(
                "Inference warnings cannot contain null entries.",
                nameof(warnings));
        }

        Provider = provider;
        ExecutablePath =
            System.IO.Path.GetFullPath(executablePath);
        ExecutableSha256 =
            ModelArtifactManifest.Sha256Value(
                executableSha256,
                nameof(executableSha256));
        ExecutableVersionOutput =
            executableVersionOutput.Trim();
        Model = model;
        _normalizedOptions =
            new ReadOnlyDictionary<string, string>(
                optionSnapshot);
        StartedAtUtc = startedAtUtc;
        CompletedAtUtc = completedAtUtc;
        Elapsed = elapsed;
        WasCancelled = wasCancelled;
        ExecutionBackend =
            string.IsNullOrWhiteSpace(executionBackend)
                ? null
                : executionBackend.Trim();
        _warnings =
            Array.AsReadOnly(warningSnapshot);
    }

    public InferenceProviderIdentity Provider { get; }

    public string ExecutablePath { get; }

    public string ExecutableSha256 { get; }

    public string ExecutableVersionOutput { get; }

    public ModelArtifactManifest Model { get; }

    public IReadOnlyDictionary<string, string> NormalizedOptions =>
        _normalizedOptions;

    public string? ExecutionBackend { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset CompletedAtUtc { get; }

    public TimeSpan Elapsed { get; }

    public bool WasCancelled { get; }

    public IReadOnlyList<InferenceWarning> Warnings =>
        _warnings;
}
