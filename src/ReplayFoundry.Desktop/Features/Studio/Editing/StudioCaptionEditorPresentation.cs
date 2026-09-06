using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

internal static class StudioCaptionEditorPresentation
{
    internal static string PhraseSizeDescription(GenerationOutputAsset? asset,
        GenerationCaptionStylePreset style, SelectionOption<StudioCaptionWordLimitPreset> wordLimit)
    {
        if (StudioCaptionPresentationPolicy.RequiresTimedWords(style) &&
            asset?.Captions is { } track &&
            !StudioCaptionPresentationPolicy
                .HasCompleteTimedWordCoverage(track))
        {
            var visible = StudioCaptionCutProjection.Project(track, asset.SourceStart, asset.SourceEnd).Track;
            var coverage = StudioCaptionPresentationPolicy.GetTimingCoverage(visible,
                StudioCaptionPresentationPolicy.ResolveEffectiveWordLimit(style, wordLimit.Value));
            return coverage.TimedPages > 0
                ? $"{coverage.TimedPages} caption pages follow measured words; {coverage.PhrasePages} use phrase timing where individual word timing is unreliable."
                : coverage.PhrasePages > 0 ? $"{coverage.PhrasePages} caption pages use phrase timing because individual word timing is unavailable."
                : "No caption pages are visible within this cut.";
        }

        return style switch
        {
            GenerationCaptionStylePreset.Clean =>
                wordLimit.Description +
                " Clean replaces the complete phrase as one static caption.",
            GenerationCaptionStylePreset.WordFocus =>
                wordLimit.Description +
                " Word focus keeps the phrase visible while the active word animates.",
            GenerationCaptionStylePreset.KaraokeSweep =>
                wordLimit.Description +
                " Karaoke keeps the phrase visible while its word sweep animates.",
            GenerationCaptionStylePreset.HighContrast =>
                wordLimit.Description +
                " High contrast replaces the complete phrase as one static panel.",
            GenerationCaptionStylePreset.Pop =>
                "Pop shows one spoken word at a time. Your saved phrase " +
                "size returns when you choose another effect.",
            _ => throw new ArgumentOutOfRangeException(nameof(style)),
        };
    }
}
