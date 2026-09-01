using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Platform.Processes;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed class FfmpegStudioPreviewMediaService :
    IStudioPreviewMediaService
{
    private readonly object _keyLockSync = new();
    private readonly object _activeRootSync = new();
    private readonly Dictionary<string, KeyLockEntry> _keyLocks =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _activeRoots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _pruneGate = new(1, 1);
    private readonly IProcessRunner _processRunner;
    private readonly IFfmpegToolLocator _toolLocator;
    private readonly string _cacheRoot;
    private readonly long _maximumCacheBytes;

    public FfmpegStudioPreviewMediaService(
        IProcessRunner processRunner,
        IFfmpegToolLocator toolLocator,
        string? cacheRoot = null,
        long maximumCacheBytes = 2L * 1024 * 1024 * 1024)
    {
        _processRunner = processRunner ??
            throw new ArgumentNullException(nameof(processRunner));
        _toolLocator = toolLocator ??
            throw new ArgumentNullException(nameof(toolLocator));
        _cacheRoot = ReplayFoundryLocalDataPaths.Resolve(
            cacheRoot,
            Path.Combine("Cache", "StudioPreview"));
        _maximumCacheBytes = maximumCacheBytes > 0
            ? maximumCacheBytes
            : throw new ArgumentOutOfRangeException(
                nameof(maximumCacheBytes),
                maximumCacheBytes,
                "The Studio preview cache limit must be positive.");
    }

    public async Task<StudioPreviewMediaLease> MaterializeAsync(
        StudioPreviewMediaRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        StudioPreviewCacheKey cacheKey = StudioPreviewCacheKey.Create(request);
        string finalRoot = Path.Combine(_cacheRoot, cacheKey.Hash);
        string finalOutput = Path.Combine(finalRoot, "preview.mp4");
        string finalIdentity = Path.Combine(finalRoot, "identity.txt");
        if (TryRetainComplete(
                finalRoot,
                finalOutput,
                finalIdentity,
                cacheKey.CanonicalInput,
                request,
                out StudioPreviewMediaLease? cached))
        {
            Touch(finalOutput);
            return cached!;
        }

        using (KeyLockLease gate = await AcquireKeyLockAsync(
                   cacheKey.Hash,
                   cancellationToken))
        {
            if (TryRetainComplete(
                    finalRoot,
                    finalOutput,
                    finalIdentity,
                    cacheKey.CanonicalInput,
                    request,
                    out cached))
            {
                Touch(finalOutput);
                return cached!;
            }
            return await MaterializeAndCommitAsync(
                request,
                cacheKey,
                finalRoot,
                finalOutput,
                cancellationToken);
        }
    }

    private async Task<StudioPreviewMediaLease> MaterializeAndCommitAsync(
        StudioPreviewMediaRequest request,
        StudioPreviewCacheKey cacheKey,
        string finalRoot,
        string finalOutput,
        CancellationToken cancellationToken)
    {
        ThrowIfRootActive(finalRoot);
        FileInfo sourceBefore = Snapshot(request.Asset.SourceFullPath);
        string root = Path.Combine(
            _cacheRoot,
            ".staging",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string output = Path.Combine(root, "preview.mp4");
        try
        {
            GenerationClipOutputProfile full =
                GenerationClipOutputProfile.FromReference(
                    request.Asset.SourceMedia.PrimaryVideoStream);
            GenerationClipOutputProfile preview = FitPreview(full);
            FfmpegClipRenderCommand command =
                FfmpegClipRenderCommandBuilder.BuildSegment(
                    request.Asset.SourceMedia,
                    request.SourceStart,
                    request.SourceEnd,
                    preview,
                    output,
                    subtitleFileName: null,
                    root,
                    request.Asset.Appearance.VideoEffect,
                    request.Asset.Appearance.VideoEffectIntensityPercent,
                    request.Asset.Appearance.GraphicOverlays);
            ProcessRunResult process = await _processRunner.RunAsync(
                new ProcessRunRequest(
                    _toolLocator.LocateFfmpeg(),
                    command.Arguments,
                    command.Timeout,
                    command.WorkingDirectory,
                    64 * 1024,
                    2 * 1024 * 1024),
                cancellationToken);
            if (!process.Succeeded ||
                !File.Exists(output) ||
                new FileInfo(output).Length <= 0)
            {
                throw new InvalidOperationException(
                    "Replay Foundry could not prepare the bounded Studio preview. " +
                    $"FFmpeg exit code: {process.ExitCode}. {process.StandardError}");
            }

            FileInfo sourceAfter = Snapshot(request.Asset.SourceFullPath);
            if (sourceBefore.Length != sourceAfter.Length ||
                sourceBefore.LastWriteTimeUtc != sourceAfter.LastWriteTimeUtc)
            {
                throw new IOException(
                    "The source changed while Studio was preparing its preview.");
            }
            await File.WriteAllTextAsync(
                Path.Combine(root, "identity.txt"),
                cacheKey.CanonicalInput,
                System.Text.Encoding.UTF8,
                cancellationToken);
            Directory.CreateDirectory(_cacheRoot);
            Directory.CreateDirectory(QuarantineRoot);
            StudioPreviewMediaLease lease;
            string? replacedRoot = null;
            lock (_activeRootSync)
            {
                ThrowIfRootActiveUnsafe(finalRoot);
                if (Directory.Exists(finalRoot))
                {
                    replacedRoot = MoveToQuarantine(finalRoot);
                }
                Directory.Move(root, finalRoot);
                lease = RetainRootUnsafe(finalRoot, finalOutput, request);
            }
            TryCleanup(replacedRoot);
            _ = PruneCacheAsync();
            return lease;
        }
        catch
        {
            Cleanup(root);
            throw;
        }
    }

    private bool TryRetainComplete(
        string root,
        string output,
        string identityPath,
        string expectedIdentity,
        StudioPreviewMediaRequest request,
        out StudioPreviewMediaLease? lease)
    {
        lock (_activeRootSync)
        {
            if (!IsComplete(output, identityPath, expectedIdentity))
            {
                lease = null;
                return false;
            }

            lease = RetainRootUnsafe(root, output, request);
            return true;
        }
    }

    private StudioPreviewMediaLease RetainRootUnsafe(
        string root,
        string output,
        StudioPreviewMediaRequest request)
    {
        _activeRoots.TryGetValue(root, out int referenceCount);
        _activeRoots[root] = checked(referenceCount + 1);
        try
        {
            return new StudioPreviewMediaLease(
                output,
                request.SourceStart,
                request.Duration,
                () => ReleaseRoot(root));
        }
        catch
        {
            ReleaseRootUnsafe(root);
            throw;
        }
    }

    private void ReleaseRoot(string root)
    {
        lock (_activeRootSync)
        {
            ReleaseRootUnsafe(root);
        }
        _ = PruneCacheAsync();
    }

    private void ReleaseRootUnsafe(string root)
    {
        if (!_activeRoots.TryGetValue(root, out int referenceCount))
        {
            return;
        }

        if (referenceCount == 1)
        {
            _activeRoots.Remove(root);
        }
        else
        {
            _activeRoots[root] = referenceCount - 1;
        }
    }

    private void ThrowIfRootActive(string root)
    {
        lock (_activeRootSync)
        {
            ThrowIfRootActiveUnsafe(root);
        }
    }

    private void ThrowIfRootActiveUnsafe(string root)
    {
        if (_activeRoots.ContainsKey(root))
        {
            throw new InvalidOperationException(
                "The active Studio preview must finish playback before its cache entry can be rebuilt.");
        }
    }

    private static bool IsComplete(
        string output,
        string identityPath,
        string expectedIdentity)
    {
        try
        {
            return File.Exists(output) &&
                   new FileInfo(output).Length > 0 &&
                   File.Exists(identityPath) &&
                   string.Equals(
                       File.ReadAllText(identityPath),
                       expectedIdentity,
                       StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Touch(string path)
    {
        try
        {
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
        }
        catch (IOException)
        {
            // Cache recency is best effort and never changes media output.
        }
        catch (UnauthorizedAccessException)
        {
            // A read-only cache remains usable without a recency update.
        }
    }

    private async Task PruneCacheAsync()
    {
        await _pruneGate.WaitAsync().ConfigureAwait(false);

        try
        {
            await Task.Run(PruneCache)
                .ConfigureAwait(false);
        }
        finally
        {
            _pruneGate.Release();
        }
    }

    private void PruneCache()
    {
        try
        {
            Directory.CreateDirectory(QuarantineRoot);
            CleanupAbandonedQuarantine();
            CacheEntry[] entries = new DirectoryInfo(_cacheRoot)
                .EnumerateDirectories()
                .Where(static value =>
                    !value.Name.Equals(".staging", StringComparison.Ordinal) &&
                    !value.Name.Equals(".pruning", StringComparison.Ordinal))
                .Select(static value => new CacheEntry(
                    value,
                    value.EnumerateFiles("*", SearchOption.AllDirectories)
                        .Sum(static file => file.Length),
                    PreviewLastAccessTimeUtc(value)))
                .OrderByDescending(static value => value.LastAccessTimeUtc)
                .ToArray();
            long total = entries.Sum(static entry => entry.Length);
            foreach (CacheEntry entry in entries.Reverse())
            {
                if (total <= _maximumCacheBytes)
                {
                    break;
                }

                string? quarantinedRoot;
                lock (_activeRootSync)
                {
                    if (_activeRoots.ContainsKey(entry.Directory.FullName))
                    {
                        continue;
                    }
                    if (!Directory.Exists(entry.Directory.FullName))
                    {
                        total -= entry.Length;
                        continue;
                    }
                    quarantinedRoot = MoveToQuarantine(
                        entry.Directory.FullName);
                }
                total -= entry.Length;
                TryCleanup(quarantinedRoot);
            }
        }
        catch (IOException)
        {
            // Cache pruning is opportunistic; local cleanup can retry later.
        }
        catch (UnauthorizedAccessException)
        {
            // Do not fail preview playback when another process owns a cache file.
        }
    }

    private string QuarantineRoot => Path.Combine(_cacheRoot, ".pruning");

    private string MoveToQuarantine(string root)
    {
        string destination = Path.Combine(
            QuarantineRoot,
            $"{Path.GetFileName(root)}-{Guid.NewGuid():N}");
        Directory.Move(root, destination);
        return destination;
    }

    private void CleanupAbandonedQuarantine()
    {
        foreach (string root in Directory.EnumerateDirectories(QuarantineRoot))
        {
            TryCleanup(root);
        }
    }

    private static void TryCleanup(string? root)
    {
        try
        {
            if (root is not null)
            {
                Cleanup(root);
            }
        }
        catch (IOException)
        {
            // A later prune retries quarantined cache entries.
        }
        catch (UnauthorizedAccessException)
        {
            // A read-only quarantine remains isolated from active playback.
        }
    }

    private static DateTime PreviewLastAccessTimeUtc(DirectoryInfo directory)
    {
        string preview = Path.Combine(directory.FullName, "preview.mp4");
        return File.Exists(preview)
            ? File.GetLastAccessTimeUtc(preview)
            : directory.LastAccessTimeUtc;
    }

    private async ValueTask<KeyLockLease> AcquireKeyLockAsync(
        string key,
        CancellationToken cancellationToken)
    {
        KeyLockEntry entry;
        lock (_keyLockSync)
        {
            if (!_keyLocks.TryGetValue(key, out entry!))
            {
                entry = new KeyLockEntry();
                _keyLocks.Add(key, entry);
            }
            entry.ReferenceCount++;
        }

        try
        {
            await entry.Gate.WaitAsync(cancellationToken);
            return new KeyLockLease(this, key, entry);
        }
        catch
        {
            ReleaseKeyLockReference(key, entry);
            throw;
        }
    }

    private void ReleaseKeyLock(
        string key,
        KeyLockEntry entry)
    {
        entry.Gate.Release();
        ReleaseKeyLockReference(key, entry);
    }

    private void ReleaseKeyLockReference(
        string key,
        KeyLockEntry entry)
    {
        bool dispose;
        lock (_keyLockSync)
        {
            entry.ReferenceCount--;
            dispose = entry.ReferenceCount == 0;
            if (dispose)
            {
                _keyLocks.Remove(key);
            }
        }
        if (dispose)
        {
            entry.Gate.Dispose();
        }
    }

    private sealed class KeyLockEntry
    {
        internal SemaphoreSlim Gate { get; } = new(1, 1);
        internal int ReferenceCount { get; set; }
    }

    private sealed class KeyLockLease : IDisposable
    {
        private FfmpegStudioPreviewMediaService? _owner;
        private readonly string _key;
        private readonly KeyLockEntry _entry;

        internal KeyLockLease(
            FfmpegStudioPreviewMediaService owner,
            string key,
            KeyLockEntry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose() =>
            Interlocked.Exchange(ref _owner, null)?
                .ReleaseKeyLock(_key, _entry);
    }

    private sealed record CacheEntry(
        DirectoryInfo Directory,
        long Length,
        DateTime LastAccessTimeUtc);

    private static GenerationClipOutputProfile FitPreview(
        GenerationClipOutputProfile full)
    {
        const int maximumLongEdge = 720;
        double scale = Math.Min(
            1,
            maximumLongEdge /
            (double)Math.Max(full.Width, full.Height));
        int width = PositiveEven(full.Width * scale);
        int height = PositiveEven(full.Height * scale);
        return new GenerationClipOutputProfile(
            width,
            height,
            Math.Min(30, full.FramesPerSecond));
    }

    private static int PositiveEven(double value)
    {
        int rounded = Math.Max(2, checked((int)Math.Round(value)));
        return (rounded + 1) & ~1;
    }

    private static FileInfo Snapshot(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException(
                "The Studio preview source no longer exists.",
                path);
        }
        info.Refresh();
        return info;
    }

    private static void Cleanup(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

public static class StudioPreviewMediaFactory
{
    public static IStudioPreviewMediaService CreateDefault() =>
        new FfmpegStudioPreviewMediaService(
            new WindowsProcessRunner(),
            new FfmpegToolLocator());
}
