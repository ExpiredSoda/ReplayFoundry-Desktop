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
    bool IsQueued = false);

public sealed record StudioAudioStreamSummary(
    string StreamLabel,
    string Format,
    string MetadataHint);
