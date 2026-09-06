using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Library;

namespace ReplayFoundry.Desktop.Features.Publish.YouTube;

/// <summary>An immutable snapshot of the submitted asset, never reconstructed from later Studio edits.</summary>
public sealed class YouTubePublishProvenance
{
    public YouTubePublishProvenance(string sourceFullPath, TimeSpan sourceStart, TimeSpan sourceEnd,
        string? gameName, StudioOutputCanvas canvas, int outputWidth, int outputHeight,
        StudioCompositionLayout compositionLayout, bool captionsEnabled, StudioCaptionLook? captionLook,
        string? channelId = null,
        IReadOnlyList<YouTubePublishProvenance>? contributingCuts = null,
        TimeSpan? outputDuration = null)
    {
        if (string.IsNullOrWhiteSpace(sourceFullPath) || !Path.IsPathFullyQualified(sourceFullPath) ||
            sourceStart < TimeSpan.Zero || sourceEnd <= sourceStart ||
            !Enum.IsDefined(canvas) || !Enum.IsDefined(compositionLayout) || outputWidth <= 0 || outputHeight <= 0 ||
            captionsEnabled != (captionLook is not null))
            throw new ArgumentException("Publish provenance requires the submitted source cut, output frame, and caption look.");
        SourceFullPath = Path.GetFullPath(sourceFullPath); SourceStart = sourceStart; SourceEnd = sourceEnd;
        GameName = string.IsNullOrWhiteSpace(gameName) ? null : gameName.Trim(); Canvas = canvas;
        OutputWidth = outputWidth; OutputHeight = outputHeight; CompositionLayout = compositionLayout;
        CaptionsEnabled = captionsEnabled; CaptionLook = captionLook;
        ChannelId = string.IsNullOrWhiteSpace(channelId) ? null : channelId.Trim();
        YouTubePublishProvenance[] cuts = contributingCuts?.ToArray() ?? [];
        if (cuts.Length > 300 || cuts.Any(static cut => cut is null || cut.ContributingCuts.Count > 0) || outputDuration <= TimeSpan.Zero)
            throw new ArgumentException("Publish provenance requires bounded, flat source cuts and a positive output duration.");
        ContributingCuts = Array.AsReadOnly(cuts);
        OutputDuration = outputDuration;
    }
    public string SourceFullPath { get; }
    public TimeSpan SourceStart { get; }
    public TimeSpan SourceEnd { get; }
    public TimeSpan SourceCutDuration => SourceEnd - SourceStart;
    public string? GameName { get; }
    public StudioOutputCanvas Canvas { get; }
    public int OutputWidth { get; }
    public int OutputHeight { get; }
    public StudioCompositionLayout CompositionLayout { get; }
    public bool CaptionsEnabled { get; }
    public StudioCaptionLook? CaptionLook { get; }
    public string? ChannelId { get; }
    public IReadOnlyList<YouTubePublishProvenance> ContributingCuts { get; }
    public TimeSpan? OutputDuration { get; }
    public YouTubePublishProvenance WithPublishedContext(TimeSpan duration, string? channelId) => new(
        SourceFullPath, SourceStart, SourceEnd, GameName, Canvas, OutputWidth, OutputHeight,
        CompositionLayout, CaptionsEnabled, CaptionLook, channelId, ContributingCuts, duration);
    public static YouTubePublishProvenance? Capture(LibraryMediaAsset asset, string? channelId = null)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return asset.SourceProvenance?.WithPublishedContext(asset.Duration, channelId);
    }
    public static YouTubePublishProvenance CaptureSequence(IReadOnlyList<GenerationOutputAsset> assets, TimeSpan duration)
    {
        if (assets.Count == 0) throw new ArgumentException("A rendered sequence needs at least one source cut.", nameof(assets));
        YouTubePublishProvenance first = Capture(assets[0]);
        return new(first.SourceFullPath, first.SourceStart, first.SourceEnd, first.GameName, first.Canvas,
            first.OutputWidth, first.OutputHeight, first.CompositionLayout, first.CaptionsEnabled, first.CaptionLook,
            contributingCuts: assets.Count > 1 ? assets.Select(asset => Capture(asset)).ToArray() : [], outputDuration: duration);
    }
    public static YouTubePublishProvenance Capture(GenerationOutputAsset asset, string? channelId = null)
    {
        ArgumentNullException.ThrowIfNull(asset);
        GenerationClipOutputProfile profile = GenerationClipOutputProfile.FromAsset(asset);
        bool burnedCaptions = asset.RenderSettings.BurnCaptions && asset.Captions is { } track &&
            StudioCaptionCutProjection.Project(track, asset.SourceStart, asset.SourceEnd).Track.HasRenderableSegments;
        return new(asset.SourceFullPath, asset.SourceStart, asset.SourceEnd,
            asset.EditorialContext?.GameContext.IsUserGrounded == true ? asset.EditorialContext.GameContext.GameName : null,
            asset.RenderSettings.Canvas, profile.Width, profile.Height, asset.RenderSettings.Layout,
            burnedCaptions, burnedCaptions ? StudioCaptionLook.FromAppearance(asset.Appearance) : null, channelId);
    }
}
