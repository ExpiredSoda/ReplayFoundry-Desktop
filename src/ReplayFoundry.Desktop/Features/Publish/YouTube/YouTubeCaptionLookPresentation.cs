using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Studio.Editing;

namespace ReplayFoundry.Desktop.Features.Publish.YouTube;

internal static class YouTubeCaptionLookPresentation
{
    public static string Key(YouTubePublishProvenance? provenance) => provenance is null ? "unknown" :
        JsonSerializer.Serialize(Cuts(provenance).Select(cut => cut.CaptionLook).ToArray());

    public static string Identifier(YouTubePublishProvenance? provenance) => provenance is null ? "unknown" :
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Key(provenance))))[..16];

    public static string Details(YouTubePublishProvenance? provenance)
    {
        if (provenance is null) return "Caption look was not recorded for this upload.";
        IReadOnlyList<YouTubePublishProvenance> cuts = Cuts(provenance);
        return string.Join("\n", cuts.Select((cut, index) =>
            (cuts.Count > 1 ? $"Cut {index + 1}: " : "") + Describe(cut.CaptionLook)));
    }

    private static IReadOnlyList<YouTubePublishProvenance> Cuts(YouTubePublishProvenance provenance) =>
        provenance.ContributingCuts.Count > 0 ? provenance.ContributingCuts : [provenance];

    private static string Describe(StudioCaptionLook? look)
    {
        if (look is null) return "Captions off";
        StudioCaptionTypography typography = look.CaptionTypography;
        return $"{look.CaptionStyle}; {typography.FontFamily}, {(typography.Bold ? "bold" : "regular")}; " +
            $"text {typography.TextColor}, accent {typography.AccentColor}; " +
            $"outline {typography.OutlineColor} / {typography.OutlineWidth:0.##}px, shadow {typography.ShadowDepth:0.##}px; " +
            $"background {typography.Background} / {typography.BackgroundColor} / {typography.BackgroundOpacityPercent:0.##}%; " +
            $"size {look.CaptionFontScalePercent:0.##}%, words {look.CaptionWordLimit}, " +
            $"position {look.CaptionVerticalPositionPercent:0.##}%, width {look.CaptionMaximumWidthPercent:0.##}%; " +
            $"{typography.SafeArea} safe area, {typography.Alignment} alignment, {typography.Casing} casing, " +
            $"{(typography.RightToLeft ? "right-to-left" : "left-to-right")}; " +
            $"motion {typography.AnimationIntensityPercent:0.##}%, line spacing {typography.LineSpacingPercent:0.##}%";
    }
}
