using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public sealed record VisualSemanticCompositionRegion
{
    public VisualSemanticCompositionRegion(
        string id,
        CompositionRegionRole role,
        NormalizedRectangle geometry,
        CompositionValueSource geometrySource,
        CompositionValueSource roleSource)
    {
        if (!Enum.IsDefined(role) ||
            role is not (
                CompositionRegionRole.Gameplay or
                CompositionRegionRole.Presenter
            ) ||
            !Enum.IsDefined(geometrySource) ||
            !Enum.IsDefined(roleSource) ||
            geometrySource == CompositionValueSource.NotAvailable ||
            roleSource == CompositionValueSource.NotAvailable)
        {
            throw new ArgumentException(
                "Visual-semantic regions require available Gameplay or Presenter geometry and provenance.");
        }

        ArgumentNullException.ThrowIfNull(geometry);
        Id = VisualSemanticContractText.Required(
            id,
            nameof(id),
            128);
        Role = role;
        Geometry = geometry;
        GeometrySource = geometrySource;
        RoleSource = roleSource;
    }

    public string Id { get; }

    public CompositionRegionRole Role { get; }

    public NormalizedRectangle Geometry { get; }

    public CompositionValueSource GeometrySource { get; }

    public CompositionValueSource RoleSource { get; }
}

public sealed class VisualSemanticCompositionMetadata
{
    private readonly ReadOnlyCollection<VisualSemanticCompositionRegion>
        _regions;

    public VisualSemanticCompositionMetadata(
        string layoutDescription,
        CompositionCoordinateSpace coordinateSpace,
        IEnumerable<VisualSemanticCompositionRegion>? regions = null)
    {
        if (!Enum.IsDefined(coordinateSpace) ||
            coordinateSpace !=
                CompositionCoordinateSpace
                    .EffectiveDisplayNormalizedBeforeCrop)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coordinateSpace),
                coordinateSpace,
                "Visual-semantic geometry must use effective-display normalized coordinates before crop.");
        }

        VisualSemanticCompositionRegion[] snapshot =
            regions?
                .OrderBy(static value => value.Role)
                .ThenBy(static value => value.Id, StringComparer.Ordinal)
                .ToArray() ??
            [];

        if (snapshot.Any(static value => value is null) ||
            snapshot
                .GroupBy(static value => value.Id, StringComparer.OrdinalIgnoreCase)
                .Any(static group => group.Count() > 1) ||
            snapshot
                .GroupBy(static value => value.Role)
                .Any(static group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Visual-semantic composition regions must be non-null and unique by ID and role.",
                nameof(regions));
        }

        LayoutDescription = VisualSemanticContractText.Required(
            layoutDescription,
            nameof(layoutDescription),
            128);
        CoordinateSpace = coordinateSpace;
        _regions = Array.AsReadOnly(snapshot);
    }

    public string LayoutDescription { get; }

    public CompositionCoordinateSpace CoordinateSpace { get; }

    public IReadOnlyList<VisualSemanticCompositionRegion> Regions =>
        _regions;
}
