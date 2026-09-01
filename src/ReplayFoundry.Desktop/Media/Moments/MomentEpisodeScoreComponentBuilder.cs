namespace ReplayFoundry.Desktop.Media.Moments;

internal static class MomentEpisodeScoreComponentBuilder
{
    internal static void AddComponents(
        List<MomentScoreComponent> components,
        MediaMomentFindingRequest request,
        ProposedMomentWindow proposal,
        MomentSignalScoreMeasurements signals,
        MomentEpisodeScoreMeasurements measurements)
    {
        MomentEventEpisode? episode = proposal.Episode;
        MomentEpisodeFeatureVector? features = measurements.Features;
        StandaloneEpisodeFeatureVector? standalone = measurements.StandaloneFeatures;
        MontageSegmentFeatureVector? montage = measurements.MontageFeatures;

        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.EpisodePeakStrength,
            episode?.PeakActivation ?? 0,
            measurements.PeakStrength,
            "Peak of the versioned event-activation episode.",
            episode?.EvidenceSummary.EvidenceReferences);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.EpisodeIntegratedStrength,
            episode?.IntegratedActivation ?? 0,
            measurements.IntegratedStrength,
            "Duration-normalized activation integrated across the episode.",
            episode?.EvidenceSummary.EvidenceReferences);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.EpisodeOnsetStrength,
            measurements.OnsetStrength,
            measurements.OnsetStrength,
            "Rise from the observed local baseline to the episode peak.",
            episode?.EvidenceSummary.EvidenceReferences);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.EpisodeRecoverySupport,
            measurements.RecoverySupport,
            measurements.RecoverySupport,
            "Observed decline from the episode peak into local recovery.",
            episode?.EvidenceSummary.EvidenceReferences);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.EpisodeCohesion,
            measurements.Cohesion,
            measurements.Cohesion,
            "Episode occupancy combined with observable signal-family breadth.",
            episode?.EvidenceSummary.EvidenceReferences);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.MontageRepresentativeCoverage,
            proposal.MontageObjective?.ObjectiveScore ?? 0,
            proposal.MontageObjective?.ObjectiveScore ?? 0,
            "Transparent representative-segment objective inside the episode.",
            episode?.EvidenceSummary.EvidenceReferences);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.MontageEpisodeRedundancyPenalty,
            0,
            0,
            "No duplicate segment is proposed from the same episode.",
            []);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.EpisodeDistinctiveness,
            features?.Distinctiveness ?? 0,
            features?.Distinctiveness ?? 0,
            "Versioned separation of the episode core from its baseline after transparent continuous-activity and correlated-support controls.",
            episode?.EvidenceSummary.EvidenceReferences);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.BaselineCoreSeparation,
            features?.BaselineCoreSeparation ?? 0,
            features?.BaselineCoreSeparation ?? 0,
            "Measured separation between the pre-episode baseline and the observed Core phase.",
            episode?.EvidenceSummary.EvidenceReferences);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.CoreRecoverySeparation,
            features?.CoreRecoverySeparation ?? 0,
            features?.CoreRecoverySeparation ?? 0,
            episode?.LocalRecoveryAfter is null
                ? "Recovery is unavailable at the source boundary; no measured recovery separation is awarded."
                : "Measured decline from the Core phase into observed recovery.",
            episode?.EvidenceSummary.EvidenceReferences);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.IndependentFamilyAgreement,
            features?.IndependentFamilyAgreement ?? 0,
            features?.IndependentFamilyAgreement ?? 0,
            "Agreement among independently measured signal families after correlated visual-transition deduplication.",
            episode?.EvidenceSummary.EvidenceReferences);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.CorrelatedVisualSupportPenalty,
            features?.CorrelatedVisualSupport ?? 0,
            features?.CorrelatedVisualSupport ?? 0,
            "Likely shared Gameplay/Presenter visual transition; references are retained but it is not counted as two independent families.",
            signals.Presenter?.EvidenceReferences);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.SingleFamilyDominancePenalty,
            features?.SingleFamilyDominancePenalty ?? 0,
            features?.SingleFamilyDominancePenalty ?? 0,
            "Penalty when one observable family dominates without independent support.",
            episode?.EvidenceSummary.EvidenceReferences);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.ContinuousUniformityPenalty,
            features?.ContinuousUniformityPenalty ?? 0,
            features?.ContinuousUniformityPenalty ?? 0,
            "Uniform high occupancy lacking onset, concentration, baseline separation, or recovery.",
            episode?.EvidenceSummary.EvidenceReferences);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.StandaloneEpisodeCompleteness,
            standalone?.EpisodeCompleteness ?? 0,
            standalone?.RankingValue ?? 0,
            request.Options.OutputKind == MomentOutputKind.StandaloneClip
                ? "Mode-specific Standalone ranking combines complete episode coverage, onset, recovery, context, scene support, and low continuous uniformity."
                : "Not active for Montage ranking.",
            episode?.EvidenceSummary.EvidenceReferences);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.MontageRepresentativeDensity,
            montage?.ConciseActivationDensity ?? 0,
            montage?.RankingValue ?? 0,
            request.Options.OutputKind == MomentOutputKind.MontageSegment
                ? "Mode-specific representative-segment density, peak coverage, and episode distinctiveness."
                : "Not active for Standalone ranking.",
            episode?.EvidenceSummary.EvidenceReferences);
    }
}
