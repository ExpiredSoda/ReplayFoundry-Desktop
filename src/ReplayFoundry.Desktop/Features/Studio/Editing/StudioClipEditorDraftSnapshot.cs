using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

internal sealed record StudioClipEditorDraftSnapshot(
    double StartAdjustmentSeconds,
    double EndAdjustmentSeconds,
    GenerationCaptionStylePreset CaptionStyle,
    StudioCaptionWordLimitPreset CaptionWordLimit,
    double CaptionVerticalPositionPercent,
    double CaptionMaximumWidthPercent,
    double CaptionFontScalePercent,
    StudioVideoEffectPreset VideoEffect,
    double VideoEffectIntensityPercent,
    StudioCaptionTypography? CaptionTypography = null);
