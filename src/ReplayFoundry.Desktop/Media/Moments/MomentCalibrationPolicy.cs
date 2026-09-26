namespace ReplayFoundry.Desktop.Media.Moments;

/// <summary>
/// Versioned, source-independent calibration values used by the deterministic
/// feature policy. Values describe signal context, not a game or source.
/// </summary>
public sealed class MomentCalibrationPolicy
{
    public const string CurrentVersion = "1.1";

    public MomentCalibrationPolicy(
        TimeSpan localBaselineHalfWindow,
        TimeSpan localBaselineGuardHalfWindow,
        TimeSpan onsetLookback,
        double prominenceSpreadFloor,
        double prominenceSaturationMultiple,
        double minimumBurstProminence,
        double minimumBurstOnset,
        double burstStartThreshold,
        double burstEndThreshold,
        TimeSpan minimumBurstDuration,
        TimeSpan maximumBurstMergeGap,
        TimeSpan continuousActivityPenaltyWindow,
        double continuousActivityOccupancyThreshold,
        TimeSpan eventNeighborhoodMaximumGap,
        double neighborhoodValleyProminenceThreshold,
        TimeSpan minimumNeighborhoodValleyDuration,
        TimeSpan montageMinimumCooldown,
        double clusterLeadInShare,
        double burstLeadInShare,
        TimeSpan minimumLeadInContext,
        TimeSpan minimumPayoffContext,
        string sourceEdgeReallocationPolicyVersion,
        string version = CurrentVersion)
    {
        ValidatePositive(localBaselineHalfWindow, nameof(localBaselineHalfWindow));
        ValidateNonNegative(localBaselineGuardHalfWindow, nameof(localBaselineGuardHalfWindow));
        ValidatePositive(onsetLookback, nameof(onsetLookback));
        ValidatePositive(prominenceSpreadFloor, nameof(prominenceSpreadFloor));
        ValidatePositive(prominenceSaturationMultiple, nameof(prominenceSaturationMultiple));
        ValidateRatio(minimumBurstProminence, nameof(minimumBurstProminence));
        ValidateRatio(minimumBurstOnset, nameof(minimumBurstOnset));
        ValidateRatio(burstStartThreshold, nameof(burstStartThreshold));
        ValidateRatio(burstEndThreshold, nameof(burstEndThreshold));
        ValidatePositive(minimumBurstDuration, nameof(minimumBurstDuration));
        ValidateNonNegative(maximumBurstMergeGap, nameof(maximumBurstMergeGap));
        ValidatePositive(continuousActivityPenaltyWindow, nameof(continuousActivityPenaltyWindow));
        ValidateRatio(continuousActivityOccupancyThreshold, nameof(continuousActivityOccupancyThreshold));
        ValidateNonNegative(eventNeighborhoodMaximumGap, nameof(eventNeighborhoodMaximumGap));
        ValidateRatio(neighborhoodValleyProminenceThreshold, nameof(neighborhoodValleyProminenceThreshold));
        ValidatePositive(minimumNeighborhoodValleyDuration, nameof(minimumNeighborhoodValleyDuration));
        ValidateNonNegative(montageMinimumCooldown, nameof(montageMinimumCooldown));
        ValidateRatio(clusterLeadInShare, nameof(clusterLeadInShare));
        ValidateRatio(burstLeadInShare, nameof(burstLeadInShare));
        ValidateNonNegative(minimumLeadInContext, nameof(minimumLeadInContext));
        ValidateNonNegative(minimumPayoffContext, nameof(minimumPayoffContext));

        if (localBaselineGuardHalfWindow >= localBaselineHalfWindow)
        {
            throw new ArgumentException(
                "The local baseline guard must be smaller than the local baseline window.");
        }

        if (burstStartThreshold <= burstEndThreshold)
        {
            throw new ArgumentException(
                "Burst hysteresis requires a start threshold greater than its end threshold.");
        }

        if (minimumBurstProminence < burstStartThreshold)
        {
            throw new ArgumentException(
                "Minimum burst prominence cannot be below the burst start threshold.");
        }

        ValidateText(sourceEdgeReallocationPolicyVersion, nameof(sourceEdgeReallocationPolicyVersion));
        ValidateText(version, nameof(version));

        LocalBaselineHalfWindow = localBaselineHalfWindow;
        LocalBaselineGuardHalfWindow = localBaselineGuardHalfWindow;
        OnsetLookback = onsetLookback;
        ProminenceSpreadFloor = prominenceSpreadFloor;
        ProminenceSaturationMultiple = prominenceSaturationMultiple;
        MinimumBurstProminence = minimumBurstProminence;
        MinimumBurstOnset = minimumBurstOnset;
        BurstStartThreshold = burstStartThreshold;
        BurstEndThreshold = burstEndThreshold;
        MinimumBurstDuration = minimumBurstDuration;
        MaximumBurstMergeGap = maximumBurstMergeGap;
        ContinuousActivityPenaltyWindow = continuousActivityPenaltyWindow;
        ContinuousActivityOccupancyThreshold = continuousActivityOccupancyThreshold;
        EventNeighborhoodMaximumGap = eventNeighborhoodMaximumGap;
        NeighborhoodValleyProminenceThreshold = neighborhoodValleyProminenceThreshold;
        MinimumNeighborhoodValleyDuration = minimumNeighborhoodValleyDuration;
        MontageMinimumCooldown = montageMinimumCooldown;
        ClusterLeadInShare = clusterLeadInShare;
        BurstLeadInShare = burstLeadInShare;
        MinimumLeadInContext = minimumLeadInContext;
        MinimumPayoffContext = minimumPayoffContext;
        SourceEdgeReallocationPolicyVersion = sourceEdgeReallocationPolicyVersion.Trim();
        Version = version.Trim();
    }

    public TimeSpan LocalBaselineHalfWindow { get; }
    public TimeSpan LocalBaselineGuardHalfWindow { get; }
    public TimeSpan OnsetLookback { get; }
    public double ProminenceSpreadFloor { get; }
    public double ProminenceSaturationMultiple { get; }
    public double MinimumBurstProminence { get; }
    public double MinimumBurstOnset { get; }
    public double BurstStartThreshold { get; }
    public double BurstEndThreshold { get; }
    public TimeSpan MinimumBurstDuration { get; }
    public TimeSpan MaximumBurstMergeGap { get; }
    public TimeSpan ContinuousActivityPenaltyWindow { get; }
    public double ContinuousActivityOccupancyThreshold { get; }
    public TimeSpan EventNeighborhoodMaximumGap { get; }
    public double NeighborhoodValleyProminenceThreshold { get; }
    public TimeSpan MinimumNeighborhoodValleyDuration { get; }
    public TimeSpan MontageMinimumCooldown { get; }
    public double ClusterLeadInShare { get; }
    public double BurstLeadInShare { get; }
    public TimeSpan MinimumLeadInContext { get; }
    public TimeSpan MinimumPayoffContext { get; }
    public string SourceEdgeReallocationPolicyVersion { get; }
    public string Version { get; }

    public static MomentCalibrationPolicy CreateDefaults() =>
        new(
            localBaselineHalfWindow: TimeSpan.FromSeconds(20),
            localBaselineGuardHalfWindow: TimeSpan.FromSeconds(3),
            onsetLookback: TimeSpan.FromSeconds(6),
            prominenceSpreadFloor: 0.01,
            prominenceSaturationMultiple: 4,
            minimumBurstProminence: 0.35,
            minimumBurstOnset: 0.10,
            burstStartThreshold: 0.30,
            burstEndThreshold: 0.12,
            minimumBurstDuration: TimeSpan.FromSeconds(1),
            maximumBurstMergeGap: TimeSpan.FromSeconds(1),
            continuousActivityPenaltyWindow: TimeSpan.FromSeconds(20),
            continuousActivityOccupancyThreshold: 0.70,
            eventNeighborhoodMaximumGap: TimeSpan.FromSeconds(3),
            neighborhoodValleyProminenceThreshold: 0.18,
            minimumNeighborhoodValleyDuration: TimeSpan.FromSeconds(3),
            montageMinimumCooldown: TimeSpan.FromSeconds(8),
            clusterLeadInShare: 0.75,
            burstLeadInShare: 0.65,
            minimumLeadInContext: TimeSpan.FromSeconds(5),
            minimumPayoffContext: TimeSpan.FromSeconds(3),
            sourceEdgeReallocationPolicyVersion: "1.1");

    private static void ValidatePositive(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidateNonNegative(TimeSpan value, string name)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidatePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidateRatio(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidateText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A version value cannot be blank.", name);
        }
    }
}
