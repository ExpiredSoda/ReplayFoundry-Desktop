using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class MomentComponentWeightProfile
{
    private readonly ReadOnlyDictionary<MomentScoreComponentCode, double>
        _weights;

    public MomentComponentWeightProfile(
        string version,
        IEnumerable<KeyValuePair<MomentScoreComponentCode, double>> weights)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException(
                "A component-weight profile requires a version.",
                nameof(version));
        }

        ArgumentNullException.ThrowIfNull(weights);

        KeyValuePair<MomentScoreComponentCode, double>[] snapshot =
            weights.ToArray();

        if (snapshot.Any(
                static item =>
                    !Enum.IsDefined(item.Key)))
        {
            throw new ArgumentException(
                "Component-weight codes must be defined.",
                nameof(weights));
        }

        if (snapshot
            .GroupBy(static item => item.Key)
            .Any(static group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Component weights cannot contain duplicate codes.",
                nameof(weights));
        }

        if (snapshot.Any(
                static item =>
                    !double.IsFinite(item.Value) ||
                    item.Value is < -100 or > 100))
        {
            throw new ArgumentOutOfRangeException(
                nameof(weights),
                "Component weights must be finite values from -100 through 100.");
        }

        MomentScoreComponentCode[] required =
            Enum.GetValues<MomentScoreComponentCode>();

        if (!required.All(
                code =>
                    snapshot.Any(item => item.Key == code)))
        {
            throw new ArgumentException(
                "A component-weight profile must define every score component.",
                nameof(weights));
        }

        Version = version.Trim();
        _weights =
            new ReadOnlyDictionary<MomentScoreComponentCode, double>(
                snapshot.ToDictionary(
                    static item => item.Key,
                    static item => item.Value));
    }

    public string Version { get; }

    public IReadOnlyDictionary<MomentScoreComponentCode, double>
        Weights =>
        _weights;

    public double this[MomentScoreComponentCode code] =>
        _weights[code];

    public static MomentComponentWeightProfile Create(
        MomentContentEmphasis emphasis) =>
        Create(
            emphasis,
            MomentOutputKind.StandaloneClip);

    public static MomentComponentWeightProfile Create(
        MomentContentEmphasis emphasis,
        MomentOutputKind outputKind)
    {
        if (!Enum.IsDefined(emphasis))
        {
            throw new ArgumentOutOfRangeException(nameof(emphasis));
        }

        if (!Enum.IsDefined(outputKind))
        {
            throw new ArgumentOutOfRangeException(nameof(outputKind));
        }

        return emphasis switch
        {
            MomentContentEmphasis.GameplayFocused =>
                Build(
                    $"1.4-gameplay-{ModeName(outputKind)}",
                    outputKind,
                    [30, 22, 14, 11, 5, 4, 3, 1, 8, 3, 2, 1, 3]),
            MomentContentEmphasis.Balanced =>
                Build(
                    $"1.4-balanced-{ModeName(outputKind)}",
                    outputKind,
                    [28, 20, 12, 9, 4, 5, 3, 4, 9, 3, 2, 1, 3]),
            MomentContentEmphasis.CommentaryFocused =>
                Build(
                    $"1.4-commentary-{ModeName(outputKind)}",
                    outputKind,
                    [16, 13, 7, 6, 2, 11, 4, 12, 11, 2, 1, 1, 2]),
            _ => throw new ArgumentOutOfRangeException(nameof(emphasis)),
        };
    }

    private static MomentComponentWeightProfile Build(
        string version,
        MomentOutputKind outputKind,
        IReadOnlyList<double> positive)
    {
        MomentScoreComponentCode[] positiveCodes =
        [
            MomentScoreComponentCode.GameplayProminence,
            MomentScoreComponentCode.GameplayOnset,
            MomentScoreComponentCode.GameplayBurstIntegration,
            MomentScoreComponentCode.GameplaySceneChange,
            MomentScoreComponentCode.GameplaySceneDensity,
            MomentScoreComponentCode.AudioNovelty,
            MomentScoreComponentCode.AudioReentry,
            MomentScoreComponentCode.PresenterGatedSupport,
            MomentScoreComponentCode.MultiSignalOnsetAgreement,
            MomentScoreComponentCode.DurationFit,
            MomentScoreComponentCode.ClusterCoherence,
            MomentScoreComponentCode.VisualContextChange,
            MomentScoreComponentCode.PayoffSupport,
        ];

        var weights =
            positiveCodes
                .Select(
                    (code, index) =>
                        new KeyValuePair<MomentScoreComponentCode, double>(
                            code,
                            positive[index]))
                .ToList();

        weights.AddRange(
        [
            new(MomentScoreComponentCode.ContinuousActivityPenalty, -20),
            new(MomentScoreComponentCode.FullFrameBlackPenalty, -35),
            new(MomentScoreComponentCode.FullFrameFreezePenalty, -35),
            new(MomentScoreComponentCode.GameplayLowInformationPenalty, -10),
            new(MomentScoreComponentCode.SourceEdgePenalty, -4),
            new(MomentScoreComponentCode.NeighborhoodRedundancyPenalty, -12),
            new(MomentScoreComponentCode.EpisodePeakStrength, 6),
            new(MomentScoreComponentCode.EpisodeIntegratedStrength, 7),
            new(MomentScoreComponentCode.EpisodeOnsetStrength, 5),
            new(MomentScoreComponentCode.EpisodeRecoverySupport, 4),
            new(MomentScoreComponentCode.EpisodeCohesion, 3),
            new(
                MomentScoreComponentCode.MontageRepresentativeCoverage,
                outputKind == MomentOutputKind.MontageSegment ? 8 : 0),
            new(MomentScoreComponentCode.MontageEpisodeRedundancyPenalty, -10),
            new(MomentScoreComponentCode.EpisodeDistinctiveness, 10),
            new(MomentScoreComponentCode.BaselineCoreSeparation, 4),
            new(MomentScoreComponentCode.CoreRecoverySeparation, 4),
            new(MomentScoreComponentCode.IndependentFamilyAgreement, 5),
            new(MomentScoreComponentCode.CorrelatedVisualSupportPenalty, -10),
            new(MomentScoreComponentCode.SingleFamilyDominancePenalty, -18),
            new(MomentScoreComponentCode.ContinuousUniformityPenalty, -24),
            new(
                MomentScoreComponentCode.StandaloneEpisodeCompleteness,
                outputKind == MomentOutputKind.StandaloneClip ? 8 : 0),
            new(
                MomentScoreComponentCode.MontageRepresentativeDensity,
                outputKind == MomentOutputKind.MontageSegment ? 16 : 0),
        ]);

        return new MomentComponentWeightProfile(
            version,
            weights);
    }

    private static string ModeName(
        MomentOutputKind outputKind) =>
        outputKind == MomentOutputKind.StandaloneClip
            ? "standalone"
            : "montage";
}
