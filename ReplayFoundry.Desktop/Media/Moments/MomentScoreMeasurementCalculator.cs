using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Media.Moments;

internal sealed record MomentSignalScoreMeasurements(
    ActivityBurst? Gameplay,
    ActivityBurst? Presenter,
    AudioNoveltyEvent? Audio,
    IReadOnlyList<AttributedGameplaySceneBoundary> Scenes,
    double GameplayProminence,
    double GameplayOnset,
    double GameplayIntegration,
    double SceneChange,
    double SceneDensity,
    double AudioNovelty,
    double AudioReentry,
    double PresenterSupport,
    double Agreement,
    string AgreementExplanation,
    double DurationFit,
    double Coherence,
    double VisualContext,
    IReadOnlyList<MomentEvidenceReference> VisualReferences,
    double Payoff,
    double ContinuousPenalty,
    string ContinuousExplanation,
    double NeighborhoodRedundancy,
    double CorrelatedVisualSupport);

internal sealed record MomentIntegrityScoreMeasurements(
    double FullFrameBlack,
    double FullFrameFreeze,
    double GameplayBlack,
    double GameplayFreeze,
    double LowInformation,
    double SourceEdge);

internal sealed record MomentEpisodeScoreMeasurements(
    MomentEpisodeFeatureVector? Features,
    StandaloneEpisodeFeatureVector? StandaloneFeatures,
    MontageSegmentFeatureVector? MontageFeatures,
    double IntegratedStrength,
    double PeakStrength,
    double OnsetStrength,
    double RecoverySupport,
    double Cohesion);

internal static class MomentScoreMeasurementCalculator
{
    internal static MomentSignalScoreMeasurements MeasureSignals(
        MediaMomentFindingRequest request,
        NormalizedMomentSignals signals,
        ProposedMomentWindow proposal)
    {
        MomentCandidateWindow window = proposal.Window;
        MomentEventNeighborhood neighborhood = proposal.Neighborhood;
        MomentEventEpisode? episode = proposal.Episode;
        ActivityBurst[] gameplayBursts = signals.GameplayBursts
            .Where(burst => MomentScoreSupport.Intersects(window, burst.Start, burst.End))
            .ToArray();
        ActivityBurst[] presenterBursts = signals.PresenterBursts
            .Where(burst => MomentScoreSupport.Intersects(window, burst.Start, burst.End))
            .ToArray();
        AudioNoveltyEvent[] audioEvents = signals.AudioNoveltyEvents
            .Where(item => MomentScoreSupport.Intersects(window, item.Start, item.End))
            .ToArray();
        AttributedGameplaySceneBoundary[] scenes = signals.GameplayScenes
            .Where(scene => window.Contains(scene.Boundary.Timestamp))
            .ToArray();

        ActivityBurst? gameplay = gameplayBursts
            .OrderByDescending(
                burst => burst.PeakProminence * MomentScoreSupport.Proximity(
                    burst.PeakTimestamp,
                    neighborhood.PeakTimestamp,
                    request.Options.CrossSignalAgreementWindow))
            .ThenBy(static burst => burst.PeakTimestamp)
            .FirstOrDefault();
        double gameplayProminence = gameplay?.PeakProminence ?? 0;
        double gameplayOnset = gameplay?.OnsetStrength ?? 0;
        double gameplayIntegration = gameplay is null
            ? 0
            : Math.Clamp(
                gameplay.IntegratedExcess /
                Math.Max(
                    gameplay.LocalSpread *
                    request.Options.CalibrationPolicy.ProminenceSaturationMultiple *
                    Math.Max(1, gameplay.EvidenceReferences.Count),
                    0.000000001),
                0,
                1);
        double sceneChange = scenes.Length == 0
            ? 0
            : Math.Clamp(
                scenes.Max(static scene => scene.Boundary.ScorePercent ?? 0) / 100d,
                0,
                1);
        double sceneDensity = Math.Clamp(
            scenes.Select(static scene => scene.Boundary.Timestamp)
                .Distinct()
                .Count() / 5d,
            0,
            1);
        double primaryGate = Math.Max(
            gameplayProminence,
            Math.Max(sceneChange, sceneDensity * 0.75));

        ActivityBurst? presenter = presenterBursts
            .OrderByDescending(
                burst => burst.PeakProminence * MomentScoreSupport.Proximity(
                    burst.PeakTimestamp,
                    neighborhood.PeakTimestamp,
                    request.Options.CrossSignalAgreementWindow))
            .ThenBy(static burst => burst.PeakTimestamp)
            .FirstOrDefault();
        AudioNoveltyEvent? audio = audioEvents
            .OrderByDescending(
                item => item.NormalizedProminence * MomentScoreSupport.Proximity(
                    item.PeakTimestamp,
                    neighborhood.PeakTimestamp,
                    request.Options.CrossSignalAgreementWindow))
            .ThenBy(static item => item.PeakTimestamp)
            .FirstOrDefault();
        double correlatedVisualSupport = gameplay is null || presenter is null
            ? 0
            : MomentVisualSupportCorrelation.Measure(
                [gameplay],
                [presenter],
                request.Options.CrossSignalAgreementWindow);
        double commentaryPairGate =
            request.Options.ContentEmphasis == MomentContentEmphasis.CommentaryFocused &&
            presenter is not null &&
            audio is not null &&
            (presenter.PeakTimestamp - audio.PeakTimestamp).Duration() <=
                request.Options.CrossSignalAgreementWindow
                ? Math.Sqrt(presenter.PeakProminence * audio.NormalizedProminence)
                : 0;
        double supportGate = Math.Max(primaryGate, commentaryPairGate);
        double audioNovelty = audio is null
            ? 0
            : Math.Clamp(
                audio.NormalizedProminence *
                MomentScoreSupport.EpisodeProximity(
                    audio.PeakTimestamp,
                    episode,
                    neighborhood,
                    request.Options.CrossSignalAgreementWindow) *
                supportGate,
                0,
                1);
        double audioReentry = audio?.IsSilenceReentry == true ? audioNovelty : 0;
        double presenterSupport = presenter is null
            ? 0
            : Math.Clamp(
                presenter.PeakProminence *
                MomentScoreSupport.EpisodeProximity(
                    presenter.PeakTimestamp,
                    episode,
                    neighborhood,
                    request.Options.CrossSignalAgreementWindow) *
                supportGate *
                (1 - correlatedVisualSupport),
                0,
                1);
        if (presenterSupport <
            request.Options.DistinctivenessPolicy.MinimumIncrementalPresenterProminence)
        {
            presenterSupport = 0;
        }

        (double agreement, string agreementExplanation) = MomentScoreSupport.CalculateAgreement(
            request,
            gameplay,
            scenes,
            audio,
            presenter,
            correlatedVisualSupport,
            presenterSupport);
        double durationFit = Math.Clamp(
            1 - Math.Abs(window.Duration.Ticks - request.Options.TargetDuration.Ticks) /
            (double)Math.Max(window.Duration.Ticks, request.Options.TargetDuration.Ticks),
            0,
            1);
        double coherence = proposal.Anchors
            .Where(static anchor => anchor.Kind == MomentAnchorKind.GameplaySceneCluster)
            .Select(static anchor => anchor.NormalizedStrength)
            .DefaultIfEmpty(0)
            .Max();
        (double visualContext, IReadOnlyList<MomentEvidenceReference> visualReferences) =
            MomentScoreSupport.CalculateVisualContext(
                signals,
                neighborhood,
                request.Options.CrossSignalAgreementWindow);
        double payoff = request.Options.CalibrationPolicy.MinimumPayoffContext <= TimeSpan.Zero
            ? 1
            : Math.Clamp(
                (window.End - neighborhood.End).TotalSeconds /
                request.Options.CalibrationPolicy.MinimumPayoffContext.TotalSeconds,
                0,
                1);
        (double continuousPenalty, string continuousExplanation) =
            MomentScoreSupport.CalculateContinuousPenalty(
                request,
                signals,
                window,
                gameplay);

        return new MomentSignalScoreMeasurements(
            gameplay,
            presenter,
            audio,
            scenes,
            gameplayProminence,
            gameplayOnset,
            gameplayIntegration,
            sceneChange,
            sceneDensity,
            audioNovelty,
            audioReentry,
            presenterSupport,
            agreement,
            agreementExplanation,
            durationFit,
            coherence,
            visualContext,
            visualReferences,
            payoff,
            continuousPenalty,
            continuousExplanation,
            MomentScoreSupport.CalculateNeighborhoodRedundancy(proposal.Anchors),
            correlatedVisualSupport);
    }

    internal static MomentIntegrityScoreMeasurements MeasureIntegrity(
        MediaMomentFindingRequest request,
        NormalizedMomentSignals signals,
        ProposedMomentWindow proposal)
    {
        MomentCandidateWindow window = proposal.Window;
        MomentEventEpisode? episode = proposal.Episode;
        (TimeSpan Start, TimeSpan End)[] fullFrameBlackIntervals =
            request.Evidence.FullFrame.BlackIntervals
                .Select(static interval => (interval.Start, interval.End))
                .ToArray();
        (TimeSpan Start, TimeSpan End)[] fullFrameFreezeIntervals =
            request.Evidence.FullFrame.FreezeIntervals
                .Select(static interval => (interval.Start, interval.End))
                .ToArray();
        double fullFrameBlack = MomentScoreSupport.IntegrityOverlapRatio(
            request,
            window,
            episode,
            fullFrameBlackIntervals);
        double fullFrameFreeze = MomentScoreSupport.IntegrityOverlapRatio(
            request,
            window,
            episode,
            fullFrameFreezeIntervals);
        VisualTargetEvidenceResult[] gameplayTargets = request.Evidence.RegionVisualResults
            .Where(static result =>
                result.Target.Role == CompositionRegionRole.Gameplay)
            .ToArray();
        double gameplayBlack = MomentIntervalMath.OverlapRatio(
            window,
            gameplayTargets.SelectMany(static result =>
                result.BlackIntervals.Select(interval => (interval.Start, interval.End))));
        double gameplayFreeze = MomentIntervalMath.OverlapRatio(
            window,
            gameplayTargets.SelectMany(static result =>
                result.FreezeIntervals.Select(interval => (interval.Start, interval.End))));

        return new MomentIntegrityScoreMeasurements(
            fullFrameBlack,
            fullFrameFreeze,
            gameplayBlack,
            gameplayFreeze,
            MomentScoreSupport.CalculateLowInformation(signals, window),
            MomentScoreSupport.CalculateSourceEdgePenalty(request, proposal));
    }

    internal static MomentEpisodeScoreMeasurements MeasureEpisode(
        MediaMomentFindingRequest request,
        NormalizedMomentSignals signals,
        MomentActivationSeries activation,
        ProposedMomentWindow proposal)
    {
        MomentEventEpisode? episode = proposal.Episode;
        MomentEpisodeFeatureVector? features = episode is null
            ? null
            : MomentEpisodeFeatureVectorBuilder.Build(request, episode, activation, signals);
        StandaloneEpisodeFeatureVector? standaloneFeatures =
            features is not null &&
            request.Options.OutputKind == MomentOutputKind.StandaloneClip
                ? StandaloneEpisodeRanker.Rank(proposal, features)
                : null;
        MontageSegmentFeatureVector? montageFeatures =
            features is not null &&
            request.Options.OutputKind == MomentOutputKind.MontageSegment
                ? MontageSegmentRanker.Rank(proposal, features)
                : null;
        double integratedStrength = episode is null
            ? 0
            : Math.Clamp(
                episode.IntegratedActivation /
                Math.Max(0.000001, episode.Duration.TotalSeconds),
                0,
                1);
        double peakStrength = episode is null
            ? 0
            : Math.Clamp(episode.PeakActivation, 0, 1);
        double onsetStrength = episode is null ? 0 : features?.OnsetStrength ?? 0;
        double recoverySupport = episode?.LocalRecoveryAfter is null
            ? 0
            : Math.Clamp(
                episode.PeakActivation - episode.LocalRecoveryAfter.Value,
                0,
                1);
        double cohesion = episode is null
            ? 0
            : Math.Clamp(
                (episode.ActivationOccupancy +
                 Math.Min(1, episode.EvidenceSummary.DominantSignalFamilies.Count / 3d)) /
                2d,
                0,
                1);

        return new MomentEpisodeScoreMeasurements(
            features,
            standaloneFeatures,
            montageFeatures,
            integratedStrength,
            peakStrength,
            onsetStrength,
            recoverySupport,
            cohesion);
    }
}
