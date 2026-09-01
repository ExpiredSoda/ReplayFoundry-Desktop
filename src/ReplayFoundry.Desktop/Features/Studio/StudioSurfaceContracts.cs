namespace ReplayFoundry.Desktop.Features.Studio;

public enum StudioToolSection
{
    MomentsClips,
    StickersGraphics,
}

public enum StudioInspectorSection
{
    Clip,
    Audio,
    Captions,
    Effects,
    Graphics,
    Metadata,
}

public sealed record StudioToolItem(
    StudioToolSection Key,
    string Label,
    string Glyph,
    string Description);

public sealed record StudioInspectorItem(
    StudioInspectorSection Key,
    string Label,
    string Glyph,
    string Description);

public sealed record StudioBrowserPreviewItem(
    string Title,
    string Detail,
    string Status,
    string Glyph,
    string? AssetId = null,
    bool IsSelected = false,
    bool IsIncluded = true,
    bool IsQueued = false,
    string? SourcePositionText = null,
    string? BatchRankText = null,
    string? WhyThisClip = null)
{
    public bool HasClipRationale =>
        !string.IsNullOrWhiteSpace(WhyThisClip);

    public bool IsExcluded => !IsIncluded;

    public string AccessibilityStatus => IsSelected
        ? $"{Status}. Selected for editing."
        : Status;

    public string QueueActionText => IsQueued
        ? "Queued"
        : "Add to queue";

    public string QueueActionAutomationName => IsQueued
        ? $"{Title} is already queued"
        : $"Add {Title} to the queue";
}

internal sealed record StudioClipSelectionPresentation(
    string Label,
    string Explanation);

public sealed record StudioAudioStreamSummary(
    string StreamLabel,
    string Format,
    string MetadataHint);
