using System.Diagnostics;

namespace ReplayFoundry.RuntimePacks;

internal sealed class RuntimePackStoreMutationLease : IAsyncDisposable
{
    private static readonly TimeSpan AcquisitionTimeout =
        TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryDelay =
        TimeSpan.FromMilliseconds(100);

    private readonly FileStream _stream;

    private RuntimePackStoreMutationLease(FileStream stream)
    {
        _stream = stream;
    }

    public static async Task<RuntimePackStoreMutationLease> AcquireAsync(
        ReplayFoundryRuntimePackStorePaths paths,
        CancellationToken cancellationToken)
    {
        string lockPath = GetLockPath(paths);
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        long started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < AcquisitionTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new RuntimePackStoreMutationLease(new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose));
            }
            catch (IOException)
            {
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }

        throw new IOException(
            "Another Replay Foundry runtime-pack maintenance operation " +
            "is still running. Try again after it completes.");
    }

    internal static string GetLockPath(
        ReplayFoundryRuntimePackStorePaths paths)
    {
        string root = paths.RootDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        string? parent = Path.GetDirectoryName(root);
        if (string.IsNullOrWhiteSpace(parent))
        {
            throw new ArgumentException(
                "The runtime-pack store must have a parent directory.",
                nameof(paths));
        }

        return Path.Combine(
            parent,
            $".{Path.GetFileName(root)}.mutation.lock");
    }

    public ValueTask DisposeAsync() => _stream.DisposeAsync();
}
