using System;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Media.Analysis.Visual;

/// <summary>
/// One internally keyed visual-analysis branch.
/// </summary>
public sealed class VisualEvidenceTarget
{
    public VisualEvidenceTarget(
        string targetKey,
        VisualEvidenceTargetKind kind,
        TimeSpan start,
        TimeSpan end,
        int effectiveDisplayWidth,
        int effectiveDisplayHeight,
        int? intervalIndex = null,
        string? regionId = null,
        CompositionRegionRole? role = null,
        CompositionRegionTraits? traits = null,
        NormalizedRectangle? requestedRectangle = null,
        PixelRectangle? actualPixelCrop = null,
        CompositionConfidence? geometryConfidence = null,
        CompositionConfidence? roleConfidence = null,
        CompositionValueSource? geometrySource = null,
        CompositionValueSource? roleSource = null)
    {
        if (string.IsNullOrWhiteSpace(targetKey))
        {
            throw new ArgumentException(
                "A visual evidence target requires an internal key.",
                nameof(targetKey));
        }

        string normalizedTargetKey =
            targetKey.Trim();

        if (normalizedTargetKey.Length < 5 ||
            normalizedTargetKey[0] != 't' ||
            normalizedTargetKey
                .AsSpan(1)
                .IndexOfAnyExceptInRange(
                    '0',
                    '9') >= 0)
        {
            throw new ArgumentException(
                "A visual target key must use the internal tNNNN numeric format.",
                nameof(targetKey));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The visual target kind is not defined.");
        }

        if (start < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(start),
                start,
                "A visual target cannot start before the source begins.");
        }

        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                end,
                "A visual target must end after it starts.");
        }

        if (effectiveDisplayWidth <= 0 ||
            effectiveDisplayHeight <= 0 ||
            (effectiveDisplayWidth & 1) != 0 ||
            (effectiveDisplayHeight & 1) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectiveDisplayWidth),
                "Effective-display dimensions must be positive even values.");
        }

        if (kind == VisualEvidenceTargetKind.FullFrame)
        {
            if (start != TimeSpan.Zero)
            {
                throw new ArgumentException(
                    "A full-frame target must start at source zero.",
                    nameof(start));
            }

            if (intervalIndex is not null ||
                regionId is not null ||
                role is not null ||
                traits is not null ||
                requestedRectangle is not null ||
                actualPixelCrop is not null ||
                geometryConfidence is not null ||
                roleConfidence is not null ||
                geometrySource is not null ||
                roleSource is not null)
            {
                throw new ArgumentException(
                    "A full-frame target cannot contain composition-region geometry or provenance.");
            }
        }
        else
        {
            ValidateCompositionValues(
                intervalIndex,
                regionId,
                role,
                traits,
                requestedRectangle,
                actualPixelCrop,
                geometryConfidence,
                roleConfidence,
                geometrySource,
                roleSource,
                effectiveDisplayWidth,
                effectiveDisplayHeight);
        }

        TargetKey = normalizedTargetKey;
        Kind = kind;
        Start = start;
        End = end;
        EffectiveDisplayWidth = effectiveDisplayWidth;
        EffectiveDisplayHeight = effectiveDisplayHeight;
        IntervalIndex = intervalIndex;
        RegionId = regionId?.Trim();
        Role = role;
        Traits = traits;
        RequestedRectangle = requestedRectangle;
        ActualPixelCrop = actualPixelCrop;
        GeometryConfidence = geometryConfidence;
        RoleConfidence = roleConfidence;
        GeometrySource = geometrySource;
        RoleSource = roleSource;
    }

    public string TargetKey { get; }

    public VisualEvidenceTargetKind Kind { get; }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public TimeSpan Duration =>
        End - Start;

    public int EffectiveDisplayWidth { get; }

    public int EffectiveDisplayHeight { get; }

    public int? IntervalIndex { get; }

    public string? RegionId { get; }

    public CompositionRegionRole? Role { get; }

    public CompositionRegionTraits? Traits { get; }

    public NormalizedRectangle? RequestedRectangle { get; }

    public PixelRectangle? ActualPixelCrop { get; }

    public CompositionConfidence? GeometryConfidence { get; }

    public CompositionConfidence? RoleConfidence { get; }

    public CompositionValueSource? GeometrySource { get; }

    public CompositionValueSource? RoleSource { get; }

    private static void ValidateCompositionValues(
        int? intervalIndex,
        string? regionId,
        CompositionRegionRole? role,
        CompositionRegionTraits? traits,
        NormalizedRectangle? requestedRectangle,
        PixelRectangle? actualPixelCrop,
        CompositionConfidence? geometryConfidence,
        CompositionConfidence? roleConfidence,
        CompositionValueSource? geometrySource,
        CompositionValueSource? roleSource,
        int frameWidth,
        int frameHeight)
    {
        if (intervalIndex is null or < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalIndex),
                intervalIndex,
                "A composition target requires a non-negative interval index.");
        }

        if (string.IsNullOrWhiteSpace(regionId))
        {
            throw new ArgumentException(
                "A composition target requires a region identifier.",
                nameof(regionId));
        }

        if (role is null ||
            !Enum.IsDefined(role.Value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(role),
                role,
                "A composition target requires a defined role.");
        }

        if (traits is null ||
            requestedRectangle is null ||
            actualPixelCrop is null ||
            geometryConfidence is null ||
            roleConfidence is null ||
            geometrySource is null ||
            roleSource is null)
        {
            throw new ArgumentException(
                "A composition target requires complete geometry, role, and provenance values.");
        }

        PixelRectangle crop =
            actualPixelCrop.Value;

        if (crop.Right > frameWidth ||
            crop.Bottom > frameHeight ||
            (crop.X & 1) != 0 ||
            (crop.Y & 1) != 0 ||
            (crop.Width & 1) != 0 ||
            (crop.Height & 1) != 0)
        {
            throw new ArgumentException(
                "A composition target crop must be bounded and even.",
                nameof(actualPixelCrop));
        }

        if (crop.X >
                requestedRectangle.X *
                frameWidth ||
            crop.Y >
                requestedRectangle.Y *
                frameHeight ||
            crop.Right <
                requestedRectangle.Right *
                frameWidth ||
            crop.Bottom <
                requestedRectangle.Bottom *
                frameHeight)
        {
            throw new ArgumentException(
                "A composition target crop must contain its requested normalized region.",
                nameof(actualPixelCrop));
        }
    }
}
