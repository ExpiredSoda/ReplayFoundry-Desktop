using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Media.Moments;

internal static class MomentScoreSupport
{
    internal static void Add(
        ICollection<MomentScoreComponent> components,
        MediaMomentFindingRequest request,
        MomentScoreComponentCode code,
        double raw,
        double normalized,
        string explanation,
        IEnumerable<MomentEvidenceReference>? references)
    {
        double bounded = Math.Clamp(normalized, 0, 1);
        double weight = request.Options.ComponentWeights[code];
        components.Add(
            new MomentScoreComponent(
                code,
                double.IsFinite(raw) ? raw : 0,
                bounded,
                weight,
                bounded * weight,
                explanation,
                references));
    }

    internal static (double Value, string Explanation) CalculateAgreement(
        MediaMomentFindingRequest request,
        ActivityBurst? gameplay,
        IReadOnlyList<AttributedGameplaySceneBoundary> scenes,
        AudioNoveltyEvent? audio,
        ActivityBurst? presenter,
        double correlatedVisualSupport,
        double presenterIncrementalSupport)
    {
        var events =
            new List<(MomentSignalFamily Family, TimeSpan Time)>();
        if (gameplay is not null)
        {
            events.Add((MomentSignalFamily.GameplayBurst, gameplay.PeakTimestamp));
        }
        if (scenes.Count > 0)
        {
            events.Add(
                (
                    MomentSignalFamily.GameplayScene,
                    scenes
                        .OrderByDescending(static item => item.Boundary.ScorePercent ?? 0)
                        .ThenBy(static item => item.Boundary.Timestamp)
                        .First()
                        .Boundary.Timestamp));
        }
        if (audio is not null)
        {
            events.Add((MomentSignalFamily.AudioNovelty, audio.PeakTimestamp));
        }
        if (presenter is not null &&
            (
                correlatedVisualSupport <
                    request.Options.DistinctivenessPolicy
                        .CorrelationThreshold ||
                presenterIncrementalSupport > 0
            ))
        {
            events.Add((MomentSignalFamily.PresenterProminence, presenter.PeakTimestamp));
        }

        if (events.Count < 2)
        {
            return (0, "Fewer than two independent event families have coincident onsets.");
        }

        var coincident =
            events
                .Select(
                    reference =>
                        events
                            .Where(
                                item =>
                                    (item.Time - reference.Time).Duration() <=
                                    request.Options.CrossSignalAgreementWindow)
                            .GroupBy(static item => item.Family)
                            .Select(static group => group.First())
                            .OrderBy(static item => item.Time)
                            .ThenBy(static item => item.Family)
                            .ToArray())
                .OrderByDescending(static group => group.Length)
                .ThenBy(
                    static group =>
                        group.Length == 0
                            ? TimeSpan.MaxValue
                            : group[^1].Time - group[0].Time)
                .First();
        (MomentSignalFamily Family, TimeSpan Time) reference =
            coincident[0];
        double normalized =
            Math.Clamp(
                (coincident.Length - 1) / 4d,
                0,
                1);
        string detail =
            string.Join(
                ", ",
                coincident.Select(
                    item =>
                        $"{item.Family}:{(item.Time - reference.Time).TotalSeconds:+0.###;-0.###;0}s"));
        return (
            normalized,
            coincident.Length < 2
                ? "Independent event-family timestamps were outside the configured agreement window."
                : $"Independent onset/peak families within the configured timing window: {detail}.");
    }

    internal static (double Value, IReadOnlyList<MomentEvidenceReference> References)
        CalculateVisualContext(
            NormalizedMomentSignals signals,
            MomentEventNeighborhood neighborhood,
            TimeSpan window)
    {
        NormalizedVisualMomentSample? before =
            signals.Gameplay
                .Where(
                    sample =>
                        sample.Sample.Timestamp <= neighborhood.Start &&
                        neighborhood.Start - sample.Sample.Timestamp <= window)
                .OrderByDescending(static sample => sample.Sample.Timestamp)
                .FirstOrDefault();
        NormalizedVisualMomentSample? after =
            signals.Gameplay
                .Where(
                    sample =>
                        sample.Sample.Timestamp >= neighborhood.End &&
                        sample.Sample.Timestamp - neighborhood.End <= window)
                .OrderBy(static sample => sample.Sample.Timestamp)
                .FirstOrDefault();
        if (before is null || after is null)
        {
            return (0, []);
        }

        double raw =
            Math.Max(
                Math.Abs(
                    after.Sample.NormalizedMeanLuma -
                    before.Sample.NormalizedMeanLuma),
                Math.Abs(
                    after.Sample.NormalizedMeanSaturation -
                    before.Sample.NormalizedMeanSaturation));
        double normalized = Math.Clamp(raw / 0.20, 0, 1);
        return (
            normalized,
            [
                new MomentEvidenceReference(
                    MomentEvidenceReferenceKind.LumaChange,
                    before.Sample.Timestamp,
                    after.Sample.Timestamp,
                    "Gameplay visual context around the event",
                    before.Sample.TargetKey,
                    before.IntervalIndex,
                    before.RegionId,
                    CompositionRegionRole.Gameplay,
                    rawValue: raw,
                    normalizedValue: normalized),
            ]);
    }

    internal static (double Value, string Explanation) CalculateContinuousPenalty(
        MediaMomentFindingRequest request,
        NormalizedMomentSignals signals,
        MomentCandidateWindow window,
        ActivityBurst? burst)
    {
        NormalizedVisualMomentSample[] samples =
            signals.Gameplay
                .Where(
                    sample =>
                        window.Contains(sample.Sample.Timestamp))
                .ToArray();
        if (samples.Length == 0)
        {
            return (0, "No Gameplay activity samples are available for continuous-activity measurement.");
        }

        double occupancy =
            samples.Count(
                static sample =>
                    sample.Context.RawValue >=
                    Math.Max(0.03, sample.Context.LocalBaseline)) /
            (double)samples.Length;
        double occupancyExcess =
            Math.Clamp(
                (occupancy -
                 request.Options.CalibrationPolicy.ContinuousActivityOccupancyThreshold) /
                Math.Max(
                    0.000000001,
                    1 -
                    request.Options.CalibrationPolicy.ContinuousActivityOccupancyThreshold),
                0,
                1);
        double onset = burst?.OnsetStrength ?? samples.Max(static sample => sample.Context.OnsetStrength);
        double prominence = burst?.PeakProminence ?? samples.Max(static sample => sample.Context.NormalizedProminence);
        double returnToBaseline = burst?.ReturnToBaseline ?? samples.Max(static sample => sample.Context.ReturnToBaseline);
        double concentration = burst?.Concentration ?? samples.Max(static sample => sample.Context.LocalConcentration);
        double noveltyProtection =
            Math.Clamp(
                (onset + prominence + returnToBaseline + concentration) / 4d,
                0,
                1);
        double penalty =
            Math.Clamp(
                occupancyExcess *
                Math.Pow(1 - noveltyProtection, 3),
                0,
                1);
        return (
            penalty,
            $"Uniform activity occupancy {occupancy:0.###}; onset {onset:0.###}; prominence {prominence:0.###}; return {returnToBaseline:0.###}; concentration {concentration:0.###}.");
    }

    internal static double CalculateLowInformation(
        NormalizedMomentSignals signals,
        MomentCandidateWindow window)
    {
        NormalizedVisualMomentSample[] samples =
            signals.Gameplay
                .Where(sample => window.Contains(sample.Sample.Timestamp))
                .ToArray();
        return samples.Length == 0
            ? 0
            : samples.Count(
                  static sample =>
                      sample.Sample.NormalizedMeanLuma < 0.08 &&
                      sample.Sample.NormalizedLumaSpan < 0.05) /
              (double)samples.Length;
    }

    internal static double CalculateSourceEdgePenalty(
        MediaMomentFindingRequest request,
        ProposedMomentWindow proposal)
    {
        double missingLead =
            proposal.Window.Start == TimeSpan.Zero
                ? Math.Max(
                    0,
                    request.Options.CalibrationPolicy.MinimumLeadInContext.TotalSeconds -
                    proposal.Neighborhood.Start.TotalSeconds)
                : 0;
        double missingPayoff =
            proposal.Window.End == request.Media.Duration
                ? Math.Max(
                    0,
                    request.Options.CalibrationPolicy.MinimumPayoffContext.TotalSeconds -
                    (request.Media.Duration - proposal.Neighborhood.End).TotalSeconds)
                : 0;
        double requested =
            request.Options.CalibrationPolicy.MinimumLeadInContext.TotalSeconds +
            request.Options.CalibrationPolicy.MinimumPayoffContext.TotalSeconds;
        double contextPenalty = requested <= 0
            ? 0
            : Math.Clamp((missingLead + missingPayoff) / requested, 0, 1);
        double missingBaselinePenalty =
            proposal.Episode?.LocalBaselineBefore is null
                ? 0.50
                : 0;
        return Math.Max(
            contextPenalty,
            missingBaselinePenalty);
    }

    internal static double CalculateNeighborhoodRedundancy(
        IReadOnlyList<MomentAnchor> anchors)
    {
        if (anchors.Count < 2)
        {
            return 0;
        }

        int redundant =
            anchors
                .GroupBy(MomentSignalFamilyMap.FromAnchor)
                .Sum(static group => Math.Max(0, group.Count() - 1));
        return Math.Clamp(redundant / (double)anchors.Count, 0, 1);
    }

    internal static IEnumerable<MomentEvidenceReference> SceneReferences(
        IEnumerable<AttributedGameplaySceneBoundary> scenes) =>
        scenes.Select(
            scene =>
                new MomentEvidenceReference(
                    MomentEvidenceReferenceKind.SceneBoundary,
                    scene.Boundary.Timestamp,
                    scene.Boundary.Timestamp,
                    "Attributed Gameplay scene boundary",
                    scene.Result.Target.TargetKey,
                    scene.Result.Target.IntervalIndex,
                    scene.Result.Target.RegionId,
                    scene.Result.Target.Role,
                    rawValue: scene.Boundary.ScorePercent,
                    normalizedValue:
                        scene.Boundary.ScorePercent is null
                            ? null
                            : Math.Clamp(scene.Boundary.ScorePercent.Value / 100d, 0, 1)));

    internal static IEnumerable<MomentEvidenceReference> IntegrityReferences<TInterval>(
        string targetKey,
        IEnumerable<TInterval> intervals,
        MomentEvidenceReferenceKind kind,
        string description)
        where TInterval : class
    {
        foreach (TInterval item in intervals)
        {
            (TimeSpan start, TimeSpan end) =
                item switch
                {
                    BlackInterval black => (black.Start, black.End),
                    FreezeInterval freeze => (freeze.Start, freeze.End),
                    _ => throw new ArgumentOutOfRangeException(nameof(intervals)),
                };
            yield return new MomentEvidenceReference(
                kind,
                start,
                end,
                description,
                targetKey);
        }
    }

    internal static double IntegrityOverlapRatio(
        MediaMomentFindingRequest request,
        MomentCandidateWindow shapedWindow,
        MomentEventEpisode? episode,
        IReadOnlyList<(TimeSpan Start, TimeSpan End)> intervals)
    {
        double shapedRatio =
            MomentIntervalMath.OverlapRatio(
                shapedWindow,
                intervals);
        if (episode is null)
        {
            return shapedRatio;
        }

        var episodeWindow =
            new MomentCandidateWindow(
                episode.Start,
                episode.End,
                request.Media.Duration);
        return Math.Max(
            shapedRatio,
            MomentIntervalMath.OverlapRatio(
                episodeWindow,
                intervals));
    }

    internal static bool Intersects(
        MomentCandidateWindow window,
        TimeSpan start,
        TimeSpan end) =>
        start < window.End &&
        end > window.Start;

    internal static double Proximity(
        TimeSpan left,
        TimeSpan right,
        TimeSpan window) =>
        MomentAnchorBuilder.TemporalProximity(left, right, window);

    internal static double EpisodeProximity(
        TimeSpan timestamp,
        MomentEventEpisode? episode,
        MomentEventNeighborhood neighborhood,
        TimeSpan window) =>
        episode is not null &&
        timestamp >= episode.Start &&
        timestamp <= episode.End
            ? 1
            : Proximity(timestamp, neighborhood.PeakTimestamp, window);
}
