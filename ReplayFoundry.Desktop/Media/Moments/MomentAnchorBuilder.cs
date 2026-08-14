using System.IO;
using ReplayFoundry.Desktop.Media.Analysis.Summaries;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Media.Moments;

internal static class MomentAnchorBuilder
{
    public static IReadOnlyList<MomentAnchor> Build(
        MediaMomentFindingRequest request,
        NormalizedMomentSignals signals,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(signals);

        var primary =
            new List<MomentAnchor>();
        primary.AddRange(
            signals.GameplayBursts.Select(
                burst => CreateBurstAnchor(
                    request,
                    burst,
                    MomentAnchorKind.GameplayActivityBurst)));
        primary.AddRange(
            BuildSceneAnchors(
                request,
                signals.GameplayScenes,
                cancellationToken));

        var supporting =
            new List<MomentAnchor>();
        supporting.AddRange(
            BuildAudioAnchors(
                request,
                signals.AudioNoveltyEvents,
                signals.PresenterBursts,
                primary,
                cancellationToken));
        supporting.AddRange(
            BuildPresenterAnchors(
                request,
                signals.PresenterBursts,
                signals.AudioNoveltyEvents,
                primary,
                cancellationToken));

        MomentAnchor[] automatic = primary
            .Concat(supporting)
            .ToArray();
        IEnumerable<MomentAnchor> human =
            BuildHumanGuidanceAnchors(request, automatic);

        return automatic
            .Concat(human)
            .GroupBy(static anchor => anchor.Id, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static anchor => anchor.Timestamp)
            .ThenBy(static anchor => anchor.Kind)
            .ThenBy(static anchor => anchor.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<MomentAnchor> BuildHumanGuidanceAnchors(
        MediaMomentFindingRequest request,
        IReadOnlyList<MomentAnchor> automatic)
    {
        foreach (MediaMomentGuidanceItem item in request.Guidance.Items)
        {
            TimeSpan timestamp = item.Kind == MediaMomentGuidanceKind.PriorityPoint
                ? item.Start
                : automatic
                    .Where(anchor => anchor.Timestamp >= item.Start && anchor.Timestamp <= item.End)
                    .OrderByDescending(static anchor => anchor.NormalizedStrength)
                    .ThenBy(static anchor => anchor.Timestamp)
                    .Select(static anchor => (TimeSpan?)anchor.Timestamp)
                    .FirstOrDefault() ??
                  TimeSpan.FromTicks(item.Start.Ticks + item.Duration.Ticks / 2);
            yield return CreateAnchor(
                request,
                MomentAnchorKind.UserConfirmedPriority,
                timestamp,
                raw: 1,
                normalized: 1,
                [
                    new MomentEvidenceReference(
                        MomentEvidenceReferenceKind.UserConfirmedMomentGuidance,
                        item.Start,
                        item.End,
                        $"User-confirmed moment guidance {item.Id}"),
                ]);
        }
    }

    private static MomentAnchor CreateBurstAnchor(
        MediaMomentFindingRequest request,
        ActivityBurst burst,
        MomentAnchorKind kind)
    {
        MomentEvidenceReference envelope =
            new(
                burst.Role == CompositionRegionRole.Gameplay
                    ? MomentEvidenceReferenceKind.GameplayActivityBurst
                    : MomentEvidenceReferenceKind.PresenterActivitySample,
                burst.Start,
                burst.End,
                burst.Role == CompositionRegionRole.Gameplay
                    ? "Gameplay local-prominence activity burst"
                    : "Presenter local-prominence activity burst",
                burst.TargetKey,
                burst.EvidenceReferences[0].CompositionIntervalIndex,
                burst.EvidenceReferences[0].RegionId,
                burst.Role,
                rawValue: burst.RawPeakActivity,
                normalizedValue: burst.PeakProminence);

        return CreateAnchor(
            request,
            kind,
            burst.PeakTimestamp,
            burst.RawPeakActivity,
            burst.PeakProminence,
            burst.EvidenceReferences.Prepend(envelope));
    }

    private static IEnumerable<MomentAnchor> BuildSceneAnchors(
        MediaMomentFindingRequest request,
        IReadOnlyList<AttributedGameplaySceneBoundary> unique,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SceneBoundaryCluster> clusters =
            SceneBoundaryClusterBuilder.Build(
                unique.Select(static item => item.Boundary),
                request.Options.SceneClusterMaximumGap);

        foreach (SceneBoundaryCluster cluster in clusters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AttributedGameplaySceneBoundary[] members =
                unique
                    .Where(
                        item =>
                            item.Boundary.Timestamp >= cluster.Start &&
                            item.Boundary.Timestamp <= cluster.End)
                    .ToArray();

            if (cluster.BoundaryCount > 1)
            {
                TimeSpan timestamp =
                    TimeSpan.FromTicks(
                        cluster.Start.Ticks +
                        ((cluster.End.Ticks - cluster.Start.Ticks) / 2));
                yield return CreateAnchor(
                    request,
                    MomentAnchorKind.GameplaySceneCluster,
                    timestamp,
                    cluster.BoundaryCount,
                    Math.Clamp(
                        (cluster.BoundaryCount / 5d) *
                        ((cluster.MaximumScorePercent ?? 0) / 100d),
                        0,
                        1),
                    members.Select(
                        item =>
                            CreateSceneReference(
                                item,
                                MomentEvidenceReferenceKind.SceneCluster,
                                cluster.Start,
                                cluster.End,
                                $"Gameplay scene cluster containing {cluster.BoundaryCount} boundaries")));
            }
            else if (members.Length == 1)
            {
                AttributedGameplaySceneBoundary member = members[0];
                double raw = member.Boundary.ScorePercent ?? 0;
                yield return CreateAnchor(
                    request,
                    MomentAnchorKind.GameplaySceneBoundary,
                    member.Boundary.Timestamp,
                    raw,
                    Math.Clamp(raw / 100d, 0, 1),
                    [
                        CreateSceneReference(
                            member,
                            MomentEvidenceReferenceKind.SceneBoundary,
                            member.Boundary.Timestamp,
                            member.Boundary.Timestamp,
                            "Isolated Gameplay scene boundary"),
                    ]);
            }
        }
    }

    private static IEnumerable<MomentAnchor> BuildAudioAnchors(
        MediaMomentFindingRequest request,
        IReadOnlyList<AudioNoveltyEvent> events,
        IReadOnlyList<ActivityBurst> presenterBursts,
        IReadOnlyList<MomentAnchor> primary,
        CancellationToken cancellationToken)
    {
        foreach (AudioNoveltyEvent item in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MomentAnchor? gate =
                FindNearest(
                    primary,
                    item.PeakTimestamp,
                    request.Options.CrossSignalAgreementWindow);
            ActivityBurst? presenter =
                presenterBursts
                    .Where(
                        burst =>
                            (burst.PeakTimestamp - item.PeakTimestamp).Duration() <=
                            request.Options.CrossSignalAgreementWindow)
                    .OrderByDescending(static burst => burst.PeakProminence)
                    .ThenBy(static burst => burst.PeakTimestamp)
                    .FirstOrDefault();

            bool commentaryPair =
                request.Options.ContentEmphasis ==
                    MomentContentEmphasis.CommentaryFocused &&
                presenter is not null;
            if (gate is null && !commentaryPair)
            {
                continue;
            }

            double proximity =
                gate is null
                    ? 1
                    : TemporalProximity(
                        gate.Timestamp,
                        item.PeakTimestamp,
                        request.Options.CrossSignalAgreementWindow);
            double gateStrength =
                gate?.NormalizedStrength ??
                presenter!.PeakProminence;
            double strength =
                Math.Clamp(
                    item.NormalizedProminence *
                    proximity *
                    gateStrength,
                    0,
                    1);

            yield return CreateAnchor(
                request,
                item.IsSilenceReentry
                    ? MomentAnchorKind.AudioReentry
                    : MomentAnchorKind.AudioNovelty,
                item.PeakTimestamp,
                item.PeakFiniteRmsDbfs,
                strength,
                item.EvidenceReferences.Concat(
                    gate?.EvidenceReferences ?? []));
        }
    }

    private static IEnumerable<MomentAnchor> BuildPresenterAnchors(
        MediaMomentFindingRequest request,
        IReadOnlyList<ActivityBurst> presenterBursts,
        IReadOnlyList<AudioNoveltyEvent> audioEvents,
        IReadOnlyList<MomentAnchor> primary,
        CancellationToken cancellationToken)
    {
        foreach (ActivityBurst burst in presenterBursts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MomentAnchor? gate =
                FindNearest(
                    primary,
                    burst.PeakTimestamp,
                    request.Options.CrossSignalAgreementWindow);
            AudioNoveltyEvent? audio =
                audioEvents
                    .Where(
                        item =>
                            (item.PeakTimestamp - burst.PeakTimestamp).Duration() <=
                            request.Options.CrossSignalAgreementWindow)
                    .OrderByDescending(static item => item.NormalizedProminence)
                    .ThenBy(static item => item.PeakTimestamp)
                    .FirstOrDefault();
            bool commentaryPair =
                request.Options.ContentEmphasis ==
                    MomentContentEmphasis.CommentaryFocused &&
                audio is not null;
            if (gate is null && !commentaryPair)
            {
                continue;
            }

            double partnerStrength =
                gate?.NormalizedStrength ??
                audio!.NormalizedProminence;
            TimeSpan partnerTime =
                gate?.Timestamp ??
                audio!.PeakTimestamp;
            double strength =
                Math.Clamp(
                    burst.PeakProminence *
                    TemporalProximity(
                        burst.PeakTimestamp,
                        partnerTime,
                        request.Options.CrossSignalAgreementWindow) *
                    partnerStrength,
                    0,
                    1);
            MomentEvidenceReference envelope =
                new(
                    MomentEvidenceReferenceKind.PresenterActivitySample,
                    burst.Start,
                    burst.End,
                    "Confirmed Presenter local-prominence burst gated by a coincident event",
                    burst.TargetKey,
                    burst.EvidenceReferences[0].CompositionIntervalIndex,
                    burst.EvidenceReferences[0].RegionId,
                    CompositionRegionRole.Presenter,
                    rawValue: burst.RawPeakActivity,
                    normalizedValue: strength);

            IEnumerable<MomentEvidenceReference> partnerReferences =
                gate?.EvidenceReferences ??
                audio!.EvidenceReferences;
            yield return CreateAnchor(
                request,
                commentaryPair
                    ? MomentAnchorKind.PresenterAudioAgreement
                    : MomentAnchorKind.PresenterGatedSupport,
                burst.PeakTimestamp,
                burst.RawPeakActivity,
                strength,
                burst.EvidenceReferences
                    .Prepend(envelope)
                    .Concat(partnerReferences));
        }
    }

    private static MomentAnchor? FindNearest(
        IReadOnlyList<MomentAnchor> anchors,
        TimeSpan timestamp,
        TimeSpan window) =>
        anchors
            .Where(anchor => (anchor.Timestamp - timestamp).Duration() <= window)
            .OrderBy(anchor => (anchor.Timestamp - timestamp).Duration())
            .ThenByDescending(static anchor => anchor.NormalizedStrength)
            .ThenBy(static anchor => anchor.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    internal static double TemporalProximity(
        TimeSpan left,
        TimeSpan right,
        TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
        {
            return left == right ? 1 : 0;
        }

        return Math.Clamp(
            1 -
            ((left - right).Duration().TotalSeconds /
             window.TotalSeconds),
            0,
            1);
    }

    private static MomentAnchor CreateAnchor(
        MediaMomentFindingRequest request,
        MomentAnchorKind kind,
        TimeSpan timestamp,
        double raw,
        double normalized,
        IEnumerable<MomentEvidenceReference> references)
    {
        MomentEvidenceReference[] snapshot =
            references
                .GroupBy(
                    static item =>
                        (item.Kind, item.Start, item.End, item.VisualTargetKey, item.AudioStreamIndex))
                .Select(static group => group.First())
                .OrderBy(static item => item.Start)
                .ThenBy(static item => item.Kind)
                .ThenBy(static item => item.VisualTargetKey, StringComparer.Ordinal)
                .ThenBy(static item => item.AudioStreamIndex)
                .ToArray();

        return new MomentAnchor(
            MomentStableId.Create(
                "a",
                Path.GetFullPath(request.Media.FullPath).ToUpperInvariant(),
                kind,
                timestamp,
                string.Join(
                    "|",
                    snapshot.Select(
                        static item =>
                            $"{item.Kind}:{item.VisualTargetKey}:{item.AudioStreamIndex}:{item.Start.Ticks}:{item.End.Ticks}"))),
            kind,
            timestamp,
            raw,
            normalized,
            snapshot);
    }

    private static MomentEvidenceReference CreateSceneReference(
        AttributedGameplaySceneBoundary item,
        MomentEvidenceReferenceKind kind,
        TimeSpan start,
        TimeSpan end,
        string description) =>
        new(
            kind,
            start,
            end,
            description,
            item.Result.Target.TargetKey,
            item.Result.Target.IntervalIndex,
            item.Result.Target.RegionId,
            item.Result.Target.Role,
            rawValue: item.Boundary.ScorePercent);
}
