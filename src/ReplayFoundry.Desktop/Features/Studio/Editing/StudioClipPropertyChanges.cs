namespace ReplayFoundry.Desktop.Features.Studio.Editing;

internal static class StudioClipPropertyChanges
{
    internal static readonly string[] Draft = new[]
        {
            nameof(StudioClipEditorViewModel.StartAdjustmentSeconds),
            nameof(StudioClipEditorViewModel.EndAdjustmentSeconds),
            nameof(StudioClipEditorViewModel.StartAdjustmentMinimumSeconds),
            nameof(StudioClipEditorViewModel.StartAdjustmentMaximumSeconds),
            nameof(StudioClipEditorViewModel.EndAdjustmentMinimumSeconds),
            nameof(StudioClipEditorViewModel.EndAdjustmentMaximumSeconds),
            nameof(StudioClipEditorViewModel.DraftSourceStart),
            nameof(StudioClipEditorViewModel.DraftSourceEnd),
            nameof(StudioClipEditorViewModel.DraftDuration),
            nameof(StudioClipEditorViewModel.DraftSourceStartText),
            nameof(StudioClipEditorViewModel.DraftSourceEndText),
            nameof(StudioClipEditorViewModel.DraftDurationText),
            nameof(StudioClipEditorViewModel.StartAdjustmentText),
            nameof(StudioClipEditorViewModel.EndAdjustmentText),
            nameof(StudioClipEditorViewModel.StartAdjustmentSummary),
            nameof(StudioClipEditorViewModel.EndAdjustmentSummary),
            nameof(StudioClipEditorViewModel.BoundaryFrameStepSeconds),
            nameof(StudioClipEditorViewModel.BoundaryPrecisionText),
            nameof(StudioClipEditorViewModel.IsBoundaryDraftValid),
            nameof(StudioClipEditorViewModel.HasPendingEdit),
            nameof(StudioClipEditorViewModel.IsApplyingBoundaryEdit),
            nameof(StudioClipEditorViewModel.BoundaryEditStatus),
            nameof(StudioClipEditorViewModel.BoundaryEditError),
            nameof(StudioClipEditorViewModel.HasBoundaryEditError),
            nameof(StudioClipEditorViewModel.CaptionStyleOptions),
            nameof(StudioClipEditorViewModel.SelectedCaptionStyle),
            nameof(StudioClipEditorViewModel.CaptionWordLimitOptions),
            nameof(StudioClipEditorViewModel.SelectedCaptionWordLimit),
            nameof(StudioClipEditorViewModel.SelectedCaptionPhraseSizeDescription),
            nameof(StudioClipEditorViewModel.IsCaptionPhraseSizeEditorEnabled),
            nameof(StudioClipEditorViewModel.IsCaptionPhraseSizeSelectorVisible),
            nameof(StudioClipEditorViewModel.IsPopCaptionPhraseSizeLocked),
            nameof(StudioClipEditorViewModel.PopCaptionPhraseSizeText),
            nameof(StudioClipEditorViewModel.CaptionVerticalPositionPercent),
            nameof(StudioClipEditorViewModel.CaptionVerticalPositionText),
            nameof(StudioClipEditorViewModel.CaptionPresentationWarning),
            nameof(StudioClipEditorViewModel.HasCaptionPresentationWarning),
            nameof(StudioClipEditorViewModel.CaptionMaximumWidthPercent),
            nameof(StudioClipEditorViewModel.CaptionMaximumWidthText),
            nameof(StudioClipEditorViewModel.CaptionFontScalePercent),
            nameof(StudioClipEditorViewModel.CaptionFontScaleText),
            nameof(StudioClipEditorViewModel.CaptionedClipCount),
            nameof(StudioClipEditorViewModel.ApplyCaptionLookToAllText),
            nameof(StudioClipEditorViewModel.VideoEffectOptions),
            nameof(StudioClipEditorViewModel.SelectedVideoEffect),
            nameof(StudioClipEditorViewModel.SelectedVideoEffectDescription),
            nameof(StudioClipEditorViewModel.VideoEffectIntensityPercent),
            nameof(StudioClipEditorViewModel.VideoEffectIntensityText),
        };

    internal static void NotifyTypography(StudioCaptionTypography? previous, StudioCaptionTypography current, Action<string> notify)
    {
        notify(nameof(StudioClipEditorViewModel.CaptionTypography));
        Notify(previous?.FontFamily != current.FontFamily, nameof(StudioClipEditorViewModel.CaptionFontFamily));
        if (previous?.FontFamily != current.FontFamily)
        {
            notify(nameof(StudioClipEditorViewModel.CaptionFontFamilies));
            notify(nameof(StudioClipEditorViewModel.CaptionFontWarning));
            notify(nameof(StudioClipEditorViewModel.HasCaptionFontWarning));
        }
        Notify(previous?.TextColor != current.TextColor, nameof(StudioClipEditorViewModel.CaptionTextColor));
        Notify(previous?.AccentColor != current.AccentColor, nameof(StudioClipEditorViewModel.CaptionAccentColor));
        Notify(previous?.AccentColor != current.AccentColor, nameof(StudioClipEditorViewModel.SelectedCaptionAccentChoice));
        Notify(previous?.OutlineColor != current.OutlineColor, nameof(StudioClipEditorViewModel.CaptionOutlineColor));
        Notify(previous?.Bold != current.Bold, nameof(StudioClipEditorViewModel.CaptionBold));
        Notify(previous?.RightToLeft != current.RightToLeft, nameof(StudioClipEditorViewModel.CaptionRightToLeft));
        Notify(previous?.OutlineWidth != current.OutlineWidth, nameof(StudioClipEditorViewModel.CaptionOutlineWidth));
        Notify(previous?.ShadowDepth != current.ShadowDepth, nameof(StudioClipEditorViewModel.CaptionShadowDepth));
        Notify(previous?.SafeArea != current.SafeArea, nameof(StudioClipEditorViewModel.CaptionSafeArea));
        Notify(previous?.Background != current.Background, nameof(StudioClipEditorViewModel.CaptionBackground));
        Notify(previous?.BackgroundColor != current.BackgroundColor, nameof(StudioClipEditorViewModel.CaptionBackgroundColor));
        Notify(previous?.BackgroundOpacityPercent != current.BackgroundOpacityPercent, nameof(StudioClipEditorViewModel.CaptionBackgroundOpacityPercent));
        Notify(previous?.Alignment != current.Alignment, nameof(StudioClipEditorViewModel.CaptionAlignment));
        Notify(previous?.Casing != current.Casing, nameof(StudioClipEditorViewModel.CaptionCasing));
        Notify(previous?.AnimationIntensityPercent != current.AnimationIntensityPercent, nameof(StudioClipEditorViewModel.CaptionAnimationIntensityPercent));
        Notify(previous?.LineSpacingPercent != current.LineSpacingPercent, nameof(StudioClipEditorViewModel.CaptionLineSpacingPercent));
        Notify(previous is null || (previous.SafeAreaInsets is not null) != (current.SafeAreaInsets is not null), nameof(StudioClipEditorViewModel.UseCustomCaptionSafeArea));
        var beforeInsets = previous?.GetSafeAreaInsets();
        var afterInsets = current.GetSafeAreaInsets();
        Notify(beforeInsets?.LeftPercent != afterInsets.LeftPercent, nameof(StudioClipEditorViewModel.CaptionSafeLeftPercent));
        Notify(beforeInsets?.RightPercent != afterInsets.RightPercent, nameof(StudioClipEditorViewModel.CaptionSafeRightPercent));
        Notify(beforeInsets?.TopPercent != afterInsets.TopPercent, nameof(StudioClipEditorViewModel.CaptionSafeTopPercent));
        Notify(beforeInsets?.BottomPercent != afterInsets.BottomPercent, nameof(StudioClipEditorViewModel.CaptionSafeBottomPercent));

        void Notify(bool changed, string name) { if (changed) notify(name); }
    }
}
