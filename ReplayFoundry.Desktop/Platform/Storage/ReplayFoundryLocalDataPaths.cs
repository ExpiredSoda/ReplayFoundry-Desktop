using System.IO;

namespace ReplayFoundry.Desktop.Platform.Storage;

internal static class ReplayFoundryLocalDataPaths
{
    public static string Resolve(string? overridePath, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        string path = overridePath ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "ReplayFoundry",
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
}
