using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.Rendering;

namespace ReplayFoundry.Desktop.Platform.Media;

/// <summary>Portable OTIO cut lists; app-specific styling is retained as metadata, not claimed as NLE effects.</summary>
public static class StudioTimelineHandoffWriter
{
    public static async Task WriteAsync(IReadOnlyList<GenerationOutputAsset> assets, string directory,
        CancellationToken cancellationToken)
    {
        GenerationOutputAsset[] ordered = assets.OrderBy(asset => asset.Rank).ToArray();
        if (ordered.Length == 0) return;
        string root = Path.GetFullPath(directory);
        if (ordered.Any(asset => asset.OutputFullPath is not { } path ||
            !string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), root, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Timeline outputs must belong to the publishing package.", nameof(assets));
        var sourceVideo = new List<object>();
        var sourceAudio = new List<object>();
        foreach (GenerationOutputAsset asset in ordered)
        {
            double rate = asset.SourceMedia.PrimaryVideoStream.PreferredFrameRate is { } fps && double.IsFinite(fps) && fps > 0 ? fps : 30;
            var metadata = new Dictionary<string, object?>
            {
                ["replayfoundry"] = new { asset.Id, asset.Rank, asset.RenderSettings, asset.Appearance,
                    title = asset.EditorialMetadata?.Title, description = asset.EditorialMetadata?.Description, tags = asset.EditorialMetadata?.Tags,
                    sourceAudioStreamIndices = asset.SourceMedia.AudioStreams.Select(stream => stream.Index).ToArray(),
                    note = "Original source cut. Layout, captions and audio mix require recreation in the receiving editor. See rendered.otio for baked output." },
            };
            object clip = Clip(asset.EditorialMetadata?.Title ?? $"Clip {asset.Rank}",
                new Uri(asset.SourceFullPath).AbsoluteUri, asset.SourceStart, asset.Duration, asset.SourceMedia.Duration, rate, metadata);
            sourceVideo.Add(clip);
            sourceAudio.Add(asset.SourceMedia.AudioStreams.Count > 0 ? clip : Gap(asset.Duration, rate));
        }
        await Write("source-cuts.otio", Timeline("Replay Foundry — original source cuts", sourceVideo, sourceAudio));
        var renderedVideo = new List<object>();
        var renderedAudio = new List<object>();
        foreach (var group in ordered.GroupBy(asset => asset.OutputFullPath!, StringComparer.OrdinalIgnoreCase))
        {
            GenerationOutputAsset first = group.First();
            TimeSpan duration = TimeSpan.FromTicks(group.Sum(asset => asset.Duration.Ticks));
            object clip = Clip(first.EditorialMetadata?.Title ?? Path.GetFileName(group.Key),
                Uri.EscapeDataString(Path.GetFileName(group.Key)), TimeSpan.Zero, duration, duration,
                GenerationClipOutputProfile.FromAsset(first).FramesPerSecond,
                new Dictionary<string, object?> { ["replayfoundry"] = new { contributingAssetIds = group.Select(asset => asset.Id).ToArray(),
                    note = "Rendered video retains the exported picture and audio. Captions and composition may be baked in; subtitle sidecars remain separately editable." } });
            renderedVideo.Add(clip);
            renderedAudio.Add(clip);
        }
        await Write("rendered.otio", Timeline("Replay Foundry — rendered outputs", renderedVideo, renderedAudio));

        Task Write(string name, object document) => File.WriteAllTextAsync(Path.Combine(root, name),
            JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private static Dictionary<string, object?> Obj(string schema, params (string key, object? value)[] fields)
    {
        var result = new Dictionary<string, object?> { ["OTIO_SCHEMA"] = schema };
        foreach (var field in fields) result.Add(field.key, field.value);
        return result;
    }
    private static object Time(TimeSpan value, double rate) => Obj("RationalTime.1", ("value", value.TotalSeconds * rate), ("rate", rate));
    private static object Range(TimeSpan start, TimeSpan duration, double rate) => Obj("TimeRange.1",
        ("start_time", Time(start, rate)), ("duration", Time(duration, rate)));
    private static object Clip(string name, string url, TimeSpan start, TimeSpan duration, TimeSpan available,
        double rate, object metadata) => Obj("Clip.1", ("name", name), ("metadata", metadata), ("enabled", true),
        ("source_range", Range(start, duration, rate)), ("effects", Array.Empty<object>()), ("markers", Array.Empty<object>()),
        ("media_reference", Obj("ExternalReference.1", ("target_url", url), ("name", name),
            ("metadata", new Dictionary<string, object>()), ("available_range", Range(TimeSpan.Zero, available, rate)))));
    private static object Gap(TimeSpan duration, double rate) => Obj("Gap.1", ("name", "No source audio"),
        ("metadata", new Dictionary<string, object>()), ("source_range", Range(TimeSpan.Zero, duration, rate)),
        ("effects", Array.Empty<object>()), ("markers", Array.Empty<object>()));
    private static object Track(string kind, List<object> children) => Obj("Track.1", ("name", kind), ("kind", kind),
        ("children", children), ("source_range", null), ("metadata", new Dictionary<string, object>()),
        ("effects", Array.Empty<object>()), ("markers", Array.Empty<object>()));
    private static object Timeline(string name, List<object> video, List<object> audio) => Obj("Timeline.1", ("name", name),
        ("metadata", new Dictionary<string, object>()), ("global_start_time", Time(TimeSpan.Zero, 30)),
        ("tracks", Obj("Stack.1", ("name", "Tracks"), ("children", new[] { Track("Video", video), Track("Audio", audio) }),
            ("source_range", null), ("metadata", new Dictionary<string, object>()),
            ("effects", Array.Empty<object>()), ("markers", Array.Empty<object>()))));
}
