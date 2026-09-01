namespace ReplayFoundry.Desktop.Media.Composition;

/// <summary>
/// Identifies the coordinate system used by composition-region geometry.
/// </summary>
public enum CompositionCoordinateSpace
{
    /// <summary>
    /// Encoded-frame coordinates before display transforms. This is retained
    /// as an explicit non-effective space so consumers can reject it rather
    /// than silently misinterpreting geometry.
    /// </summary>
    EncodedFrameNormalizedBeforeAutorotation,

    /// <summary>
    /// Normalized coordinates over the effective displayed source after
    /// applying stream rotation and pixel-aspect-ratio correction, but before
    /// cropping, reframing, vertical conversion, or Studio transforms.
    /// </summary>
    EffectiveDisplayNormalizedBeforeCrop,
}
