using System.IO;
using System.Reflection;

namespace ReplayFoundry.Desktop.Platform.Storage;

internal enum ReplayFoundryDataChannel
{
    Production,
    Development,
    Test,
}

internal sealed record ReplayFoundryDataRoots(
    ReplayFoundryDataChannel Channel,
    string MutableRoot,
    string TemporaryRoot,
    string SharedRuntimeRoot,
    string SharedInstallerRoot);

internal static class ReplayFoundryLocalDataPaths
{
    private const string DataChannelMetadataName = "ReplayFoundry.DataChannel";
    private static readonly ReplayFoundryDataRoots CurrentRoots = CreateDefaultRoots();

    internal static ReplayFoundryDataRoots Current => CurrentRoots;

    public static string Resolve(string? overridePath, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        string path = overridePath ?? Path.Combine(
            CurrentRoots.MutableRoot,
            fileName);
        if (string.IsNullOrWhiteSpace(path) ||
            !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                "A local Replay Foundry data path must be fully qualified.",
                nameof(overridePath));
        }

        return Path.GetFullPath(path);
    }

    public static string ResolveTemporary(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return RequireDescendant(CurrentRoots.TemporaryRoot, relativePath);
    }

    internal static ReplayFoundryDataRoots CreateRoots(
        ReplayFoundryDataChannel channel,
        string localApplicationData,
        string temporaryRoot,
        string testIdentity)
    {
        string local = RequireFocusedRoot(
            localApplicationData,
            nameof(localApplicationData));
        string temporary = RequireFocusedRoot(
            temporaryRoot,
            nameof(temporaryRoot));
        string sharedRoot = Path.Combine(local, "ReplayFoundry");
        return channel switch
        {
            ReplayFoundryDataChannel.Production => new(
                channel,
                sharedRoot,
                Path.Combine(temporary, "ReplayFoundry"),
                Path.Combine(sharedRoot, "R"),
                Path.Combine(sharedRoot, "Installers")),
            ReplayFoundryDataChannel.Development => new(
                channel,
                Path.Combine(local, "ReplayFoundry-Development"),
                Path.Combine(temporary, "ReplayFoundry-Development"),
                Path.Combine(sharedRoot, "R"),
                Path.Combine(sharedRoot, "Installers")),
            ReplayFoundryDataChannel.Test => CreateTestRoots(
                local,
                temporary,
                testIdentity),
            _ => throw new ArgumentOutOfRangeException(nameof(channel)),
        };
    }

    private static ReplayFoundryDataRoots CreateDefaultRoots()
    {
        ReplayFoundryDataChannel channel = ResolveChannel(
            Assembly.GetEntryAssembly() ?? typeof(ReplayFoundryLocalDataPaths).Assembly);
        string identity = $"{Environment.ProcessId}-{Environment.ProcessPath?.GetHashCode(StringComparison.Ordinal) ?? 0:X8}";
        return CreateRoots(
            channel,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.GetTempPath(),
            identity);
    }

    private static ReplayFoundryDataChannel ResolveChannel(Assembly assembly)
    {
        string[] values = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(static value => value.Key == DataChannelMetadataName)
            .Select(static value => value.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (values.Length != 1 ||
            !Enum.TryParse(values[0], ignoreCase: false, out ReplayFoundryDataChannel channel) ||
            !Enum.IsDefined(channel))
        {
            throw new InvalidOperationException(
                "Replay Foundry requires one valid build data-channel identity.");
        }
        return channel;
    }

    private static ReplayFoundryDataRoots CreateTestRoots(
        string localApplicationData,
        string temporaryRoot,
        string testIdentity)
    {
        string identity = RequireSingleSegment(testIdentity, nameof(testIdentity));
        string root = Path.Combine(temporaryRoot, "ReplayFoundry.Tests", identity);
        return new(
            ReplayFoundryDataChannel.Test,
            Path.Combine(root, "Data"),
            Path.Combine(root, "Temp"),
            Path.Combine(localApplicationData, "ReplayFoundry", "R"),
            Path.Combine(localApplicationData, "ReplayFoundry", "Installers"));
    }

    private static string RequireFocusedRoot(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        if (!Path.IsPathFullyQualified(full))
        {
            throw new ArgumentException(
                "A Replay Foundry data base must be fully qualified.",
                parameterName);
        }
        return full;
    }

    private static string RequireDescendant(string root, string relativePath)
    {
        if (Path.IsPathFullyQualified(relativePath))
        {
            throw new ArgumentException(
                "A Replay Foundry data child must be relative.",
                nameof(relativePath));
        }
        string full = Path.GetFullPath(Path.Combine(root, relativePath));
        string prefix = Path.TrimEndingDirectorySeparator(root) +
            Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "A Replay Foundry data child escaped its owned root.",
                nameof(relativePath));
        }
        return full;
    }

    private static string RequireSingleSegment(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (Path.GetFileName(value) != value || value is "." or "..")
        {
            throw new ArgumentException(
                "A Replay Foundry test identity must be one path segment.",
                parameterName);
        }
        return value;
    }
}
