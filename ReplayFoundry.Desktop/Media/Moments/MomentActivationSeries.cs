using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class MomentActivationComponent
{
    private readonly ReadOnlyCollection<MomentEvidenceReference> _references;

    public MomentActivationComponent(
        MomentActivationComponentCode code,
        double? rawValue,
        double? normalizedValue,
        double signedWeight,
        IEnumerable<MomentEvidenceReference>? references = null)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        if (rawValue is not null && !double.IsFinite(rawValue.Value) ||
            normalizedValue is not null &&
            (!double.IsFinite(normalizedValue.Value) || normalizedValue.Value is < 0 or > 1) ||
            !double.IsFinite(signedWeight) || signedWeight is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(normalizedValue));
        }

        MomentEvidenceReference[] snapshot = references?.ToArray() ?? [];
        if (snapshot.Any(static item => item is null))
        {
            throw new ArgumentException("Activation references cannot contain null entries.", nameof(references));
        }

        Code = code;
        RawValue = rawValue;
        NormalizedValue = normalizedValue;
        SignedWeight = signedWeight;
        SignedContribution = normalizedValue is null ? 0 : normalizedValue.Value * signedWeight;
        _references = Array.AsReadOnly(snapshot);
    }

    public MomentActivationComponentCode Code { get; }
    public double? RawValue { get; }
    public double? NormalizedValue { get; }
    public bool IsAvailable => NormalizedValue is not null;
    public double SignedWeight { get; }
    public double SignedContribution { get; }
    public IReadOnlyList<MomentEvidenceReference> EvidenceReferences => _references;
}

public sealed class MomentActivationSample
{
    private readonly ReadOnlyCollection<MomentActivationComponent> _components;

    public MomentActivationSample(
        TimeSpan timestamp,
        IEnumerable<MomentActivationComponent> components,
        double rawCombinedActivation,
        double smoothedCombinedActivation,
        MomentActivationIntegrityState integrityState)
    {
        if (timestamp < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp));
        }

        ArgumentNullException.ThrowIfNull(components);
        MomentActivationComponent[] snapshot = components.ToArray();
        if (snapshot.Any(static item => item is null) ||
            snapshot.GroupBy(static item => item.Code).Any(static group => group.Count() > 1))
        {
            throw new ArgumentException("Activation components must be non-null and unique.", nameof(components));
        }

        ValidateRatio(rawCombinedActivation, nameof(rawCombinedActivation));
        ValidateRatio(smoothedCombinedActivation, nameof(smoothedCombinedActivation));
        if (!Enum.IsDefined(integrityState))
        {
            throw new ArgumentOutOfRangeException(nameof(integrityState));
        }

        Timestamp = timestamp;
        RawCombinedActivation = rawCombinedActivation;
        SmoothedCombinedActivation = smoothedCombinedActivation;
        IntegrityState = integrityState;
        _components = Array.AsReadOnly(snapshot);
    }

    public TimeSpan Timestamp { get; }
    public IReadOnlyList<MomentActivationComponent> Components => _components;
    public double RawCombinedActivation { get; }
    public double SmoothedCombinedActivation { get; }
    public MomentActivationIntegrityState IntegrityState { get; }

    internal MomentActivationSample WithSmoothed(double value) =>
        new(Timestamp, Components, RawCombinedActivation, value, IntegrityState);

    private static void ValidateRatio(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}

public sealed class MomentActivationCoverage
{
    public MomentActivationCoverage(
        TimeSpan start,
        TimeSpan end,
        TimeSpan cadence,
        int sampleCount,
        bool complete)
    {
        if (start < TimeSpan.Zero || end < start || cadence <= TimeSpan.Zero || sampleCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(end));
        }

        Start = start;
        End = end;
        Cadence = cadence;
        SampleCount = sampleCount;
        Complete = complete;
    }

    public TimeSpan Start { get; }
    public TimeSpan End { get; }
    public TimeSpan Cadence { get; }
    public int SampleCount { get; }
    public bool Complete { get; }
}

public sealed class MomentActivationSeries
{
    private readonly ReadOnlyCollection<MomentActivationSample> _samples;

    public MomentActivationSeries(
        IEnumerable<MomentActivationSample> samples,
        MomentActivationCoverage coverage,
        string policyVersion)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(coverage);
        if (string.IsNullOrWhiteSpace(policyVersion))
        {
            throw new ArgumentException("Activation policy version cannot be blank.", nameof(policyVersion));
        }

        MomentActivationSample[] snapshot = samples.OrderBy(static item => item.Timestamp).ToArray();
        if (snapshot.Any(static item => item is null) ||
            snapshot.Select(static item => item.Timestamp).Distinct().Count() != snapshot.Length ||
            snapshot.Any(item => item.Timestamp < coverage.Start || item.Timestamp > coverage.End) ||
            snapshot.Length != coverage.SampleCount)
        {
            throw new ArgumentException("Activation samples must be unique, ordered, and inside coverage.", nameof(samples));
        }

        _samples = Array.AsReadOnly(snapshot);
        Coverage = coverage;
        PolicyVersion = policyVersion.Trim();
    }

    public IReadOnlyList<MomentActivationSample> Samples => _samples;
    public MomentActivationCoverage Coverage { get; }
    public string PolicyVersion { get; }
}

internal static class MomentActivationCurveBuilder
{
    private static readonly IReadOnlyDictionary<MomentActivationComponentCode, double> Weights =
        new Dictionary<MomentActivationComponentCode, double>
        {
            [MomentActivationComponentCode.GameplayProminence] = 0.28,
            [MomentActivationComponentCode.GameplayOnset] = 0.18,
            [MomentActivationComponentCode.GameplayIntegratedBurst] = 0.14,
            [MomentActivationComponentCode.GameplaySceneSupport] = 0.10,
            [MomentActivationComponentCode.AudioNovelty] = 0.08,
            [MomentActivationComponentCode.AudioReentry] = 0.04,
            [MomentActivationComponentCode.PresenterGatedSupport] = 0.06,
            [MomentActivationComponentCode.VisualContextSupport] = 0.06,
            [MomentActivationComponentCode.ContinuousActivityPenalty] = -0.20,
            [MomentActivationComponentCode.GatedEventAnchorSupport] = 0,
            [MomentActivationComponentCode.CorrelatedVisualSupportPenalty] = -0.08,
        };

    public static MomentActivationSeries Build(
        MediaMomentFindingRequest request,
        NormalizedMomentSignals signals,
        IReadOnlyList<MomentAnchor> anchors,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(anchors);

        TimeSpan cadence = request.Evidence.Manifest.Options.VisualSignalSampleInterval;
        NormalizedVisualMomentSample[] gameplay = signals.Gameplay
            .GroupBy(static sample => sample.Sample.Timestamp)
            .Select(static group => group
                .OrderByDescending(static item => item.Context.NormalizedProminence)
                .ThenBy(static item => item.Sample.TargetKey, StringComparer.Ordinal)
                .First())
            .OrderBy(static sample => sample.Sample.Timestamp)
            .ToArray();
        var raw = new List<MomentActivationSample>(gameplay.Length);

        for (int index = 0; index < gameplay.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NormalizedVisualMomentSample sample = gameplay[index];
            TimeSpan timestamp = sample.Sample.Timestamp;
            ActivityBurst? gameplayBurst = FindNearestContaining(signals.GameplayBursts, timestamp);
            MomentAnchor? gatedAnchor = anchors
                .Where(item =>
                    (item.Timestamp - timestamp).Duration() <=
                    Max(request.Options.CrossSignalAgreementWindow, cadence))
                .OrderByDescending(static item => item.NormalizedStrength)
                .ThenBy(item => (item.Timestamp - timestamp).Duration())
                .ThenBy(static item => item.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            AttributedGameplaySceneBoundary? scene = signals.GameplayScenes
                .Where(item => (item.Boundary.Timestamp - timestamp).Duration() <= request.Options.CrossSignalAgreementWindow)
                .OrderByDescending(static item => item.Boundary.ScorePercent ?? 0)
                .ThenBy(item => (item.Boundary.Timestamp - timestamp).Duration())
                .FirstOrDefault();
            AudioNoveltyEvent? audio = signals.AudioNoveltyEvents
                .Where(item => (item.PeakTimestamp - timestamp).Duration() <= request.Options.CrossSignalAgreementWindow)
                .OrderByDescending(static item => item.NormalizedProminence)
                .ThenBy(item => (item.PeakTimestamp - timestamp).Duration())
                .FirstOrDefault();
            ActivityBurst? presenter = signals.PresenterBursts
                .Where(item => (item.PeakTimestamp - timestamp).Duration() <= request.Options.CrossSignalAgreementWindow)
                .OrderByDescending(static item => item.PeakProminence)
                .ThenBy(item => (item.PeakTimestamp - timestamp).Duration())
                .FirstOrDefault();

            double sceneSupport = scene?.Boundary.ScorePercent is null
                ? 0
                : Math.Clamp(scene.Boundary.ScorePercent.Value / 50d, 0, 1);
            double burstIntegration = gameplayBurst is null
                ? 0
                : Math.Clamp(gameplayBurst.IntegratedExcess / Math.Max(0.000001, gameplayBurst.Duration.TotalSeconds), 0, 1);
            double visualContext = index == 0
                ? 0
                : Math.Clamp(
                    Math.Max(
                        Math.Abs(sample.Sample.NormalizedMeanLuma - gameplay[index - 1].Sample.NormalizedMeanLuma),
                        Math.Abs(sample.Sample.NormalizedMeanSaturation - gameplay[index - 1].Sample.NormalizedMeanSaturation)) / 0.20,
                    0,
                    1);
            double occupancy = CalculateOccupancy(signals.Gameplay, timestamp, request.Options.CalibrationPolicy.ContinuousActivityPenaltyWindow);
            double noveltyProtection = Math.Max(sample.Context.NormalizedProminence, sample.Context.OnsetStrength);
            double continuousPenalty = Math.Clamp(
                Math.Max(0, occupancy - request.Options.CalibrationPolicy.ContinuousActivityOccupancyThreshold) /
                Math.Max(0.000001, 1 - request.Options.CalibrationPolicy.ContinuousActivityOccupancyThreshold) *
                Math.Pow(1 - noveltyProtection, 3),
                0,
                1);
            bool gameplayQualifies = sample.Context.NormalizedProminence >=
                request.Options.CalibrationPolicy.MinimumBurstProminence ||
                sceneSupport > 0;
            bool commentaryPair =
                request.Options.ContentEmphasis == MomentContentEmphasis.CommentaryFocused &&
                audio is not null;
            double presenterSupport = presenter is not null && (gameplayQualifies || commentaryPair)
                ? presenter.PeakProminence
                : 0;
            double correlatedVisualSupport =
                gameplayBurst is null || presenter is null
                    ? 0
                    : MomentVisualSupportCorrelation.Measure(
                        [gameplayBurst],
                        [presenter],
                        request.Options.CrossSignalAgreementWindow);
            double incrementalPresenterSupport =
                presenterSupport *
                (1 - correlatedVisualSupport);
            if (incrementalPresenterSupport <
                request.Options.DistinctivenessPolicy
                    .MinimumIncrementalPresenterProminence)
            {
                incrementalPresenterSupport = 0;
            }
            double audioSupport = audio is not null && (gameplayQualifies || commentaryPair)
                ? audio.NormalizedProminence
                : 0;

            var components = new[]
            {
                Component(MomentActivationComponentCode.GameplayProminence, sample.Context.NormalizedProminence, sample.Context.NormalizedProminence, VisualReference(sample)),
                Component(MomentActivationComponentCode.GameplayOnset, sample.Context.OnsetStrength, sample.Context.OnsetStrength, VisualReference(sample)),
                Component(MomentActivationComponentCode.GameplayIntegratedBurst, gameplayBurst?.IntegratedExcess, gameplayBurst is null ? null : burstIntegration, gameplayBurst?.EvidenceReferences),
                Component(MomentActivationComponentCode.GameplaySceneSupport, scene?.Boundary.ScorePercent, scene is null ? null : sceneSupport, scene is null ? null : SceneReference(scene)),
                Component(MomentActivationComponentCode.AudioNovelty, audio?.PeakLiftDb, audio is null ? null : audioSupport, audio?.EvidenceReferences),
                Component(MomentActivationComponentCode.AudioReentry, audio?.IsSilenceReentry == true ? 1 : audio is null ? null : 0, audio is null ? null : audio.IsSilenceReentry ? audioSupport : 0, audio?.EvidenceReferences),
                Component(MomentActivationComponentCode.PresenterGatedSupport, presenter?.PeakProminence, presenter is null ? null : incrementalPresenterSupport, presenter?.EvidenceReferences),
                Component(MomentActivationComponentCode.VisualContextSupport, visualContext, visualContext, VisualReference(sample)),
                Component(MomentActivationComponentCode.ContinuousActivityPenalty, occupancy, continuousPenalty, VisualReference(sample)),
                Component(
                    MomentActivationComponentCode.GatedEventAnchorSupport,
                    gatedAnchor?.RawFeatureValue,
                    gatedAnchor is null ? null : Math.Sqrt(gatedAnchor.NormalizedStrength),
                    gatedAnchor?.EvidenceReferences),
                Component(
                    MomentActivationComponentCode.CorrelatedVisualSupportPenalty,
                    correlatedVisualSupport,
                    correlatedVisualSupport,
                    presenter?.EvidenceReferences),
            };
            double strongestObservable = components
                .Where(static item =>
                    item.Code != MomentActivationComponentCode.ContinuousActivityPenalty &&
                    item.IsAvailable)
                .Select(static item => item.NormalizedValue!.Value)
                .DefaultIfEmpty(0)
                .Max();
            double gatedActivation = components
                .Single(static item => item.Code == MomentActivationComponentCode.GatedEventAnchorSupport)
                .NormalizedValue ?? 0;
            double combined = Math.Clamp(
                Math.Max(
                    Math.Max(
                        components.Sum(static item => item.SignedContribution),
                        strongestObservable * 0.40 - continuousPenalty * 0.20),
                    gatedActivation),
                0,
                1);
            raw.Add(new MomentActivationSample(timestamp, components, combined, combined, GetIntegrity(request, timestamp)));
        }

        MomentActivationSample[] smoothed = Smooth(raw, request.Options.EpisodePolicy.EpisodeSmoothingHalfWindow, cancellationToken);
        TimeSpan start = smoothed.Length == 0 ? TimeSpan.Zero : smoothed[0].Timestamp;
        TimeSpan end = smoothed.Length == 0 ? request.Media.Duration : smoothed[^1].Timestamp;
        bool complete = smoothed.Length > 0 &&
            start <= cadence &&
            request.Media.Duration - end <= cadence;
        return new MomentActivationSeries(
            smoothed,
            new MomentActivationCoverage(start, end, cadence, smoothed.Length, complete),
            request.Options.EpisodePolicy.Version);
    }

    private static MomentActivationComponent Component(
        MomentActivationComponentCode code,
        double? raw,
        double? normalized,
        IEnumerable<MomentEvidenceReference>? references) =>
        new(code, raw, normalized, Weights[code], references);

    private static ActivityBurst? FindNearestContaining(IEnumerable<ActivityBurst> bursts, TimeSpan timestamp) =>
        bursts
            .Where(item => timestamp >= item.Start && timestamp <= item.End)
            .OrderByDescending(static item => item.PeakProminence)
            .ThenBy(static item => item.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    private static double CalculateOccupancy(
        IReadOnlyList<NormalizedVisualMomentSample> samples,
        TimeSpan timestamp,
        TimeSpan halfWindow)
    {
        NormalizedVisualMomentSample[] local = samples
            .Where(item => (item.Sample.Timestamp - timestamp).Duration() <= halfWindow)
            .ToArray();
        return local.Length == 0
            ? 0
            : local.Count(static item => item.Context.RawValue >= Math.Max(0.03, item.Context.LocalBaseline)) /
              (double)local.Length;
    }

    private static MomentActivationSample[] Smooth(
        IReadOnlyList<MomentActivationSample> samples,
        TimeSpan halfWindow,
        CancellationToken cancellationToken)
    {
        if (halfWindow <= TimeSpan.Zero)
        {
            return samples.ToArray();
        }

        var output = new MomentActivationSample[samples.Count];
        for (int index = 0; index < samples.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MomentActivationSample center = samples[index];
            (double weighted, double weights) = samples
                .Select(item =>
                {
                    double distance = (item.Timestamp - center.Timestamp).Duration().TotalSeconds;
                    double weight = Math.Max(0, 1 - (distance / Math.Max(0.000001, halfWindow.TotalSeconds)));
                    return (Value: item.RawCombinedActivation * weight, Weight: weight);
                })
                .Aggregate((Value: 0d, Weight: 0d), static (sum, item) => (sum.Value + item.Value, sum.Weight + item.Weight));
            output[index] = center.WithSmoothed(weights <= 0 ? center.RawCombinedActivation : Math.Clamp(weighted / weights, 0, 1));
        }
        return output;
    }

    private static MomentActivationIntegrityState GetIntegrity(
        MediaMomentFindingRequest request,
        TimeSpan timestamp)
    {
        bool black = request.Evidence.FullFrame.BlackIntervals
            .Any(item => timestamp >= item.Start && timestamp <= item.End);
        bool frozen = request.Evidence.FullFrame.FreezeIntervals
            .Any(item => timestamp >= item.Start && timestamp <= item.End);
        return (black, frozen) switch
        {
            (true, true) => MomentActivationIntegrityState.FullFrameBlackAndFrozen,
            (true, false) => MomentActivationIntegrityState.FullFrameBlack,
            (false, true) => MomentActivationIntegrityState.FullFrameFrozen,
            _ => MomentActivationIntegrityState.Clear,
        };
    }

    private static IEnumerable<MomentEvidenceReference> SceneReference(AttributedGameplaySceneBoundary item) =>
    [
        new MomentEvidenceReference(
            MomentEvidenceReferenceKind.SceneBoundary,
            item.Boundary.Timestamp,
            item.Boundary.Timestamp,
            "Gameplay scene support for activation",
            item.Result.Target.TargetKey,
            item.Result.Target.IntervalIndex,
            item.Result.Target.RegionId,
            item.Result.Target.Role,
            rawValue: item.Boundary.ScorePercent,
            normalizedValue: item.Boundary.ScorePercent is null ? null : Math.Clamp(item.Boundary.ScorePercent.Value / 100d, 0, 1)),
    ];

    private static IEnumerable<MomentEvidenceReference> VisualReference(NormalizedVisualMomentSample item) =>
    [
        new MomentEvidenceReference(
            MomentEvidenceReferenceKind.GameplayActivitySample,
            item.Sample.Timestamp,
            item.Sample.Timestamp,
            "Gameplay activation sample",
            item.Sample.TargetKey,
            item.IntervalIndex,
            item.RegionId,
            item.Role,
            rawValue: item.Context.RawValue,
            normalizedValue: item.Context.NormalizedProminence),
    ];

    private static TimeSpan Max(TimeSpan left, TimeSpan right) =>
        left >= right ? left : right;
}
