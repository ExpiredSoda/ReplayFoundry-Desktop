using ReplayFoundry.Desktop.Media.Analysis.Signals.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Visual;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Media.Moments;

internal static class ActivityBurstDetector
{
    public static ActivityBurst[] Detect(
        MediaMomentFindingRequest request,
        IEnumerable<NormalizedVisualMomentSample> samples,
        CancellationToken cancellationToken)
    {
        var detected =
            new List<ActivityBurst>();

        foreach (IGrouping<string, NormalizedVisualMomentSample> target in
                 samples
                     .GroupBy(static sample => sample.Sample.TargetKey, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            NormalizedVisualMomentSample[] ordered =
                target.OrderBy(static sample => sample.Sample.Timestamp).ToArray();
            if (ordered.Length == 0)
            {
                continue;
            }

            var active =
                new List<NormalizedVisualMomentSample>();
            foreach (NormalizedVisualMomentSample sample in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (active.Count == 0)
                {
                    if (sample.Context.NormalizedProminence >=
                        request.Options.CalibrationPolicy.BurstStartThreshold)
                    {
                        active.Add(sample);
                    }

                    continue;
                }

                if (sample.Context.NormalizedProminence >=
                    request.Options.CalibrationPolicy.BurstEndThreshold)
                {
                    active.Add(sample);
                    continue;
                }

                AddIfQualifying(request, active, detected);
                active.Clear();
            }

            AddIfQualifying(request, active, detected);
        }

        return MergeShortGaps(
            request,
            detected)
            .OrderBy(static burst => burst.Start)
            .ThenBy(static burst => burst.TargetKey, StringComparer.Ordinal)
            .ThenBy(static burst => burst.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddIfQualifying(
        MediaMomentFindingRequest request,
        IReadOnlyList<NormalizedVisualMomentSample> samples,
        ICollection<ActivityBurst> destination)
    {
        if (samples.Count == 0)
        {
            return;
        }

        TimeSpan cadence =
            request.Evidence.Manifest.Options.VisualSignalSampleInterval;
        TimeSpan end =
            TimeSpan.FromTicks(
                Math.Min(
                    request.Media.Duration.Ticks,
                    samples[^1].Sample.Timestamp.Ticks + cadence.Ticks));
        TimeSpan start = samples[0].Sample.Timestamp;
        NormalizedVisualMomentSample peak =
            samples
                .OrderByDescending(static sample => sample.Context.NormalizedProminence)
                .ThenByDescending(static sample => sample.Context.OnsetStrength)
                .ThenBy(static sample => sample.Sample.Timestamp)
                .First();

        if (end - start < request.Options.CalibrationPolicy.MinimumBurstDuration ||
            peak.Context.NormalizedProminence <
                request.Options.CalibrationPolicy.MinimumBurstProminence ||
            samples.Max(static sample => sample.Context.OnsetStrength) <
                request.Options.CalibrationPolicy.MinimumBurstOnset)
        {
            return;
        }

        MomentEvidenceReference[] references =
            samples
                .Select(
                    sample =>
                        new MomentEvidenceReference(
                            sample.Role == CompositionRegionRole.Gameplay
                                ? MomentEvidenceReferenceKind.GameplayActivityBurst
                                : MomentEvidenceReferenceKind.PresenterActivitySample,
                            sample.Sample.Timestamp,
                            sample.Sample.Timestamp,
                            sample.Role == CompositionRegionRole.Gameplay
                                ? "Gameplay local-prominence burst sample"
                                : "Presenter local-prominence burst sample",
                            sample.Sample.TargetKey,
                            sample.IntervalIndex,
                            sample.RegionId,
                            sample.Role,
                            rawValue: sample.Context.RawValue,
                            normalizedValue: sample.Context.NormalizedProminence))
                .ToArray();

        destination.Add(
            Create(
                request,
                samples,
                peak,
                start,
                end,
                references));
    }
    private static ActivityBurst Create(
        MediaMomentFindingRequest request,
        IReadOnlyList<NormalizedVisualMomentSample> samples,
        NormalizedVisualMomentSample peak,
        TimeSpan start,
        TimeSpan end,
        IEnumerable<MomentEvidenceReference> references)
    {
        double occupancy =
            samples.Count(
                sample =>
                    sample.Context.NormalizedProminence >=
                    request.Options.CalibrationPolicy.BurstEndThreshold) /
            (double)samples.Count;
        double integrated =
            samples.Sum(static sample => sample.Context.RawExcess);
        double concentration =
            integrated <= 0
                ? 0
                : samples
                    .OrderByDescending(static sample => sample.Context.RawExcess)
                    .Take(Math.Max(1, samples.Count / 3))
                    .Sum(static sample => sample.Context.RawExcess) /
                  integrated;

        return new ActivityBurst(
            MomentStableId.Create(
                "b",
                peak.Sample.TargetKey,
                peak.Role,
                start,
                end),
            peak.Sample.TargetKey,
            peak.Role,
            start,
            peak.Sample.Timestamp,
            end,
            peak.Context.LocalBaseline,
            peak.Context.LocalSpread,
            peak.Context.RawValue,
            peak.Context.NormalizedProminence,
            samples.Max(static sample => sample.Context.OnsetStrength),
            integrated,
            Math.Clamp(occupancy, 0, 1),
            Math.Clamp(concentration, 0, 1),
            samples.Max(static sample => sample.Context.ReturnToBaseline),
            references);
    }

    private static IEnumerable<ActivityBurst> MergeShortGaps(
        MediaMomentFindingRequest request,
        IReadOnlyList<ActivityBurst> bursts)
    {
        foreach (IGrouping<string, ActivityBurst> target in
                 bursts.GroupBy(static burst => burst.TargetKey, StringComparer.Ordinal))
        {
            ActivityBurst[] ordered =
                target.OrderBy(static burst => burst.Start).ToArray();
            int index = 0;
            while (index < ordered.Length)
            {
                var group = new List<ActivityBurst> { ordered[index++] };
                while (index < ordered.Length &&
                       ordered[index].Start - group[^1].End <=
                       request.Options.CalibrationPolicy.MaximumBurstMergeGap)
                {
                    group.Add(ordered[index++]);
                }

                if (group.Count == 1)
                {
                    yield return group[0];
                    continue;
                }

                ActivityBurst peak =
                    group
                        .OrderByDescending(static burst => burst.PeakProminence)
                        .ThenBy(static burst => burst.PeakTimestamp)
                        .First();
                double integrated = group.Sum(static burst => burst.IntegratedExcess);
                yield return new ActivityBurst(
                    MomentStableId.Create(
                        "b",
                        peak.TargetKey,
                        peak.Role,
                        group[0].Start,
                        group[^1].End),
                    peak.TargetKey,
                    peak.Role,
                    group[0].Start,
                    peak.PeakTimestamp,
                    group[^1].End,
                    peak.LocalBaseline,
                    peak.LocalSpread,
                    peak.RawPeakActivity,
                    peak.PeakProminence,
                    group.Max(static burst => burst.OnsetStrength),
                    integrated,
                    group.Average(static burst => burst.Occupancy),
                    integrated <= 0
                        ? 0
                        : group.Max(static burst => burst.IntegratedExcess) / integrated,
                    group.Max(static burst => burst.ReturnToBaseline),
                    group.SelectMany(static burst => burst.EvidenceReferences));
            }
        }
    }
}
