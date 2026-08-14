using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class MomentWindowContextAllocation
{
    public MomentWindowContextAllocation(
        TimeSpan requestedLeadIn,
        TimeSpan achievedLeadIn,
        TimeSpan requestedRecovery,
        TimeSpan achievedRecovery,
        bool reallocatedAtSourceEdge)
    {
        if (requestedLeadIn < TimeSpan.Zero || achievedLeadIn < TimeSpan.Zero ||
            requestedRecovery < TimeSpan.Zero || achievedRecovery < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedLeadIn));
        }
        RequestedLeadIn = requestedLeadIn;
        AchievedLeadIn = achievedLeadIn;
        RequestedRecovery = requestedRecovery;
        AchievedRecovery = achievedRecovery;
        ReallocatedAtSourceEdge = reallocatedAtSourceEdge;
    }

    public TimeSpan RequestedLeadIn { get; }
    public TimeSpan AchievedLeadIn { get; }
    public TimeSpan RequestedRecovery { get; }
    public TimeSpan AchievedRecovery { get; }
    public bool ReallocatedAtSourceEdge { get; }
}

public sealed class MontageSegmentObjectiveComponent
{
    public MontageSegmentObjectiveComponent(
        MontageSegmentObjectiveComponentCode code,
        double normalizedValue,
        double signedWeight,
        string explanation)
    {
        if (!Enum.IsDefined(code) ||
            !double.IsFinite(normalizedValue) || normalizedValue is < 0 or > 1 ||
            !double.IsFinite(signedWeight) || signedWeight is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(normalizedValue));
        }
        if (string.IsNullOrWhiteSpace(explanation))
        {
            throw new ArgumentException("Objective component explanation cannot be blank.", nameof(explanation));
        }
        Code = code;
        NormalizedValue = normalizedValue;
        SignedWeight = signedWeight;
        SignedContribution = normalizedValue * signedWeight;
        Explanation = explanation.Trim();
    }

    public MontageSegmentObjectiveComponentCode Code { get; }
    public double NormalizedValue { get; }
    public double SignedWeight { get; }
    public double SignedContribution { get; }
    public string Explanation { get; }
}

public sealed class MontageSegmentObjective
{
    private readonly ReadOnlyCollection<MontageSegmentObjectiveComponent> _components;
    private readonly ReadOnlyCollection<MomentEventEpisodePhaseKind> _coveredPhases;

    public MontageSegmentObjective(
        IEnumerable<MontageSegmentObjectiveComponent> components,
        IEnumerable<MomentEventEpisodePhaseKind> coveredPhases)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(coveredPhases);
        MontageSegmentObjectiveComponent[] snapshot = components.ToArray();
        MomentEventEpisodePhaseKind[] phases = coveredPhases.Distinct().OrderBy(static item => item).ToArray();
        if (snapshot.Any(static item => item is null) ||
            snapshot.GroupBy(static item => item.Code).Any(static group => group.Count() > 1) ||
            phases.Any(static item => !Enum.IsDefined(item)))
        {
            throw new ArgumentException("Montage objective components and phases must be unique and valid.");
        }
        _components = Array.AsReadOnly(snapshot);
        _coveredPhases = Array.AsReadOnly(phases);
        RawTotal = snapshot.Sum(static item => item.SignedContribution);
        ObjectiveScore = Math.Clamp(RawTotal, 0, 1);
    }

    public IReadOnlyList<MontageSegmentObjectiveComponent> Components => _components;
    public IReadOnlyList<MomentEventEpisodePhaseKind> CoveredPhases => _coveredPhases;
    public double RawTotal { get; }
    public double ObjectiveScore { get; }
}

internal static class StandaloneMomentWindowShaper
{
    public static ProposedMomentWindow Shape(
        MediaMomentFindingRequest request,
        MomentEventEpisode episode,
        IReadOnlyList<MomentAnchor> anchors)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(episode);
        MomentAnchor[] bounded = BoundedAnchors(episode, anchors);
        MomentEventNeighborhood neighborhood = ProjectNeighborhood(request, episode, bounded);
        TimeSpan sourceDuration = request.Media.Duration;
        MediaMomentGuidanceItem? reserved = FindReservedRange(request, episode);
        if (reserved is not null)
        {
            return ShapeReservedRange(
                request,
                episode,
                neighborhood,
                reserved);
        }
        if (sourceDuration < request.Options.MinimumDuration)
        {
            return new ProposedMomentWindow(
                new MomentCandidateWindow(TimeSpan.Zero, sourceDuration, sourceDuration),
                MomentCandidateConstructionReason.ShortSource,
                neighborhood,
                bounded,
                episode,
                new MomentWindowContextAllocation(
                    request.Options.CalibrationPolicy.MinimumLeadInContext,
                    episode.Start,
                    request.Options.CalibrationPolicy.MinimumPayoffContext,
                    sourceDuration - episode.End,
                    true));
        }

        TimeSpan preferredLead = TimeSpan.FromTicks(
            Math.Max(
                request.Options.PreRoll.Ticks,
                request.Options.CalibrationPolicy.MinimumLeadInContext.Ticks));
        TimeSpan preferredRecovery = TimeSpan.FromTicks(
            Math.Max(
                request.Options.PostRoll.Ticks,
                request.Options.CalibrationPolicy.MinimumPayoffContext.Ticks));
        TimeSpan desired = TimeSpan.FromTicks(Math.Min(
            request.Options.MaximumDuration.Ticks,
            Math.Max(
                request.Options.TargetDuration.Ticks,
                episode.Duration.Ticks +
                preferredLead.Ticks +
                preferredRecovery.Ticks)));
        TimeSpan episodeStart = episode.Start;
        TimeSpan episodeEnd = episode.End;
        if (episode.Duration > request.Options.MaximumDuration)
        {
            long leadTicks = Math.Min(
                request.Options.CalibrationPolicy.MinimumLeadInContext.Ticks,
                request.Options.MaximumDuration.Ticks / 3);
            episodeStart = episode.OnsetTimestamp - TimeSpan.FromTicks(leadTicks);
            episodeStart = TimeSpan.FromTicks(Math.Max(0, episodeStart.Ticks));
            episodeEnd = episodeStart + request.Options.MaximumDuration;
            if (episodeEnd > sourceDuration)
            {
                episodeEnd = sourceDuration;
                episodeStart = sourceDuration - request.Options.MaximumDuration;
            }
            desired = request.Options.MaximumDuration;
        }

        TimeSpan context = TimeSpan.FromTicks(
            Math.Max(
                0,
                desired.Ticks - (episodeEnd - episodeStart).Ticks));
        TimeSpan minimumRecovery = TimeSpan.FromTicks(
            Math.Min(
                context.Ticks,
                request.Options.CalibrationPolicy.MinimumPayoffContext.Ticks));
        TimeSpan requestedLead = TimeSpan.FromTicks(
            Math.Min(
                preferredLead.Ticks,
                Math.Max(0, context.Ticks - minimumRecovery.Ticks)));
        TimeSpan requestedRecovery = TimeSpan.FromTicks(
            Math.Min(
                preferredRecovery.Ticks,
                context.Ticks - requestedLead.Ticks));
        TimeSpan unallocated =
            context - requestedLead - requestedRecovery;
        requestedLead += TimeSpan.FromTicks((long)Math.Round(
            unallocated.Ticks *
            request.Options.CalibrationPolicy.ClusterLeadInShare,
            MidpointRounding.AwayFromZero));
        requestedRecovery = context - requestedLead;
        TimeSpan start = episodeStart - requestedLead;
        TimeSpan end = episodeEnd + requestedRecovery;
        bool reallocated = ClampAndReallocate(ref start, ref end, sourceDuration, desired);
        if (end - start < request.Options.MinimumDuration)
        {
            desired = request.Options.MinimumDuration;
            end = start + desired;
            reallocated |= ClampAndReallocate(ref start, ref end, sourceDuration, desired);
        }
        var shapedWindow = new MomentCandidateWindow(start, end, sourceDuration);
        MomentAnchor[] shapedAnchors = neighborhood.Anchors
            .Where(item => shapedWindow.Contains(item.Timestamp))
            .DefaultIfEmpty(CreateSyntheticAnchor(episode))
            .ToArray();
        neighborhood = ProjectNeighborhood(request, episode, shapedAnchors);
        return new ProposedMomentWindow(
            shapedWindow,
            MomentCandidateConstructionReason.StandaloneEpisode,
            neighborhood,
            shapedAnchors,
            episode,
            new MomentWindowContextAllocation(
                requestedLead,
                episode.Start - start < TimeSpan.Zero ? TimeSpan.Zero : episode.Start - start,
                requestedRecovery,
                end - episode.End < TimeSpan.Zero ? TimeSpan.Zero : end - episode.End,
                reallocated));
    }

    private static ProposedMomentWindow ShapeReservedRange(
        MediaMomentFindingRequest request,
        MomentEventEpisode episode,
        MomentEventNeighborhood neighborhood,
        MediaMomentGuidanceItem guidance)
    {
        TimeSpan desired = guidance.Duration <= request.Options.MinimumDuration
            ? guidance.Duration
            : TimeSpan.FromTicks(
                Math.Min(
                    guidance.Duration.Ticks,
                    Math.Clamp(
                        request.Options.TargetDuration.Ticks,
                        request.Options.MinimumDuration.Ticks,
                        request.Options.MaximumDuration.Ticks)));
        long preferredLead = Math.Min(
            request.Options.PreRoll.Ticks,
            desired.Ticks / 2);
        long minimumStart = Math.Max(
            guidance.Start.Ticks,
            episode.PrimaryPeakTimestamp.Ticks - desired.Ticks);
        long maximumStart = Math.Min(
            episode.PrimaryPeakTimestamp.Ticks,
            guidance.End.Ticks - desired.Ticks);
        TimeSpan start = TimeSpan.FromTicks(
            Math.Clamp(
                episode.OnsetTimestamp.Ticks - preferredLead,
                minimumStart,
                maximumStart));
        TimeSpan end = start + desired;
        var window = new MomentCandidateWindow(
            start,
            end,
            request.Media.Duration);
        MomentAnchor[] shapedAnchors = neighborhood.Anchors
            .Where(item => window.Contains(item.Timestamp))
            .DefaultIfEmpty(CreateSyntheticAnchor(episode))
            .ToArray();
        neighborhood = ProjectNeighborhood(request, episode, shapedAnchors);
        return new ProposedMomentWindow(
            window,
            MomentCandidateConstructionReason.StandaloneEpisode,
            neighborhood,
            shapedAnchors,
            episode,
            new MomentWindowContextAllocation(
                request.Options.PreRoll,
                episode.Start > start ? episode.Start - start : TimeSpan.Zero,
                request.Options.PostRoll,
                end > episode.End ? end - episode.End : TimeSpan.Zero,
                start == guidance.Start || end == guidance.End));
    }

    internal static MediaMomentGuidanceItem? FindReservedRange(
        MediaMomentFindingRequest request,
        MomentEventEpisode episode) =>
        request.Guidance.Items
            .Where(
                item => item.ReservesCandidateSearch &&
                        episode.PrimaryPeakTimestamp >= item.Start &&
                        episode.PrimaryPeakTimestamp <= item.End)
            .OrderBy(static item => item.Duration)
            .ThenBy(static item => item.Start)
            .ThenBy(static item => item.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    internal static MomentEventNeighborhood ProjectNeighborhood(
        MediaMomentFindingRequest request,
        MomentEventEpisode episode,
        IReadOnlyList<MomentAnchor> anchors)
    {
        MomentAnchor[] effective = anchors.Count > 0
            ? anchors.ToArray()
            :
            [
                CreateSyntheticAnchor(episode),
            ];
        return new MomentEventNeighborhood(
            MomentStableId.Create("n", request.Media.FullPath.ToUpperInvariant(), episode.Id),
            episode.Start,
            episode.PrimaryPeakTimestamp,
            episode.End,
            effective,
            effective.Select(MomentSignalFamilyMap.FromAnchor));
    }

    internal static MomentAnchor[] BoundedAnchors(
        MomentEventEpisode episode,
        IReadOnlyList<MomentAnchor> anchors) =>
        anchors
            .Where(item => item.Timestamp >= episode.Start && item.Timestamp <= episode.End)
            .OrderBy(static item => item.Timestamp)
            .ThenBy(static item => item.Id, StringComparer.Ordinal)
            .ToArray();

    internal static MomentAnchor CreateSyntheticAnchor(MomentEventEpisode episode) =>
        new(
            MomentStableId.Create("a", episode.Id, episode.PrimaryPeakTimestamp),
            MomentAnchorKind.EpisodeActivationPeak,
            episode.PrimaryPeakTimestamp,
            episode.PeakActivation,
            episode.PeakActivation,
            episode.EvidenceSummary.EvidenceReferences.Count > 0
                ? episode.EvidenceSummary.EvidenceReferences
                :
                [
                    new MomentEvidenceReference(
                        MomentEvidenceReferenceKind.GameplayActivitySample,
                        episode.PrimaryPeakTimestamp,
                        episode.PrimaryPeakTimestamp,
                        "Episode activation peak"),
                ]);

    internal static bool ClampAndReallocate(
        ref TimeSpan start,
        ref TimeSpan end,
        TimeSpan sourceDuration,
        TimeSpan intendedDuration)
    {
        bool reallocated = false;
        if (start < TimeSpan.Zero)
        {
            end -= start;
            start = TimeSpan.Zero;
            reallocated = true;
        }
        if (end > sourceDuration)
        {
            TimeSpan overflow = end - sourceDuration;
            start = TimeSpan.FromTicks(Math.Max(0, start.Ticks - overflow.Ticks));
            end = sourceDuration;
            reallocated = true;
        }
        if (end - start < intendedDuration && intendedDuration <= sourceDuration)
        {
            if (start == TimeSpan.Zero)
            {
                end = intendedDuration;
            }
            else if (end == sourceDuration)
            {
                start = sourceDuration - intendedDuration;
            }
            reallocated = true;
        }
        return reallocated;
    }
}

internal static class MontageMomentSegmentShaper
{
    public static ProposedMomentWindow Shape(
        MediaMomentFindingRequest request,
        MomentEventEpisode episode,
        MomentActivationSeries activation,
        IReadOnlyList<MomentAnchor> anchors,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(episode);
        ArgumentNullException.ThrowIfNull(activation);
        MomentAnchor[] bounded = StandaloneMomentWindowShaper.BoundedAnchors(episode, anchors);
        MomentEventNeighborhood neighborhood = StandaloneMomentWindowShaper.ProjectNeighborhood(request, episode, bounded);
        MediaMomentGuidanceItem? reserved =
            StandaloneMomentWindowShaper.FindReservedRange(request, episode);
        TimeSpan duration = TimeSpan.FromTicks(Math.Min(
            request.Options.MaximumDuration.Ticks,
            Math.Max(request.Options.MinimumDuration.Ticks, request.Options.TargetDuration.Ticks)));
        if (reserved is not null)
        {
            duration = TimeSpan.FromTicks(
                Math.Min(duration.Ticks, reserved.Duration.Ticks));
        }
        duration = TimeSpan.FromTicks(Math.Min(duration.Ticks, request.Media.Duration.Ticks));
        long lowerBound = reserved?.Start.Ticks ?? 0;
        long upperBound = reserved?.End.Ticks ?? request.Media.Duration.Ticks;
        TimeSpan earliest = TimeSpan.FromTicks(Math.Max(
            lowerBound,
            episode.PrimaryPeakTimestamp.Ticks - duration.Ticks));
        TimeSpan latest = TimeSpan.FromTicks(Math.Min(
            episode.PrimaryPeakTimestamp.Ticks,
            upperBound - duration.Ticks));
        TimeSpan cadence = activation.Coverage.Cadence;
        var candidates = new List<(MomentCandidateWindow Window, MontageSegmentObjective Objective)>();
        for (TimeSpan start = earliest; start <= latest; start += cadence)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan end = start + duration;
            var window = new MomentCandidateWindow(start, end, request.Media.Duration);
            candidates.Add((window, Evaluate(request, episode, activation, window)));
            if (cadence <= TimeSpan.Zero)
            {
                break;
            }
        }
        if (candidates.Count == 0)
        {
            TimeSpan start = TimeSpan.FromTicks(Math.Clamp(
                episode.PrimaryPeakTimestamp.Ticks - duration.Ticks / 2,
                0,
                request.Media.Duration.Ticks - duration.Ticks));
            var window = new MomentCandidateWindow(start, start + duration, request.Media.Duration);
            candidates.Add((window, Evaluate(request, episode, activation, window)));
        }
        var best = candidates
            .OrderByDescending(static item => item.Objective.ObjectiveScore)
            .ThenBy(item => (Midpoint(item.Window) - episode.PrimaryPeakTimestamp).Duration())
            .ThenBy(static item => item.Window.Start)
            .First();
        MomentAnchor[] shapedAnchors = neighborhood.Anchors
            .Where(item => best.Window.Contains(item.Timestamp))
            .DefaultIfEmpty(StandaloneMomentWindowShaper.CreateSyntheticAnchor(episode))
            .ToArray();
        neighborhood = StandaloneMomentWindowShaper.ProjectNeighborhood(request, episode, shapedAnchors);
        return new ProposedMomentWindow(
            best.Window,
            MomentCandidateConstructionReason.MontageRepresentativeSegment,
            neighborhood,
            shapedAnchors,
            episode,
            new MomentWindowContextAllocation(
                request.Options.PreRoll,
                episode.OnsetTimestamp - best.Window.Start < TimeSpan.Zero ? TimeSpan.Zero : episode.OnsetTimestamp - best.Window.Start,
                request.Options.PostRoll,
                best.Window.End - episode.End < TimeSpan.Zero ? TimeSpan.Zero : best.Window.End - episode.End,
                best.Window.Start == TimeSpan.Zero || best.Window.End == request.Media.Duration),
            best.Objective,
            episode.SplitRationale == MomentEpisodeSplitRationale.None
                ? MontageSegmentSelectionReason.PrimaryEpisodeRepresentative
                : MontageSegmentSelectionReason.ValidatedSubepisodeRepresentative);
    }

    private static MontageSegmentObjective Evaluate(
        MediaMomentFindingRequest request,
        MomentEventEpisode episode,
        MomentActivationSeries activation,
        MomentCandidateWindow window)
    {
        MomentActivationSample[] episodeSamples = activation.Samples
            .Where(item => item.Timestamp >= episode.Start && item.Timestamp <= episode.End)
            .ToArray();
        MomentActivationSample[] inside = episodeSamples.Where(item => window.Contains(item.Timestamp)).ToArray();
        double episodeTotal = episodeSamples.Sum(static item => item.SmoothedCombinedActivation);
        double coverage = episodeTotal <= 0 ? 0 : inside.Sum(static item => item.SmoothedCombinedActivation) / episodeTotal;
        double peak = window.Contains(episode.PrimaryPeakTimestamp) ? 1 : 0;
        double onset = Math.Clamp(
            1 - Math.Abs((window.Start - episode.OnsetTimestamp).TotalSeconds) /
            Math.Max(0.001, window.Duration.TotalSeconds),
            0,
            1);
        double recovery = window.End >= episode.End
            ? 1
            : Math.Clamp(1 - (episode.End - window.End).TotalSeconds / Math.Max(0.001, window.Duration.TotalSeconds), 0, 1);
        double agreement = episode.EvidenceSummary.DominantSignalFamilies.Count <= 1
            ? 0
            : Math.Clamp((episode.EvidenceSummary.DominantSignalFamilies.Count - 1) / 4d, 0, 1);
        bool hasScene = episode.EvidenceSummary.DominantSignalFamilies.Contains(MomentSignalFamily.GameplayScene);
        double integrity = inside.Length == 0
            ? 0
            : inside.Count(static item => item.IntegrityState != MomentActivationIntegrityState.Clear) / (double)inside.Length;
        double edge = window.Start == TimeSpan.Zero || window.End == request.Media.Duration ? 1 : 0;
        var components = new[]
        {
            new MontageSegmentObjectiveComponent(MontageSegmentObjectiveComponentCode.IntegratedActivationCoverage, coverage, 0.30, "Fraction of episode activation covered."),
            new MontageSegmentObjectiveComponent(MontageSegmentObjectiveComponentCode.PeakContainment, peak, 0.22, "Whether the primary activation peak is contained."),
            new MontageSegmentObjectiveComponent(MontageSegmentObjectiveComponentCode.OnsetProximity, onset, 0.22, "Proximity of the segment start to measured onset."),
            new MontageSegmentObjectiveComponent(MontageSegmentObjectiveComponentCode.RecoveryCoverage, recovery, 0.08, "Measured recovery coverage."),
            new MontageSegmentObjectiveComponent(MontageSegmentObjectiveComponentCode.MultiSignalAgreement, agreement, 0.08, "Agreement among observable signal families."),
            new MontageSegmentObjectiveComponent(MontageSegmentObjectiveComponentCode.SceneClusterContainment, hasScene ? 1 : 0, 0.06, "Gameplay scene-cluster support."),
            new MontageSegmentObjectiveComponent(MontageSegmentObjectiveComponentCode.IntegrityPenalty, integrity, -0.18, "Full-frame capture-integrity overlap."),
            new MontageSegmentObjectiveComponent(MontageSegmentObjectiveComponentCode.SourceEdgePenalty, edge, -0.04, "Source-edge context constraint."),
            new MontageSegmentObjectiveComponent(MontageSegmentObjectiveComponentCode.IntraEpisodeRedundancyPenalty, 0, -0.10, "No earlier segment is shaped from this episode."),
        };
        MomentEventEpisodePhaseKind[] phases = episode.Phases
            .Where(item => item.Start < window.End && item.End >= window.Start)
            .Select(static item => item.Kind)
            .ToArray();
        return new MontageSegmentObjective(components, phases);
    }

    private static TimeSpan Midpoint(MomentCandidateWindow window) =>
        TimeSpan.FromTicks(window.Start.Ticks + window.Duration.Ticks / 2);
}
