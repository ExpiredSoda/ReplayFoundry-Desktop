namespace ReplayFoundry.Desktop.Features.Studio.Preview;

// Ordered dependency groups shared by the preview and live-caption projections.
internal static class StudioPreviewPropertyNotifications
{
    internal static void Context(Action<string> changed)
    {
        foreach (string propertyName in new[]
        {
            nameof(StudioPreviewViewModel.SequenceSummary),
            nameof(StudioPreviewViewModel.ProjectPromptTitle),
            nameof(StudioPreviewViewModel.PreviewFormatText),
            nameof(StudioPreviewViewModel.PreviewCanvasWidth),
            nameof(StudioPreviewViewModel.PreviewCanvasHeight),
            nameof(StudioPreviewViewModel.PreviewScaleText),
            nameof(StudioPreviewViewModel.CanShowCaptionControls),
            nameof(StudioPreviewViewModel.IsCaptionContentVisible),
            nameof(StudioPreviewViewModel.CaptionVisibilityText),
            nameof(StudioPreviewViewModel.CaptionVisibilityShortText),
        })
        {
            changed(propertyName);
        }
    }

    internal static void Preview(Action<string> changed)
    {
        foreach (string propertyName in new[]
        {
            nameof(StudioPreviewViewModel.PreviewMediaPath),
            nameof(StudioPreviewViewModel.PreviewSourceOffsetSeconds),
            nameof(StudioPreviewViewModel.PreviewSeekVersion),
            nameof(StudioPreviewViewModel.IsPreviewPlaying),
            nameof(StudioPreviewViewModel.IsPreviewLoading),
            nameof(StudioPreviewViewModel.IsPreviewAvailable),
            nameof(StudioPreviewViewModel.IsPreviewSynchronized),
            nameof(StudioPreviewViewModel.ProjectPromptTitle),
            nameof(StudioPreviewViewModel.PreviewStatus),
            nameof(StudioPreviewViewModel.PreviewError),
            nameof(StudioPreviewViewModel.HasPreviewError),
            nameof(StudioPreviewViewModel.PreviewPlayPauseText),
            nameof(StudioPreviewViewModel.PreviewPlayPauseIconKey),
            nameof(StudioPreviewViewModel.HasLiveCaption),
            nameof(StudioPreviewViewModel.LiveCaptionText),
            nameof(StudioPreviewViewModel.LiveCaptionEmphasisSpans),
            nameof(StudioPreviewViewModel.LiveSecondaryCaptionText),
            nameof(StudioPreviewViewModel.HasLiveSecondaryCaption),
            nameof(StudioPreviewViewModel.LiveSecondaryCaptionVerticalPercent),
            nameof(StudioPreviewViewModel.LiveSecondaryCaptionFontSizePixels),
            nameof(StudioPreviewViewModel.LiveCaptionActiveWord),
            nameof(StudioPreviewViewModel.LiveCaptionAccentStartIndex),
            nameof(StudioPreviewViewModel.LiveCaptionAccentLength),
            nameof(StudioPreviewViewModel.LiveCaptionSweepLength),
            nameof(StudioPreviewViewModel.LiveCaptionAccentProgress),
            nameof(StudioPreviewViewModel.LiveCaptionScale),
            nameof(StudioPreviewViewModel.LiveCaptionVerticalPercent),
            nameof(StudioPreviewViewModel.LiveCaptionStyle),
            nameof(StudioPreviewViewModel.LiveCaptionTypography),
            nameof(StudioPreviewViewModel.LiveCaptionMaximumWidthPixels),
            nameof(StudioPreviewViewModel.LiveCaptionFontSizePixels),
            nameof(StudioPreviewViewModel.LiveCaptionPresentationWarning),
            nameof(StudioPreviewViewModel.HasLiveCaptionPresentationWarning),
        })
        {
            changed(propertyName);
        }
    }

    internal static void LiveCaption(Action<string> changed)
    {
        LiveCaptionContent(changed);
        changed(nameof(StudioPreviewViewModel.LiveCaptionVerticalPercent));
        changed(nameof(StudioPreviewViewModel.LiveCaptionStyle));
        changed(nameof(StudioPreviewViewModel.LiveCaptionTypography));
        changed(nameof(StudioPreviewViewModel.LiveCaptionMaximumWidthPixels));
        changed(nameof(StudioPreviewViewModel.LiveCaptionFontSizePixels));
        changed(nameof(StudioPreviewViewModel.LiveSecondaryCaptionVerticalPercent));
        changed(nameof(StudioPreviewViewModel.LiveSecondaryCaptionFontSizePixels));
        changed(nameof(StudioPreviewViewModel.LiveCaptionPresentationWarning));
        changed(nameof(StudioPreviewViewModel.HasLiveCaptionPresentationWarning));
    }

    internal static void LiveCaptionContent(Action<string> changed)
    {
        changed(nameof(StudioPreviewViewModel.HasLiveCaption));
        changed(nameof(StudioPreviewViewModel.LiveCaptionText));
        changed(nameof(StudioPreviewViewModel.LiveCaptionEmphasisSpans));
        changed(nameof(StudioPreviewViewModel.LiveSecondaryCaptionText));
        changed(nameof(StudioPreviewViewModel.HasLiveSecondaryCaption));
        changed(nameof(StudioPreviewViewModel.LiveCaptionActiveWord));
        changed(nameof(StudioPreviewViewModel.LiveCaptionAccentStartIndex));
        changed(nameof(StudioPreviewViewModel.LiveCaptionAccentLength));
        changed(nameof(StudioPreviewViewModel.LiveCaptionSweepLength));
        changed(nameof(StudioPreviewViewModel.LiveCaptionAccentProgress));
        changed(nameof(StudioPreviewViewModel.LiveCaptionScale));
    }
}
