using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Moments;

public enum MomentDiagnosticGroupCode
{
    VisualEventEvidence,
    SceneEvidence,
    AudioSupport,
    PresenterSupport,
    TemporalShape,
    Integrity,
    ContextAndBoundaries,
}

public sealed class MomentScoreDiagnosticGroup
{
    private readonly ReadOnlyCollection<MomentScoreComponent>
        _components;

    public MomentScoreDiagnosticGroup(
        MomentDiagnosticGroupCode code,
        IEnumerable<MomentScoreComponent> components)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        ArgumentNullException.ThrowIfNull(components);

        MomentScoreComponent[] snapshot =
            components.ToArray();

        if (snapshot.Any(static component => component is null))
        {
            throw new ArgumentException(
                "Diagnostic groups cannot contain null components.",
                nameof(components));
        }

        Code = code;
        _components = Array.AsReadOnly(snapshot);
        SignedContribution =
            snapshot.Length == 0
                ? null
                : snapshot.Sum(
                    static component =>
                        component.SignedContribution);
    }

    public MomentDiagnosticGroupCode Code { get; }

    public bool IsAvailable =>
        _components.Count > 0;

    public double? SignedContribution { get; }

    public IReadOnlyList<MomentScoreComponent> Components =>
        _components;
}

public sealed class MomentScoreGroupedDiagnostics
{
    private readonly ReadOnlyCollection<MomentScoreDiagnosticGroup>
        _groups;

    internal MomentScoreGroupedDiagnostics(
        MomentScore score,
        IEnumerable<MomentScoreDiagnosticGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(groups);

        MomentScoreDiagnosticGroup[] snapshot =
            groups.ToArray();
        MomentScoreComponent[] groupedComponents =
            snapshot
                .SelectMany(
                    static group =>
                        group.Components)
                .ToArray();

        if (snapshot.Length !=
                Enum.GetValues<MomentDiagnosticGroupCode>()
                    .Length ||
            snapshot.GroupBy(static group => group.Code)
                .Any(static group => group.Count() > 1) ||
            groupedComponents.Length !=
                score.Components.Count ||
            groupedComponents
                .Select(static component => component.Code)
                .OrderBy(static code => code)
                .SequenceEqual(
                    score.Components
                        .Select(
                            static component =>
                                component.Code)
                        .OrderBy(static code => code)) is false)
        {
            throw new ArgumentException(
                "Grouped diagnostics must project every score component exactly once.",
                nameof(groups));
        }

        double groupedTotal =
            snapshot.Sum(
                static group =>
                    group.SignedContribution ?? 0);

        if (Math.Abs(
                groupedTotal -
                score.RawComponentTotal) >
            0.000000001)
        {
            throw new ArgumentException(
                "Grouped diagnostics must exactly reconcile to the authoritative score.",
                nameof(groups));
        }

        RawComponentTotal = score.RawComponentTotal;
        _groups = Array.AsReadOnly(snapshot);
    }

    public IReadOnlyList<MomentScoreDiagnosticGroup> Groups =>
        _groups;

    public double RawComponentTotal { get; }
}

public static class MomentScoreGroupedDiagnosticProjector
{
    public static MomentScoreGroupedDiagnostics Project(
        MomentScore score)
    {
        ArgumentNullException.ThrowIfNull(score);

        MomentScoreDiagnosticGroup[] groups =
            Enum.GetValues<MomentDiagnosticGroupCode>()
                .Select(
                    code =>
                        new MomentScoreDiagnosticGroup(
                            code,
                            score.Components.Where(
                                component =>
                                    GroupFor(
                                        component.Code) ==
                                    code)))
                .ToArray();

        return new MomentScoreGroupedDiagnostics(
            score,
            groups);
    }

    internal static MomentDiagnosticGroupCode GroupFor(
        MomentScoreComponentCode code) =>
        code switch
        {
            MomentScoreComponentCode.GameplayProminence or
            MomentScoreComponentCode.GameplayOnset or
            MomentScoreComponentCode.GameplayBurstIntegration or
            MomentScoreComponentCode.VisualContextChange or
            MomentScoreComponentCode.EpisodePeakStrength or
            MomentScoreComponentCode.EpisodeIntegratedStrength or
            MomentScoreComponentCode.EpisodeDistinctiveness or
            MomentScoreComponentCode.BaselineCoreSeparation or
            MomentScoreComponentCode.IndependentFamilyAgreement or
            MomentScoreComponentCode.SingleFamilyDominancePenalty or
            MomentScoreComponentCode.ContinuousUniformityPenalty =>
                MomentDiagnosticGroupCode
                    .VisualEventEvidence,

            MomentScoreComponentCode.GameplaySceneChange or
            MomentScoreComponentCode.GameplaySceneDensity or
            MomentScoreComponentCode.ClusterCoherence =>
                MomentDiagnosticGroupCode.SceneEvidence,

            MomentScoreComponentCode.AudioNovelty or
            MomentScoreComponentCode.AudioReentry =>
                MomentDiagnosticGroupCode.AudioSupport,

            MomentScoreComponentCode.PresenterGatedSupport or
            MomentScoreComponentCode.CorrelatedVisualSupportPenalty =>
                MomentDiagnosticGroupCode.PresenterSupport,

            MomentScoreComponentCode.MultiSignalOnsetAgreement or
            MomentScoreComponentCode.ContinuousActivityPenalty or
            MomentScoreComponentCode.EpisodeOnsetStrength or
            MomentScoreComponentCode.EpisodeRecoverySupport or
            MomentScoreComponentCode.EpisodeCohesion or
            MomentScoreComponentCode.CoreRecoverySeparation or
            MomentScoreComponentCode.MontageRepresentativeCoverage or
            MomentScoreComponentCode.MontageEpisodeRedundancyPenalty or
            MomentScoreComponentCode.MontageRepresentativeDensity =>
                MomentDiagnosticGroupCode.TemporalShape,

            MomentScoreComponentCode.FullFrameBlackPenalty or
            MomentScoreComponentCode.FullFrameFreezePenalty or
            MomentScoreComponentCode.GameplayLowInformationPenalty =>
                MomentDiagnosticGroupCode.Integrity,

            MomentScoreComponentCode.DurationFit or
            MomentScoreComponentCode.PayoffSupport or
            MomentScoreComponentCode.SourceEdgePenalty or
            MomentScoreComponentCode.NeighborhoodRedundancyPenalty or
            MomentScoreComponentCode.StandaloneEpisodeCompleteness =>
                MomentDiagnosticGroupCode
                    .ContextAndBoundaries,

            _ => throw new ArgumentOutOfRangeException(
                nameof(code),
                code,
                "Every deterministic score component must map to exactly one diagnostic group."),
        };
}
