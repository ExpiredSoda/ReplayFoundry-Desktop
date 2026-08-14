namespace ReplayFoundry.Desktop.Media.Moments;

internal static class MomentSignalScoreComponentBuilder
{
    internal static void AddComponents(
        List<MomentScoreComponent> components,
        MediaMomentFindingRequest request,
        ProposedMomentWindow proposal,
        MomentSignalScoreMeasurements measurements,
        MomentIntegrityScoreMeasurements integrity)
    {
        MomentCandidateWindow window = proposal.Window;
        MomentEventNeighborhood neighborhood = proposal.Neighborhood;
        ActivityBurst? gameplay = measurements.Gameplay;
        ActivityBurst? presenter = measurements.Presenter;
        AudioNoveltyEvent? audio = measurements.Audio;
        IReadOnlyList<AttributedGameplaySceneBoundary> scenes = measurements.Scenes;

        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.GameplayProminence,
            gameplay?.RawPeakActivity ?? 0,
            measurements.GameplayProminence,
            "Peak Gameplay activity prominence above its guarded local baseline.",
            gameplay?.EvidenceReferences);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.GameplayOnset,
            measurements.GameplayOnset,
            measurements.GameplayOnset,
            "Gameplay burst onset relative to the preceding local lookback.",
            gameplay?.EvidenceReferences);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.GameplayBurstIntegration,
            gameplay?.IntegratedExcess ?? 0,
            measurements.GameplayIntegration,
            "Integrated local excess across the Gameplay burst.",
            gameplay?.EvidenceReferences);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.GameplaySceneChange,
            scenes.Select(static scene => scene.Boundary.ScorePercent ?? 0)
                .DefaultIfEmpty(0)
                .Max(),
            measurements.SceneChange,
            "Strongest attributed Gameplay scene change in the candidate.",
            MomentScoreSupport.SceneReferences(scenes));
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.GameplaySceneDensity,
            scenes.Select(static scene => scene.Boundary.Timestamp).Distinct().Count(),
            measurements.SceneDensity,
            "Deduplicated Gameplay scene-boundary density in the candidate.",
            MomentScoreSupport.SceneReferences(scenes));
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.AudioNovelty,
            audio?.PeakLiftDb ?? 0,
            measurements.AudioNovelty,
            "Local non-semantic audio novelty, gated by a coincident primary event.",
            audio?.EvidenceReferences);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.AudioReentry,
            audio?.IsSilenceReentry == true ? 1 : 0,
            measurements.AudioReentry,
            "Audio novelty that follows a retained silence interval, with the same primary-event gate.",
            audio?.EvidenceReferences);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.PresenterGatedSupport,
            presenter?.PeakProminence ?? 0,
            measurements.PresenterSupport,
            "Confirmed Presenter prominence multiplied by temporal proximity and the primary-event gate.",
            presenter?.EvidenceReferences);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.MultiSignalOnsetAgreement,
            measurements.Agreement * 5,
            measurements.Agreement,
            measurements.AgreementExplanation,
            proposal.Anchors.SelectMany(static anchor => anchor.EvidenceReferences));
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.DurationFit,
            window.Duration.TotalSeconds,
            measurements.DurationFit,
            "Fit to the versioned target duration.",
            []);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.ClusterCoherence,
            proposal.Anchors.Count(static anchor =>
                anchor.Kind == MomentAnchorKind.GameplaySceneCluster),
            measurements.Coherence,
            "Coherence of an attributed dense Gameplay scene cluster.",
            proposal.Anchors
                .Where(static anchor =>
                    anchor.Kind == MomentAnchorKind.GameplaySceneCluster)
                .SelectMany(static anchor => anchor.EvidenceReferences));
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.VisualContextChange,
            measurements.VisualContext,
            measurements.VisualContext,
            "Local change in Gameplay luma or saturation around the event peak.",
            measurements.VisualReferences);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.PayoffSupport,
            Math.Max(0, (window.End - neighborhood.End).TotalSeconds),
            measurements.Payoff,
            "Outcome context retained after the measured event span.",
            []);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.ContinuousActivityPenalty,
            measurements.ContinuousPenalty,
            measurements.ContinuousPenalty,
            measurements.ContinuousExplanation,
            gameplay?.EvidenceReferences);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.FullFrameBlackPenalty,
            integrity.FullFrameBlack,
            integrity.FullFrameBlack,
            "Capture-integrity penalty for full-frame black overlap.",
            MomentScoreSupport.IntegrityReferences(
                request.Evidence.FullFrame.Target.TargetKey,
                request.Evidence.FullFrame.BlackIntervals,
                MomentEvidenceReferenceKind.BlackInterval,
                "Full-frame black interval"));
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.FullFrameFreezePenalty,
            integrity.FullFrameFreeze,
            integrity.FullFrameFreeze,
            "Capture-integrity penalty for full-frame freeze overlap.",
            MomentScoreSupport.IntegrityReferences(
                request.Evidence.FullFrame.Target.TargetKey,
                request.Evidence.FullFrame.FreezeIntervals,
                MomentEvidenceReferenceKind.FreezeInterval,
                "Full-frame freeze interval"));
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.GameplayLowInformationPenalty,
            integrity.LowInformation,
            integrity.LowInformation,
            "Low-information Gameplay samples combine very low luma with very low contrast; darkness alone is not penalized.",
            []);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.SourceEdgePenalty,
            integrity.SourceEdge,
            integrity.SourceEdge,
            "Missing requested event context at a source edge.",
            []);
        MomentScoreSupport.Add(
            components,
            request,
            MomentScoreComponentCode.NeighborhoodRedundancyPenalty,
            measurements.NeighborhoodRedundancy,
            measurements.NeighborhoodRedundancy,
            "Redundant same-family anchors inside one event neighborhood.",
            proposal.Anchors.SelectMany(static anchor => anchor.EvidenceReferences));
    }
}
