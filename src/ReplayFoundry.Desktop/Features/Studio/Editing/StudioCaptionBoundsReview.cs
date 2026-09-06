using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

/// <summary>Reviews measured text at its saved position; never stretches speech timing or silently shrinks glyphs.</summary>
public static class StudioCaptionBoundsReview
{
    public static string? GetWarning(GenerationCandidateCaptionTrack track, StudioCaptionTypography typography,
        GenerationCaptionStylePreset style, StudioCaptionWordLimitPreset wordLimit,
        StudioCaptionFrameLayout layout, int frameWidth, int frameHeight, double verticalPositionPercent)
    {
        if (typography.SafeArea == StudioCaptionSafeArea.None && typography.SafeAreaInsets is null) return null;
        var insets = typography.GetSafeAreaInsets();
        var safe = new Rect(frameWidth * insets.LeftPercent / 100, frameHeight * insets.TopPercent / 100,
            frameWidth * (100 - insets.LeftPercent - insets.RightPercent) / 100,
            frameHeight * (100 - insets.TopPercent - insets.BottomPercent) / 100);
        int outside = 0;
        foreach (var cue in StudioCaptionPresentationPolicy.ProjectCues(track, wordLimit))
        {
            // Pop displays one observed word at a time when alignment is complete.
            IEnumerable<string> texts = style == GenerationCaptionStylePreset.Pop && cue.Words.Count > 0
                ? cue.Words.Select(static word => word.Text) : [cue.Text];
            if (texts.Any(text => !safe.Contains(Measure(typography.DisplayText(text), typography, style, layout,
                    frameWidth, frameHeight * verticalPositionPercent / 100)))) outside++;
        }
        foreach (var segment in track.Segments.Where(static segment => !string.IsNullOrWhiteSpace(segment.SecondaryText)))
        {
            var secondaryLayout = layout with
            {
                BaseFontSizePixels = (int)Math.Round(layout.BaseFontSizePixels * .75),
                EffectiveFontSizePixels = (int)Math.Round(layout.BaseFontSizePixels * .75),
            };
            if (!safe.Contains(Measure(typography.DisplayText(segment.SecondaryText!), typography, GenerationCaptionStylePreset.Clean,
                    secondaryLayout, frameWidth, frameHeight * Math.Min(90, verticalPositionPercent + 10) / 100))) outside++;
        }
        return outside == 0 ? null : $"{outside} caption display{(outside == 1 ? "" : "s")} extend beyond the safe-area margins at the saved position. " +
            "Review the highlighted bounds, reduce text size, split phrases, or adjust margins. " +
            "Measurement includes text and padding at rest; animation can extend farther.";
    }

    internal static Rect Measure(string text, StudioCaptionTypography typography, GenerationCaptionStylePreset style,
        StudioCaptionFrameLayout layout, int frameWidth, double centerY)
    {
        double fontSize = StudioCaptionPresentationPolicy.GetWpfPreviewFontSize(layout);
        double textWidth, textHeight;
        if (typography.SafeAreaInsets is not null || typography.LineSpacingPercent != 100 || typography.HasCustomBackground(style))
        {
            var lines = StudioCaptionLineLayout.Create(text, typography, fontSize, layout.MaximumWidthPixels);
            textWidth = lines.Lines.Select(static line => line.Width).DefaultIfEmpty(0).Max();
            textHeight = lines.Height;
        }
        else
        {
            var measured = new FormattedText(text, CultureInfo.CurrentUICulture,
                typography.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                new Typeface(new FontFamily(StudioCaptionFontResolver.Resolve(typography.FontFamily).Family), FontStyles.Normal,
                    typography.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), fontSize, Brushes.White, 1)
                { MaxTextWidth = layout.MaximumWidthPixels };
            textWidth = measured.WidthIncludingTrailingWhitespace; textHeight = measured.Height;
        }
        double left = typography.Alignment switch
        {
            StudioCaptionAlignment.Left => layout.HorizontalMarginPixels,
            StudioCaptionAlignment.Right => frameWidth - layout.HorizontalMarginPixels - textWidth,
            _ => (frameWidth - textWidth) / 2,
        };
        var bounds = new Rect(left, centerY - textHeight / 2, textWidth, textHeight);
        double padding = Math.Max(typography.OutlineWidth + typography.ShadowDepth,
            typography.HasBackground(style) ? typography.HasCustomBackground(style) ? 8 : typography.OutlineWidth + 12 : 0);
        bounds.Inflate(padding, padding);
        return bounds;
    }
}
