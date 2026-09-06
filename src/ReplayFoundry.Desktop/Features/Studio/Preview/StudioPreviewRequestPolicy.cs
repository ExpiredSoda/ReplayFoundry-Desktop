using ReplayFoundry.Desktop.Features.Generate.Handoff;

namespace ReplayFoundry.Desktop.Features.Studio.Preview;

internal static class StudioPreviewRequestPolicy
{
    internal static string? CreateIdentity(
        GenerationOutputAsset? asset,
        StudioPreviewRangeMode rangeMode) => asset is null
            ? null
            : StudioPreviewCacheKey.CreateMediaIdentity(
                new StudioPreviewMediaRequest(asset, rangeMode));

    internal static StudioPreviewMediaRequest CreateRequest(GenerationOutputAsset asset,
        StudioPreviewRangeMode rangeMode, bool needsEnvelope, TimeSpan rangeStart, TimeSpan rangeEnd)
    {
        if (rangeMode == StudioPreviewRangeMode.ExactSelection || !needsEnvelope)
            return new(asset, StudioPreviewRangeMode.ExactSelection);
        var envelope = new StudioPreviewMediaRequest(asset, StudioPreviewRangeMode.EditableEnvelope);
        if (envelope.SourceStart <= rangeStart && envelope.SourceEnd >= rangeEnd)
            return envelope;
        // Full-source manual edits and loudness-dependent exact previews can
        // exceed the original trim envelope. Their requested range stays exact.
        return new(asset.WithStudioEdits(rangeStart, rangeEnd, asset.Appearance), StudioPreviewRangeMode.ExactSelection);
    }

}
