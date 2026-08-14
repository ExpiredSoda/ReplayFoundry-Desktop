namespace ReplayFoundry.Desktop.Media.Moments;

internal static class MomentEventEpisodeDetector
{
    public static IReadOnlyList<MomentEventEpisode> Detect(
        MediaMomentFindingRequest request,
        MomentActivationSeries activation,
        IReadOnlyList<MomentAnchor> anchors,
        NormalizedMomentSignals signals,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(activation);
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(signals);

        MomentEpisodePolicy policy = request.Options.EpisodePolicy;
        var ranges = new List<(int Start, int End)>();
        int activeStart = -1;
        int lastContinued = -1;
        for (int index = 0; index < activation.Samples.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double value = DetectionValue(activation.Samples[index]);
            if (activeStart < 0)
            {
                if (value >= policy.EpisodeStartActivationThreshold)
                {
                    activeStart = Math.Max(0, index - 1);
                    lastContinued = index;
                }
                continue;
            }

            if (value >= policy.EpisodeContinueActivationThreshold)
            {
                lastContinued = index;
                continue;
            }

            TimeSpan gap = activation.Samples[index].Timestamp -
                activation.Samples[lastContinued].Timestamp;
            if (value <= policy.EpisodeEndActivationThreshold &&
                gap > policy.MaximumEpisodeBridgeGap)
            {
                ranges.Add((
                    activeStart,
                    Math.Min(activation.Samples.Count - 1, lastContinued + 1)));
                activeStart = -1;
                lastContinued = -1;
            }
        }

        if (activeStart >= 0)
        {
            ranges.Add((
                activeStart,
                Math.Min(activation.Samples.Count - 1, lastContinued + 1)));
        }

        var episodes = new List<MomentEventEpisode>();
        foreach ((int start, int end) in ranges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var split = SplitIfValid(request, activation.Samples, start, end);
            foreach (var item in split)
            {
                MomentEventEpisode? episode = CreateEpisode(
                    request,
                    activation.Samples,
                    item.Start,
                    item.End,
                    anchors,
                    signals,
                    item.Parent,
                    item.Split,
                    cancellationToken);
                if (episode is not null)
                {
                    episodes.Add(episode);
                }
            }
        }

        MomentEventEpisode[] ordered = episodes
            .OrderBy(static item => item.Start)
            .ThenBy(static item => item.Id, StringComparer.Ordinal)
            .ToArray();
        return AttachValidatedSplitParents(
            request,
            activation.Samples,
            ordered,
            anchors);
    }

    private static IReadOnlyList<(
        int Start,
        int End,
        string? Parent,
        MomentEpisodeSplitRationale Split)> SplitIfValid(
        MediaMomentFindingRequest request,
        IReadOnlyList<MomentActivationSample> samples,
        int start,
        int end)
    {
        MomentEpisodePolicy policy = request.Options.EpisodePolicy;
        var valleys = new List<(int Start, int End)>();
        int valleyStart = -1;
        for (int index = start + 1; index < end; index++)
        {
            if (DetectionValue(samples[index]) <=
                policy.SplitValleyActivationThreshold)
            {
                valleyStart = valleyStart < 0 ? index : valleyStart;
            }
            else if (valleyStart >= 0)
            {
                valleys.Add((valleyStart, index - 1));
                valleyStart = -1;
            }
        }
        if (valleyStart >= 0)
        {
            valleys.Add((valleyStart, end - 1));
        }

        foreach ((int valleyFrom, int valleyTo) in valleys
            .OrderBy(item => DetectionValue(samples[item.Start]))
            .ThenByDescending(item =>
                samples[item.End].Timestamp - samples[item.Start].Timestamp))
        {
            if (samples[valleyTo].Timestamp - samples[valleyFrom].Timestamp <
                policy.MinimumSplitValleyDuration)
            {
                continue;
            }
            if (!RangeQualifies(samples, start, valleyFrom - 1, policy) ||
                !RangeQualifies(samples, valleyTo + 1, end, policy))
            {
                continue;
            }

            string parent = MomentStableId.Create(
                "ep",
                request.Media.FullPath.ToUpperInvariant(),
                samples[start].Timestamp,
                samples[end].Timestamp);
            return
            [
                (
                    start,
                    valleyFrom - 1,
                    parent,
                    MomentEpisodeSplitRationale.DeepSustainedValley),
                (
                    valleyTo + 1,
                    end,
                    parent,
                    MomentEpisodeSplitRationale.DeepSustainedValley),
            ];
        }

        return [(start, end, null, MomentEpisodeSplitRationale.None)];
    }

    private static bool RangeQualifies(
        IReadOnlyList<MomentActivationSample> samples,
        int start,
        int end,
        MomentEpisodePolicy policy)
    {
        if (end <= start)
        {
            return false;
        }
        TimeSpan duration = samples[end].Timestamp - samples[start].Timestamp;
        double peak = samples
            .Skip(start)
            .Take(end - start + 1)
            .Max(DetectionValue);
        double integrated = Integrate(samples, start, end);
        return duration >= policy.MinimumEpisodeDuration &&
            peak >= policy.MinimumEpisodePeakActivation &&
            integrated / Math.Max(0.000001, duration.TotalSeconds) >=
                policy.MinimumEpisodeIntegratedActivation;
    }

    private static MomentEventEpisode? CreateEpisode(
        MediaMomentFindingRequest request,
        IReadOnlyList<MomentActivationSample> samples,
        int startIndex,
        int endIndex,
        IReadOnlyList<MomentAnchor> anchors,
        NormalizedMomentSignals signals,
        string? parent,
        MomentEpisodeSplitRationale split,
        CancellationToken cancellationToken)
    {
        MomentEpisodePolicy policy = request.Options.EpisodePolicy;
        if (!RangeQualifies(samples, startIndex, endIndex, policy))
        {
            return null;
        }

        TimeSpan start = samples[startIndex].Timestamp;
        TimeSpan end = samples[endIndex].Timestamp;
        if (end - start > policy.MaximumEpisodeDuration)
        {
            end = start + policy.MaximumEpisodeDuration;
            endIndex = samples
                .Select((item, index) => (item, index))
                .Where(item => item.index >= startIndex && item.item.Timestamp <= end)
                .Select(static item => item.index)
                .DefaultIfEmpty(startIndex)
                .Max();
        }

        MomentActivationSample peak = samples
            .Skip(startIndex)
            .Take(endIndex - startIndex + 1)
            .OrderByDescending(DetectionValue)
            .ThenBy(static item => item.Timestamp)
            .First();
        int peakIndex = samples.IndexOfReference(peak);
        int onsetIndex = Enumerable.Range(
                startIndex,
                peakIndex - startIndex + 1)
            .First(index => DetectionValue(samples[index]) >=
                policy.EpisodeStartActivationThreshold);
        double integrated = Integrate(samples, startIndex, endIndex);
        double occupancy = samples
            .Skip(startIndex)
            .Take(endIndex - startIndex + 1)
            .Count(item => DetectionValue(item) >=
                policy.EpisodeContinueActivationThreshold) /
            (double)(endIndex - startIndex + 1);
        double? baseline = startIndex == 0
            ? null
            : DetectionValue(samples[startIndex - 1]);
        double? recovery = endIndex + 1 >= samples.Count
            ? null
            : DetectionValue(samples[endIndex + 1]);
        MomentAnchor[] episodeAnchors = anchors
            .Where(item => item.Timestamp >= start && item.Timestamp <= end)
            .OrderBy(static item => item.Timestamp)
            .ThenBy(static item => item.Id, StringComparer.Ordinal)
            .ToArray();
        MomentSignalFamily[] families = episodeAnchors
            .Select(MomentSignalFamilyMap.FromAnchor)
            .Distinct()
            .ToArray();
        string[] burstIds = signals.GameplayBursts
            .Concat(signals.PresenterBursts)
            .Where(item => item.Start <= end && item.End >= start)
            .Select(static item => item.Id)
            .ToArray();
        MomentEvidenceReference[] references = episodeAnchors
            .SelectMany(static item => item.EvidenceReferences)
            .Concat(samples
                .Skip(startIndex)
                .Take(endIndex - startIndex + 1)
                .SelectMany(static item => item.Components)
                .SelectMany(static item => item.EvidenceReferences))
            .GroupBy(static item => (
                item.Kind,
                item.Start,
                item.End,
                item.VisualTargetKey,
                item.AudioStreamIndex))
            .Select(static group => group.First())
            .ToArray();
        string id = MomentStableId.Create(
            "ep",
            request.Media.FullPath.ToUpperInvariant(),
            start,
            peak.Timestamp,
            end,
            parent ?? "root");
        MomentEventEpisodePhase[] phases = BuildPhases(
            samples,
            startIndex,
            onsetIndex,
            peakIndex,
            endIndex,
            policy);
        cancellationToken.ThrowIfCancellationRequested();
        return new MomentEventEpisode(
            id,
            start,
            samples[onsetIndex].Timestamp,
            peak.Timestamp,
            end,
            DetectionValue(peak),
            integrated,
            occupancy,
            baseline,
            recovery,
            new MomentEpisodeEvidenceSummary(
                families,
                burstIds,
                episodeAnchors.Select(static item => item.Id),
                references),
            phases,
            split == MomentEpisodeSplitRationale.None
                ? "Hysteresis activation remained coherent across brief gaps."
                : "A deep sustained activation valley separated independently " +
                  "strong sides.",
            parent,
            split,
            recovery is null
                ? ["Recovery evidence is unavailable at the source boundary."]
                : []);
    }

    private static MomentEventEpisodePhase[] BuildPhases(
        IReadOnlyList<MomentActivationSample> samples,
        int start,
        int onset,
        int peak,
        int end,
        MomentEpisodePolicy policy)
    {
        double coreThreshold = Math.Max(
            policy.EpisodeContinueActivationThreshold,
            DetectionValue(samples[peak]) * 0.75);
        int coreStart = Enumerable.Range(onset, peak - onset + 1)
            .FirstOrDefault(
                index => DetectionValue(samples[index]) >= coreThreshold,
                peak);
        int coreEnd = Enumerable.Range(peak, end - peak + 1)
            .TakeWhile(index => DetectionValue(samples[index]) >=
                policy.EpisodeContinueActivationThreshold)
            .DefaultIfEmpty(peak)
            .Last();
        int recoveryStart = Math.Min(end, coreEnd + 1);
        return
        [
            new(
                MomentEventEpisodePhaseKind.LeadIn,
                samples[start].Timestamp,
                samples[onset].Timestamp,
                onset > start),
            new(
                MomentEventEpisodePhaseKind.Rising,
                samples[onset].Timestamp,
                samples[coreStart].Timestamp,
                coreStart > onset),
            new(
                MomentEventEpisodePhaseKind.Core,
                samples[coreStart].Timestamp,
                samples[coreEnd].Timestamp,
                true),
            new(
                MomentEventEpisodePhaseKind.Falling,
                samples[coreEnd].Timestamp,
                samples[recoveryStart].Timestamp,
                recoveryStart > coreEnd),
            new(
                MomentEventEpisodePhaseKind.Recovery,
                samples[recoveryStart].Timestamp,
                samples[end].Timestamp,
                end > recoveryStart),
        ];
    }

    private static double Integrate(
        IReadOnlyList<MomentActivationSample> samples,
        int start,
        int end)
    {
        double total = 0;
        for (int index = start; index < end; index++)
        {
            double seconds = (
                samples[index + 1].Timestamp -
                samples[index].Timestamp).TotalSeconds;
            total += seconds * (
                DetectionValue(samples[index]) +
                DetectionValue(samples[index + 1])) / 2;
        }
        return Math.Max(0, total);
    }

    private static double DetectionValue(MomentActivationSample sample) =>
        Math.Max(
            sample.RawCombinedActivation,
            sample.SmoothedCombinedActivation);

    private static MomentEventEpisode[] AttachValidatedSplitParents(
        MediaMomentFindingRequest request,
        IReadOnlyList<MomentActivationSample> samples,
        IReadOnlyList<MomentEventEpisode> episodes,
        IReadOnlyList<MomentAnchor> anchors)
    {
        var output = new List<MomentEventEpisode>(episodes.Count);
        int index = 0;
        while (index < episodes.Count)
        {
            if (index + 1 >= episodes.Count)
            {
                output.Add(episodes[index]);
                break;
            }

            MomentEventEpisode left = episodes[index];
            MomentEventEpisode right = episodes[index + 1];
            MomentActivationSample[] valley = samples
                .Where(item =>
                    item.Timestamp > left.End &&
                    item.Timestamp < right.Start)
                .ToArray();
            TimeSpan valleyDuration = right.Start - left.End;
            bool deep = valley.Length > 0 &&
                valley.Min(DetectionValue) <=
                    request.Options.EpisodePolicy.SplitValleyActivationThreshold &&
                valley.Average(DetectionValue) <=
                    request.Options.EpisodePolicy.EpisodeContinueActivationThreshold;
            bool clusterCrosses = anchors.Any(anchor =>
                anchor.Kind == MomentAnchorKind.GameplaySceneCluster &&
                anchor.Timestamp >= left.End &&
                anchor.Timestamp <= right.Start);
            bool withinParentLimit = right.End - left.Start <=
                request.Options.EpisodePolicy.MaximumEpisodeDuration;
            if (deep &&
                valleyDuration >=
                    request.Options.EpisodePolicy.MinimumSplitValleyDuration &&
                !clusterCrosses &&
                withinParentLimit)
            {
                string parent = MomentStableId.Create(
                    "ep",
                    request.Media.FullPath.ToUpperInvariant(),
                    left.Start,
                    right.End,
                    "deep-sustained-valley");
                output.Add(CloneAsSplit(left, parent));
                output.Add(CloneAsSplit(right, parent));
                index += 2;
                continue;
            }

            output.Add(left);
            index++;
        }
        return output.ToArray();
    }

    private static MomentEventEpisode CloneAsSplit(
        MomentEventEpisode episode,
        string parent) =>
        new(
            episode.Id,
            episode.Start,
            episode.OnsetTimestamp,
            episode.PrimaryPeakTimestamp,
            episode.End,
            episode.PeakActivation,
            episode.IntegratedActivation,
            episode.ActivationOccupancy,
            episode.LocalBaselineBefore,
            episode.LocalRecoveryAfter,
            episode.EvidenceSummary,
            episode.Phases,
            "A deep sustained activation valley separates two independently " +
            "valid subepisodes.",
            parent,
            MomentEpisodeSplitRationale.DeepSustainedValley,
            episode.Warnings);

    private static int IndexOfReference<T>(
        this IReadOnlyList<T> source,
        T value)
        where T : class
    {
        for (int index = 0; index < source.Count; index++)
        {
            if (ReferenceEquals(source[index], value))
            {
                return index;
            }
        }
        throw new InvalidOperationException(
            "Episode sample reference was not found.");
    }
}
