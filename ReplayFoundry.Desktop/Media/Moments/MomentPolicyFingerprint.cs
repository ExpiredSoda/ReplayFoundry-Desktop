using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ReplayFoundry.Desktop.Media.Moments;

public static class MomentPolicyFingerprint
{
    public static string Create(MediaMomentFindingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        MomentCalibrationPolicy policy = options.CalibrationPolicy;
        MomentEpisodePolicy episode = options.EpisodePolicy;
        MomentDistinctivenessPolicy distinctiveness =
            options.DistinctivenessPolicy;
        string values =
            string.Join(
                "|",
                options.PolicyVersion,
                options.OutputKind,
                options.ContentEmphasis,
                options.SignalAblation,
                options.MinimumDuration.Ticks,
                options.TargetDuration.Ticks,
                options.MaximumDuration.Ticks,
                options.SceneClusterMaximumGap.Ticks,
                options.CrossSignalAgreementWindow.Ticks,
                options.FullFrameBlackHardRejectionRatio.ToString("R", CultureInfo.InvariantCulture),
                options.FullFrameFreezeHardRejectionRatio.ToString("R", CultureInfo.InvariantCulture),
                policy.Version,
                policy.LocalBaselineHalfWindow.Ticks,
                policy.LocalBaselineGuardHalfWindow.Ticks,
                policy.OnsetLookback.Ticks,
                policy.ProminenceSpreadFloor.ToString("R", CultureInfo.InvariantCulture),
                policy.ProminenceSaturationMultiple.ToString("R", CultureInfo.InvariantCulture),
                policy.MinimumBurstProminence.ToString("R", CultureInfo.InvariantCulture),
                policy.MinimumBurstOnset.ToString("R", CultureInfo.InvariantCulture),
                policy.BurstStartThreshold.ToString("R", CultureInfo.InvariantCulture),
                policy.BurstEndThreshold.ToString("R", CultureInfo.InvariantCulture),
                policy.MinimumBurstDuration.Ticks,
                policy.MaximumBurstMergeGap.Ticks,
                policy.ContinuousActivityPenaltyWindow.Ticks,
                policy.ContinuousActivityOccupancyThreshold.ToString("R", CultureInfo.InvariantCulture),
                policy.EventNeighborhoodMaximumGap.Ticks,
                policy.NeighborhoodValleyProminenceThreshold.ToString("R", CultureInfo.InvariantCulture),
                policy.MinimumNeighborhoodValleyDuration.Ticks,
                policy.MontageMinimumCooldown.Ticks,
                policy.ClusterLeadInShare.ToString("R", CultureInfo.InvariantCulture),
                policy.BurstLeadInShare.ToString("R", CultureInfo.InvariantCulture),
                policy.MinimumLeadInContext.Ticks,
                policy.MinimumPayoffContext.Ticks,
                policy.SourceEdgeReallocationPolicyVersion,
                episode.Version,
                episode.EpisodeStartActivationThreshold.ToString("R", CultureInfo.InvariantCulture),
                episode.EpisodeContinueActivationThreshold.ToString("R", CultureInfo.InvariantCulture),
                episode.EpisodeEndActivationThreshold.ToString("R", CultureInfo.InvariantCulture),
                episode.MinimumEpisodeDuration.Ticks,
                episode.MaximumEpisodeDuration.Ticks,
                episode.MaximumEpisodeBridgeGap.Ticks,
                episode.MinimumEpisodeIntegratedActivation.ToString("R", CultureInfo.InvariantCulture),
                episode.MinimumEpisodePeakActivation.ToString("R", CultureInfo.InvariantCulture),
                episode.MinimumRecoveryDuration.Ticks,
                episode.RecoveryActivationThreshold.ToString("R", CultureInfo.InvariantCulture),
                episode.EpisodeSmoothingHalfWindow.Ticks,
                episode.SplitValleyActivationThreshold.ToString("R", CultureInfo.InvariantCulture),
                episode.MinimumSplitValleyDuration.Ticks,
                distinctiveness.Version,
                distinctiveness.CorrelationThreshold.ToString("R", CultureInfo.InvariantCulture),
                distinctiveness.MinimumIncrementalPresenterProminence.ToString("R", CultureInfo.InvariantCulture),
                distinctiveness.UniformActivityOccupancyThreshold.ToString("R", CultureInfo.InvariantCulture),
                distinctiveness.PeakSeparationWeight.ToString("R", CultureInfo.InvariantCulture),
                distinctiveness.IntegratedSeparationWeight.ToString("R", CultureInfo.InvariantCulture),
                distinctiveness.OnsetWeight.ToString("R", CultureInfo.InvariantCulture),
                distinctiveness.ConcentrationWeight.ToString("R", CultureInfo.InvariantCulture),
                distinctiveness.FamilyAgreementWeight.ToString("R", CultureInfo.InvariantCulture),
                distinctiveness.RecoveryOrBoundaryWeight.ToString("R", CultureInfo.InvariantCulture),
                distinctiveness.BaselineCoreSeparationWeight.ToString("R", CultureInfo.InvariantCulture),
                distinctiveness.ContinuousUniformityPenaltyWeight.ToString("R", CultureInfo.InvariantCulture),
                distinctiveness.SingleFamilyDominancePenaltyWeight.ToString("R", CultureInfo.InvariantCulture),
                distinctiveness.CorrelatedVisualSupportPenaltyWeight.ToString("R", CultureInfo.InvariantCulture),
                options.ComponentWeights.Version,
                string.Join(
                    ",",
                    options.ComponentWeights.Weights
                        .OrderBy(static item => item.Key)
                        .Select(
                            static item =>
                                $"{item.Key}:{item.Value.ToString("R", CultureInfo.InvariantCulture)}")));
        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(values)));
    }
}
