using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal static class GenerationSemanticExplorationPlanner
{
    public static GenerationMomentFindingResult Expand(
        GenerationMomentFindingResult original,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        int remaining = GenerationSemanticReviewBudgetPolicy.MaximumCandidates;
        int sourcesRemaining = original.Sources.Count;
        var sources = new List<GenerationSourceMomentResult>();
        foreach (GenerationSourceMomentResult source in original.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int windows = Math.Min(remaining,
                Math.Min(Math.Max(1, remaining / sourcesRemaining),
                    (int)Math.Clamp(Math.Ceiling(source.AnalyzedSource.PreparedSource.Media.Duration.TotalMinutes / 5), 2, 8)));
            sources.Add(windows == 0 ? source : new GenerationSourceMomentResult(
                source.AnalyzedSource, Expand(source.Moments, windows, cancellationToken)));
            remaining -= windows;
            sourcesRemaining--;
        }
        return new(original.Request, sources, original.SelectedCandidates);
    }

    public static MediaMomentFindingResult Expand(
        MediaMomentFindingResult original,
        int maximumWindows,
        CancellationToken cancellationToken,
        IReadOnlyList<AudioTranscriptionSegment>? transcriptSeeds = null,
        IReadOnlyList<GenerationTimedExplorationSeed>? semanticSeeds = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (maximumWindows is < 1 or > GenerationSemanticReviewBudgetPolicy.MaximumCandidates)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumWindows));
        }
        MediaMomentFindingRequest request = original.Request;
        TimeSpan duration = request.Media.Duration;
        long windowTicks = Math.Min(duration.Ticks,
            Math.Min(request.Options.TargetDuration.Ticks,
                Math.Min(request.Options.MaximumDuration.Ticks, TimeSpan.FromSeconds(60).Ticks)));
        var proposals = original.Proposals.ToList();
        for (int index = 0; index < maximumWindows; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long middle = (long)(duration.Ticks * ((index + 0.5) / maximumWindows));
            long startTicks = Math.Clamp(middle - windowTicks / 2,
                0, duration.Ticks - windowTicks);
            AudioTranscriptionSegment? seed = transcriptSeeds is not null && index < transcriptSeeds.Count ? transcriptSeeds[index] : null;
            GenerationTimedExplorationSeed? semanticSeed = semanticSeeds is not null && index < semanticSeeds.Count ? semanticSeeds[index] : null;
            long currentWindowTicks = windowTicks;
            if (seed is not null || semanticSeed is not null)
            {
                TimeSpan seedStart = semanticSeed?.Start ?? seed!.AbsoluteSourceStart;
                TimeSpan seedEnd = semanticSeed?.End ?? seed!.AbsoluteSourceEnd;
                if (seedStart < TimeSpan.Zero || seedEnd <= seedStart || seedEnd > duration)
                    throw new ArgumentException("A transcript exploration seed must be bounded to its retained source.");
                long seedTicks = seedEnd.Ticks - seedStart.Ticks;
                long required = seedTicks + TimeSpan.FromSeconds(1.5).Ticks;
                long maximum = Math.Min(request.Options.MaximumDuration.Ticks, TimeSpan.FromSeconds(60).Ticks);
                if (semanticSeed is not null)
                {
                    // A semantic nomination is an alternative edit around a complete
                    // retrieved passage. Inflating it to the heuristic target can
                    // consume the room needed for later speech-boundary repair.
                    if (seedTicks > maximum || duration < request.Options.MinimumDuration)
                        continue;
                    currentWindowTicks = Math.Max(request.Options.MinimumDuration.Ticks,
                        Math.Min(duration.Ticks, Math.Min(maximum, required)));
                    startTicks = Math.Clamp(seedStart.Ticks - TimeSpan.FromMilliseconds(750).Ticks,
                        Math.Max(0, seedEnd.Ticks - currentWindowTicks),
                        Math.Min(seedStart.Ticks, duration.Ticks - currentWindowTicks));
                }
                else
                {
                    if (required > maximum)
                        continue;
                    currentWindowTicks = Math.Min(duration.Ticks, Math.Max(windowTicks, required));
                    startTicks = Math.Clamp(seedStart.Ticks - TimeSpan.FromMilliseconds(750).Ticks,
                        0, duration.Ticks - currentWindowTicks);
                }
            }
            var window = new MomentCandidateWindow(TimeSpan.FromTicks(startTicks),
                TimeSpan.FromTicks(startTicks + currentWindowTicks), duration);
            // A nearby heuristic cut may start or end inside the retrieved
            // sentence. Only an identical edit can stand in for this nomination;
            // ordinary source/literal exploration keeps its overlap policy.
            if (proposals.Any(candidate => semanticSeed is not null
                    ? candidate.Window.Start == window.Start && candidate.Window.End == window.End
                    : Overlap(candidate.Window, window) >= 0.5))
            {
                continue;
            }
            double black = MomentIntervalMath.OverlapRatio(window,
                request.Evidence.FullFrame.BlackIntervals.Select(static interval =>
                    (interval.Start, interval.End)));
            double freeze = MomentIntervalMath.OverlapRatio(window,
                request.Evidence.FullFrame.FreezeIntervals.Select(static interval =>
                    (interval.Start, interval.End)));
            // Exploration must not bypass capture-integrity requirements.
            if (black >= request.Options.FullFrameBlackHardRejectionRatio ||
                freeze >= request.Options.FullFrameFreezeHardRejectionRatio)
            {
                continue;
            }
            string id = MomentStableId.Create(semanticSeed is not null ? "semantic-text" : seed is null ? "explore" : "spoken", request.Media.FullPath,
                window.Start, window.End);
            var evidence = new MomentEvidenceReference(
                seed is null && semanticSeed is null ? MomentEvidenceReferenceKind.SourceCoverage : MomentEvidenceReferenceKind.TranscriptSegment,
                window.Start, window.End,
                semanticSeed is not null ? semanticSeed.EvidenceDescription : seed is null
                    ? "A uniformly sampled source window awaiting semantic review; no event is inferred from its position."
                    : $"A spoken passage ({seed.Id}) outside the heuristic selection, awaiting review of its meaning and context.");
            var anchor = new MomentAnchor(id + "-coverage", MomentAnchorKind.SourceCoverage,
                TimeSpan.FromTicks(startTicks + currentWindowTicks / 2), 0, 0, [evidence]);
            var neighborhood = new MomentEventNeighborhood(id + "-window",
                window.Start, anchor.Timestamp, window.End, [anchor],
                [MomentSignalFamily.SourceCoverage]);
            var gameplay = request.Evidence.RegionVisualResults.Where(static result =>
                result.Target.Role == CompositionRegionRole.Gameplay).ToArray();
            proposals.Add(new MomentCandidate(id, window,
                MomentCandidateConstructionReason.SemanticExploration,
                neighborhood, [anchor], new MomentScore([]),
                MomentCandidateDisposition.BelowThreshold, black, freeze,
                MomentIntervalMath.OverlapRatio(window, gameplay.SelectMany(static result =>
                    result.BlackIntervals.Select(static interval => (interval.Start, interval.End)))),
                MomentIntervalMath.OverlapRatio(window, gameplay.SelectMany(static result =>
                    result.FreezeIntervals.Select(static interval => (interval.Start, interval.End))))));
        }
        if (proposals.Count == original.Proposals.Count)
        {
            return original;
        }

        MediaMomentFindingManifest previous = original.Manifest;
        MomentAnchor[] anchors = proposals.SelectMany(static candidate => candidate.Anchors)
            .DistinctBy(static anchor => anchor.Id).ToArray();
        var manifest = new MediaMomentFindingManifest(previous.FinderIdentity,
            previous.FoundAtUtc, previous.SourcePath, previous.SourceDuration,
            previous.Options, previous.EvidenceAnalyzerName, previous.EvidenceAnalyzerVersion,
            previous.EvidenceSignalSchemaVersion, previous.VisualSampleCadence,
            previous.AudioWindowCadence, previous.IncludedRoles,
            previous.CompositionSchemaVersion, previous.CompositionCoordinateSpaceVersion,
            previous.CompositionPlanOrigin,
            Enum.GetValues<MomentAnchorKind>().Select(kind =>
                new KeyValuePair<MomentAnchorKind, int>(kind, anchors.Count(anchor => anchor.Kind == kind))),
            proposals.Count, previous.HardRejectedCount,
            previous.BelowThresholdCount + proposals.Count - original.Proposals.Count,
            previous.OverlapSuppressedCount, previous.SelectedCount, previous.TotalElapsed,
            previous.DeterministicCoverageStatement +
                " Additional source-coverage windows carry zero heuristic event score and require qualified semantic evidence before automatic selection.",
            previous.NeighborhoodCount + proposals.Count - original.Proposals.Count,
            previous.NeighborhoodSuppressedCount, previous.PolicyHash,
            previous.EpisodeCount, previous.EpisodeSuppressedCount);
        return new(original.Request, proposals, original.SelectedCandidates,
            original.Warnings, manifest, original.ActivationSeries, original.Episodes);
    }

    private static double Overlap(MomentCandidateWindow left, MomentCandidateWindow right)
    {
        long start = Math.Max(left.Start.Ticks, right.Start.Ticks);
        long end = Math.Min(left.End.Ticks, right.End.Ticks);
        return Math.Max(0, end - start) / (double)right.Duration.Ticks;
    }
}
