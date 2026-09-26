using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace ReplayFoundry.Desktop.Presentation.Converters;

public sealed class FileImageSourceConverter : IValueConverter
{
    private const int MaximumCachedEntries = 256;
    private static readonly object CacheLock = new();
    private static readonly Dictionary<ThumbnailCacheKey, WeakReference<BitmapSource>> Cache = [];

    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        try
        {
            if (value is not string path ||
                string.IsNullOrWhiteSpace(path) ||
                !Path.IsPathFullyQualified(path) ||
                !File.Exists(path))
            {
                return null;
            }

            int decodeWidth = ParseDecodeWidth(parameter);
            var key = new ThumbnailCacheKey(
                path,
                File.GetLastWriteTimeUtc(path).Ticks,
                decodeWidth);
            if (TryGetCached(key, out BitmapSource? cached))
            {
                return cached;
            }

            using FileStream stream = File.Open(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (decodeWidth > 0)
            {
                image.DecodePixelWidth = decodeWidth;
            }
            image.StreamSource = stream;
            image.EndInit();
            if (image.CanFreeze)
            {
                image.Freeze();
            }

            CacheImage(key, image);
            return image;
        }
        catch (IOException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException(
            "File image conversion is one-way.");

    internal static void Invalidate(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return;
        }

        string normalized;
        try
        {
            normalized = Path.GetFullPath(fullPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return;
        }

        lock (CacheLock)
        {
            foreach (ThumbnailCacheKey key in Cache.Keys
                         .Where(key => key.Path.Equals(
                             normalized,
                             StringComparison.OrdinalIgnoreCase))
                         .ToArray())
            {
                Cache.Remove(key);
            }
        }
    }

    private static int ParseDecodeWidth(object? parameter) =>
        int.TryParse(parameter?.ToString(), out int width)
            ? Math.Clamp(width, 32, 1920)
            : 0;

    private static bool TryGetCached(
        ThumbnailCacheKey key,
        out BitmapSource? image)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(key, out WeakReference<BitmapSource>? reference) &&
                reference.TryGetTarget(out image))
            {
                return true;
            }

            Cache.Remove(key);
            image = null;
            return false;
        }
    }

    private static void CacheImage(ThumbnailCacheKey key, BitmapSource image)
    {
        lock (CacheLock)
        {
            Cache[key] = new WeakReference<BitmapSource>(image);
            if (Cache.Count <= MaximumCachedEntries)
            {
                return;
            }

            foreach (ThumbnailCacheKey expired in Cache
                         .Where(pair => !pair.Value.TryGetTarget(out _))
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                Cache.Remove(expired);
            }

            while (Cache.Count > MaximumCachedEntries)
            {
                Cache.Remove(Cache.Keys.First());
            }
        }
    }

    private readonly record struct ThumbnailCacheKey(
        string Path,
        long LastWriteTicks,
        int DecodeWidth);
}
