using System.IO;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.Editing;

namespace ReplayFoundry.Desktop.Features.Studio;

internal static class StudioSurfaceCatalog
{
    public static IReadOnlyList<StudioToolItem> ToolSections { get; } =
        Array.AsReadOnly<StudioToolItem>(
        [
            new(StudioToolSection.MomentsClips, "Clips", "Icon.Spark", "Choose the generated clip to preview and edit"),
            new(StudioToolSection.StickersGraphics, "Graphics", "Icon.Graphics", "Add and position visual overlays"),
        ]);

    public static IReadOnlyList<StudioInspectorItem> InspectorSections { get; } =
        Array.AsReadOnly<StudioInspectorItem>(
        [
            new(StudioInspectorSection.Clip, "Clip", "Icon.Media", "Keep, rate, and trim"),
            new(StudioInspectorSection.Audio, "Audio", "Icon.Audio", "Retained source streams"),
            new(StudioInspectorSection.Captions, "Captions", "Icon.Caption", "Style and placement"),
            new(StudioInspectorSection.Effects, "Effects", "Icon.Effects", "Color and transition"),
            new(StudioInspectorSection.Graphics, "Graphics", "Icon.Graphics", "Overlay position and scale"),
            new(StudioInspectorSection.Metadata, "Metadata", "Icon.Info", "Title and tags"),
        ]);

    public static IReadOnlyList<SelectionOption<GenerationCaptionStylePreset>> CaptionStyles { get; } =
        Array.AsReadOnly<SelectionOption<GenerationCaptionStylePreset>>(
        [
            new(GenerationCaptionStylePreset.Clean, "Clean", "Readable two-line subtitles."),
            new(GenerationCaptionStylePreset.WordFocus, "Word focus", "Keeps context visible while the spoken word lifts and glows."),
            new(GenerationCaptionStylePreset.KaraokeSweep, "Karaoke focus", "Moves a gold, pulsing focus word across each phrase."),
            new(GenerationCaptionStylePreset.Pop, "Pop", "Bounces each spoken word with energetic short-form timing."),
            new(GenerationCaptionStylePreset.HighContrast, "High contrast", "Uses an opaque rounded panel and strong edge on busy footage."),
        ]);

    public static IReadOnlyList<SelectionOption<StudioCaptionWordLimitPreset>> CaptionWordLimits { get; } =
        Array.AsReadOnly<SelectionOption<StudioCaptionWordLimitPreset>>(
        [
            new(StudioCaptionWordLimitPreset.Streamlined, "Streamlined · 5 words", "Keeps each timed caption window to four or five words when possible."),
            new(StudioCaptionWordLimitPreset.Balanced, "More context · 8 words", "Shows a longer phrase while keeping dense segments moving."),
            new(StudioCaptionWordLimitPreset.Punchy, "Punchy · 3 words", "Uses short, fast caption windows for energetic edits."),
            new(StudioCaptionWordLimitPreset.FullSegment, "Full segment", "Keeps the provider's complete legacy caption segment on screen."),
        ]);

    public static IReadOnlyList<SelectionOption<StudioVideoEffectPreset>> VideoEffects { get; } =
        Array.AsReadOnly<SelectionOption<StudioVideoEffectPreset>>(
        [
            new(StudioVideoEffectPreset.None, "None", "Keep the source color unchanged."),
            new(StudioVideoEffectPreset.Noir, "Noir", "Reduce color and deepen contrast."),
            new(StudioVideoEffectPreset.Chromatic, "Chromatic", "Offset red and blue channels for a subtle digital edge."),
            new(StudioVideoEffectPreset.SoftBloom, "Soft bloom", "Soften highlights and gently lift the image."),
            new(StudioVideoEffectPreset.Vivid, "Vivid", "Increase selective color and contrast."),
        ]);

    public static StudioToolItem GetTool(StudioToolSection section) =>
        ToolSections.SingleOrDefault(item => item.Key == section) ??
        throw new InvalidOperationException(
            "The selected Studio tool is not defined.");

    public static StudioInspectorItem GetInspector(StudioInspectorSection section) =>
        InspectorSections.SingleOrDefault(item => item.Key == section) ??
        throw new InvalidOperationException(
            "The selected Studio inspector is not defined.");

    public static IReadOnlyList<StudioBrowserPreviewItem> BuildBrowserPreviewItems(
        StudioToolSection section,
        GenerationOutputProject? project,
        string? selectedAssetId,
        IReadOnlySet<string>? queuedAssetIds = null) => section switch
        {
            StudioToolSection.MomentsClips => BuildMomentItems(
                project,
                selectedAssetId,
                queuedAssetIds),
            _ => BuildGraphicItems(project, selectedAssetId),
        };

    private static IReadOnlyList<StudioBrowserPreviewItem> BuildGraphicItems(
        GenerationOutputProject? project,
        string? selectedAssetId)
    {
        GenerationOutputAsset? asset = project?.Assets.FirstOrDefault(item =>
            selectedAssetId is not null && item.Id.Equals(selectedAssetId, StringComparison.Ordinal));
        if (asset?.Appearance.GraphicOverlays.Count is not > 0)
        {
            return Items(new StudioBrowserPreviewItem(
                "No graphics added",
                "Drag a PNG, JPG, or WebP from Windows onto the preview.",
                "DROP ON PREVIEW",
                "Icon.Graphics"));
        }

        return Array.AsReadOnly(asset.Appearance.GraphicOverlays
            .Select(overlay => new StudioBrowserPreviewItem(
                overlay.DisplayName,
                $"Center {overlay.CenterXPercent:0.#}% × {overlay.CenterYPercent:0.#}% · width {overlay.WidthPercent:0.#}%",
                "ON SELECTED CLIP",
                "Icon.Graphics"))
            .ToArray());
    }

    private static IReadOnlyList<StudioBrowserPreviewItem> BuildMomentItems(
        GenerationOutputProject? project,
        string? selectedAssetId,
        IReadOnlySet<string>? queuedAssetIds)
    {
        if (project is null)
        {
            return Items(
                new StudioBrowserPreviewItem(
                    "No generated clips yet",
                    "Complete Generate to choose and edit real candidates here.",
                    "EMPTY",
                    "Icon.Spark"));
        }

        return Array.AsReadOnly(
            project.Assets
                .Select(
                    asset => new StudioBrowserPreviewItem(
                        asset.EditorialMetadata?.Title ??
                            $"Clip {asset.Rank:00}",
                        $"Clip {asset.Rank:00} · " +
                        $"{StudioTimeFormatter.FormatDuration(asset.Duration)} · " +
                        Path.GetFileName(asset.SourceFullPath),
                        asset.RequiredDiversityRelaxation
                            ? "SIMILAR / COUNT FILL"
                            : asset.MeetsQualityTarget
                                ? "QUALITY TARGET"
                                : "BEST AVAILABLE",
                        "Icon.Media",
                        asset.Id,
                        asset.Id.Equals(selectedAssetId, StringComparison.Ordinal),
                        asset.IsIncludedInFinalRender,
                        queuedAssetIds?.Contains(asset.Id) == true))
                .ToArray());
    }

    private static IReadOnlyList<StudioBrowserPreviewItem> Items(
        params StudioBrowserPreviewItem[] items) => Array.AsReadOnly(items);

}
