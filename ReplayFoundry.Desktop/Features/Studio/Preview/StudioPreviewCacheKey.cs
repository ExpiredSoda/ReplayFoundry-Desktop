using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ReplayFoundry.Desktop.Features.Studio.Editing;

namespace ReplayFoundry.Desktop.Features.Studio.Preview;

internal sealed record StudioPreviewCacheKey(
    string Hash,
    string CanonicalInput)
{
    public const string PolicyVersion = "1.0";

    public static string CreateMediaIdentity(
        StudioPreviewMediaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var builder = new StringBuilder();
        Add(builder, "source", request.Asset.SourceFullPath.ToUpperInvariant());
        Add(builder, "context-start", request.SourceStart.Ticks);
        Add(builder, "context-end", request.SourceEnd.Ticks);
        Add(builder, "clip-start", request.Asset.SourceStart.Ticks);
        Add(builder, "clip-end", request.Asset.SourceEnd.Ticks);
        Add(builder, "effect", (int)request.Asset.Appearance.VideoEffect);
        Add(
            builder,
            "effect-intensity",
            request.Asset.Appearance.VideoEffectIntensityPercent);
        foreach (StudioGraphicOverlay overlay in
                 request.Asset.Appearance.GraphicOverlays)
        {
            Add(builder, "overlay-id", overlay.Id.ToUpperInvariant());
            Add(
                builder,
                "overlay-path",
                overlay.ImageFullPath.ToUpperInvariant());
            Add(builder, "overlay-x", overlay.CenterXPercent);
            Add(builder, "overlay-y", overlay.CenterYPercent);
            Add(builder, "overlay-width", overlay.WidthPercent);
        }
        return builder.ToString();
    }

    public static StudioPreviewCacheKey Create(
        StudioPreviewMediaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        FileInfo source = Snapshot(request.Asset.SourceFullPath);
        var builder = new StringBuilder();
        Add(builder, "policy", PolicyVersion);
        Add(builder, "source", source.FullName.ToUpperInvariant());
        Add(builder, "source-length", source.Length);
        Add(builder, "source-write-utc", source.LastWriteTimeUtc.Ticks);
        Add(builder, "context-start", request.SourceStart.Ticks);
        Add(builder, "context-end", request.SourceEnd.Ticks);
        Add(builder, "clip-start", request.Asset.SourceStart.Ticks);
        Add(builder, "clip-end", request.Asset.SourceEnd.Ticks);
        Add(builder, "effect", (int)request.Asset.Appearance.VideoEffect);
        Add(builder, "effect-intensity", request.Asset.Appearance.VideoEffectIntensityPercent);

        foreach (var overlay in request.Asset.Appearance.GraphicOverlays)
        {
            FileInfo file = Snapshot(overlay.ImageFullPath);
            Add(builder, "overlay-id", overlay.Id.ToUpperInvariant());
            Add(builder, "overlay-path", file.FullName.ToUpperInvariant());
            Add(builder, "overlay-length", file.Length);
            Add(builder, "overlay-write-utc", file.LastWriteTimeUtc.Ticks);
            Add(builder, "overlay-x", overlay.CenterXPercent);
            Add(builder, "overlay-y", overlay.CenterYPercent);
            Add(builder, "overlay-width", overlay.WidthPercent);
        }

        string canonical = builder.ToString();
        string hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new StudioPreviewCacheKey(hash, canonical);
    }

    private static FileInfo Snapshot(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException(
                "A Studio preview input no longer exists.",
                path);
        }
        info.Refresh();
        return info;
    }

    private static void Add(StringBuilder builder, string name, object value)
    {
        string text = value switch
        {
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
        builder.Append(name).Append(':')
            .Append(text.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':').Append(text).Append('\n');
    }
}
