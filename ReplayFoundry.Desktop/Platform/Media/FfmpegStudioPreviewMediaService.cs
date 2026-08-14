using System.IO;
using System.Collections.Concurrent;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed class FfmpegStudioPreviewMediaService :
    IStudioPreviewMediaService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim>
        KeyLocks = new(StringComparer.Ordinal);
    private readonly IProcessRunner _processRunner;
    private readonly FfmpegToolLocator _toolLocator;
    private readonly string _cacheRoot;

    public FfmpegStudioPreviewMediaService(
        IProcessRunner processRunner,
        FfmpegToolLocator toolLocator,
        string? cacheRoot = null)
    {
        _processRunner = processRunner ??
            throw new ArgumentNullException(nameof(processRunner));
        _toolLocator = toolLocator ??
            throw new ArgumentNullException(nameof(toolLocator));
        _cacheRoot = Path.GetFullPath(cacheRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ReplayFoundry",
            "Cache",
            "StudioPreview"));
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
        if (IsComplete(finalOutput, finalIdentity, cacheKey.CanonicalInput))
        {
            Touch(finalOutput);
            return RetainedLease(finalOutput, request);
        }

        SemaphoreSlim gate = KeyLocks.GetOrAdd(
            cacheKey.Hash,
            static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (IsComplete(finalOutput, finalIdentity, cacheKey.CanonicalInput))
            {
                Touch(finalOutput);
                return RetainedLease(finalOutput, request);
            }
            return await MaterializeAndCommitAsync(
                request,
                cacheKey,
                finalRoot,
                finalOutput,
                cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<StudioPreviewMediaLease> MaterializeAndCommitAsync(
        StudioPreviewMediaRequest request,
        StudioPreviewCacheKey cacheKey,
        string finalRoot,
        string finalOutput,
        CancellationToken cancellationToken)
    {
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
            if (Directory.Exists(finalRoot))
            {
                Cleanup(finalRoot);
            }
            Directory.Move(root, finalRoot);
            PruneCache(finalRoot);
            return RetainedLease(finalOutput, request);
        }
        catch
        {
            Cleanup(root);
            throw;
        }
    }

    private static StudioPreviewMediaLease RetainedLease(
        string output,
        StudioPreviewMediaRequest request) =>
        new(output, request.SourceStart, request.Duration, static () => { });

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

    private void PruneCache(string retainedRoot)
    {
        const long maximumBytes = 2L * 1024 * 1024 * 1024;
        try
        {
            DirectoryInfo[] entries = new DirectoryInfo(_cacheRoot)
                .EnumerateDirectories()
                .Where(static value => !value.Name.Equals(".staging", StringComparison.Ordinal))
                .OrderByDescending(static value => value.LastAccessTimeUtc)
                .ToArray();
            long total = entries.Sum(static entry => entry
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(static file => file.Length));
            foreach (DirectoryInfo entry in entries.Reverse())
            {
                if (total <= maximumBytes ||
                    entry.FullName.Equals(retainedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                long length = entry
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(static file => file.Length);
                entry.Delete(recursive: true);
                total -= length;
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
