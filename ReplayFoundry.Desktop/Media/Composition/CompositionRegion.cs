using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ReplayFoundry.Desktop.Media.Composition;

/// <summary>
/// A semantic region within a composition layout interval.
/// </summary>
public sealed class CompositionRegion
{
    private const CompositionRegionTraits AllDefinedTraits =
        CompositionRegionTraits.Static |
        CompositionRegionTraits.Dynamic |
        CompositionRegionTraits.Transient |
        CompositionRegionTraits.Occluding;

    private readonly ReadOnlyCollection<CompositionWarning> _warnings;

    public CompositionRegion(
        string id,
        NormalizedRectangle geometry,
        CompositionRegionRole role,
        CompositionRegionTraits traits,
        CompositionConfidence geometryConfidence,
        CompositionConfidence roleConfidence,
        CompositionValueSource geometrySource,
        CompositionValueSource roleSource,
        IEnumerable<CompositionWarning>? warnings = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "A composition region requires an identifier.",
                nameof(id));
        }

        ArgumentNullException.ThrowIfNull(geometry);

        ValidateEnum(
            role,
            nameof(role));

        ValidateEnum(
            geometrySource,
            nameof(geometrySource));

        ValidateEnum(
            roleSource,
            nameof(roleSource));

        if ((traits & ~AllDefinedTraits) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(traits),
                traits,
                "The composition region contains undefined behavior traits.");
        }

        if (traits.HasFlag(
                CompositionRegionTraits.Static) &&
            traits.HasFlag(
                CompositionRegionTraits.Dynamic))
        {
            throw new ArgumentException(
                "A composition region cannot be both Static and Dynamic.",
                nameof(traits));
        }

        if (geometrySource ==
            CompositionValueSource.NotAvailable)
        {
            throw new ArgumentException(
                "Available region geometry must identify its provenance.",
                nameof(geometrySource));
        }

        ValidateRoleAvailability(
            role,
            roleSource,
            roleConfidence);

        ValidateAuthoritativeConfidence(
            geometrySource,
            geometryConfidence,
            nameof(geometryConfidence));

        ValidateAuthoritativeConfidence(
            roleSource,
            roleConfidence,
            nameof(roleConfidence));

        CompositionWarning[] warningSnapshot =
            warnings?.ToArray() ??
            [];

        if (warningSnapshot.Any(
                static warning =>
                    warning is null))
        {
            throw new ArgumentException(
                "Composition region warnings cannot contain null entries.",
                nameof(warnings));
        }

        string normalizedId =
            id.Trim();

        if (warningSnapshot.Any(
                warning =>
                    warning.RegionId is not null &&
                    !string.Equals(
                        warning.RegionId,
                        normalizedId,
                        StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "A region warning cannot reference a different composition region.",
                nameof(warnings));
        }

        Id = normalizedId;
        Geometry = geometry;
        Role = role;
        Traits = traits;
        GeometryConfidence = geometryConfidence;
        RoleConfidence = roleConfidence;
        GeometrySource = geometrySource;
        RoleSource = roleSource;
        _warnings =
            Array.AsReadOnly(
                warningSnapshot);
    }

    public string Id { get; }

    public NormalizedRectangle Geometry { get; }

    public CompositionRegionRole Role { get; }

    public CompositionRegionTraits Traits { get; }

    public CompositionConfidence GeometryConfidence { get; }

    public CompositionConfidence RoleConfidence { get; }

    public CompositionValueSource GeometrySource { get; }

    public CompositionValueSource RoleSource { get; }

    public IReadOnlyList<CompositionWarning> Warnings =>
        _warnings;

    public bool IsGeometryUserConfirmed =>
        GeometrySource ==
        CompositionValueSource.UserConfirmed;

    public bool IsRoleUserConfirmed =>
        RoleSource ==
        CompositionValueSource.UserConfirmed;

    public bool IsFullyUserConfirmed =>
        IsGeometryUserConfirmed &&
        IsRoleUserConfirmed;

    private static void ValidateRoleAvailability(
        CompositionRegionRole role,
        CompositionValueSource roleSource,
        CompositionConfidence roleConfidence)
    {
        if (roleSource ==
            CompositionValueSource.NotAvailable)
        {
            if (role != CompositionRegionRole.Unknown)
            {
                throw new ArgumentException(
                    "A role without provenance must remain Unknown.",
                    nameof(roleSource));
            }

            if (roleConfidence != CompositionConfidence.None)
            {
                throw new ArgumentException(
                    "A role without evidence must use zero confidence.",
                    nameof(roleConfidence));
            }

            return;
        }

        if (role == CompositionRegionRole.Unknown &&
            roleSource == CompositionValueSource.UserConfirmed)
        {
            throw new ArgumentException(
                "An Unknown role cannot be represented as user-confirmed.",
                nameof(roleSource));
        }
    }

    private static void ValidateAuthoritativeConfidence(
        CompositionValueSource source,
        CompositionConfidence confidence,
        string parameterName)
    {
        if (source ==
                CompositionValueSource.UserConfirmed &&
            !confidence.IsCertain)
        {
            throw new ArgumentException(
                "A user-confirmed composition value must use authoritative confidence.",
                parameterName);
        }
    }

    private static void ValidateEnum<TEnum>(
        TEnum value,
        string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(
                typeof(TEnum),
                value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "The composition value is not defined.");
        }
    }
}
