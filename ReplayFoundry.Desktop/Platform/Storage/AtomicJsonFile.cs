using System.IO;
using System.Text.Json;

namespace ReplayFoundry.Desktop.Platform.Storage;

internal static class AtomicJsonFile
{
    public static void Write<T>(
        string fullPath,
        T value,
        JsonSerializerOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentNullException.ThrowIfNull(options);

        string resolvedPath = Path.GetFullPath(fullPath);
        string directory = Path.GetDirectoryName(resolvedPath) ??
            throw new ArgumentException(
                "A JSON file path must include a parent directory.",
                nameof(fullPath));
        Directory.CreateDirectory(directory);

        string stagingPath = Path.Combine(
            directory,
            Path.GetFileName(resolvedPath) + "." +
            Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            File.WriteAllText(
                stagingPath,
                JsonSerializer.Serialize(value, options));
            File.Move(stagingPath, resolvedPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(stagingPath))
            {
                File.Delete(stagingPath);
            }
        }
    }
}
