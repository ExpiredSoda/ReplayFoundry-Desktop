namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public enum StudioPlatformExportPreset { Custom, YouTubeShorts, TikTok, InstagramReels }

public static class StudioPlatformExportPresets
{
    public static string DisplayName(StudioPlatformExportPreset preset) => preset switch
    {
        StudioPlatformExportPreset.YouTubeShorts => "YouTube Shorts",
        StudioPlatformExportPreset.TikTok => "TikTok",
        StudioPlatformExportPreset.InstagramReels => "Instagram Reels",
        _ => "Custom",
    };

    public static StudioRenderSettings Apply(StudioPlatformExportPreset preset, StudioRenderSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return new StudioRenderSettings(preset == StudioPlatformExportPreset.Custom ? current.Canvas : StudioOutputCanvas.Portrait,
            current.Layout, current.GameplayRegion, current.FacecamRegion, current.FacecamHeightPercent,
            current.AudioTracks, current.VoiceAudioStreamIndex, current.DuckGameplay, current.Quality,
            current.ColorOutput, preset, current.FrameKeyframes, current.TimedTextOverlays,
            current.AudioMastering, current.Decoration, current.BurnCaptions, current.Resolution)
            .WithCompatibleSourceCropTracksFrom(current);
    }

    public static StudioClipAppearance Apply(StudioPlatformExportPreset preset, StudioClipAppearance current)
    {
        ArgumentNullException.ThrowIfNull(current);
        StudioCaptionSafeArea safeArea = preset switch
        {
            StudioPlatformExportPreset.YouTubeShorts => StudioCaptionSafeArea.Shorts,
            StudioPlatformExportPreset.TikTok => StudioCaptionSafeArea.TikTok,
            StudioPlatformExportPreset.InstagramReels => StudioCaptionSafeArea.Reels,
            _ => current.CaptionTypography.SafeArea,
        };
        StudioCaptionTypography style = current.CaptionTypography;
        var typography = new StudioCaptionTypography(style.FontFamily, style.TextColor, style.AccentColor,
            style.OutlineColor, style.Bold, style.OutlineWidth, style.ShadowDepth, safeArea, style.RightToLeft,
            style.Background, style.BackgroundColor, style.BackgroundOpacityPercent, style.Alignment, style.Casing,
            style.AnimationIntensityPercent, style.LineSpacingPercent, style.SafeAreaInsets);
        return new StudioClipAppearance(current.CaptionStyle, current.CaptionVerticalPositionPercent,
            current.VideoEffect, current.VideoEffectIntensityPercent, current.GraphicOverlays,
            current.CaptionWordLimit, current.CaptionMaximumWidthPercent, current.CaptionFontScalePercent, typography);
    }
}
