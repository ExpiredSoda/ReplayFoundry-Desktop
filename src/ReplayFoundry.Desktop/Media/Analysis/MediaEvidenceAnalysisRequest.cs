using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Media.Analysis;

/// <summary>
/// Immutable input for one deterministic evidence-analysis run.
/// </summary>
public sealed class MediaEvidenceAnalysisRequest
{
    private static readonly CompositionRegionRole[]
        DefaultCompositionRoles =
        [
            CompositionRegionRole.Gameplay,
            CompositionRegionRole.Presenter,
        ];

    private readonly ReadOnlyCollection<CompositionRegionRole>
        _includedRegionRoles;

    private MediaEvidenceAnalysisRequest(
        MediaProbeResult media,
        CompositionPlan? composition,
        MediaEvidenceAnalysisOptions options,
        IEnumerable<CompositionRegionRole> includedRegionRoles)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(includedRegionRoles);

        CompositionRegionRole[] roleSnapshot =
            includedRegionRoles.ToArray();

        foreach (CompositionRegionRole role in roleSnapshot)
        {
            if (!Enum.IsDefined(role))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(includedRegionRoles),
                    role,
                    "Included composition roles must be defined values.");
            }
        }

        CompositionRegionRole? duplicateRole =
            roleSnapshot
                .GroupBy(static role => role)
                .FirstOrDefault(
                    static group =>
                        group.Count() > 1)
                ?.Key;

        if (duplicateRole is CompositionRegionRole duplicate)
        {
            throw new ArgumentException(
                $"Included composition role '{duplicate}' is duplicated.",
                nameof(includedRegionRoles));
        }

        if (composition is null)
        {
            if (roleSnapshot.Length != 0)
            {
                throw new ArgumentException(
                    "A full-frame-only request cannot include composition roles.",
                    nameof(includedRegionRoles));
            }
        }
        else
        {
            ValidateComposition(
                media,
                composition);
        }

        Media = media;
        Composition = composition;
        Options = options;
        _includedRegionRoles =
            Array.AsReadOnly(
                roleSnapshot);
    }

    public MediaProbeResult Media { get; }

    public CompositionPlan? Composition { get; }

    public MediaEvidenceAnalysisOptions Options { get; }

    public IReadOnlyList<CompositionRegionRole> IncludedRegionRoles =>
        _includedRegionRoles;

    public bool IsCompositionAware =>
        Composition is not null;

    public static MediaEvidenceAnalysisRequest CreateFullFrameOnly(
        MediaProbeResult media,
        MediaEvidenceAnalysisOptions options)
    {
        return new MediaEvidenceAnalysisRequest(
            media,
            composition: null,
            options,
            []);
    }

    public static MediaEvidenceAnalysisRequest CreateCompositionAware(
        MediaProbeResult media,
        CompositionPlan composition,
        MediaEvidenceAnalysisOptions options)
    {
        return new MediaEvidenceAnalysisRequest(
            media,
            composition,
            options,
            DefaultCompositionRoles);
    }

    public static MediaEvidenceAnalysisRequest CreateCompositionAware(
        MediaProbeResult media,
        CompositionPlan composition,
        MediaEvidenceAnalysisOptions options,
        IEnumerable<CompositionRegionRole> includedRegionRoles)
    {
        ArgumentNullException.ThrowIfNull(includedRegionRoles);

        return new MediaEvidenceAnalysisRequest(
            media,
            composition,
            options,
            includedRegionRoles);
    }

    private static void ValidateComposition(
        MediaProbeResult media,
        CompositionPlan composition)
    {
        ArgumentNullException.ThrowIfNull(composition);

        if (!string.Equals(
                media.FullPath,
                composition.SourcePath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The composition plan source path must match the inspected media path.",
                nameof(composition));
        }

        if (media.Duration !=
            composition.SourceDuration)
        {
            throw new ArgumentException(
                "The composition plan duration must exactly match the inspected media duration.",
                nameof(composition));
        }

        if (composition.CoordinateSpace !=
            CompositionCoordinateSpace
                .EffectiveDisplayNormalizedBeforeCrop)
        {
            throw new ArgumentException(
                "Evidence analysis requires effective-display normalized composition coordinates.",
                nameof(composition));
        }

        if (!composition.HasGameplay)
        {
            throw new ArgumentException(
                "Composition-aware evidence analysis requires at least one Gameplay region.",
                nameof(composition));
        }
    }
}
