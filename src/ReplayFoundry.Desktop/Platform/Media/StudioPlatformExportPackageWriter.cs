using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Studio.Editing;

namespace ReplayFoundry.Desktop.Platform.Media;

/// <summary>Writes portable local publishing material beside the rendered media.</summary>
public static class StudioPlatformExportPackageWriter
{
    public static async Task WriteAsync(GenerationOutputProject project, IReadOnlyList<GenerationOutputAsset> assets,
        string outputDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(assets);
        string root = Path.GetFullPath(outputDirectory);
        await StudioTimelineHandoffWriter.WriteAsync(assets, root, cancellationToken);
        var index = new StringBuilder("# Replay Foundry publishing package\n\n")
            .AppendLine("Open each video's `.publish.md` to review and copy its title, description, and tags. Its `.publish.json` contains the same retained metadata and relative file names.")
            .AppendLine().AppendLine("Review the rendered video, caption spelling and timing, crop, game HUD, and target platform preview before uploading. Platform interfaces can cover different parts of the frame. Subtitle files are supplied when captions are available. Each clip's caption-delivery setting below identifies whether its captions are burned into the MP4 or supplied only as separate files.")
            .AppendLine().AppendLine("These are local export files. Upload the video and any subtitle file through your chosen platform; nothing has been published automatically.")
            .AppendLine().AppendLine("## Editor handoff")
            .AppendLine().AppendLine("Open `rendered.otio` in an editor that supports OpenTimelineIO to arrange the finished videos with their exported picture and sound. `source-cuts.otio` references the original recordings and preserves source trims/order for deeper editing; keep those recordings available. Original-source audio stream selection and Replay Foundry layouts, text, caption animation, and mixes must be recreated in the receiving editor. Their settings are retained as metadata, with SRT/VTT supplied for captions. Import support varies by editor. See the [OpenTimelineIO format documentation](https://opentimelineio.readthedocs.io/en/latest/tutorials/otio-serialized-schema.html).");
        foreach (var group in assets.Where(static asset => asset.OutputFullPath is not null)
                     .GroupBy(static asset => asset.OutputFullPath!, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.GetFullPath(group.Key);
            if (!string.Equals(Path.GetDirectoryName(path), root, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Publishing files must belong to the render output directory.", nameof(assets));
            GenerationOutputAsset[] clips = group.OrderBy(static asset => asset.Rank).ToArray();
            string fileName = Path.GetFileName(path);
            string stem = Path.GetFileNameWithoutExtension(path);
            string? ExistingSibling(string extension) => File.Exists(Path.Combine(root, stem + extension)) ? stem + extension : null;
            bool burnedCaptions = clips.Any(asset => asset.RenderSettings.BurnCaptions && asset.Captions is not null);
            string? ExistingCaption(string extension)
            {
                string caption = StudioCaptionSidecarPaths.Resolve(path, extension, burnedCaptions);
                return File.Exists(caption) ? Path.GetRelativePath(root, caption).Replace('\\', '/') : null;
            }
            string[] platforms = clips.Select(static asset => StudioPlatformExportPresets.DisplayName(asset.RenderSettings.PlatformPreset)).Distinct().ToArray();
            var files = new { video = fileName, subtitlesSrt = ExistingCaption(".srt"), subtitlesVtt = ExistingCaption(".vtt"), thumbnail = ExistingSibling(".thumbnail.jpg"),
                originalTimeline = "source-cuts.otio", renderedTimeline = "rendered.otio" };
            bool montage = project.Mode == GenerationMode.Montage;
            var document = new
            {
                schemaVersion = 1, projectId = project.Id, platforms, mode = montage ? "montage" : "individual",
                files, durationSeconds = clips.Sum(static asset => asset.Duration.TotalSeconds),
                title = montage ? null : clips[0].EditorialMetadata?.Title,
                description = montage ? null : clips[0].EditorialMetadata?.Description,
                tags = montage ? null : clips[0].EditorialMetadata?.Tags,
                requiresPublishingReview = true,
                clips = clips.Select(static asset => new
                {
                    asset.Id, asset.Rank, sourceFileName = Path.GetFileName(asset.SourceFullPath),
                    sourceStartSeconds = asset.SourceStart.TotalSeconds, sourceEndSeconds = asset.SourceEnd.TotalSeconds,
                    title = asset.EditorialMetadata?.Title, description = asset.EditorialMetadata?.Description,
                    tags = asset.EditorialMetadata?.Tags,
                    metadataCurrentForCut = asset.IsEditorialMetadataCurrentForCut,
                    canvas = asset.RenderSettings.Canvas.ToString(), layout = asset.RenderSettings.Layout.ToString(),
                    quality = asset.RenderSettings.Quality.ToString(), captionSafeArea = asset.Appearance.CaptionTypography.SafeArea.ToString(),
                    burnCaptions = asset.RenderSettings.BurnCaptions,
                }).ToArray(),
            };
            await File.WriteAllTextAsync(Path.Combine(root, stem + ".publish.json"),
                JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), cancellationToken);
            var readable = new StringBuilder("# ").AppendLine(fileName).AppendLine()
                .Append("Target: ").AppendLine(string.Join(", ", platforms)).AppendLine()
                .Append("Video: ").AppendLine(fileName)
                .Append("Duration: ").Append(clips.Sum(static asset => asset.Duration.TotalSeconds).ToString("0.###", CultureInfo.InvariantCulture)).AppendLine(" seconds");
            if (files.subtitlesSrt is not null) readable.Append("Subtitles (SRT): ").AppendLine(files.subtitlesSrt);
            if (files.subtitlesVtt is not null) readable.Append("Subtitles (WebVTT): ").AppendLine(files.subtitlesVtt);
            if (files.thumbnail is not null) readable.Append("Thumbnail: ").AppendLine(files.thumbnail);
            if (montage) readable.AppendLine().AppendLine("This montage combines the clips below. Write a title and description that describe the whole montage before uploading; the retained clip metadata is provided for reference.");
            foreach (GenerationOutputAsset asset in clips)
            {
                if (montage) readable.AppendLine().Append("## Clip ").AppendLine(asset.Rank.ToString(CultureInfo.InvariantCulture));
                readable.AppendLine().AppendLine(asset.RenderSettings.BurnCaptions
                    ? "Caption delivery: enabled captions are burned into this clip. Optional subtitle files are in caption-files for separate upload or editing; opening them during playback adds a second caption layer."
                    : "Clean video: available captions are supplied in the adjacent SRT/VTT files and are not burned into the MP4.");
                readable.AppendLine().AppendLine("### Title").AppendLine().AppendLine(asset.EditorialMetadata?.Title ?? "Add a title before uploading.")
                    .AppendLine().AppendLine("### Description").AppendLine().AppendLine(asset.EditorialMetadata?.Description ?? "Add a description before uploading.")
                    .AppendLine().AppendLine("### Tags").AppendLine().AppendLine(string.Join(", ", asset.EditorialMetadata?.Tags ?? []));
                if (!asset.IsEditorialMetadataCurrentForCut) readable.AppendLine().AppendLine("Review or regenerate this clip's metadata: it has not been verified for the current cut.");
            }
            await File.WriteAllTextAsync(Path.Combine(root, stem + ".publish.md"), readable.ToString(), new UTF8Encoding(false), cancellationToken);
            index.AppendLine().Append("- ").Append(stem).Append(".publish.md — ").AppendLine(string.Join(", ", platforms));
        }
        await File.WriteAllTextAsync(Path.Combine(root, "Publishing Guide.md"), index.ToString(), new UTF8Encoding(false), cancellationToken);
    }
}
