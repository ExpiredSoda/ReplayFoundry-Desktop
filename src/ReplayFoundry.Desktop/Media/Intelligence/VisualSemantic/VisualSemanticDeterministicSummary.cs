using System.Collections.ObjectModel;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public sealed record VisualSemanticDeterministicSummaryInput(
    TimeSpan CandidateDuration,
    int SceneBoundaryCount,
    int GameplayActivityBurstCount,
    int AudioNoveltyEventCount,
    int PresenterSupportEventCount,
    VisualSemanticIntegrityStatus IntegrityStatus,
    TimeSpan? EventNeighborhoodStart,
    TimeSpan? EventNeighborhoodPeak,
    TimeSpan? EventNeighborhoodEnd,
    MomentOutputKind Mode,
    IReadOnlyList<CompositionRegionRole> ConfirmedRegionRoles);

public sealed class VisualSemanticDeterministicSummary
{
    private readonly ReadOnlyCollection<CompositionRegionRole>
        _confirmedRegionRoles;

    internal VisualSemanticDeterministicSummary(
        VisualSemanticDeterministicSummaryInput input,
        IReadOnlyList<CompositionRegionRole> confirmedRegionRoles)
    {
        CandidateDuration = input.CandidateDuration;
        SceneBoundaryCount = input.SceneBoundaryCount;
        GameplayActivityBurstCount =
            input.GameplayActivityBurstCount;
        AudioNoveltyEventCount = input.AudioNoveltyEventCount;
        PresenterSupportEventCount =
            input.PresenterSupportEventCount;
        IntegrityStatus = input.IntegrityStatus;
        EventNeighborhoodStart = input.EventNeighborhoodStart;
        EventNeighborhoodPeak = input.EventNeighborhoodPeak;
        EventNeighborhoodEnd = input.EventNeighborhoodEnd;
        Mode = input.Mode;
        _confirmedRegionRoles =
            Array.AsReadOnly(confirmedRegionRoles.ToArray());
    }

    public TimeSpan CandidateDuration { get; }

    public int SceneBoundaryCount { get; }

    public int GameplayActivityBurstCount { get; }

    public int AudioNoveltyEventCount { get; }

    public int PresenterSupportEventCount { get; }

    public VisualSemanticIntegrityStatus IntegrityStatus { get; }

    public TimeSpan? EventNeighborhoodStart { get; }

    public TimeSpan? EventNeighborhoodPeak { get; }

    public TimeSpan? EventNeighborhoodEnd { get; }

    public MomentOutputKind Mode { get; }

    public IReadOnlyList<CompositionRegionRole> ConfirmedRegionRoles =>
        _confirmedRegionRoles;
}

public static class VisualSemanticDeterministicSummaryBuilder
{
    public static VisualSemanticDeterministicSummary Build(
        VisualSemanticDeterministicSummaryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.CandidateDuration <= TimeSpan.Zero ||
            input.SceneBoundaryCount < 0 ||
            input.GameplayActivityBurstCount < 0 ||
            input.AudioNoveltyEventCount < 0 ||
            input.PresenterSupportEventCount < 0 ||
            !Enum.IsDefined(input.IntegrityStatus) ||
            !Enum.IsDefined(input.Mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                "Neutral deterministic summary values must be finite, non-negative, and defined.");
        }

        bool allNeighborhoodValues =
            input.EventNeighborhoodStart.HasValue &&
            input.EventNeighborhoodPeak.HasValue &&
            input.EventNeighborhoodEnd.HasValue;
        bool noNeighborhoodValues =
            !input.EventNeighborhoodStart.HasValue &&
            !input.EventNeighborhoodPeak.HasValue &&
            !input.EventNeighborhoodEnd.HasValue;

        if (!allNeighborhoodValues &&
            !noNeighborhoodValues)
        {
            throw new ArgumentException(
                "A neutral event neighborhood must be complete or unavailable.",
                nameof(input));
        }

        if (allNeighborhoodValues &&
            (
                input.EventNeighborhoodStart < TimeSpan.Zero ||
                input.EventNeighborhoodPeak <
                    input.EventNeighborhoodStart ||
                input.EventNeighborhoodEnd <
                    input.EventNeighborhoodPeak
            ))
        {
            throw new ArgumentException(
                "Neutral event-neighborhood times must remain ordered.",
                nameof(input));
        }

        ArgumentNullException.ThrowIfNull(input.ConfirmedRegionRoles);
        CompositionRegionRole[] roles =
            input.ConfirmedRegionRoles
                .Distinct()
                .OrderBy(static value => value)
                .ToArray();

        if (roles.Any(
                static value =>
                    value is not (
                        CompositionRegionRole.Gameplay or
                        CompositionRegionRole.Presenter
                    )))
        {
            throw new ArgumentException(
                "Neutral summary roles may include only confirmed Gameplay or Presenter regions.",
                nameof(input));
        }

        return new VisualSemanticDeterministicSummary(input, roles);
    }
}
