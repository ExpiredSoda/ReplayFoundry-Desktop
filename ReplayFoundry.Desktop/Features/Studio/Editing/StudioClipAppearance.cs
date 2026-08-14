using System.Collections.ObjectModel;
using System.IO;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public enum StudioVideoEffectPreset
{
    None,
    Noir,
    Chromatic,
    SoftBloom,
    Vivid,
}

public enum StudioCaptionWordLimitPreset
{
    FullSegment,
    Balanced,
    Streamlined,
    Punchy,
}

public sealed class StudioGraphicOverlay
{
    public const double MinimumPositionPercent = 0;
    public const double MaximumPositionPercent = 100;
    public const double MinimumWidthPercent = 5;
    public const double MaximumWidthPercent = 100;

    public StudioGraphicOverlay(
        string id,
        string imageFullPath,
        double centerXPercent,
        double centerYPercent,
        double widthPercent)
        : this(
            id,
            imageFullPath,
            centerXPercent,
            centerYPercent,
            widthPercent,
            requireExistingFile: true)
    {
    }

    private StudioGraphicOverlay(
        string id,
        string imageFullPath,
        double centerXPercent,
        double centerYPercent,
        double widthPercent,
        bool requireExistingFile)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128)
        {
            throw new ArgumentException("A graphic overlay requires a bounded ID.", nameof(id));
        }
        string extension = Path.GetExtension(imageFullPath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(imageFullPath) ||
            !Path.IsPathFullyQualified(imageFullPath) ||
            requireExistingFile && !File.Exists(imageFullPath) ||
            !new[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(
                extension,
                StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "A graphic overlay requires an existing PNG, JPEG, or WebP file.",
                nameof(imageFullPath));
        }
        RequirePercent(centerXPercent, nameof(centerXPercent), MinimumPositionPercent, MaximumPositionPercent);
        RequirePercent(centerYPercent, nameof(centerYPercent), MinimumPositionPercent, MaximumPositionPercent);
        RequirePercent(widthPercent, nameof(widthPercent), MinimumWidthPercent, MaximumWidthPercent);

        Id = id.Trim();
        ImageFullPath = Path.GetFullPath(imageFullPath);
        CenterXPercent = centerXPercent;
        CenterYPercent = centerYPercent;
        WidthPercent = widthPercent;
    }

    public string Id { get; }
    public string ImageFullPath { get; }
    public string DisplayName => Path.GetFileNameWithoutExtension(ImageFullPath);
    public double CenterXPercent { get; }
    public double CenterYPercent { get; }
    public double WidthPercent { get; }

    public StudioGraphicOverlay WithPlacement(
        double centerXPercent,
        double centerYPercent,
        double widthPercent) =>
        new(Id, ImageFullPath, centerXPercent, centerYPercent, widthPercent);

    internal static StudioGraphicOverlay Restore(
        string id,
        string imageFullPath,
        double centerXPercent,
        double centerYPercent,
        double widthPercent) =>
        new(
            id,
            imageFullPath,
            centerXPercent,
            centerYPercent,
            widthPercent,
            requireExistingFile: false);

    private static void RequirePercent(
        double value,
        string parameter,
        double minimum,
        double maximum)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(parameter);
        }
    }
}

public sealed class StudioClipAppearance
{
    public const double MinimumCaptionVerticalPositionPercent = 10;
    public const double MaximumCaptionVerticalPositionPercent = 90;
    public const double DefaultCaptionVerticalPositionPercent = 82;
    public const double MinimumCaptionMaximumWidthPercent = 40;
    public const double MaximumCaptionMaximumWidthPercent = 100;
    public const double DefaultCaptionMaximumWidthPercent = 100;
    public const double MinimumCaptionFontScalePercent = 60;
    public const double MaximumCaptionFontScalePercent = 160;
    public const double DefaultCaptionFontScalePercent = 100;

    public StudioClipAppearance(
        GenerationCaptionStylePreset captionStyle,
        double captionVerticalPositionPercent,
        StudioVideoEffectPreset videoEffect,
        double videoEffectIntensityPercent,
        IEnumerable<StudioGraphicOverlay>? graphicOverlays = null,
        StudioCaptionWordLimitPreset captionWordLimit =
            StudioCaptionWordLimitPreset.Streamlined,
        double captionMaximumWidthPercent =
            DefaultCaptionMaximumWidthPercent,
        double captionFontScalePercent =
            DefaultCaptionFontScalePercent)
    {
        if (!Enum.IsDefined(captionStyle))
        {
            throw new ArgumentOutOfRangeException(nameof(captionStyle));
        }
        if (!double.IsFinite(captionVerticalPositionPercent) ||
            captionVerticalPositionPercent is <
                MinimumCaptionVerticalPositionPercent or >
                MaximumCaptionVerticalPositionPercent)
        {
            throw new ArgumentOutOfRangeException(
                nameof(captionVerticalPositionPercent));
        }
        if (!Enum.IsDefined(videoEffect))
        {
            throw new ArgumentOutOfRangeException(nameof(videoEffect));
        }
        if (!Enum.IsDefined(captionWordLimit))
        {
            throw new ArgumentOutOfRangeException(nameof(captionWordLimit));
        }
        if (!double.IsFinite(videoEffectIntensityPercent) ||
            videoEffectIntensityPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(videoEffectIntensityPercent));
        }
        RequirePercent(
            captionMaximumWidthPercent,
            nameof(captionMaximumWidthPercent),
            MinimumCaptionMaximumWidthPercent,
            MaximumCaptionMaximumWidthPercent);
        RequirePercent(
            captionFontScalePercent,
            nameof(captionFontScalePercent),
            MinimumCaptionFontScalePercent,
            MaximumCaptionFontScalePercent);

        CaptionStyle = captionStyle;
        CaptionVerticalPositionPercent =
            captionVerticalPositionPercent;
        CaptionWordLimit = captionWordLimit;
        CaptionMaximumWidthPercent = captionMaximumWidthPercent;
        CaptionFontScalePercent = captionFontScalePercent;
        VideoEffect = videoEffect;
        VideoEffectIntensityPercent =
            videoEffectIntensityPercent;
        StudioGraphicOverlay[] overlays = graphicOverlays?.ToArray() ?? [];
        if (overlays.Any(static overlay => overlay is null) ||
            overlays.Select(static overlay => overlay.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != overlays.Length)
        {
            throw new ArgumentException(
                "Graphic overlays must be non-null with unique case-insensitive IDs.",
                nameof(graphicOverlays));
        }
        GraphicOverlays = new ReadOnlyCollection<StudioGraphicOverlay>(overlays);
    }

    public GenerationCaptionStylePreset CaptionStyle { get; }

    public double CaptionVerticalPositionPercent { get; }

    public StudioCaptionWordLimitPreset CaptionWordLimit { get; }

    public double CaptionMaximumWidthPercent { get; }

    public double CaptionFontScalePercent { get; }

    public StudioVideoEffectPreset VideoEffect { get; }

    public double VideoEffectIntensityPercent { get; }

    public IReadOnlyList<StudioGraphicOverlay> GraphicOverlays { get; }

    public static StudioClipAppearance CreateDefault(
        GenerationCaptionStylePreset captionStyle) =>
        new(
            captionStyle,
            DefaultCaptionVerticalPositionPercent,
            StudioVideoEffectPreset.None,
            0);

    private static void RequirePercent(
        double value,
        string parameterName,
        double minimum,
        double maximum)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
