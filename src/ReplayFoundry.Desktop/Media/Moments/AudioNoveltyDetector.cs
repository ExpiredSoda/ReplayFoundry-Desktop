using ReplayFoundry.Desktop.Media.Analysis.Signals.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Visual;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Media.Moments;

internal static class AudioNoveltyDetector
{
    public static AudioNoveltyEvent[] Detect(
        MediaMomentFindingRequest request,
        IEnumerable<NormalizedAudioMomentSample> samples,
        CancellationToken cancellationToken)
    {
        var perStream =
            new List<AudioNoveltyEvent>();

        foreach (IGrouping<int, NormalizedAudioMomentSample> stream in
                 samples
                     .GroupBy(static sample => sample.Sample.AudioStreamIndex)
                     .OrderBy(static group => group.Key))
        {
            var active =
                new List<NormalizedAudioMomentSample>();
            foreach (NormalizedAudioMomentSample sample in
                     stream.OrderBy(static sample => sample.Sample.Start))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (sample.Context is null)
                {
                    AddIfQualifying(request, active, perStream);
                    active.Clear();
                    continue;
                }

                if (active.Count == 0)
                {
                    if (sample.Activity >=
                        request.Options.CalibrationPolicy.BurstStartThreshold)
                    {
                        active.Add(sample);
                    }

                    continue;
                }

                if (sample.Activity >=
                    request.Options.CalibrationPolicy.BurstEndThreshold)
                {
                    active.Add(sample);
                    continue;
                }

                AddIfQualifying(request, active, perStream);
                active.Clear();
            }

            AddIfQualifying(request, active, perStream);
        }

        // Several physical streams are supporting observations, not several
        // semantic events. Merge temporally coincident novelty before scoring.
        var merged =
            new List<AudioNoveltyEvent>();
        foreach (AudioNoveltyEvent item in
                 perStream.OrderBy(static item => item.Start))
        {
            if (merged.Count == 0 ||
                item.Start - merged[^1].End >
                request.Options.CrossSignalAgreementWindow)
            {
                merged.Add(item);
                continue;
            }

            AudioNoveltyEvent previous = merged[^1];
            AudioNoveltyEvent peak =
                previous.NormalizedProminence >= item.NormalizedProminence
                    ? previous
                    : item;
            merged[^1] =
                new AudioNoveltyEvent(
                    MomentStableId.Create(
                        "u",
                        previous.Start,
                        TimeSpan.FromTicks(Math.Max(previous.End.Ticks, item.End.Ticks)),
                        string.Join(",", previous.AudioStreamIndices.Concat(item.AudioStreamIndices).Distinct().OrderBy(static value => value))),
                    previous.AudioStreamIndices.Concat(item.AudioStreamIndices),
                    previous.Start,
                    peak.PeakTimestamp,
                    TimeSpan.FromTicks(Math.Max(previous.End.Ticks, item.End.Ticks)),
                    peak.LocalBaselineDbfs,
                    peak.LocalSpreadDb,
                    peak.PeakFiniteRmsDbfs,
                    Math.Max(previous.NormalizedProminence, item.NormalizedProminence),
                    Math.Max(previous.OnsetStrength, item.OnsetStrength),
                    Math.Max(previous.PeakLiftDb, item.PeakLiftDb),
                    TimeSpan.FromTicks(
                        Math.Min(
                            Math.Max(previous.End.Ticks, item.End.Ticks) -
                            previous.Start.Ticks,
                            previous.DurationAboveBaseline.Ticks +
                            item.DurationAboveBaseline.Ticks)),
                    previous.IsSilenceReentry || item.IsSilenceReentry,
                    Math.Max(previous.ReturnToBaseline, item.ReturnToBaseline),
                    previous.EvidenceReferences.Concat(item.EvidenceReferences));
        }

        return merged
            .OrderBy(static item => item.Start)
            .ThenBy(static item => item.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddIfQualifying(
        MediaMomentFindingRequest request,
        IReadOnlyList<NormalizedAudioMomentSample> samples,
        ICollection<AudioNoveltyEvent> destination)
    {
        if (samples.Count == 0)
        {
            return;
        }

        NormalizedAudioMomentSample peak =
            samples
                .OrderByDescending(static sample => sample.Activity)
                .ThenByDescending(static sample => sample.Context!.OnsetStrength)
                .ThenBy(static sample => sample.Sample.Start)
                .First();
        TimeSpan start = samples[0].Sample.Start;
        TimeSpan end = samples[^1].Sample.End;
        if (end - start < request.Options.CalibrationPolicy.MinimumBurstDuration ||
            peak.Activity < request.Options.CalibrationPolicy.MinimumBurstProminence ||
            samples.Max(static sample => sample.Context!.OnsetStrength) <
            request.Options.CalibrationPolicy.MinimumBurstOnset)
        {
            return;
        }

        int streamIndex = peak.Sample.AudioStreamIndex;
        bool reentry =
            request.Evidence.SilenceIntervals.Any(
                silence =>
                    silence.AudioStreamIndex == streamIndex &&
                    silence.End <= start &&
                    start - silence.End <=
                    request.Options.MeaningfulAudioSilenceDuration);
        MomentEvidenceReference[] references =
            samples.Select(
                    sample =>
                        new MomentEvidenceReference(
                            MomentEvidenceReferenceKind.AudioNoveltyEvent,
                            sample.Sample.Start,
                            sample.Sample.End,
                            "Non-semantic local audio-novelty window",
                            audioStreamIndex: sample.Sample.AudioStreamIndex,
                            rawValue: sample.Sample.RmsLevelDbfs,
                            normalizedValue: sample.Activity))
                .ToArray();

        destination.Add(
            new AudioNoveltyEvent(
                MomentStableId.Create(
                    "u",
                    streamIndex,
                    start,
                    end),
                [streamIndex],
                start,
                peak.Sample.Start,
                end,
                peak.Context!.LocalBaseline,
                peak.Context.LocalSpread,
                peak.Sample.RmsLevelDbfs!.Value,
                peak.Activity,
                samples.Max(static sample => sample.Context!.OnsetStrength),
                Math.Max(0, peak.Sample.RmsLevelDbfs.Value - peak.Context.LocalBaseline),
                TimeSpan.FromTicks(
                    samples
                        .Where(static sample => sample.Context!.RawExcess > 0)
                        .Sum(static sample => sample.Sample.Duration.Ticks)),
                reentry,
                samples.Max(static sample => sample.Context!.ReturnToBaseline),
                references));
    }
}
